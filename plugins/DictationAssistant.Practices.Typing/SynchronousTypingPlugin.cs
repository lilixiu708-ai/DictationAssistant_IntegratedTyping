// Source distributed under the AGPL-3.0-or-later terms of DictationAssistant.
using Avalonia.Controls;
using DictationAssistant.Plugin.Abstractions;

namespace DictationAssistant.Practices.Typing;

public sealed class SynchronousTypingPlugin : IPracticePlugin, IConfigurablePlugin, IPracticeSourceSelectionPlugin
{
    private readonly TranslationEngineSession _translationEngines = new();
    public bool CanStartWithoutWords => true;

    public async Task RunAsync(IPracticeHost host, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (host.OwnerWindow is not Window owner)
            throw new InvalidOperationException("当前宿主不支持 Avalonia 模态练习窗口。");
        var preparation = new PracticePreparationWindow(host, cancellationToken);
        var plan = await preparation.ShowDialog<PracticePlan?>(owner);
        cancellationToken.ThrowIfCancellationRequested();
        if (plan is null) return;
        var window = new TypingPracticeWindow(host, cancellationToken, _translationEngines, plan);
        await window.ShowDialog(owner);
    }

    public async Task ConfigureAsync(object ownerWindow, CancellationToken cancellationToken)
    {
        if (ownerWindow is not Window owner)
            throw new InvalidOperationException("当前宿主不支持插件配置窗口。");
        var window = new TranslationApiSettingsWindow(_translationEngines, cancellationToken);
        await window.ShowDialog(owner);
    }
}
