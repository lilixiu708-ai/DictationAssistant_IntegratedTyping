// Sideloaded importer management, 2026-10-19. AGPL-3.0-or-later.
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DictationAssistant.App.Plugins;

namespace DictationAssistant.App;

public partial class PluginManagerWindow : Window
{
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
    }

    private static Border CreateEntryCard(PluginRuntime runtime, PluginEntry entry)
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
}
