using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DictationAssistant.App.Services;
using DictationAssistant.App.Audio;
using DictationAssistant.App.Settings;
using DictationAssistant.App.Voice;
using DictationAssistant.App.ViewModels;
using DictationAssistant.App.Abstractions;

namespace DictationAssistant.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settingsStore = new AppSettingsStore();
            var wordListSource = new EditorDocumentWordListSource();
            var audioPlayer = new SdlPcmPlayer();
            var aggregator = new VoiceAggregator([
                new SapiVoiceFactoryProvider(),
                new MacSayVoiceFactoryProvider(),
                new EspeakNgVoiceFactoryProvider(),
                new EdgeTtsVoiceFactoryProvider(),
            ]);

            var initialVoice = NullVoice.Instance;

            desktop.MainWindow = new MainWindow()
            {
                DataContext = new MainWindowViewModel(wordListSource, audioPlayer, aggregator, initialVoice, settingsStore)
            };

            // Command-line file argument support (v3.x compat)
            if (desktop.Args is { Length: > 0 } cliArgs)
            {
                var filePath = cliArgs[0];
                if (File.Exists(filePath) && desktop.MainWindow is MainWindow mainWin)
                {
                    mainWin.Opened += async (_, _) =>
                    {
                        await mainWin.LoadFileByPathAsync(Path.GetFullPath(filePath));
                    };
                }
            }

            desktop.Exit += (_, _) =>
            {
                (desktop.MainWindow?.DataContext as IDisposable)?.Dispose();
                audioPlayer.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
