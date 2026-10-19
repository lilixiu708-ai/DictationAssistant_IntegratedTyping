// Modified 2026-10-19: shuffle and unified text/document file routing.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using DictationAssistant.App.Settings;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using DictationAssistant.App.ViewModels;
using XmlReader = System.Xml.XmlReader;

namespace DictationAssistant.App;

public partial class MainWindow : Window
{
    private IStorageFile? _currentFile;
    private HighlightedLineBackgroundRenderer? _highlightedLineRenderer;

    public MainWindow()
    {
        InitializeComponent();
        InitializeWordListShuffle();
        InitializePlugins();
        InitializePractice();

        Opened += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                HookEditor(vm);
                vm.InitializeVoices();
                vm.AlertRequested += message =>
                {
                    Dispatcher.UIThread.Post(async () =>
                    {
                        var box = MessageBoxManager.GetMessageBoxStandard("自动默写", message, ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Info);
                        await box.ShowWindowDialogAsync(this);
                    });
                };
                vm.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(MainWindowViewModel.CurrentLineIndex) ||
                        args.PropertyName == nameof(MainWindowViewModel.HighlightCurrentLine) ||
                        args.PropertyName == nameof(MainWindowViewModel.AutoScrollCurrentLine))
                    {
                        Dispatcher.UIThread.Post(() => MoveEditorToCurrentLine(vm));
                    }
                };
            }
        };

        AddHandler(DragDrop.DropEvent, OnDrop);

    }

    private void HookEditor(MainWindowViewModel vm)
    {
        if (WordlistEditor is null)
        {
            return;
        }

        vm.WordListSource.AttachDocument(WordlistEditor.Document);

        var highlightingAssembly = Assembly.GetExecutingAssembly();
        using var highlightingStream = highlightingAssembly.GetManifestResourceStream("DictationAssistant.App.Resources.WordlistHighlighting.xshd");
        if (highlightingStream != null)
        {
            using var reader = XmlReader.Create(highlightingStream);
            WordlistEditor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }

        _highlightedLineRenderer = new HighlightedLineBackgroundRenderer(WordlistEditor.TextArea.TextView)
        {
            Background = new SolidColorBrush(Colors.LightGreen)
        };
        WordlistEditor.TextArea.TextView.BackgroundRenderers.Add(_highlightedLineRenderer);
    }

    private void MoveEditorToCurrentLine(MainWindowViewModel vm)
    {
        if (WordlistEditor is null)
        {
            return;
        }

        var index = vm.CurrentLineIndex;
        if (index < 0 || index >= WordlistEditor.Document.LineCount)
        {
            _highlightedLineRenderer?.LineNumber = 0;
            return;
        }

        var lineNumber = index + 1;
        var line = WordlistEditor.Document.GetLineByNumber(lineNumber);

        if (vm.AutoScrollCurrentLine)
        {
            WordlistEditor.ScrollToLine(lineNumber);
        }
        _highlightedLineRenderer?.LineNumber = vm.HighlightCurrentLine ? lineNumber : 0;
    }

    private MainWindowViewModel? GetViewModel() => DataContext as MainWindowViewModel;

    private async void OpenFile_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        await ChooseAndOpenWordListAsync(documentOnly: false);
    }

    private async void SaveFile_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (WordlistEditor is null)
        {
            return;
        }
        try
        {
            var suggestedStartLocation = _currentFile != null
                ? await _currentFile.GetParentAsync()
                : null;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存纯文本词表",
                SuggestedStartLocation = suggestedStartLocation,
                SuggestedFileName = _currentFile?.Name ?? _suggestedWordListFileName,
                DefaultExtension = "txt",
                FileTypeChoices = [WordListTextType]
            });
            if (file is null) return;
            if (!TextExtensions.Contains(Path.GetExtension(file.Name)))
                throw new InvalidDataException("词表只能保存为纯文本格式，请使用 .txt 扩展名。Word 文档需要通过导入插件读取。");

            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(WordlistEditor.Text);
            await writer.FlushAsync();
            _currentFile = file;
            _suggestedWordListFileName = file.Name;
            ShuffleStatusMessage.Text = $"已保存词表：{file.Name}";
        }
        catch (Exception ex)
        {
            await ShowFileErrorAsync($"保存失败：{ex.Message}");
        }
    }

    private async void NewWordList_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (WordlistEditor is null)
        {
            return;
        }

        if (_isOpeningWordList || _isApplyingWordList || _isShufflingWordList) return;
        try
        {
            await ApplyWordListTextAsync(string.Empty, append: false);
            _currentFile = null;
            _suggestedWordListFileName = "wordlist.txt";
            ShuffleStatusMessage.Text = "已新建词表。可输入文字或从文档导入。";
        }
        catch (Exception ex) { await ShowFileErrorAsync($"新建词表失败：{ex.Message}"); }
    }

    private void Exit_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Close();
    }

    private async void Preference_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (GetViewModel() is not { } vm)
        {
            return;
        }

        var dialog = new PreferenceWindow(vm.CreatePreferenceSnapshot(), vm.VoiceOptions);
        var result = await dialog.ShowDialog<bool?>(this);
        if (result == true)
        {
            vm.ApplyPreferenceSettings(dialog.ResultSettings);
        }
    }

    private async void SaveAudio_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var vm = GetViewModel();
        if (vm is null)
        {
            return;
        }

        var dialog = new SaveAudioWindow(vm.DictationPlayer);
        var result = await dialog.ShowDialog<bool?>(this);
        if (GetViewModel() is { } currentVm)
        {
        }
    }

    private async void About_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var dialog = new AboutWindow();
        await dialog.ShowDialog(this);
    }

    private void Cut_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        WordlistEditor?.Cut();
    }

    private void Copy_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        WordlistEditor?.Copy();
    }

    private void Paste_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        WordlistEditor?.Paste();
    }

    private void Delete_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (WordlistEditor is null)
        {
            return;
        }

        if (WordlistEditor.SelectionLength > 0)
        {
            WordlistEditor.SelectedText = string.Empty;
        }
    }

    private void Undo_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        WordlistEditor?.Undo();
    }

    private void Redo_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        WordlistEditor?.Redo();
    }

    private void SelectAll_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        WordlistEditor?.SelectAll();
    }

    private async void Count_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (WordlistEditor is null || GetViewModel() is not { } vm)
        {
            return;
        }

        var count = Importing.ImportPreviewBuilder.CountEntries(WordlistEditor.Text);
        var box = MessageBoxManager.GetMessageBoxStandard("自动默写", $"词语数量：{count}", ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Info);
        await box.ShowWindowDialogAsync(this);
    }

    private void SpeakSelection_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (WordlistEditor is null || GetViewModel() is not { } vm)
        {
            return;
        }

        var index = Math.Max(WordlistEditor.TextArea.Caret.Line - 1, 0);
        vm.SpeakLine(index);
    }

    private async void ViewSelectionInBingDictionary_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (WordlistEditor is null)
        {
            return;
        }

        var text = string.IsNullOrWhiteSpace(WordlistEditor.SelectedText)
            ? ReadCaretLineText(WordlistEditor)
            : WordlistEditor.SelectedText;

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var query = Uri.EscapeDataString(text.Trim());
        var url = $"https://cn.bing.com/dict/search?q={query}";
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            if (GetViewModel() is { } vm)
            {
                var box = MessageBoxManager.GetMessageBoxStandard("自动默写", "无法打开浏览器", ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
                await box.ShowWindowDialogAsync(this);
            }
        }
    }

    private void StartFromSelection_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (WordlistEditor is null || GetViewModel() is not { } vm)
        {
            return;
        }

        var index = Math.Max(WordlistEditor.TextArea.Caret.Line - 1, 0);
        vm.StartAutoFromLine(index);
    }

    private static string ReadCaretLineText(TextEditor editor)
    {
        var lineNumber = Math.Max(editor.TextArea.Caret.Line, 1);
        var line = editor.Document.GetLineByNumber(lineNumber);
        return editor.Document.GetText(line.Offset, line.Length);
    }

    private async void ResetRecord_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (GetViewModel() is not { } vm)
        {
            return;
        }

        if (vm.DictationPlayer.AutoMode)
        {
            var box = MessageBoxManager.GetMessageBoxStandard("自动默写", "请先停止自动播报！", ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Info);
            await box.ShowWindowDialogAsync(this);
            return;
        }

        vm.DictationPlayer.ResetProgress();
    }
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (WordlistEditor is null)
        {
            return;
        }

#pragma warning disable CS0618 // Avalonia 11: DataTransfer does not support GetFiles yet
        var files = e.Data.GetFiles()?.ToList();
#pragma warning restore CS0618
        if (files is null || files.Count == 0)
        {
            return;
        }

        if (files[0] is not IStorageFile file)
        {
            return;
        }
        e.Handled = true;
        await RunFileOpenAsync(() => Task.FromResult<IStorageFile?>(file));
    }

    public async Task LoadFileByPathAsync(string filePath)
    {
        await RunFileOpenAsync(async () => await StorageProvider.TryGetFileFromPathAsync(filePath)
            ?? throw new FileNotFoundException("找不到要打开的词表或文档。", filePath));
    }

}
