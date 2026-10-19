namespace DictationAssistant.App.Models;

public sealed class VoiceInfo
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? LocaleOrLanguage { get; init; }
}
