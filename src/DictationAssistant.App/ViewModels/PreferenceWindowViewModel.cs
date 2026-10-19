using CommunityToolkit.Mvvm.ComponentModel;
using DictationAssistant.App.Abstractions;
using DictationAssistant.App.Settings;
using ALampy.XamlFontPicker.Avalonia;

namespace DictationAssistant.App.ViewModels;

public partial class PreferenceWindowViewModel : ObservableObject
{
    public PreferenceWindowViewModel()
        : this(new PreferenceSettings(), [])
    {
    }

    public PreferenceWindowViewModel(PreferenceSettings settings, IReadOnlyList<IVoiceFactory> voiceOptions)
    {
        EditorFontInfo = settings.EditorFont;
        DefaultChineseVoice = voiceOptions.FirstOrDefault(v => v.Info.Id == settings.DefaultChineseVoiceId);
        DefaultEnglishVoice = voiceOptions.FirstOrDefault(v => v.Info.Id == settings.DefaultEnglishVoiceId);
        ImprovedResourcePath = settings.ImprovedResourcePath;
        VoiceOptions = voiceOptions;
    }

    public IReadOnlyList<IVoiceFactory> VoiceOptions { get; }

    [ObservableProperty]
    private PickedFontInfo _editorFontInfo = new();

    [ObservableProperty]
    private IVoiceFactory? _defaultChineseVoice = null;

    [ObservableProperty]
    private IVoiceFactory? _defaultEnglishVoice = null;

    [ObservableProperty]
    private string _improvedResourcePath = string.Empty;

    public PreferenceSettings ToSettings()
    {
        return new PreferenceSettings
        {
            EditorFont = EditorFontInfo,
            ImprovedResourcePath = ImprovedResourcePath,
            DefaultChineseVoiceId = DefaultChineseVoice?.Info.Id ?? string.Empty,
            DefaultEnglishVoiceId = DefaultEnglishVoice?.Info.Id ?? string.Empty
        };
    }
}
