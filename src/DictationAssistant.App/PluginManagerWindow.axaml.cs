// Sideloaded importer management, 2026-10-19. AGPL-3.0-or-later.
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using DictationAssistant.App.Plugins;

namespace DictationAssistant.App;

public partial class PluginManagerWindow : Window
{
    private readonly CancellationTokenSource _windowLifetime = new();
    private readonly HashSet<Task> _configurationTasks = [];
    private bool _isClosing;
    private bool _allowClose;

    public PluginManagerWindow(PluginRuntime runtime, Action openFolder)
    {
        var catalog = runtime.Catalog;
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(openFolder);
        InitializeComponent();

        PluginDirectoryTextBlock.Text = catalog.RootDirectory;
        var loaded = catalog.Entries.Count(entry => entry.IsLoaded);
        PluginCountTextBlock.Text = $"已加载 {loaded} 个插件 · 发现 {catalog.Entries.Count} 项";
        EmptyPluginsTextBlock.IsVisible = catalog.Entries.Count == 0;
        foreach (var entry in catalog.Entries)
            PluginEntriesPanel.Children.Add(CreateEntryCard(runtime, entry));

        OpenFolderButton.Click += (_, _) =>
        {
            try
            {
                openFolder();
                FolderErrorTextBlock.IsVisible = false;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PluginManagerWindow] Opening plugin folder failed: {ex}");
                FolderErrorTextBlock.Text = $"无法打开插件目录：{ex.Message}";
                FolderErrorTextBlock.IsVisible = true;
            }
        };
        CloseButton.Click += (_, _) => Close();
        Closing += PluginManagerWindow_Closing;
        Closed += (_, _) =>
        {
            CancelConfiguration();
            _windowLifetime.Dispose();
        };
    }

    private Border CreateEntryCard(PluginRuntime runtime, PluginEntry entry)
    {
        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(new TextBlock
        {
            Text = entry.DisplayName,
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        if (entry.Manifest is { } manifest)
        {
            content.Children.Add(new TextBlock
            {
                Text = $"版本 v{manifest.Version} · 接口 v{manifest.ApiVersion} · " +
                    $"功能级别 {manifest.MinimumHostFeatureLevel} · {string.Join("、", entry.Capabilities)}" +
                    (manifest.Extensions.Length > 0 ? $" · {string.Join("、", manifest.Extensions)}" : ""),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75
            });
            if (!string.IsNullOrWhiteSpace(manifest.Description))
            {
                content.Children.Add(new TextBlock
                {
                    Text = manifest.Description,
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }
        content.Children.Add(new TextBlock
        {
            Text = entry.StatusText,
            FontWeight = entry.IsLoaded ? FontWeight.Normal : FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        if (entry.Manifest is { } validManifest)
        {
            var enable = new CheckBox { Content = "下次启动时启用", IsChecked = entry.IsEnabled };
            var preferenceStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
            enable.IsCheckedChanged += (_, _) =>
            {
                try
                {
                    runtime.Catalog.SetEnabled(validManifest.Id, enable.IsChecked == true);
                    preferenceStatus.Text = "设置已保存，重启程序后生效。";
                }
                catch (Exception ex) { preferenceStatus.Text = $"设置保存失败：{ex.Message}"; }
            };
            content.Children.Add(enable);
            content.Children.Add(preferenceStatus);
        }
        if (entry.IsLoaded && entry.ConfigurationPlugin is not null)
        {
            var configure = new Button { Content = "配置…", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
            var configurationStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
            configure.Click += async (_, _) =>
            {
                if (_isClosing || !configure.IsEnabled) return;
                configure.IsEnabled = false;
                configurationStatus.Text = "正在打开插件配置…";
                var task = ConfigureEntryAsync(runtime, entry, configurationStatus);
                _configurationTasks.Add(task);
                try { await task; }
                finally
                {
                    _configurationTasks.Remove(task);
                    if (!_isClosing) configure.IsEnabled = true;
                }
            };
            content.Children.Add(configure);
            content.Children.Add(configurationStatus);
        }
        if (entry.LastError is { } lastError)
            content.Children.Add(new SelectableTextBlock { Text = $"最近错误：{lastError}", TextWrapping = TextWrapping.Wrap });
        var logs = runtime.Logs.Where(log => log.PluginId == entry.Manifest?.Id).TakeLast(10).ToArray();
        if (logs.Length > 0)
            content.Children.Add(new Expander
            {
                Header = "最近日志",
                Content = new SelectableTextBlock
                {
                    Text = string.Join(Environment.NewLine, logs.Select(log => $"{log.Timestamp:HH:mm:ss} {log.Level}: {log.Message}")),
                    TextWrapping = TextWrapping.Wrap, FontSize = 12
                }
            });
        content.Children.Add(new SelectableTextBlock
        {
            Text = $"目录：{entry.FolderPath}",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.75
        });
        return new Border
        {
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.Parse("#70808080")),
            Child = content
        };
    }

    private async Task ConfigureEntryAsync(PluginRuntime runtime, PluginEntry entry, TextBlock status)
    {
        try
        {
            await runtime.InvokeAsync(entry, "configure", async token =>
            {
                var callback = runtime.CaptureInvocation(async () =>
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        await entry.ConfigurationPlugin!.ConfigureAsync(this, token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception)
                    {
                        // Provider errors can contain credentials; keep runtime logs and UI generic.
                        throw new InvalidOperationException("插件配置失败，请检查本机设置后重试。");
                    }
                });
                // This overload returns the callback's task; retain the invocation until the dialog closes.
                var dialogTask = await Dispatcher.UIThread.InvokeAsync(callback, DispatcherPriority.Normal, token);
                await dialogTask;
            }, _windowLifetime.Token, waitForTurn: false);
            if (!_isClosing) status.Text = "配置窗口已关闭，设置按插件说明生效。";
        }
        catch (OperationCanceledException) when (_windowLifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!_isClosing) status.Text = "插件正忙或配置无法打开，请稍后重试。";
        }
    }

    private async void PluginManagerWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose || _configurationTasks.Count == 0) return;
        e.Cancel = true;
        if (_isClosing) return;
        _isClosing = true;
        CloseButton.IsEnabled = false;
        CloseButton.Content = "正在关闭…";
        CancelConfiguration();
        // Keep the dispatcher free while configuration dialogs observe cancellation and close.
        await Task.WhenAll(_configurationTasks.ToArray());
        _allowClose = true;
        Close();
    }

    private void CancelConfiguration()
    {
        try { _windowLifetime.Cancel(); }
        catch (Exception)
        {
            Trace.WriteLine("[PluginManagerWindow] A configuration cancellation callback failed.");
        }
    }
}
