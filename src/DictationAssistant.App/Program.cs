// Modified 2026-10-19: support plugin diagnostics and batch document extraction.
using Avalonia;
using DictationAssistant.App.Importing;

namespace DictationAssistant.App;

internal sealed class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (PluginCommandLine.Handles(args))
            return PluginCommandLine.RunAsync(args).GetAwaiter().GetResult();
        return ImportCommandLine.Handles(args)
            ? ImportCommandLine.RunAsync(args).GetAwaiter().GetResult()
            : BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
