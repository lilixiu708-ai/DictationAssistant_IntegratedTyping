using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DictationAssistant.App.Audio;
using DictationAssistant.App.Models;
using DictationAssistant.App.Plugins;
using DictationAssistant.Plugin.Abstractions;

namespace DictationAssistant.App;

public partial class MainWindow
{
    private sealed class WindowPluginContext : IPluginContext
    {
        private readonly MainWindow _window;
        private readonly PluginEntry _entry;
        private readonly CancellationToken _lifetimeToken;

        public WindowPluginContext(MainWindow window, PluginEntry entry)
        {
            _window = window;
            _entry = entry;
            _lifetimeToken = window._windowLifetime.Token;
            PluginId = entry.Manifest!.Id;
            DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
                    Environment.SpecialFolderOption.DoNotVerify),
                "DictationAssistant", "Plugins", PluginId, "Data");
            Directory.CreateDirectory(DataDirectory);
        }

        public string PluginId { get; }
        public string HostVersion => typeof(MainWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        public string DataDirectory { get; }
        public object OwnerWindow => _window;

        private CancellationTokenSource Link(CancellationToken token) =>
            CancellationTokenSource.CreateLinkedTokenSource(token, _lifetimeToken);

        public async Task<PluginWordListSnapshot> GetWordListAsync(CancellationToken cancellationToken)
        {
            using var linked = Link(cancellationToken);
            cancellationToken = linked.Token;
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return _window.GetPluginWordListSnapshot();
            }, DispatcherPriority.Normal, cancellationToken);
        }

        public async Task UpdateWordListAsync(PluginWordListUpdate update, CancellationToken cancellationToken)
        {
            using var linked = Link(cancellationToken);
            cancellationToken = linked.Token;
            ArgumentNullException.ThrowIfNull(update);
            ArgumentNullException.ThrowIfNull(update.Text);
            if (update.Text.Length > 16_000_000 || Encoding.UTF8.GetByteCount(update.Text) > 64 * 1024 * 1024 ||
                update.Text.Contains('\0'))
                throw new InvalidDataException("插件词表必须为不含空字符、最多 64 MiB 的文本。");
            await InvokeOnUiAsync(() => _window.ApplyWordListTextAsync(update.Text, update.Append,
                update.ExpectedRevision, cancellationToken, maximumCharacters: 16_000_000), cancellationToken);
        }

        public async Task InvokeOnUiAsync(Func<Task> action, CancellationToken cancellationToken)
        {
            using var linked = Link(cancellationToken);
            cancellationToken = linked.Token;
            ArgumentNullException.ThrowIfNull(action);
            cancellationToken.ThrowIfCancellationRequested();
            if (Dispatcher.UIThread.CheckAccess()) await action();
            else
            {
                var callback = _window.PluginRuntime.CaptureInvocation(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _window._windowLifetime.Token.ThrowIfCancellationRequested();
                    return action();
                });
                await Dispatcher.UIThread.InvokeAsync(callback, DispatcherPriority.Normal, cancellationToken);
            }
        }

        public async Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken)
        {
            using var linked = Link(cancellationToken);
            cancellationToken = linked.Token;
            await InvokeOnUiAsync(async () =>
            {
                var close = new Button { Content = "关闭", HorizontalAlignment = HorizontalAlignment.Right };
                var content = new StackPanel { Spacing = 16, Margin = new Thickness(20) };
                content.Children.Add(new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
                content.Children.Add(close);
                var dialog = new Window
                {
                    Title = title, Width = 500, MaxHeight = 600, SizeToContent = SizeToContent.Height,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = content
                };
                close.Click += (_, _) => dialog.Close();
                var showing = dialog.ShowDialog(_window);
                using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(dialog.Close));
                await showing;
                cancellationToken.ThrowIfCancellationRequested();
            }, cancellationToken);
        }

        public async Task SpeakAsync(string text, CancellationToken cancellationToken)
        {
            using var linked = Link(cancellationToken);
            cancellationToken = linked.Token;
            var player = await Dispatcher.UIThread.InvokeAsync(() => _window.GetViewModel()!.DictationPlayer,
                DispatcherPriority.Normal, cancellationToken);
            await Task.Run(() => player.SpeakPracticeWordAsync(text, cancellationToken), cancellationToken).ConfigureAwait(false);
        }

        public async Task<PluginAudioData> SynthesizeAsync(string text, int rate, CancellationToken cancellationToken)
        {
            using var linked = Link(cancellationToken);
            cancellationToken = linked.Token;
            var voice = await Dispatcher.UIThread.InvokeAsync(() => _window.GetViewModel()!.DictationPlayer.Voice,
                DispatcherPriority.Normal, cancellationToken);
            using var audio = await voice.SynthesizePcmAsync(text, new VoiceSynthesisOptions
                { Rate = Math.Clamp(rate, -10, 10) }, cancellationToken).ConfigureAwait(false);
            return await PluginAudioBridge.FromPcmAsync(audio, cancellationToken).ConfigureAwait(false);
        }

        public async Task PlayAudioAsync(PluginAudioData audio, CancellationToken cancellationToken)
        {
            using var linked = Link(cancellationToken);
            cancellationToken = linked.Token;
            var vm = await Dispatcher.UIThread.InvokeAsync(() => _window.GetViewModel()!,
                DispatcherPriority.Normal, cancellationToken);
            await vm.PlayPluginAudioAsync(audio, cancellationToken).ConfigureAwait(false);
        }

        public async Task<PluginAudioData> ProcessAudioAsync(PluginAudioData audio, CancellationToken cancellationToken)
        {
            using var linked = Link(cancellationToken);
            return await PluginAudioBridge.ProcessAsync(_window.PluginRuntime, audio, linked.Token).ConfigureAwait(false);
        }

        public async Task ExportWaveAsync(PluginAudioData audio, Stream destination, CancellationToken cancellationToken)
        {
            using var linked = Link(cancellationToken);
            await PluginAudioBridge.WriteWaveAsync(audio, destination, linked.Token).ConfigureAwait(false);
        }

        public void Log(PluginLogLevel level, string message) => _window.PluginRuntime.Log(_entry, level, message);
    }
}
