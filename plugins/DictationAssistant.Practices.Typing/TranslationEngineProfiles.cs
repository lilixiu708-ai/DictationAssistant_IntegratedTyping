// Source distributed under the AGPL-3.0-or-later terms of DictationAssistant.
// Modified 2026-10-20: Multiple translation bindings and a shared plugin settings session.
using System.Text.Json.Serialization;

namespace DictationAssistant.Practices.Typing;

internal sealed record TranslationEngineBinding(string Id, string Name, string Provider, string ApiKey)
{
    // A record's generated string representation would include the credential.
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
    public static IReadOnlyList<string> Ids { get; } =
        Array.AsReadOnly(new[] { "deepl-free", "deepl-pro" });

    public static bool IsSupported(string provider) => provider is "deepl-free" or "deepl-pro";

    public static string DisplayName(string provider) => provider switch
    {
        "deepl-free" => "DeepL API Free",
        "deepl-pro" => "DeepL API Pro",
        _ => "未知翻译引擎"
    };

    public static void Validate(TranslationEngineBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (string.IsNullOrEmpty(binding.Id) || binding.Id.Length > 64
            || binding.Id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
            throw new ArgumentException("翻译引擎配置标识无效。", nameof(binding));
        if (string.IsNullOrWhiteSpace(binding.Name) || binding.Name.Length > 80
            || binding.Name != binding.Name.Trim() || binding.Name.Any(char.IsControl))
            throw new ArgumentException("请填写 1 至 80 个字符的配置名称，不要包含首尾空格或控制字符。", nameof(binding));
        if (!IsSupported(binding.Provider))
            throw new ArgumentException("请选择受支持的翻译引擎。", nameof(binding));
        if (string.IsNullOrEmpty(binding.ApiKey) || binding.ApiKey.Length > 512
            || binding.ApiKey.Any(character => character is <= ' ' or >= '\u007f'))
            throw new ArgumentException("请填写有效的 API 密钥，不要包含空格或换行。", nameof(binding));
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
