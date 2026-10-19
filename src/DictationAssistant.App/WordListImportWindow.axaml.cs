// Document importer preview, 2026-10-19. AGPL-3.0-or-later.
using System.Diagnostics;
using Avalonia.Controls;
using DictationAssistant.App.Importing;
using DictationAssistant.App.Plugins;
using DictationAssistant.Plugin.Abstractions;

namespace DictationAssistant.App;

public partial class WordListImportWindow : Window
{
    private readonly Func<PluginEntry, CancellationToken, Task<ImportDocument>> _readAsync;
    private CancellationTokenSource? _readCancellation;
    private ImportDocument? _document;
    private ImportPreviewResult? _lastPreview;
    private int _requestVersion;
    private bool _opened;
    private bool _closed;
    private bool _busy;
    private bool _updatingChoices;

    public WordListImportWindow(
        string fileName,
        IReadOnlyList<PluginEntry> importers,
        Func<PluginEntry, CancellationToken, Task<ImportDocument>> readAsync)
    {
        ArgumentNullException.ThrowIfNull(importers);
        ArgumentNullException.ThrowIfNull(readAsync);
        _readAsync = readAsync;
        InitializeComponent();

        FileNameTextBlock.Text = Path.GetFileName(fileName);
        var choices = importers.Where(entry => entry.IsLoaded)
            .Select(entry => new PluginChoice(entry)).ToArray();
        PluginComboBox.ItemsSource = choices;
        PluginComboBox.SelectedIndex = choices.Length > 0 ? 0 : -1;
        PluginComboBox.IsEnabled = choices.Length > 1;

        Opened += async (_, _) =>
        {
            if (_opened)
                return;
            _opened = true;
            await ReadSelectedPluginAsync();
        };
        Closed += (_, _) =>
        {
            _closed = true;
            CancelActiveRead();
        };
        PluginComboBox.SelectionChanged += async (_, _) =>
        {
            if (_opened && !_updatingChoices)
                await ReadSelectedPluginAsync();
        };
        ReadAgainButton.Click += async (_, _) => await ReadSelectedPluginAsync();
        SectionComboBox.SelectionChanged += (_, _) =>
        {
            if (_updatingChoices || _document is null)
                return;
            PopulateColumns();
            RebuildPreview();
        };
        ColumnComboBox.SelectionChanged += (_, _) => RebuildPreview();
        SkipHeadersCheckBox.IsCheckedChanged += (_, _) => RebuildPreview();
        DeduplicateCheckBox.IsCheckedChanged += (_, _) => RebuildPreview();
        StripNumberingCheckBox.IsCheckedChanged += (_, _) => RebuildPreview();
        PreviewTextBox.TextChanged += (_, _) => UpdatePreviewSummary();
        CancelButton.Click += (_, _) => Close();
        AppendButton.Click += (_, _) => AcceptSelection(append: true);
        ReplaceButton.Click += (_, _) => AcceptSelection(append: false);
        UpdateControlState();
    }

