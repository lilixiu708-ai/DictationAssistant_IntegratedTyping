// Modified 2026-10-20: show complete legal notices as virtualized lines with original copyright and matching source.
using System.Reflection;
using System.Text;
using Avalonia.Platform;

namespace DictationAssistant.App.ViewModels;

public class AboutWindowViewModel
{
    public string Title { get; } = "自动默写";

    public string Version { get; } = ResolveVersion();

    public string Description { get; } = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
        .GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description ?? string.Empty;

    public IReadOnlyList<string> NoticeLines { get; } = LoadLegalNotices().ReplaceLineEndings("\n").Split('\n');

    private static string ResolveVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? "0.0.0";
    }

    private static string LoadLegalNotices()
    {
        var version = ResolveVersion();
        var separator = version.IndexOf('+');
        var revision = separator >= 0 ? version[(separator + 1)..] : string.Empty;
        var source = revision.Length == 40 && revision.All(Uri.IsHexDigit)
            ? $"https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/tree/{revision}"
            : "https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping (development build)";
        var text = new StringBuilder();
        text.AppendLine($"本程序版本：{version}");
        text.AppendLine($"对应源码：{source}");
        foreach (var (resource, title) in new[]
        {
            ("EULA.txt", "许可与使用说明"),
            ("NOTICE.md", "来源与版权声明"),
            ("LICENSE.md", "GNU AGPL 完整条款"),
            ("THIRD_PARTY_NOTICES.md", "第三方组件声明")
        })
        {
            text.AppendLine().AppendLine(title).AppendLine();
            using var stream = AssetLoader.Open(new Uri($"avares://DictationAssistant.App/Resources/{resource}"));
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            text.AppendLine(reader.ReadToEnd());
        }
        return text.ToString();
    }
}
