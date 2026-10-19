// Word-order shuffle extension, 2026-10-19. Licensed under AGPL-3.0-or-later.
using System.Diagnostics;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using DictationAssistant.App.Extensions;

namespace DictationAssistant.App;

public partial class MainWindow
{
    private bool _isShufflingWordList;

    private void InitializeWordListShuffle()
    {
        // InputGesture on a menu item is only a label; register the shortcut too.
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.R, KeyModifiers.Control | KeyModifiers.Shift),
            Command = new AsyncRelayCommand(ShuffleWordListAsync)
        });
    }

    private async void ShuffleWordList_Click(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        await ShuffleWordListAsync();
    }

    private async Task ShuffleWordListAsync()
    {
        if (_isShufflingWordList || _isOpeningWordList || _isApplyingWordList || WordlistEditor is null || GetViewModel() is not { } vm)
        {
            return;
        }

        if (!WordListShuffle.CanShuffle(WordlistEditor.Text))
        {
            ShuffleStatusMessage.Text = "请先输入至少两个内容不同的词条，每行一个。";
            return;
        }

        _isShufflingWordList = true;
        var wasEnabled = IsEnabled;
        try
        {
            // Block new playback, edits and repeated clicks while old speech drains.
            IsEnabled = false;
            ShuffleStatusMessage.Text = "正在结束当前播报并打乱顺序…";
            await vm.DictationPlayer.ResetProgressAsync();

            // Take the snapshot after the await, so an earlier file-open operation
            // cannot leave us shuffling a stale copy of the editor text.
            var document = WordlistEditor.Document;
            var originalText = document.Text;
            var shuffledText = WordListShuffle.Shuffle(originalText);
            if (string.Equals(originalText, shuffledText, StringComparison.Ordinal))
            {
                ShuffleStatusMessage.Text = "当前没有可调整顺序的不同词条。";
                return;
            }

            // Keep the same TextDocument: playback is attached to this instance.
            // A single update is one undo operation and preserves earlier edits.
            document.BeginUpdate();
            try
            {
                document.Replace(0, document.TextLength, shuffledText);
            }
            finally
            {
                document.EndUpdate();
            }

            WordlistEditor.Select(0, 0);
            WordlistEditor.ScrollToLine(1);
            if (_highlightedLineRenderer is not null)
            {
                _highlightedLineRenderer.LineNumber = 0;
            }

            var count = WordListShuffle.CountEntries(shuffledText);
            ShuffleStatusMessage.Text = $"已打乱 {count} 条。点击“开始播报”即可；Ctrl+Z 可撤销。";
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[WordListShuffle] Failed to shuffle: {ex}");
            ShuffleStatusMessage.Text = "本次打乱未完成，请停止播报后重试。";
        }
        finally
        {
            IsEnabled = wasEnabled;
            _isShufflingWordList = false;
            WordlistEditor.Focus();
        }
    }
}
