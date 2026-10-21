// SPDX-License-Identifier: AGPL-3.0-or-later
// Modified 2026-10-21: keep translation and candidate-answer model settings distinct.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DictationAssistant.Practices.Typing;

internal sealed class PracticePluginSettingsWindow : Window
{
    private readonly TranslationEngineSession _translations;
    private readonly CancellationTokenSource _cancellation;
    private readonly CancellationTokenRegistration _registration;
    private readonly Button _translationButton = new() { Content = "翻译引擎配置…", MinWidth = 190 };
    private readonly Button _answerModelButton = new() { Content = "同义答案模型…", MinWidth = 190 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed };
    private Task? _operation;
    private bool _closing;
    private bool _allowClose;

    public PracticePluginSettingsWindow(TranslationEngineSession translations, CancellationToken token)
    {
        _translations = translations;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        Title = "背诵练习 · 配置";
        Width = 560;
        Height = 380;
        MinWidth = 440;
        MinHeight = 330;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        // Let our Closing handler cancel and await work before closing owned dialogs.
        ClosingBehavior = WindowClosingBehavior.OwnerWindowOnly;
        var content = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
        Content = content;
        content.Children.Add(_translationButton);
        content.Children.Add(new TextBlock
        {
            Text = "管理翻译服务，用于练习前补全提示。",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(_answerModelButton);
        content.Children.Add(new TextBlock
        {
            Text = "配置用于生成同义答案候选的模型。生成后还需逐条确认保存；练习时不调用模型。",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(_status);
        var close = new Button { Content = "关闭", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 90 };
        content.Children.Add(close);
        close.Click += (_, _) => Close();
        _translationButton.Click += async (_, _) => await OpenAsync(false);
        _answerModelButton.Click += async (_, _) => await OpenAsync(true);
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _closing = true;
            _registration.Dispose();
            _cancellation.Cancel();
            _cancellation.Dispose();
        };
        _registration = token.Register(() => Dispatcher.UIThread.Post(() => Close()));
    }

    private async Task OpenAsync(bool model)
    {
        if (_closing || _operation is { IsCompleted: false }) return;
        _translationButton.IsEnabled = _answerModelButton.IsEnabled = false;
        _status.Text = string.Empty;
        try
        {
            Window window = model ? new AnswerModelSettingsWindow(_cancellation.Token)
                : new TranslationApiSettingsWindow(_translations, _cancellation.Token);
            _operation = window.ShowDialog(this);
            await _operation;
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!_closing) _status.Text = "无法打开配置窗口，请稍后重试。";
        }
        finally
        {
            if (!_closing) _translationButton.IsEnabled = _answerModelButton.IsEnabled = true;
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose || _operation is null || _operation.IsCompleted) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        _cancellation.Cancel();
        try { await _operation; }
        catch (Exception) { }
        _allowClose = true;
        Close();
    }
}
