// Feature-level 2 SDK example. AGPL-3.0-or-later.
using System.Text;
using DictationAssistant.Plugin.Abstractions;

namespace Example.ExtensionToolkit;

public sealed class ExtensionToolkit : ICommandPlugin, IPluginLifecycle, IWordListObserverPlugin
{
    public const string NormalizeCommand = "normalize-word-list";
    public const string StatisticsCommand = "word-list-statistics";

    public IReadOnlyList<PluginCommand> Commands { get; } = Array.AsReadOnly(new[]
    {
        new PluginCommand(NormalizeCommand, "规范并去重词表", "去除空行和行首尾空白，按大小写敏感规则保留首次出现的词条。"),
        new PluginCommand(StatisticsCommand, "词表统计", "查看非空词条、唯一词条及重复词条数量。")
    });

    private IPluginContext? _context;
    private long _lastObservedRevision = -1;

    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _context = context;
        context.Log(PluginLogLevel.Information, $"词表工具已初始化，宿主版本 {context.HostVersion}。");
        return Task.CompletedTask;
    }

    public async Task ExecuteAsync(string commandId, IPluginContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (commandId is not (NormalizeCommand or StatisticsCommand))
            throw new ArgumentException("未知的词表工具命令。", nameof(commandId));

        var snapshot = await context.GetWordListAsync(cancellationToken).ConfigureAwait(false);
        var result = ReadTerms(snapshot.Text, commandId == NormalizeCommand, cancellationToken);
        if (commandId == StatisticsCommand)
        {
            var summary = Describe(result);
            context.Log(PluginLogLevel.Information, $"修订 {snapshot.Revision}：{summary}");
            if (context.OwnerWindow is not null)
                await context.ShowMessageAsync("词表统计", summary, cancellationToken).ConfigureAwait(false);
            return;
        }

        // If the user edits after this snapshot, the host rejects this update.
        // Do not retry without taking another snapshot or remove ExpectedRevision.
        if (!string.Equals(snapshot.Text, result.NormalizedText, StringComparison.Ordinal))
        {
            await context.UpdateWordListAsync(new PluginWordListUpdate(result.NormalizedText!,
                ExpectedRevision: snapshot.Revision), cancellationToken).ConfigureAwait(false);
        }
        context.Log(PluginLogLevel.Information, $"规范命令完成；来源修订 {snapshot.Revision}。{Describe(result)}");
        if (context.OwnerWindow is not null)
            await context.ShowMessageAsync("词表已规范", Describe(result), cancellationToken).ConfigureAwait(false);
    }

    public Task OnWordListChangedAsync(PluginWordListSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (snapshot.Revision <= _lastObservedRevision) return Task.CompletedTask;
        var result = ReadTerms(snapshot.Text, false, cancellationToken);
        _lastObservedRevision = snapshot.Revision;
        // Observe and log counts only: do not retain text or write back from an observer.
        _context?.Log(PluginLogLevel.Information, $"词表变更，修订 {snapshot.Revision}：{Describe(result)}");
        return Task.CompletedTask;
    }

    public Task ShutdownAsync(CancellationToken cancellationToken)
    {
        _context?.Log(PluginLogLevel.Information, "词表工具已关闭。");
        _context = null;
        return Task.CompletedTask;
    }

    private static TermSummary ReadTerms(string text, bool normalize, CancellationToken cancellationToken)
    {
        var unique = new HashSet<string>(StringComparer.Ordinal);
        var output = normalize ? new StringBuilder(text.Length) : null;
        var count = 0;
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var term = line.Trim();
            if (term.Length == 0) continue;
            count++;
            if (!unique.Add(term) || output is null) continue;
            if (output.Length > 0) output.AppendLine();
            output.Append(term);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new TermSummary(count, unique.Count, output?.ToString());
    }

    private static string Describe(TermSummary summary) =>
        $"非空词条 {summary.Total}，唯一词条 {summary.Unique}，重复词条 {summary.Total - summary.Unique}。";

    private sealed record TermSummary(int Total, int Unique, string? NormalizedText);
}
