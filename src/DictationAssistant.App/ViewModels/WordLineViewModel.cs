using CommunityToolkit.Mvvm.ComponentModel;

namespace DictationAssistant.App.ViewModels;

public partial class WordLineViewModel : ObservableObject
{
    [ObservableProperty]
    private int _lineNumber;

    [ObservableProperty]
    private string _text = string.Empty;
}
