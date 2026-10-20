// SPDX-License-Identifier: AGPL-3.0-or-later
// Modified 2026-10-20: keep editable bindings separate from the settings window and saved session.
namespace DictationAssistant.Practices.Typing;

internal sealed class TranslationEngineDraft
{
    private readonly List<TranslationEngineBinding> _bindings = [];

    public TranslationEngineDraft() => Bindings = _bindings.AsReadOnly();

    public IReadOnlyList<TranslationEngineBinding> Bindings { get; }
    public string? PreferredEngineId { get; private set; }

    public void Load(TranslationEngineConfiguration configuration)
    {
        var snapshot = TranslationEngineProviders.Snapshot(configuration);
        _bindings.Clear();
        _bindings.AddRange(snapshot.Bindings);
        PreferredEngineId = snapshot.PreferredEngineId;
    }

    public void Reset()
    {
        _bindings.Clear();
        PreferredEngineId = null;
    }

    public TranslationEngineBinding Apply(TranslationEngineBinding? editing, string? name,
        string provider, string? apiKey, string? secretKey, string? region)
    {
        var definition = TranslationEngineProviders.Get(provider);
        var reuse = editing?.Provider == provider;
        var key = apiKey?.Trim();
        if (string.IsNullOrEmpty(key) && reuse) key = editing?.ApiKey;
        var secret = definition.SecretLabel is null ? null : secretKey?.Trim();
        if (definition.SecretLabel is not null && string.IsNullOrEmpty(secret) && reuse)
            secret = editing?.SecretKey;
        region = definition.SupportsRegion ? region?.Trim() : null;
        if (string.IsNullOrEmpty(region)) region = null;
        var binding = new TranslationEngineBinding(editing?.Id ?? Guid.NewGuid().ToString("N"),
            name?.Trim() ?? string.Empty, provider, key ?? string.Empty, secret, region);
        if (TranslationEngineProviders.GetValidationError(binding) is { } error)
            throw new InvalidDataException(error);
        if (_bindings.Any(existing => existing.Id != binding.Id
            && string.Equals(existing.Name, binding.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("绑定名称已存在，请使用其他名称。");
        var index = _bindings.FindIndex(existing => existing.Id == binding.Id);
        if (index < 0)
        {
            if (_bindings.Count >= 20)
                throw new InvalidDataException("最多可保存 20 个绑定，请先移除不再使用的绑定。");
            _bindings.Add(binding);
        }
        else _bindings[index] = binding;
        PreferredEngineId ??= binding.Id;
        return binding;
    }

    public TranslationEngineBinding? Remove(string id)
    {
        var index = _bindings.FindIndex(binding => binding.Id == id);
        if (index < 0) throw new InvalidOperationException("请选择已绑定的翻译引擎。");
        _bindings.RemoveAt(index);
        if (PreferredEngineId == id) PreferredEngineId = _bindings.FirstOrDefault()?.Id;
        return _bindings.Count > 0 ? _bindings[Math.Min(index, _bindings.Count - 1)] : null;
    }

    public void MakePreferred(string id)
    {
        if (!_bindings.Any(binding => binding.Id == id))
            throw new InvalidOperationException("请选择已绑定的翻译引擎。");
        PreferredEngineId = id;
    }

    public TranslationEngineConfiguration Snapshot() => TranslationEngineProviders.Snapshot(
        new TranslationEngineConfiguration(Bindings, PreferredEngineId));
}
