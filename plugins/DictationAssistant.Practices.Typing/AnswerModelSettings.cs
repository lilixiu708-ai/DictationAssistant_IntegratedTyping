// SPDX-License-Identifier: AGPL-3.0-or-later
// Modified 2026-10-21: optional candidate-model settings, separate from translation profiles.
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DictationAssistant.Practices.Typing;

internal sealed record AnswerModelConfiguration(string Endpoint, string Model, string ApiKey)
{
    // Record-generated text must not expose a key, URL or model configuration.
    public override string ToString() => "同义答案模型配置";
}

internal static class AnswerModelSettings
{
    public const string DefaultEndpoint = "https://api.openai.com/v1";
    private const int MaximumSettingsBytes = 32 * 1024;
    private const int MaximumPlaintextBytes = 16 * 1024;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 8
    };
    private static AnswerModelConfiguration? _sessionConfiguration;

    private static string StoragePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "DictationAssistant", "Plugins", "SynchronousTyping", "answer-model-api.json");

    public static bool CanPersist => OperatingSystem.IsWindows();

    public static void Validate(AnswerModelConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (string.IsNullOrWhiteSpace(configuration.Endpoint) || configuration.Endpoint.Length > 1024
            || configuration.Endpoint != configuration.Endpoint.Trim()
            || !Uri.TryCreate(configuration.Endpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(endpoint.Host)
            || !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment) || configuration.Endpoint.Any(char.IsControl))
            throw new ArgumentException("请填写 HTTPS 模型接口地址，不要在地址中填写密钥、查询参数或片段。");
        if (string.IsNullOrWhiteSpace(configuration.Model) || configuration.Model.Length > 200
            || configuration.Model != configuration.Model.Trim() || configuration.Model.Any(char.IsControl))
            throw new ArgumentException("请填写服务商提供的模型名称，最多 200 个字符。");
        if (string.IsNullOrWhiteSpace(configuration.ApiKey) || configuration.ApiKey.Length > 4096
            || configuration.ApiKey.Any(character => character is < '!' or > '~'))
            throw new ArgumentException("请填写有效的模型 API 密钥，不要包含空格、换行或其他非英文字符。");
    }

    public static async Task<AnswerModelConfiguration?> LoadAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsWindows()) return _sessionConfiguration;
            if (!File.Exists(StoragePath)) return null;
            StoredConfiguration stored;
            try
            {
                await using var stream = new FileStream(StoragePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    4096, FileOptions.Asynchronous);
                if (stream.Length > MaximumSettingsBytes) throw InvalidSettings();
                stored = await JsonSerializer.DeserializeAsync<StoredConfiguration>(stream, JsonOptions,
                    cancellationToken).ConfigureAwait(false) ?? throw InvalidSettings();
            }
            catch (JsonException) { throw InvalidSettings(); }
            catch (IOException) { throw new InvalidOperationException("无法读取模型设置，请检查当前用户的数据目录权限后重试。"); }
            catch (UnauthorizedAccessException) { throw new InvalidOperationException("无法读取模型设置，请检查当前用户的数据目录权限后重试。"); }
            cancellationToken.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsWindows()) throw UnsupportedPlatform();
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!OperatingSystem.IsWindows()) throw UnsupportedPlatform();
                return Decrypt(stored);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }

    // Saving configuration does not contact the configured model service.
    public static async Task SaveAsync(AnswerModelConfiguration configuration, CancellationToken cancellationToken)
    {
        Validate(configuration);
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsWindows())
            {
                _sessionConfiguration = configuration;
                return;
            }
            var stored = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!OperatingSystem.IsWindows()) throw UnsupportedPlatform();
                return Encrypt(configuration);
            }, cancellationToken).ConfigureAwait(false);
            var path = StoragePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using var heldLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None);
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    4096, FileOptions.Asynchronous))
                {
                    await JsonSerializer.SerializeAsync(stream, stored, JsonOptions, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { throw new InvalidOperationException("无法保存模型设置，原设置未被替换；请检查文件权限或稍后重试。"); }
        catch (UnauthorizedAccessException) { throw new InvalidOperationException("无法保存模型设置，原设置未被替换；请检查文件权限或稍后重试。"); }
        finally { Gate.Release(); }
    }

    public static async Task DeleteAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsWindows())
            {
                _sessionConfiguration = null;
                return;
            }
            var path = StoragePath;
            if (!File.Exists(path)) return;
            await using var heldLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None);
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(path);
        }
        catch (IOException) { throw new InvalidOperationException("无法删除模型设置，请稍后重试。"); }
        catch (UnauthorizedAccessException) { throw new InvalidOperationException("无法删除模型设置，请检查文件权限后重试。"); }
        finally { Gate.Release(); }
    }

    [SupportedOSPlatform("windows")]
    private static StoredConfiguration Encrypt(AnswerModelConfiguration configuration)
    {
        var (entropy, binding) = MachineBinding();
        byte[]? plaintext = null;
        try
        {
            plaintext = JsonSerializer.SerializeToUtf8Bytes(configuration, JsonOptions);
            if (plaintext.Length > MaximumPlaintextBytes) throw new ArgumentException("模型配置超出允许的大小。");
            return new StoredConfiguration(1, binding,
                Convert.ToBase64String(ProtectedData.Protect(plaintext, entropy, DataProtectionScope.CurrentUser)));
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("Windows 无法加密模型设置；密钥未保存，请检查当前用户配置。");
        }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(entropy);
        }
    }

    [SupportedOSPlatform("windows")]
    private static AnswerModelConfiguration Decrypt(StoredConfiguration stored)
    {
        if (stored.Version != 1 || string.IsNullOrEmpty(stored.MachineBinding)
            || stored.MachineBinding.Length != 64 || string.IsNullOrEmpty(stored.EncryptedConfiguration)
            || stored.EncryptedConfiguration.Length > MaximumSettingsBytes)
            throw InvalidSettings();
        var (entropy, binding) = MachineBinding();
        byte[]? plaintext = null;
        try
        {
            if (!string.Equals(stored.MachineBinding, binding, StringComparison.Ordinal))
                throw new InvalidOperationException("模型设置属于另一台设备，请在本机重新配置。");
            var ciphertext = Convert.FromBase64String(stored.EncryptedConfiguration);
            plaintext = ProtectedData.Unprotect(ciphertext, entropy, DataProtectionScope.CurrentUser);
            if (plaintext.Length > MaximumPlaintextBytes) throw InvalidSettings();
            var configuration = JsonSerializer.Deserialize<AnswerModelConfiguration>(plaintext, JsonOptions)
                ?? throw InvalidSettings();
            Validate(configuration);
            return configuration;
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("无法解密模型设置，请使用原 Windows 用户和设备，或在本机重新配置。");
        }
        catch (FormatException) { throw InvalidSettings(); }
        catch (JsonException) { throw InvalidSettings(); }
        catch (ArgumentException) { throw InvalidSettings(); }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(entropy);
        }
    }

    [SupportedOSPlatform("windows")]
    private static (byte[] Entropy, string Binding) MachineBinding()
    {
        // Reuse only the existing device-code lookup. Translation ciphertext and
        // its entropy namespace remain unchanged; the model has its own purpose.
        var machineCode = TranslationApiSettings.GetMachineCode();
        var entropyInput = Encoding.UTF8.GetBytes("DictationAssistant/SynchronousTyping/AnswerModel/DPAPI/v1\0" + machineCode);
        var bindingInput = Encoding.UTF8.GetBytes("DictationAssistant/SynchronousTyping/AnswerModel/Machine/v1\0" + machineCode);
        try
        {
            return (SHA256.HashData(entropyInput), Convert.ToHexString(SHA256.HashData(bindingInput)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(entropyInput);
            CryptographicOperations.ZeroMemory(bindingInput);
        }
    }

    private static InvalidOperationException InvalidSettings() =>
        new("保存的模型设置无效，请在插件配置中重新填写；旧翻译设置不受影响。");

    private static InvalidOperationException UnsupportedPlatform() =>
        new("模型设置只能在 Windows 当前用户和设备上持久保存。");

    private sealed record StoredConfiguration(int Version, string MachineBinding, string EncryptedConfiguration);
}