    private async Task ReadSelectedPluginAsync()
    {
        if (_closed)
            return;

        CancelActiveRead();
        _document = null;
        _lastPreview = null;
        PreviewTextBox.Text = string.Empty;
        WarningsPanel.IsVisible = false;
        _updatingChoices = true;
        SectionComboBox.ItemsSource = null;
        ColumnComboBox.ItemsSource = null;
        _updatingChoices = false;

        if (PluginComboBox.SelectedItem is not PluginChoice choice)
        {
            _busy = false;
            ShowReadStatus("没有可用的提取插件。请在插件管理中检查插件是否已成功加载。");
            UpdateControlState();
            return;
        }

        var requestVersion = _requestVersion;
        using var cancellation = new CancellationTokenSource();
        _readCancellation = cancellation;
        _busy = true;
        ShowReadStatus("正在读取文档…");
        UpdateControlState();
        try
        {
            // The host callback opens and owns the stream, and invokes the
            // importer away from the UI thread. Each request has its own token.
            var document = await _readAsync(choice.Entry, cancellation.Token);
            if (!IsCurrentRequest(requestVersion, cancellation))
                return;

            _document = document;
            _busy = false;
            if (document.Sections.Count == 0)
            {
                ShowReadStatus("插件未在文档中找到可以提取的内容。");
                return;
            }

            PopulateSections();
            PopulateColumns();
            WarningsTextBlock.Text = string.Join(Environment.NewLine,
                document.Warnings.Select(warning => $"• {warning}"));
            WarningsPanel.IsVisible = document.Warnings.Count > 0;
            ReadStatusPanel.IsVisible = false;
            RebuildPreview();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A replacement request or a closed window owns the current UI.
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[WordListImportWindow] Import failed: {ex}");
            if (IsCurrentRequest(requestVersion, cancellation))
                ShowReadStatus($"读取失败：{ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_readCancellation, cancellation))
            {
                _readCancellation = null;
                _busy = false;
                if (!_closed)
                    UpdateControlState();
            }
        }
    }

    private bool IsCurrentRequest(int version, CancellationTokenSource cancellation) =>
        !_closed && version == _requestVersion && !cancellation.IsCancellationRequested;

    private void CancelActiveRead()
    {
        _requestVersion++;
        var cancellation = _readCancellation;
        _readCancellation = null;
        try
        {
            cancellation?.Cancel();
        }
        catch (AggregateException ex)
        {
            // A faulty plugin cancellation callback must not block closing or
            // switching plugins. The reading method disposes its own token.
            Trace.WriteLine($"[WordListImportWindow] Cancellation callback failed: {ex}");
        }
    }

    private void PopulateSections()
    {
        if (_document is null)
            return;

        var choices = _document.Sections.Select((section, index) =>
            new SectionChoice(index, string.IsNullOrWhiteSpace(section.Name)
                ? $"文档区域 {index + 1}"
                : section.Name)).ToList();
        var tables = _document.Sections.Count(section => section.Kind == ImportSectionKind.Table);
        choices.Add(new SectionChoice(-1, tables > 0 ? $"所有表格（{tables} 张）" : "所有文本区域"));

        _updatingChoices = true;
        try
        {
            SectionComboBox.ItemsSource = choices;
            var suggested = Math.Clamp(_document.SuggestedSectionIndex, 0, _document.Sections.Count - 1);
            SectionComboBox.SelectedItem = choices.First(choice => choice.Index == suggested);
        }
        finally
        {
            _updatingChoices = false;
        }
    }

    private void PopulateColumns()
    {
        if (_document is null || SectionComboBox.SelectedItem is not SectionChoice sectionChoice)
            return;

        IReadOnlyList<ImportSection> sections;
        if (sectionChoice.Index == -1)
        {
            var tables = _document.Sections.Where(section => section.Kind == ImportSectionKind.Table).ToArray();
            sections = tables.Length > 0 ? tables : _document.Sections;
        }
        else
        {
            sections = [_document.Sections[sectionChoice.Index]];
        }

        var choices = new List<ColumnChoice> { new(-1, "自动选择推荐列") };
        var columnCount = sections.Count == 0 ? 0 : sections.Max(section => section.Columns.Count);
        for (var index = 0; index < columnCount; index++)
        {
            var labels = sections.Where(section => index < section.Columns.Count)
                .Select(section => section.Columns[index].Trim())
                .Where(label => label.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Take(3);
            var label = string.Join(" / ", labels);
            if (label.Length > 70)
                label = $"{label[..69]}…";
            choices.Add(new ColumnChoice(index, label.Length > 0 ? $"第 {index + 1} 列 · {label}" : $"第 {index + 1} 列"));
        }

        _updatingChoices = true;
        try
        {
            ColumnComboBox.ItemsSource = choices;
            ColumnComboBox.SelectedItem = choices[0];
        }
        finally
        {
            _updatingChoices = false;
        }
    }

    private void RebuildPreview()
    {
        if (_updatingChoices || _busy || _document is null || _closed ||
            SectionComboBox.SelectedItem is not SectionChoice section ||
            ColumnComboBox.SelectedItem is not ColumnChoice column)
            return;

        try
        {
            _lastPreview = ImportPreviewBuilder.Build(_document, new ImportPreviewOptions(
                section.Index,
                column.Index,
                SkipHeadersCheckBox.IsChecked == true,
                DeduplicateCheckBox.IsChecked == true,
                StripNumberingCheckBox.IsChecked == true));
            PreviewTextBox.Text = _lastPreview.Text;
            ReadStatusPanel.IsVisible = false;
            UpdatePreviewSummary();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[WordListImportWindow] Preview failed: {ex}");
            _lastPreview = null;
            PreviewTextBox.Text = string.Empty;
            ShowReadStatus($"无法生成预览：{ex.Message}");
            UpdateControlState();
        }
    }

    private void UpdatePreviewSummary()
    {
        var text = PreviewTextBox.Text ?? string.Empty;
        var count = ImportPreviewBuilder.CountEntries(text);
        var edited = _lastPreview is not null && !string.Equals(text, _lastPreview.Text, StringComparison.Ordinal);
        SummaryTextBlock.Text = _lastPreview is null
            ? (_busy ? "正在读取…" : "预览 0 条")
            : $"预览 {count} 条{(edited ? " · 已手动编辑" : string.Empty)}  |  原文 {_lastPreview.CandidateRows} 行，" +
              $"跳过表头 {_lastPreview.SkippedHeaders}，空值或缺列 {_lastPreview.SkippedEmpty}，移除重复 {_lastPreview.RemovedDuplicates}。";
        ReplaceButton.IsEnabled = !_busy && _lastPreview is not null && count > 0;
        AppendButton.IsEnabled = ReplaceButton.IsEnabled;
    }

    private void UpdateControlState()
    {
        FilterGrid.IsEnabled = !_busy && _document is { Sections.Count: > 0 };
        PreviewTextBox.IsReadOnly = _busy || _lastPreview is null;
        ReadProgressBar.IsVisible = _busy;
        ReadAgainButton.IsEnabled = !_busy && PluginComboBox.SelectedItem is PluginChoice;
        UpdatePreviewSummary();
    }

    private void ShowReadStatus(string text)
    {
        ReadStatusTextBlock.Text = text;
        ReadStatusPanel.IsVisible = true;
    }

    private void AcceptSelection(bool append)
    {
        var text = PreviewTextBox.Text ?? string.Empty;
        if (_busy || _lastPreview is null || ImportPreviewBuilder.CountEntries(text) == 0)
            return;

        Close(new WordListImportSelection(text, append));
    }

    private sealed record PluginChoice(PluginEntry Entry)
    {
        public override string ToString() => $"{Entry.DisplayName} · v{Entry.Manifest?.Version}";
    }

    private sealed record SectionChoice(int Index, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record ColumnChoice(int Index, string Label)
    {
        public override string ToString() => Label;
    }
}
