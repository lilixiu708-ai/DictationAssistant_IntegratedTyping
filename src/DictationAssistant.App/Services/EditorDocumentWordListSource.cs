using Avalonia.Threading;
using AvaloniaEdit.Document;
using DictationAssistant.App.Abstractions;

namespace DictationAssistant.App.Services;

public sealed class EditorDocumentWordListSource : IWordListSource
{
    private TextDocument? _document;

    public int Count => Dispatcher.UIThread.Invoke(() => _document?.LineCount ?? 0);

    public void AttachDocument(TextDocument document)
    {
        _document = document;
    }

    public IReadOnlyList<string> GetWords() => Dispatcher.UIThread.Invoke(() =>
    {
        if (_document == null)
            return [];

        var words = new string[_document.LineCount];
        for (int i = 0; i < _document.LineCount; i++)
        {
            var line = _document.GetLineByNumber(i + 1);
            words[i] = _document.GetText(line.Offset, line.Length);
        }
        return words;
    });

    public string? TryGetAt(int index)
    {
        return Dispatcher.UIThread.Invoke(() =>
        {
            if (_document == null || index < 0 || index >= _document.LineCount)
                return null;

            var line = _document.GetLineByNumber(index + 1);
            return _document.GetText(line.Offset, line.Length);
        });
    }
}
