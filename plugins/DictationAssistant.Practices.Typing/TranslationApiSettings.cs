// Source distributed under the AGPL-3.0-or-later terms of DictationAssistant.
// Modified 2026-10-20: Windows user- and machine-bound translation profile storage.
using System.Runtime.Versioning;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace DictationAssistant.Practices.Typing;

internal static class TranslationApiSettings
{
    private const int MaximumSettingsBytes = 128 * 1024;
    private const int MaximumPlaintextBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private static string StoragePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "DictationAssistant", "Plugins", "SynchronousTyping", "translation-api.json");

    public static bool CanPersist => OperatingSystem.IsWindows();

    public static string GetMachineCode()
    {
        if (!OperatingSystem.IsWindows()) throw UnsupportedPlatform();
        var identity = ReadMachineIdentity();
        try
        {
            var code = identity.Digest[..16];
            return $"{code[..4]}-{code[4..8]}-{code[8..12]}-{code[12..16]}";
        }
        finally { CryptographicOperations.ZeroMemory(identity.Entropy); }
    }

    public static async Task<TranslationEngineConfiguration> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = StoragePath;
        if (!File.Exists(path)) return TranslationEngineConfiguration.Empty;
        if (!OperatingSystem.IsWindows()) throw UnsupportedPlatform();

        StoredConfiguration stored;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous);
            if (stream.Length > MaximumSettingsBytes) throw InvalidSettings();
            stored = await JsonSerializer.DeserializeAsync<StoredConfiguration>(stream, JsonOptions,
                cancellationToken).ConfigureAwait(false) ?? throw InvalidSettings();
        }
        catch (JsonException) { throw InvalidSettings(); }
        catch (IOException) { throw new InvalidOperationException("无法读取翻译 API 设置，请检查当前用户的数据目录权限后重试。"); }
        catch (UnauthorizedAccessException) { throw new InvalidOperationException("无法读取翻译 API 设置，请检查当前用户的数据目录权限后重试。"); }

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsWindows()) throw UnsupportedPlatform();
            return Decrypt(stored);
        }, cancellationToken).ConfigureAwait(false);
    }

    public static async Task SaveAsync(TranslationEngineConfiguration configuration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = TranslationEngineProviders.Snapshot(configuration);
        if (!OperatingSystem.IsWindows()) throw UnsupportedPlatform();
        var stored = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsWindows()) throw UnsupportedPlatform();
            return Encrypt(snapshot);
        }, cancellationToken).ConfigureAwait(false);

        var path = StoragePath;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, stored, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        catch (IOException) { throw new InvalidOperationException("无法保存翻译 API 设置，原有设置未被替换。请检查数据目录或文件占用后重试。"); }
        catch (UnauthorizedAccessException) { throw new InvalidOperationException("无法保存翻译 API 设置，请检查当前用户的数据目录权限后重试。"); }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static Task DeleteAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw UnsupportedPlatform();
        try { File.Delete(StoragePath); }
        catch (IOException) { throw new InvalidOperationException("无法删除翻译 API 设置，请关闭占用设置文件的程序后重试。"); }
        catch (UnauthorizedAccessException) { throw new InvalidOperationException("无法删除翻译 API 设置，请检查当前用户的数据目录权限后重试。"); }
    }, cancellationToken);

    [SupportedOSPlatform("windows")]
    private static StoredConfiguration Encrypt(TranslationEngineConfiguration configuration)
    {
        var identity = ReadMachineIdentity();
        byte[]? plaintext = null;
        try
        {
            plaintext = JsonSerializer.SerializeToUtf8Bytes(configuration, JsonOptions);
            if (plaintext.Length > MaximumPlaintextBytes)
                throw new ArgumentException("翻译引擎配置超出允许的大小。", nameof(configuration));
            return new StoredConfiguration
            {
                Version = 2,
                MachineDigest = identity.Digest,
                EncryptedConfiguration = Convert.ToBase64String(ProtectedData.Protect(plaintext, identity.Entropy,
                    DataProtectionScope.CurrentUser))
            };
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("Windows 无法加密此 API 密钥。请确认用户配置文件可用；密钥未保存。");
        }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(identity.Entropy);
        }
    }

    [SupportedOSPlatform("windows")]
    private static TranslationEngineConfiguration Decrypt(StoredConfiguration stored)
    {
        var encrypted = stored.Version switch
        {
            1 when stored.Provider == "DeepL" && stored.UseFreeApi.HasValue => stored.EncryptedKey,
            2 => stored.EncryptedConfiguration,
            _ => null
        };
        if (string.IsNullOrEmpty(encrypted) || encrypted.Length > MaximumSettingsBytes
            || string.IsNullOrEmpty(stored.MachineDigest) || stored.MachineDigest.Length != 64)
            throw InvalidSettings();
        var identity = ReadMachineIdentity();
        byte[]? plaintext = null;
        try
        {
            if (!string.Equals(identity.Digest, stored.MachineDigest, StringComparison.Ordinal))
                throw new InvalidOperationException("保存的 API 设置不属于此设备，请删除旧设置并在本机重新输入密钥。");
            var ciphertext = Convert.FromBase64String(encrypted);
            plaintext = ProtectedData.Unprotect(ciphertext, identity.Entropy, DataProtectionScope.CurrentUser);
            if (plaintext.Length > MaximumPlaintextBytes) throw InvalidSettings();
            if (stored.Version == 1)
            {
                // Preserve R5's binding without rewriting its file until the user saves.
                var provider = stored.UseFreeApi!.Value ? "deepl-free" : "deepl-pro";
                return TranslationEngineProviders.Snapshot(new TranslationEngineConfiguration(
                    new[] { new TranslationEngineBinding("legacy-deepl",
                        TranslationEngineProviders.DisplayName(provider), provider,
                        new UTF8Encoding(false, true).GetString(plaintext)) }, "legacy-deepl"));
            }
            var configuration = JsonSerializer.Deserialize<TranslationEngineConfiguration>(plaintext, JsonOptions)
                ?? throw InvalidSettings();
            return TranslationEngineProviders.Snapshot(configuration);
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("无法解密 API 设置，可能由其他 Windows 用户或设备保存。请删除旧设置并重新输入密钥。");
        }
        catch (FormatException) { throw InvalidSettings(); }
        catch (JsonException) { throw InvalidSettings(); }
        catch (DecoderFallbackException) { throw InvalidSettings(); }
        catch (ArgumentException) { throw InvalidSettings(); }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(identity.Entropy);
        }
    }

    [SupportedOSPlatform("windows")]
    private static (byte[] Entropy, string Digest) ReadMachineIdentity()
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var cryptography = machine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography", writable: false);
            if (cryptography?.GetValue("MachineGuid") is not string raw || !Guid.TryParse(raw, out var machineGuid))
                throw new InvalidOperationException("无法读取设备标识，不能保存或解密 API 密钥。请检查 Windows 设备配置。");
            var canonical = machineGuid.ToString("D");
            // Keep the original entropy namespace so existing R5 bindings remain decryptable.
            var entropyInput = Encoding.UTF8.GetBytes("DictationAssistant/SynchronousTyping/DeepL/DPAPI/v1\0" + canonical);
            var digestInput = Encoding.UTF8.GetBytes("DictationAssistant/SynchronousTyping/DeepL/Machine/v1\0" + canonical);
            try
            {
                return (SHA256.HashData(entropyInput), Convert.ToHexString(SHA256.HashData(digestInput)));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(entropyInput);
                CryptographicOperations.ZeroMemory(digestInput);
            }
        }
        catch (IOException) { throw new InvalidOperationException("无法读取设备标识，请检查 Windows 注册表访问权限。"); }
        catch (UnauthorizedAccessException) { throw new InvalidOperationException("无法读取设备标识，请检查 Windows 注册表访问权限。"); }
        catch (SecurityException) { throw new InvalidOperationException("无法读取设备标识，请检查 Windows 注册表访问权限。"); }
    }

    private static InvalidOperationException UnsupportedPlatform() =>
        new("API 密钥持久保存仅支持 Windows 当前用户和设备；此系统可临时输入密钥使用。");

    private static InvalidOperationException InvalidSettings() =>
        new("保存的翻译 API 设置无效，请删除旧设置并重新输入密钥。");

    private sealed class StoredConfiguration
    {
        public int Version { get; set; }
        public string? Provider { get; set; }
        public bool? UseFreeApi { get; set; }
        public string? MachineDigest { get; set; }
        public string? EncryptedKey { get; set; }
        public string? EncryptedConfiguration { get; set; }
    }
}
