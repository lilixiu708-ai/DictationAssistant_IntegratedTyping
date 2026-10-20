// Source distributed under the AGPL-3.0-or-later terms of DictationAssistant.
// Modified 2026-10-20: Multiple translation providers, credentials and a shared plugin settings session.
using System.Text.Json.Serialization;

namespace DictationAssistant.Practices.Typing;

internal sealed record TranslationEngineBinding(string Id, string Name, string Provider, string ApiKey,
    string? SecretKey = null, string? Region = null)
{
    // A record's generated string representation would include the credential.
    public override string ToString() => Name;
}

internal sealed record TranslationEngineProviderDefinition(string Id, string Name, string PrimaryLabel,
    string? SecretLabel = null, string? DefaultRegion = null, bool RequiresRegion = false,
    string? HelpText = null)
{
    public bool SupportsRegion => RequiresRegion || Id == "microsoft-translator";

    public override string ToString() => Name;
}

internal sealed record TranslationEngineConfiguration(
    IReadOnlyList<TranslationEngineBinding> Bindings, string? PreferredEngineId)
{
    public static TranslationEngineConfiguration Empty { get; } = new(
        Array.Empty<TranslationEngineBinding>(), null);

    [JsonIgnore]
    public TranslationEngineBinding? Preferred =>
        Bindings.FirstOrDefault(binding => binding.Id == PreferredEngineId);

    public override string ToString() => $"翻译引擎配置（{Bindings.Count} 项）";
}

internal static class TranslationEngineProviders
{
    public static IReadOnlyList<TranslationEngineProviderDefinition> Definitions { get; } =
        Array.AsReadOnly(new[]
        {
            new TranslationEngineProviderDefinition("deepl-free", "DeepL API Free", "API 密钥",
                HelpText: "使用 DeepL API Free 账户的密钥。"),
            new TranslationEngineProviderDefinition("deepl-pro", "DeepL API Pro", "API 密钥",
                HelpText: "使用 DeepL API Pro 账户的密钥。"),
            new TranslationEngineProviderDefinition("microsoft-translator", "必应 / Microsoft Translator（Azure）", "订阅密钥",
                HelpText: "使用 Azure Translator 密钥；区域资源或多服务资源须填写对应区域。"),
            new TranslationEngineProviderDefinition("google-cloud", "Google 翻译（Cloud API）", "API 密钥",
                HelpText: "启用 Cloud Translation Basic（v2），并使用允许调用该服务的 API 密钥。"),
            new TranslationEngineProviderDefinition("baidu", "百度翻译", "APP ID", "密钥",
                HelpText: "使用百度翻译开放平台通用文本翻译应用的 APP ID 与密钥。"),
            new TranslationEngineProviderDefinition("youdao", "有道智云", "应用 ID（AppKey）", "应用密钥（AppSecret）",
                HelpText: "使用有道智云自然语言翻译应用的凭据。"),
            new TranslationEngineProviderDefinition("tencent", "腾讯云机器翻译", "SecretId", "SecretKey",
                "ap-guangzhou", true, "使用已开通机器翻译服务的腾讯云访问密钥，并填写服务区域。"),
            new TranslationEngineProviderDefinition("aliyun", "阿里云机器翻译", "AccessKey ID", "AccessKey Secret",
                "cn-hangzhou", true, "使用已开通机器翻译服务的阿里云访问密钥，并填写服务区域。"),
            new TranslationEngineProviderDefinition("volcengine", "火山引擎机器翻译", "Access Key ID", "Secret Access Key",
                "cn-north-1", true, "使用已开通机器翻译服务的火山引擎访问密钥，并填写服务区域。")
        });

    public static IReadOnlyList<string> Ids { get; } =
        Array.AsReadOnly(Definitions.Select(definition => definition.Id).ToArray());

    public static bool IsSupported(string provider) => Definitions.Any(definition => definition.Id == provider);

    public static TranslationEngineProviderDefinition Get(string provider) =>
        Definitions.FirstOrDefault(definition => definition.Id == provider)
        ?? throw new ArgumentException("请选择受支持的翻译引擎。", nameof(provider));

    public static string DisplayName(string provider) =>
        Definitions.FirstOrDefault(definition => definition.Id == provider)?.Name ?? "未知翻译引擎";

