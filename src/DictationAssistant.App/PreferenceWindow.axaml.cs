using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using DictationAssistant.App.Abstractions;
using DictationAssistant.App.Settings;
using DictationAssistant.App.ViewModels;

namespace DictationAssistant.App;

public partial class PreferenceWindow : Window
{
    private PreferenceWindowViewModel? ViewModel => DataContext as PreferenceWindowViewModel;

    public PreferenceSettings ResultSettings { get; private set; } = new();

    public PreferenceWindow()
        : this(new PreferenceSettings(), [])
    {
    }

    public PreferenceWindow(PreferenceSettings settings, IReadOnlyList<IVoiceFactory> voiceOptions)
    {
        InitializeComponent();
        DataContext = new PreferenceWindowViewModel(settings, voiceOptions);
        ResultSettings = settings;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Close(false);
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel is not null)
        {
            ResultSettings = ViewModel.ToSettings();
        }

        Close(true);
    }

    private async void SetPathForImprovedResourceButton_Click(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel is null)
        {
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择音源增强目录",
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault();
        if (folder is null)
        {
            return;
        }

        ViewModel.ImprovedResourcePath = folder.TryGetLocalPath() ?? folder.Name;
    }
}
