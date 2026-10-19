using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using DictationAssistant.App.ViewModels;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Avalonia.Threading;
using DictationAssistant.App.Abstractions;

namespace DictationAssistant.App;

public partial class SaveAudioWindow : Window
{
    private SaveAudioWindowViewModel? ViewModel => DataContext as SaveAudioWindowViewModel;

    public SaveAudioWindow()
    {
        InitializeComponent();
        DataContext = new SaveAudioWindowViewModel();
    }

    public SaveAudioWindow(IDictationPlayer dictationPlayer) : this()
    {
        if (DataContext is SaveAudioWindowViewModel vm)
        {
            vm.SetDictationPlayer(dictationPlayer);
            vm.AlertRequested += message =>
            {
                Dispatcher.UIThread.Post(async () =>
                {
                    var box = MessageBoxManager.GetMessageBoxStandard("保存音频", message, ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Info);
                    await box.ShowWindowDialogAsync(this);
                });
            };
        }
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel is { IsExporting: true } vm)
        {
            vm.CancelExport();
            return;
        }

        Close(false);
    }

    private async void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel is { } vm)
        {
            await vm.ExportAsync();
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        // Prevent closing while exporting (user must cancel first)
        if (ViewModel is { IsExporting: true })
        {
            e.Cancel = true;
        }
    }

    private async void SetTargetPath_Click(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel is null)
        {
            return;
        }

        var extension = ViewModel.SelectedEncoder.Info.Extension.TrimStart('.');

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "选择目标文件",
            SuggestedFileName = Path.GetFileName(ViewModel.TargetPath),
            DefaultExtension = extension,
            FileTypeChoices =
            [
                new FilePickerFileType($"{ViewModel.SelectedEncoder.Info.Name} 文件")
                {
                    Patterns = [$"*.{extension}"]
                }
            ]
        });

        if (file is null)
        {
            return;
        }

        ViewModel.TargetPath = file.TryGetLocalPath() ?? file.Name;
    }
}
