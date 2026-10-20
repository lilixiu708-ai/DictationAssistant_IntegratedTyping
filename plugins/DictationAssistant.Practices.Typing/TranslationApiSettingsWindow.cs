// Source distributed under the AGPL-3.0-or-later terms of DictationAssistant.
// Modified 2026-10-20: display binding drafts while shared model rules handle edits and validation.
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
    private readonly TranslationEngineDraft _draft = new();
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
            Text = "在这里添加和修改翻译引擎，将常用的设为首选。\n" +
                "同一服务可以添加多个配置，翻译前也可以临时切换引擎。",
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
                    (binding.Id == _draft.PreferredEngineId ? " · 首选" : string.Empty) + "\n" +
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
            Text = "已保存的凭据不会显示。修改同一服务时，留空即可沿用；更换服务后需要重新填写。\n" +
                "请填写服务商开发者平台提供的 API 凭据。",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.8
        });
        var formActions = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        formActions.Children.Add(_apply);
        formActions.Children.Add(_discard);
        form.Children.Add(formActions);
        form.Children.Add(new TextBlock
        {
            Text = "修改完成后，点击“保存配置”或“仅本次使用”。点击“取消”会放弃本页修改。",
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
                    "配置由 Windows 加密，并绑定这台电脑和当前账户。若账户或程序已被控制，密钥仍可能被读取。",
                TextWrapping = TextWrapping.Wrap, Opacity = 0.8
            });
        }
        else
        {
            form.Children.Add(new TextBlock
            {
                Text = "当前版本在此系统上无法保存配置，请选择“仅本次使用”。这些修改不会保留到下次启动。",
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
            _draft.Reset();
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
            _draft.Reset();
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
            _draft.Load(configuration);
            _loaded = true;
            var selected = _draft.Bindings.FirstOrDefault(binding => binding.Id == _draft.PreferredEngineId)
                ?? _draft.Bindings.FirstOrDefault();
            ShowBinding(selected);
            RefreshList(selected?.Id);
            _status.Text = _draft.Bindings.Count == 0 ? "还没有添加翻译引擎，点击“新建绑定”添加。" : "选择左侧配置即可修改；下次打开翻译页时会默认选中首选引擎。";
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
        var hasRegion = definition.SupportsRegion;
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
        _engines.ItemsSource = _draft.Bindings.ToArray();
        _engines.SelectedItem = _draft.Bindings.FirstOrDefault(binding => binding.Id == selectedId);
        _preferred.Text = _draft.Bindings.FirstOrDefault(binding => binding.Id == _draft.PreferredEngineId) is { } preferred
            ? "首选引擎：" + preferred.Name : "首选引擎：未设置";
        _populating = false;
        UpdateControls();
    }

    private bool ApplyForm()
    {
        TranslationEngineBinding binding;
        try
        {
            binding = _draft.Apply(_editing, _name.Text, SelectedProvider.Id,
                _apiKey.Text, _secretKey.Text, _region.Text);
        }
        catch (InvalidDataException error) { return FormError(error.Message); }
        ShowBinding(binding);
        RefreshList(binding.Id);
        _status.Foreground = Brushes.DimGray;
        _status.Text = "已更新配置列表。点击“保存配置”或“仅本次使用”后生效。";
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
        var selected = _draft.Remove(_editing.Id);
        ShowBinding(selected);
        RefreshList(selected?.Id);
        _status.Foreground = Brushes.DimGray;
        _status.Text = "已移除绑定。点击“保存配置”或“仅本次使用”后生效。";
    }

    private void MakePreferred()
    {
        if (!CanLeaveForm()) return;
        if (_editing is null) { ShowError("请选择已绑定引擎后再设置首选。"); return; }
        _draft.MakePreferred(_editing.Id);
        RefreshList(_editing.Id);
        _status.Foreground = Brushes.DimGray;
        _status.Text = "已选择首选引擎。点击“保存配置”或“仅本次使用”后生效。";
    }

    private async Task CommitAsync(bool persist)
    {
        if (persist && !TranslationApiSettings.CanPersist) return;
        MarkDirty();
        if (_dirty && !ApplyForm()) return;
        var configuration = _draft.Snapshot();
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
