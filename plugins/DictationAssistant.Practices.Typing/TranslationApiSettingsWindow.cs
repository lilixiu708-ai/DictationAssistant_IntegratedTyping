// Source distributed under the AGPL-3.0-or-later terms of DictationAssistant.
// Modified 2026-10-20: manage named translation bindings and the preferred engine.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DictationAssistant.Practices.Typing;

internal sealed class TranslationApiSettingsWindow : Window
{
    private readonly TranslationEngineSession _session;
    private readonly CancellationTokenSource _cancellation;
    private readonly CancellationTokenRegistration _externalCancellationRegistration;
    private readonly List<TranslationEngineBinding> _bindings = [];
    private readonly ListBox _engines = new() { MinHeight = 150 };
    private readonly TextBlock _preferred = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _formTitle = new() { FontSize = 18, FontWeight = FontWeight.SemiBold };
    private readonly TextBox _name = new() { Watermark = "例如：常用翻译、备用账户", MaxLength = 80 };
    private readonly TextBlock _primaryLabel = new();
    private readonly TextBox _apiKey = new()
    {
        PasswordChar = '●', MinHeight = 36, MaxLength = 512,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly TextBlock _secretLabel = new();
    private readonly TextBox _secretKey = new()
    {
        PasswordChar = '●', MinHeight = 36, MaxLength = 512,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly TextBlock _regionLabel = new();
    private readonly TextBox _region = new() { MaxLength = 64 };
    private readonly TextBlock _providerHelp = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private readonly ComboBox _provider = new()
    {
        ItemsSource = TranslationEngineProviders.Definitions, MinWidth = 180,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly Button _new = new() { Content = "新建绑定" };
    private readonly Button _remove = new() { Content = "移除绑定" };
    private readonly Button _makePreferred = new() { Content = "设为首选" };
    private readonly Button _apply = new() { Content = "添加绑定" };
    private readonly Button _discard = new() { Content = "放弃修改" };
    private readonly Button _save = new() { Content = "保存配置", MinWidth = 100 };
    private readonly Button _useSession = new() { Content = "仅本次使用", MinWidth = 110 };
    private readonly Button _cancel = new() { Content = "取消", MinWidth = 80, IsCancel = true };
    private readonly Button _resetUnreadable = new()
    {
        Content = "重新绑定（替换无法读取的本机配置）", IsVisible = false
    };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private TranslationEngineBinding? _editing;
    private string? _displayedProviderId;
    private string? _preferredId;
    private Task? _operation;
    private bool _loaded;
    private bool _busy;
    private bool _populating;
    private bool _dirty;
    private bool _closing;
    private bool _allowClose;

    public TranslationApiSettingsWindow(TranslationEngineSession session, CancellationToken external)
    {
        _session = session;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(external);
        Title = "翻译引擎配置";
        Width = 900;
        Height = 710;
        MinWidth = 760;
        MinHeight = 590;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid
        {
            Margin = new Thickness(20), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"),
            RowSpacing = 14
        };
        Content = root;
        root.Children.Add(new TextBlock
        {
            Text = "管理已绑定的翻译引擎，并设置首选项。翻译准备页仍可为本次任务切换引擎。\n" +
                "已支持多种常用翻译服务，可为同一服务添加多个绑定；配置不会发送翻译请求。",
            TextWrapping = TextWrapping.Wrap
        });
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("260,*"), ColumnSpacing = 18 };
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        var list = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 10 };
        list.Children.Add(_preferred);
        _engines.ItemTemplate = new FuncDataTemplate<TranslationEngineBinding>((binding, _) =>
            new TextBlock
            {
                Text = binding is null ? string.Empty : binding.Name +
                    (binding.Id == _preferredId ? " · 首选" : string.Empty) + "\n" +
                    TranslationEngineProviders.DisplayName(binding.Provider),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6)
            });
        Grid.SetRow(_engines, 1);
        list.Children.Add(_engines);
        var listActions = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        listActions.Children.Add(_new);
        listActions.Children.Add(_remove);
        listActions.Children.Add(_makePreferred);
        Grid.SetRow(listActions, 2);
        list.Children.Add(listActions);
        body.Children.Add(list);

        var form = new StackPanel { Spacing = 10 };
        form.Children.Add(_formTitle);
        form.Children.Add(new TextBlock { Text = "绑定名称" });
        form.Children.Add(_name);
        form.Children.Add(new TextBlock { Text = "服务类型" });
        _provider.ItemTemplate = new FuncDataTemplate<TranslationEngineProviderDefinition>((definition, _) =>
            new TextBlock { Text = definition?.Name ?? string.Empty, TextWrapping = TextWrapping.Wrap });
        form.Children.Add(_provider);
        form.Children.Add(_providerHelp);
        form.Children.Add(_primaryLabel);
        form.Children.Add(_apiKey);
        form.Children.Add(_secretLabel);
        form.Children.Add(_secretKey);
        form.Children.Add(_regionLabel);
        form.Children.Add(_region);
        form.Children.Add(new TextBlock
        {
            Text = "已有凭据不会回填。编辑时留空可沿用同一服务的凭据；切换服务会清空待填凭据，需重新输入。" +
                "请使用官方开发者平台的 API 凭据，翻译网页的账户或网址不能直接绑定。",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.8
        });
        var formActions = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        formActions.Children.Add(_apply);
        formActions.Children.Add(_discard);
        form.Children.Add(formActions);
        form.Children.Add(new TextBlock
        {
            Text = "添加、修改、移除和首选项调整在下方保存或应用后才生效。取消会保留原配置。",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.8
        });
        if (TranslationApiSettings.CanPersist)
        {
            string code;
            try { code = TranslationApiSettings.GetMachineCode(); }
            catch (Exception) { code = "暂时无法读取"; }
            form.Children.Add(new TextBlock
            {
                Text = "本机标识：" + code + "\n" +
                    "配置通过 Windows 系统加密，绑定本机与当前账户。不能防止已控制当前账户或进程的软件读取。",
                TextWrapping = TextWrapping.Wrap, Opacity = 0.8
            });
        }
        else
        {
            form.Children.Add(new TextBlock
            {
                Text = "当前平台不支持 Windows 本机加密保存，可选择“仅本次使用”；配置保留至程序退出。",
                TextWrapping = TextWrapping.Wrap, Opacity = 0.8
            });
        }
        var scroll = new ScrollViewer
        {
            Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetColumn(scroll, 1);
        body.Children.Add(scroll);

        var feedback = new StackPanel { Spacing = 8 };
        feedback.Children.Add(_status);
        feedback.Children.Add(_resetUnreadable);
        Grid.SetRow(feedback, 2);
        root.Children.Add(feedback);
        var footer = new WrapPanel
        {
            ItemSpacing = 8, LineSpacing = 8, HorizontalAlignment = HorizontalAlignment.Right
        };
        footer.Children.Add(_save);
        footer.Children.Add(_useSession);
        footer.Children.Add(_cancel);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        _engines.SelectionChanged += OnEngineSelected;
        _name.TextChanged += (_, _) => MarkDirty();
        _apiKey.TextChanged += (_, _) => MarkDirty();
        _secretKey.TextChanged += (_, _) => MarkDirty();
        _region.TextChanged += (_, _) => MarkDirty();
        _provider.SelectionChanged += (_, _) => OnProviderSelected();
        _new.Click += (_, _) =>
        {
            if (!CanLeaveForm()) return;
            ShowBinding(null);
            RefreshList(null);
        };
        _apply.Click += (_, _) => ApplyForm();
        _discard.Click += (_, _) => ShowBinding(_editing);
        _remove.Click += (_, _) => RemoveBinding();
        _makePreferred.Click += (_, _) => MakePreferred();
        _save.Click += async (_, _) => await RunOperationAsync(() => CommitAsync(persist: true),
            "无法保存引擎配置，请检查本机用户配置，或选择“仅本次使用”。");
        _useSession.Click += async (_, _) => await RunOperationAsync(() => CommitAsync(persist: false),
            "无法应用引擎配置，请检查绑定内容后重试。");
        _resetUnreadable.Click += (_, _) =>
        {
            _bindings.Clear();
            _preferredId = null;
            _loaded = true;
            _resetUnreadable.IsVisible = false;
            ShowBinding(null);
            RefreshList(null);
            ShowError("已开始重新绑定；保存时才替换原设置。取消可保留原文件。");
            UpdateControls();
        };
        _cancel.Click += (_, _) => Close();
        Opened += async (_, _) => await RunOperationAsync(LoadAsync, "无法读取引擎配置。");
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _closing = true;
            _externalCancellationRegistration.Dispose();
            _apiKey.Text = string.Empty;
            _secretKey.Text = string.Empty;
            _region.Text = string.Empty;
            _name.Text = string.Empty;
            _editing = null;
            _bindings.Clear();
            _engines.ItemsSource = null;
            _cancellation.Cancel();
            _cancellation.Dispose();
        };
        _externalCancellationRegistration = external.Register(() => Dispatcher.UIThread.Post(() => Close()));
        ShowBinding(null);
        UpdateControls();
    }

