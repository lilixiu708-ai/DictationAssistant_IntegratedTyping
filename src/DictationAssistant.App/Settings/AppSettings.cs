using Avalonia.Media;
using ALampy.XamlFontPicker.Avalonia;

namespace DictationAssistant.App.Settings;

public sealed record AppSettings
{
    public DictationSettings Dictation { get; init; } = new();

    public PreferenceSettings Preference { get; init; } = new();
}

public sealed record DictationSettings
{
    public string IntervalExpression { get; init; } = "3";

    public int TimesPerWord { get; init; } = 2;

    public bool HighlightCurrentLine { get; init; } = true;

    public bool AutoScrollToCurrentLine { get; init; } = true;

    public int Volume { get; init; } = 100;

    public int Rate { get; init; }

    public string LastSelectedVoiceId { get; init; } = string.Empty;
}

public sealed record PreferenceSettings
{
    public PickedFontInfo EditorFont { get; init; } =
        new(FontFamily.Default, FontStretch.Normal, FontStyle.Normal, FontWeight.Normal, 28);

    public string ImprovedResourcePath { get; init; } = string.Empty;

    public string DefaultChineseVoiceId { get; init; } = string.Empty;

    public string DefaultEnglishVoiceId { get; init; } = string.Empty;
}
