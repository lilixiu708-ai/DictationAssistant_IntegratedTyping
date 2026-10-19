using System.Reflection;
using System.Text;
using Avalonia.Platform;

namespace DictationAssistant.App.ViewModels;

public class AboutWindowViewModel
{
    public string Title { get; } = "自动默写";

    public string Version { get; } = ResolveVersion();

    public string EulaText { get; } = LoadEulaText();

    private static string ResolveVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? "0.0.0";
    }

    private static string LoadEulaText()
    {
        using var stream = AssetLoader.Open(new Uri("avares://DictationAssistant.App/Resources/EULA.txt"));
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
