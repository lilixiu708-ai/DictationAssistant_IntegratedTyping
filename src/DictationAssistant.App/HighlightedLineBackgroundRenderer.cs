using System.Linq;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;

namespace DictationAssistant.App;

public class HighlightedLineBackgroundRenderer : IBackgroundRenderer
{
    private readonly TextView _textView;
    private int _lineNumber;
    private IBrush? _background;

    public HighlightedLineBackgroundRenderer(TextView textView)
    {
        _textView = textView ?? throw new ArgumentNullException(nameof(textView));
    }

    public KnownLayer Layer => KnownLayer.Background;

    public int LineNumber
    {
        get => _lineNumber;
        set
        {
            if (_lineNumber != value)
            {
                _lineNumber = value;
                _textView.InvalidateLayer(KnownLayer.Background);
            }
        }
    }

    public IBrush? Background
    {
        get => _background;
        set
        {
            if (_background != value)
            {
                _background = value;
                _textView.InvalidateLayer(KnownLayer.Background);
            }
        }
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (LineNumber <= 0 || Background is null || !textView.VisualLinesValid)
        {
            return;
        }

        foreach (var visualLine in textView.VisualLines)
        {
            if (visualLine.FirstDocumentLine.LineNumber == LineNumber)
            {
                var rects = BackgroundGeometryBuilder.GetRectsFromVisualSegment(textView, visualLine, 0, 1000).ToList();
                if (rects.Count > 0)
                {
                    var rect = rects[0];
                    drawingContext.FillRectangle(Background, new Rect(0, rect.Top, textView.Bounds.Width, rect.Height));
                }
                break;
            }
        }
    }
}