    private async Task LoadAsync()
    {
        try
        {
            _status.Text = "正在读取引擎配置…";
            var configuration = await _session.GetAsync(_cancellation.Token);
            _cancellation.Token.ThrowIfCancellationRequested();
            _bindings.AddRange(configuration.Bindings);
            _preferredId = configuration.PreferredEngineId;
            _loaded = true;
            var selected = _bindings.FirstOrDefault(binding => binding.Id == _preferredId)
                ?? _bindings.FirstOrDefault();
            ShowBinding(selected);
            RefreshList(selected?.Id);
            _status.Text = _bindings.Count == 0 ? "尚未绑定引擎，可新建绑定。" : "选择绑定可修改；首选引擎将用于新打开的翻译准备页。";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            _resetUnreadable.IsVisible = true;
            ShowError("无法读取已有引擎配置，原文件未被改动。可取消后检查本机设置，或选择重新绑定。");
        }
    }

    private void OnEngineSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_populating) return;
        if (_engines.SelectedItem is not TranslationEngineBinding selected) return;
        if (selected.Id == _editing?.Id) return;
        MarkDirty();
        if (_dirty)
        {
            RefreshList(_editing?.Id);
            ShowError("请先应用当前修改，或点击“放弃修改”后再切换。");
            return;
        }
        ShowBinding(selected);
    }

    private void MarkDirty()
    {
        if (_populating || !_loaded || _closing) return;
        // TextChanged can arrive after a programmatic editor refresh has completed.
        _dirty = IsFormDirty();
        UpdateControls();
    }

    private bool IsFormDirty() =>
        !string.Equals(_name.Text ?? string.Empty, _editing?.Name ?? string.Empty, StringComparison.Ordinal)
        || SelectedProvider.Id != (_editing?.Provider ?? TranslationEngineProviders.Definitions[0].Id)
        || !string.IsNullOrEmpty(_apiKey.Text)
        || !string.IsNullOrEmpty(_secretKey.Text)
        || !string.Equals(_region.Text ?? string.Empty, _editing?.Region ?? string.Empty, StringComparison.Ordinal);

    private TranslationEngineProviderDefinition SelectedProvider =>
        _provider.SelectedItem as TranslationEngineProviderDefinition ?? TranslationEngineProviders.Definitions[0];

    private void OnProviderSelected()
    {
        if (_populating || SelectedProvider.Id == _displayedProviderId) return;
        _populating = true;
        _apiKey.Text = string.Empty;
        _secretKey.Text = string.Empty;
        _region.Text = SelectedProvider.DefaultRegion ?? string.Empty;
        RefreshProviderFields();
        _populating = false;
        MarkDirty();
    }

    private void RefreshProviderFields()
    {
        var definition = SelectedProvider;
        _displayedProviderId = definition.Id;
        var reuse = _editing?.Provider == definition.Id;
        _primaryLabel.Text = definition.PrimaryLabel;
        _apiKey.Watermark = reuse ? "已配置；留空沿用" : "输入" + definition.PrimaryLabel;
        _secretLabel.Text = definition.SecretLabel;
        _secretLabel.IsVisible = _secretKey.IsVisible = definition.SecretLabel is not null;
        _secretKey.Watermark = reuse ? "已配置；留空沿用" : "输入" + definition.SecretLabel;
        var hasRegion = definition.RequiresRegion || definition.Id == "microsoft-translator";
        _regionLabel.IsVisible = _region.IsVisible = hasRegion;
        _regionLabel.Text = definition.RequiresRegion ? "服务区域（必填）" : "服务区域（可选）";
        _region.Watermark = definition.DefaultRegion ?? "按订阅资源填写，如 eastasia";
        var apiHint = definition.Id switch
        {
            "microsoft-translator" => " 必应／微软翻译需绑定官方 Azure Translator API。",
            "google-cloud" => " 此处使用 Google Cloud API，不是 Google 翻译网页。",
            _ => string.Empty
        };
        _providerHelp.Text = (definition.HelpText ?? "填写该服务官方开发者平台提供的 API 凭据。") + apiHint;
    }

    private bool CanLeaveForm()
    {
        MarkDirty();
        if (!_dirty) return true;
        ShowError("请先应用当前修改，或点击“放弃修改”。");
        return false;
    }

    private void ShowBinding(TranslationEngineBinding? binding)
    {
        _populating = true;
        _editing = binding;
        _name.Text = binding?.Name ?? string.Empty;
        _provider.SelectedItem = binding is null ? TranslationEngineProviders.Definitions[0]
            : TranslationEngineProviders.Get(binding.Provider);
        _apiKey.Text = string.Empty;
        _secretKey.Text = string.Empty;
        _region.Text = binding?.Region ?? string.Empty;
        RefreshProviderFields();
        _formTitle.Text = binding is null ? "新建绑定" : "修改已绑定引擎";
        _apply.Content = binding is null ? "添加绑定" : "应用修改";
        _dirty = IsFormDirty();
        _populating = false;
        UpdateControls();
    }

    private void RefreshList(string? selectedId)
    {
        _populating = true;
        _engines.ItemsSource = _bindings.ToArray();
        _engines.SelectedItem = _bindings.FirstOrDefault(binding => binding.Id == selectedId);
        _preferred.Text = _bindings.FirstOrDefault(binding => binding.Id == _preferredId) is { } preferred
            ? "首选引擎：" + preferred.Name : "首选引擎：未设置";
        _populating = false;
        UpdateControls();
    }

    private bool ApplyForm()
    {
        var name = _name.Text?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl))
            return FormError("请输入 1–80 个字符的绑定名称。");
        if (_bindings.Any(binding => binding.Id != _editing?.Id
            && string.Equals(binding.Name, name, StringComparison.OrdinalIgnoreCase)))
            return FormError("绑定名称已存在，请使用其他名称。");
        var definition = SelectedProvider;
        var provider = definition.Id;
        var reuse = _editing?.Provider == provider;
        var typedKey = _apiKey.Text?.Trim();
        var key = string.IsNullOrEmpty(typedKey) && reuse ? _editing?.ApiKey : typedKey;
        if (string.IsNullOrEmpty(key) || key.Length > 512 || key.Any(c => c is <= ' ' or >= '\u007f'))
            return FormError("请输入有效的" + definition.PrimaryLabel + "，不要包含空格、换行或非英文字符。");
        string? secret = null;
        if (definition.SecretLabel is not null)
        {
            var typedSecret = _secretKey.Text?.Trim();
            secret = string.IsNullOrEmpty(typedSecret) && reuse ? _editing?.SecretKey : typedSecret;
            if (string.IsNullOrEmpty(secret) || secret.Length > 512 || secret.Any(c => c is <= ' ' or >= '\u007f'))
                return FormError("请输入有效的" + definition.SecretLabel + "，不要包含空格、换行或非英文字符。");
        }
        var region = _region.IsVisible ? _region.Text?.Trim() : null;
        if (string.IsNullOrEmpty(region)) region = null;
        if (definition.RequiresRegion && region is null)
            return FormError("请填写服务区域。");
        if (region is not null && (region.Length > 64
            || region.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-')))
            return FormError("服务区域须为 1–64 个小写英文字母、数字或连字符。");
        if (_editing is null && _bindings.Count >= 20)
            return FormError("最多可保存 20 个绑定，请先移除不再使用的绑定。");

        var binding = new TranslationEngineBinding(_editing?.Id ?? Guid.NewGuid().ToString("N"),
            name, provider, key, secret, region);
        try { TranslationEngineProviders.Validate(binding); }
        catch (ArgumentException) { return FormError("绑定内容无效，请检查服务类型和所需凭据。"); }
        var index = _bindings.FindIndex(existing => existing.Id == binding.Id);
        if (index < 0) _bindings.Add(binding);
        else _bindings[index] = binding;
        _preferredId ??= binding.Id;
        ShowBinding(binding);
        RefreshList(binding.Id);
        _status.Foreground = Brushes.DimGray;
        _status.Text = "绑定已加入本页草稿，请在下方保存配置或仅本次使用。";
        return true;
    }

    private bool FormError(string message)
    {
        ShowError(message);
        return false;
    }

    private void RemoveBinding()
    {
        if (!CanLeaveForm()) return;
        if (_editing is null) { ShowError("请选择要移除的已绑定引擎。"); return; }
        var id = _editing.Id;
        var index = _bindings.FindIndex(binding => binding.Id == id);
        if (index < 0) return;
        _bindings.RemoveAt(index);
        if (_preferredId == id) _preferredId = _bindings.FirstOrDefault()?.Id;
        var selected = _bindings.Count > 0 ? _bindings[Math.Min(index, _bindings.Count - 1)] : null;
        ShowBinding(selected);
        RefreshList(selected?.Id);
        _status.Foreground = Brushes.DimGray;
        _status.Text = "已从本页草稿移除绑定，保存或应用后生效。";
    }

    private void MakePreferred()
    {
        if (!CanLeaveForm()) return;
        if (_editing is null) { ShowError("请选择已绑定引擎后再设置首选。"); return; }
        _preferredId = _editing.Id;
        RefreshList(_editing.Id);
        _status.Foreground = Brushes.DimGray;
        _status.Text = "首选项已调整，保存或应用后用于新的翻译准备页。";
    }

    private async Task CommitAsync(bool persist)
    {
        if (persist && !TranslationApiSettings.CanPersist) return;
        MarkDirty();
        if (_dirty && !ApplyForm()) return;
        var configuration = new TranslationEngineConfiguration(_bindings.ToArray(), _preferredId);
        _status.Text = persist ? "正在加密保存引擎配置…" : "正在应用本次引擎配置…";
        await _session.SetAsync(configuration, persist, _cancellation.Token);
        if (_closing) return;
        _allowClose = true;
        Close();
    }

    private async Task RunOperationAsync(Func<Task> action, string failureMessage)
    {
        if (_closing || _operation is { IsCompleted: false }) return;
        _busy = true;
        _status.Foreground = Brushes.DimGray;
        UpdateControls();
        try
        {
            _operation = action();
            await _operation;
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!_closing) ShowError(failureMessage);
        }
        finally
        {
            if (!_closing)
            {
                _busy = false;
                UpdateControls();
            }
        }
    }

    private void UpdateControls()
    {
        var enabled = _loaded && !_busy && !_closing;
        _engines.IsEnabled = _new.IsEnabled = _name.IsEnabled = _apiKey.IsEnabled = _secretKey.IsEnabled =
            _region.IsEnabled = _provider.IsEnabled = enabled;
        _apply.IsEnabled = enabled;
        _discard.IsEnabled = enabled && _dirty;
        _remove.IsEnabled = _makePreferred.IsEnabled = enabled && _editing is not null;
        _save.IsEnabled = enabled && TranslationApiSettings.CanPersist;
        _useSession.IsEnabled = enabled;
        _resetUnreadable.IsEnabled = !_busy && !_closing;
        _cancel.Content = _busy ? "取消并关闭" : "取消";
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
        catch (Exception) { /* Closing must finish without exposing underlying credential errors. */ }
        _allowClose = true;
        Close();
    }
}
