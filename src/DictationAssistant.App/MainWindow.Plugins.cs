// Plugin lifecycle and menus, modified 2026-10-20. AGPL-3.0-or-later.
using System.Diagnostics;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using DictationAssistant.App.Plugins;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace DictationAssistant.App;

public partial class MainWindow
{
    private readonly PluginCatalog _pluginCatalog = new(Path.Combine(AppContext.BaseDirectory, "Plugins"));
    private readonly CancellationTokenSource _windowLifetime = new();
    private DispatcherTimer? _pluginWordListTimer;
    private long _wordListRevision;
    private bool _isClosing;
    private bool _canClose;
    public PluginRuntime PluginRuntime { get; private set; } = null!;

    private void InitializePlugins()
    {
        PluginRuntime = new PluginRuntime(_pluginCatalog,
            entry => new WindowPluginContext(this, entry), _windowLifetime.Token);
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.I, KeyModifiers.Control | KeyModifiers.Shift),
            Command = new AsyncRelayCommand(() => ChooseAndOpenWordListAsync(documentOnly: true))
        });
        Opened += async (_, _) =>
        {
            WordlistEditor.Document.Changed += (_, _) =>
            {
                _wordListRevision++;
                if (_isClosing || !_pluginCatalog.Entries.Any(entry =>
                        entry.IsInitialized && entry.WordListObserver is not null)) return;
                _pluginWordListTimer ??= CreatePluginWordListTimer();
                _pluginWordListTimer.Stop();
                _pluginWordListTimer.Start();
            };
            try
            {
                await EnsurePluginsLoadedAsync();
                RebuildPluginMenus();
                if (!_isClosing) NotifyPluginsOfWordList();
            }
            catch (OperationCanceledException) when (_windowLifetime.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Plugins] Initialization failed: {ex}");
                if (!_isClosing) await ShowFileErrorAsync($"插件初始化失败：{ex.Message}");
            }
        };
        Closing += MainWindow_Closing;
    }

    private Task EnsurePluginsLoadedAsync() => PluginRuntime.InitializeAsync(_windowLifetime.Token);

    private DispatcherTimer CreatePluginWordListTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) => { timer.Stop(); NotifyPluginsOfWordList(); };
        return timer;
    }

    private void NotifyPluginsOfWordList()
    {
        if (_isClosing || !_pluginCatalog.Entries.Any(entry => entry.IsInitialized && entry.WordListObserver is not null)) return;
        PluginRuntime.NotifyWordListChanged(GetPluginWordListSnapshot());
    }

    private DictationAssistant.Plugin.Abstractions.PluginWordListSnapshot GetPluginWordListSnapshot() =>
        new(WordlistEditor.Text, WordlistEditor.SelectedText, _wordListRevision);

    private void RebuildPluginMenus()
    {
        PluginCommandsMenu.Items.Clear();
        PluginPracticesMenu.Items.Clear();
        foreach (var entry in _pluginCatalog.Entries.Where(entry => entry.IsLoaded && entry.IsInitialized))
        {
            foreach (var command in entry.Commands)
            {
                var item = new MenuItem { Header = $"{entry.DisplayName} · {command.Name}" };
                ToolTip.SetTip(item, command.Description);
                item.Click += async (_, _) => await RunPluginCommandAsync(entry, command.Id);
                PluginCommandsMenu.Items.Add(item);
            }
            if (entry.PracticePlugin is not null)
            {
                var item = new MenuItem { Header = entry.DisplayName };
                item.Click += async (_, _) => await StartPracticeAsync(entry.Manifest!.Id);
                PluginPracticesMenu.Items.Add(item);
            }
        }
        PluginCommandsMenu.IsEnabled = PluginCommandsMenu.Items.Count > 0;
        PluginPracticesMenu.IsEnabled = PluginPracticesMenu.Items.Count > 0;
    }

    private async Task RunPluginCommandAsync(PluginEntry entry, string commandId)
    {
        if (_isClosing || _isPracticing) return;
        try
        {
            await PluginRuntime.InvokeAsync(entry, "command:" + commandId,
                token => entry.CommandPlugin!.ExecuteAsync(commandId, PluginRuntime.GetContext(entry), token),
                _windowLifetime.Token);
        }
        catch (OperationCanceledException) when (_windowLifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Plugins] Command failed: {ex}");
            if (!_isClosing) await ShowFileErrorAsync($"插件 {entry.DisplayName} 执行失败：{ex.Message}");
        }
    }

    private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_canClose) return;
        e.Cancel = true;
        if (_isClosing) return;
        _isClosing = true;
        IsEnabled = false;
        _pluginWordListTimer?.Stop();
        try
        {
            try { _windowLifetime.Cancel(); }
            catch (Exception ex) { Trace.WriteLine($"[Shutdown] Cancellation failed: {ex}"); }
            try
            {
                if (GetViewModel() is { } vm) await vm.ShutdownAsync();
            }
            finally { await PluginRuntime.ShutdownAsync(); }
        }
        catch (Exception ex) { Trace.WriteLine($"[Shutdown] Cleanup failed: {ex}"); }
        finally
        {
            _canClose = true;
            _windowLifetime.Dispose();
            Close();
        }
    }

    private async void PluginManager_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            await EnsurePluginsLoadedAsync();
            await new PluginManagerWindow(PluginRuntime, OpenPluginFolder).ShowDialog(this);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Plugins] Manager failed: {ex}");
            if (!_isClosing) await ShowFileErrorAsync($"无法打开插件管理：{ex.Message}");
        }
    }

    private void OpenPluginFolder()
    {
        Directory.CreateDirectory(_pluginCatalog.RootDirectory);
        Process.Start(new ProcessStartInfo(_pluginCatalog.RootDirectory) { UseShellExecute = true });
    }

    private async void OpenPluginFolder_Click(object? sender, RoutedEventArgs e)
    {
        try { OpenPluginFolder(); }
        catch (Exception ex) { await ShowFileErrorAsync($"无法打开插件目录：{ex.Message}"); }
    }

    private async Task ShowFileErrorAsync(string message)
    {
        if (_isClosing) return;
        var box = MessageBoxManager.GetMessageBoxStandard("自动默写", message, ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
        await box.ShowWindowDialogAsync(this);
    }
}