    public static void Validate(TranslationEngineBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (GetValidationError(binding) is { } error)
            throw new ArgumentException(error, nameof(binding));
    }

    // The editor and persistence boundary use the same rules without exposing credential values.
    public static string? GetValidationError(TranslationEngineBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (string.IsNullOrEmpty(binding.Id) || binding.Id.Length > 64
            || binding.Id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
            return "翻译引擎配置标识无效。";
        if (string.IsNullOrWhiteSpace(binding.Name) || binding.Name.Length > 80
            || binding.Name != binding.Name.Trim() || binding.Name.Any(char.IsControl))
            return "请填写 1 至 80 个字符的配置名称，不要包含首尾空格或控制字符。";
        var provider = Definitions.FirstOrDefault(definition => definition.Id == binding.Provider);
        if (provider is null) return "请选择受支持的翻译引擎。";
        if (CredentialError(binding.ApiKey, provider.PrimaryLabel, required: true) is { } primaryError)
            return primaryError;
        if (CredentialError(binding.SecretKey, provider.SecretLabel ?? "附加密钥",
            required: provider.SecretLabel is not null) is { } secretError)
            return secretError;
        if (string.IsNullOrEmpty(binding.Region))
        {
            if (provider.RequiresRegion) return "请填写翻译服务区域。";
        }
        else if (binding.Region.Length > 64 || binding.Region.Any(character =>
            character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-'))
            return "服务区域最多 64 个字符，只能包含小写英文字母、数字和连字符。";
        return null;
    }

    public static void Validate(TranslationEngineConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.Bindings is null || configuration.Bindings.Count > 20)
            throw new ArgumentException("最多可以绑定 20 个翻译引擎配置。", nameof(configuration));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in configuration.Bindings)
        {
            Validate(binding);
            if (!ids.Add(binding.Id))
                throw new ArgumentException("翻译引擎配置标识不能重复。", nameof(configuration));
            if (!names.Add(binding.Name))
                throw new ArgumentException("翻译引擎配置名称不能重复。", nameof(configuration));
        }
        if (configuration.Bindings.Count == 0 ? configuration.PreferredEngineId is not null
            : configuration.PreferredEngineId is null || !ids.Contains(configuration.PreferredEngineId))
            throw new ArgumentException("请选择一个已绑定的首选翻译引擎。", nameof(configuration));
    }

    public static TranslationEngineConfiguration Snapshot(TranslationEngineConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.Bindings is null)
            throw new ArgumentException("翻译引擎配置无效。", nameof(configuration));
        var snapshot = new TranslationEngineConfiguration(
            Array.AsReadOnly(configuration.Bindings.ToArray()), configuration.PreferredEngineId);
        Validate(snapshot);
        return snapshot.Bindings.Count == 0 ? TranslationEngineConfiguration.Empty : snapshot;
    }

    private static string? CredentialError(string? value, string label, bool required)
    {
        if (string.IsNullOrEmpty(value))
            return required ? $"请填写{label}。" : null;
        return value.Length > 512 || value.Any(character => character is <= ' ' or >= '\u007f')
            ? $"请填写有效的{label}，不要包含空格或换行。" : null;
    }
}

internal sealed class TranslationEngineSession
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TranslationEngineConfiguration? _configuration;

    public async Task<TranslationEngineConfiguration> GetAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _configuration ??= TranslationEngineProviders.Snapshot(
                await TranslationApiSettings.LoadAsync(cancellationToken).ConfigureAwait(false));
            return _configuration;
        }
        finally { _gate.Release(); }
    }

    public async Task SetAsync(TranslationEngineConfiguration configuration, bool persist,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = TranslationEngineProviders.Snapshot(configuration);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (persist)
            {
                if (snapshot.Bindings.Count == 0)
                    await TranslationApiSettings.DeleteAsync(cancellationToken).ConfigureAwait(false);
                else
                    await TranslationApiSettings.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
            }
            // A failed save leaves the previous session usable. Once a save has committed,
            // update the cache even if its caller's token was canceled during completion.
            _configuration = snapshot;
        }
        finally { _gate.Release(); }
    }
}
