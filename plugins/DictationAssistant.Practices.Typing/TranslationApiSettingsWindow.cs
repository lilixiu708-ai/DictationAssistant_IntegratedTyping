// Source distributed under the AGPL-3.0-or-later terms of DictationAssistant.
// Modified 2026-10-20: separate translation API binding and session-only settings.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DictationAssistant.Practices.Typing;

internal sealed class TranslationApiSettingsWindow : Window
{
    private readonly CancellationTokenSource _cancellation;
    private readonly CancellationTokenRegistration _externalCancellationRegistration;
    private readonly TextBox _apiKey = new()
    {
        PasswordChar = '●', MinHeight = 36, HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly ComboBox _account = new()
    {
        ItemsSource = new[] { "DeepL API Free", "DeepL API Pro" }, MinWidth = 180
    };
    private readonly Button _save = new() { Content = "保存并使用", MinWidth = 110 };
    private readonly Button _session = new() { Content = "仅本次使用", MinWidth = 110 };
    private readonly Button _delete = new() { Content = "删除本机绑定", MinWidth = 110 };
    private readonly Button _cancel = new() { Content = "取消", MinWidth = 80 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private TranslationApiConfiguration? _current;
    private Task? _operation;
    private bool _closing;
    private bool _allowClose;

    public bool BindingDeleted { get; private set; }

    public TranslationApiSettingsWindow(TranslationApiConfiguration? current, CancellationToken external)
    {
        _current = current;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(external);
        Title = "翻译 API 设置";
        Width = 680;
        Height = 540;
        MinWidth = 600;
        MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _account.SelectedIndex = current?.UseFreeApi == false ? 1 : 0;
        // Never populate the text control with an existing key. Blank means reuse the current binding.
        _apiKey.Watermark = current is null
            ? "输入 DeepL API Key"
            : "已配置密钥；留空继续使用，输入新密钥可替换";
        _save.IsEnabled = _delete.IsEnabled = TranslationApiSettings.CanPersist;

        var root = new Grid
        {
            Margin = new Thickness(20), RowDefinitions = new RowDefinitions("*,Auto,Auto"), RowSpacing = 14
        };
        Content = root;
        var form = new StackPanel { Spacing = 12 };
        form.Children.Add(new TextBlock
        {
            Text = "翻译服务：DeepL API", FontSize = 20, FontWeight = FontWeight.SemiBold
        });
        form.Children.Add(new TextBlock
        {
            Text = "选择账户对应的 API Free 或 API Pro。设置密钥不会发送翻译请求。",
            TextWrapping = TextWrapping.Wrap
        });
        form.Children.Add(_account);
        form.Children.Add(new TextBlock { Text = "API 密钥", FontWeight = FontWeight.SemiBold });
        form.Children.Add(_apiKey);
        form.Children.Add(new TextBlock
        {
            Text = "密钥仅以掩码显示；已有密钥不会回填到输入框。",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.8
        });

        if (TranslationApiSettings.CanPersist)
        {
            string machineCode;
            try { machineCode = TranslationApiSettings.GetMachineCode(); }
            catch (Exception) { machineCode = "暂时无法读取"; }
            form.Children.Add(new TextBlock
            {
                Text = "本机标识：" + machineCode, TextWrapping = TextWrapping.Wrap
            });
            form.Children.Add(new TextBlock
            {
                Text = "保存后使用 Windows 系统加密，并绑定本机与当前账户。\n" +
                    "仅保护本地保存，不防止同账户恶意程序读取。换机或更换账户后需要重新绑定。",
                TextWrapping = TextWrapping.Wrap, Opacity = 0.8
            });
        }
        else
        {
            form.Children.Add(new TextBlock
            {
                Text = "当前平台不支持 Windows 本机加密保存，请选择“仅本次使用”。关闭本次会话后需重新输入。",
                TextWrapping = TextWrapping.Wrap, Opacity = 0.8
            });
        }

        var scroll = new ScrollViewer
        {
            Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        root.Children.Add(scroll);
        Grid.SetRow(_status, 1);
        root.Children.Add(_status);
        var footer = new WrapPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            ItemSpacing = 8, LineSpacing = 8
        };
        footer.Children.Add(_delete);
        footer.Children.Add(_save);
        footer.Children.Add(_session);
        footer.Children.Add(_cancel);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        _save.Click += async (_, _) => await RunOperationAsync(SaveBindingAsync,
            "无法保存本机绑定，请检查用户数据目录权限，或选择“仅本次使用”。");
        _session.Click += (_, _) => UseForSession();
        _delete.Click += async (_, _) => await RunOperationAsync(DeleteBindingAsync,
            "无法删除本机绑定，请检查用户数据目录权限后重试。");
        _cancel.Click += (_, _) => Close();
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _closing = true;
            _externalCancellationRegistration.Dispose();
            _apiKey.Text = string.Empty;
            _current = null;
            _cancellation.Cancel();
            _cancellation.Dispose();
        };
        _externalCancellationRegistration = external.Register(() => Dispatcher.UIThread.Post(() => Close()));
    }

    private bool TryGetConfiguration(out TranslationApiConfiguration? configuration)
    {
        configuration = null;
        var typed = _apiKey.Text?.Trim();
        var key = string.IsNullOrEmpty(typed) ? _current?.ApiKey : typed;
        if (string.IsNullOrWhiteSpace(key))
        {
            ShowError("请先输入 DeepL API Key。");
            return false;
        }
        if (key.Any(c => c is <= ' ' or >= '\u007f'))
        {
            ShowError("密钥格式无效，请检查是否包含空格、换行或非英文字符。");
            return false;
        }
        configuration = new TranslationApiConfiguration(_account.SelectedIndex == 0, key);
        return true;
    }

    private async Task SaveBindingAsync()
    {
        if (!TranslationApiSettings.CanPersist || !TryGetConfiguration(out var configuration)) return;
        _status.Text = "正在加密保存本机绑定…";
        await TranslationApiSettings.SaveAsync(configuration!.ApiKey, configuration.UseFreeApi, _cancellation.Token);
        if (_closing) return;
        _allowClose = true;
        Close(configuration);
    }

    private void UseForSession()
    {
        if (_closing || _operation is { IsCompleted: false } || !TryGetConfiguration(out var configuration)) return;
        _allowClose = true;
        Close(configuration);
    }

    private async Task DeleteBindingAsync()
    {
        if (!TranslationApiSettings.CanPersist) return;
        _status.Text = "正在删除本机绑定…";
        await TranslationApiSettings.DeleteAsync(_cancellation.Token);
        BindingDeleted = true;
        _current = null;
        _apiKey.Text = string.Empty;
        if (_closing) return;
        _allowClose = true;
        Close();
    }

    private async Task RunOperationAsync(Func<Task> action, string failureMessage)
    {
        if (_closing || _operation is { IsCompleted: false }) return;
        SetBusy(true);
        _status.Foreground = Brushes.DimGray;
        try
        {
            _operation = action();
            await _operation;
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            // Provider/key/OS exception details must not leak into the settings UI or logs.
            if (!_closing) ShowError(failureMessage);
        }
        finally
        {
            if (!_closing) SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _apiKey.IsEnabled = _account.IsEnabled = _session.IsEnabled = !busy;
        _save.IsEnabled = _delete.IsEnabled = !busy && TranslationApiSettings.CanPersist;
        _cancel.Content = busy ? "取消并关闭" : "取消";
    }

    private void ShowError(string message)
    {
        _status.Foreground = Brushes.IndianRed;
        _status.Text = message;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose || _operation is null || _operation.IsCompleted) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        _cancellation.Cancel();
        try { await _operation; }
        catch (Exception) { /* The close path must finish even when persistence failed. */ }
        _allowClose = true;
        Close();
    }
}
