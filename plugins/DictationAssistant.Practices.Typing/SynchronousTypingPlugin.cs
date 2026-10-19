// Source distributed under the AGPL-3.0-or-later terms of DictationAssistant.
using Avalonia.Controls;
using DictationAssistant.Plugin.Abstractions;

namespace DictationAssistant.Practices.Typing;

public sealed class SynchronousTypingPlugin : IPracticePlugin
{
    public async Task RunAsync(IPracticeHost host, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (host.OwnerWindow is not Window owner)
            throw new InvalidOperationException("当前宿主不支持 Avalonia 模态练习窗口。");
        if (host.Words.Count == 0)
            throw new InvalidOperationException("请先载入词表。");

        var window = new TypingPracticeWindow(host, cancellationToken);
        await window.ShowDialog(owner);
    }
}
