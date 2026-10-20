// Source distributed under the AGPL-3.0-or-later terms of DictationAssistant.
// Modified 2026-10-20: isolate the recycled result row from the exercise session window.
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;

namespace DictationAssistant.Practices.Typing;

internal sealed class TypingResultRow : Grid
{
    private readonly TextBlock _number = new();
    private readonly TextBlock _expected = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _entered = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };

    public TypingResultRow()
    {
        ColumnDefinitions = new ColumnDefinitions("40,*,64");
        RowDefinitions = new RowDefinitions("Auto,Auto");
        ColumnSpacing = 12;
        RowSpacing = 4;
        Grid.SetRowSpan(_number, 2);
        Grid.SetColumn(_expected, 1);
        Grid.SetColumn(_entered, 1);
        Grid.SetRow(_entered, 1);
        Grid.SetColumn(_status, 2);
        Grid.SetRowSpan(_status, 2);
        Children.Add(_number);
        Children.Add(_expected);
        Children.Add(_entered);
        Children.Add(_status);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not TypedResult record)
        {
            _number.Text = _expected.Text = _entered.Text = _status.Text = null;
            return;
        }

        // Virtualized rows are recycled; update every field for the new item.
        // Format only visible rows instead of retaining one string/control per word.
        _number.Text = record.Index.ToString(CultureInfo.InvariantCulture);
        _expected.Text = (record.Prompt.Length > 0 ? "提示：" + record.Prompt + "\n" : string.Empty)
            + "答案：" + record.Expected;
        _entered.Text = "你的输入：" + (string.IsNullOrEmpty(record.Entered) ? "（空白）" : record.Entered);
        _status.Text = record.Correct
            ? (record.TimedOut ? "✓ 正确\n到时提交" : "✓ 正确")
            : (record.TimedOut ? "✗ 超时" : "✗ 错误");
        _status.Foreground = record.Correct ? Brushes.SeaGreen : Brushes.IndianRed;
    }
}
