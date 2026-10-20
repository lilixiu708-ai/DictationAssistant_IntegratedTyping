// Source distributed under the AGPL-3.0-or-later terms of DictationAssistant.
// Modified 2026-10-20: editable paired translation hints and local persistence.
using System.Text;
using System.Text.Json;

namespace DictationAssistant.Practices.Typing;

internal static class PracticeTranslations
{
    private static string StoragePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "DictationAssistant", "Plugins", "SynchronousTyping", "translations.json");

    public static async Task<Dictionary<string, string>> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(StoragePath)) return new Dictionary<string, string>(StringComparer.Ordinal);

        await using var stream = new FileStream(StoragePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous);
        var saved = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream,
            cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("本地翻译文件为空或格式无效。");
        ValidateTranslations(saved);
        return new Dictionary<string, string>(saved, StringComparer.Ordinal);
    }

    // The caller supplies the complete map, retaining translations for other word lists.
    public static async Task SaveAsync(IReadOnlyDictionary<string, string> translations,
        IReadOnlyDictionary<string, string> expected,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(translations);
        ArgumentNullException.ThrowIfNull(expected);
        ValidateTranslations(translations);
        cancellationToken.ThrowIfCancellationRequested();
        var path = StoragePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        FileStream saveLock;
        try
        {
            saveLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            throw new IOException("翻译文件正由其他窗口保存，请稍后重试。");
        }
        await using var heldLock = saveLock;
        var current = await LoadAsync(cancellationToken);
        if (current.Count != expected.Count || expected.Any(pair =>
            !current.TryGetValue(pair.Key, out var value) || value != pair.Value))
            throw new InvalidDataException("翻译已在其他窗口修改，请关闭并重新打开练习后重试。");

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, translations, cancellationToken: cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static string BuildEditorText(IReadOnlyList<string> words,
        IReadOnlyDictionary<string, string> translations)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(translations);
        var result = new StringBuilder();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var word in words)
        {
            ValidateCell(word, "词条");
            if (!seen.Add(word)) continue;
            translations.TryGetValue(word, out var translation);
            translation ??= string.Empty;
            if (translation.Length > 0) ValidateCell(translation, "译文");
            if (result.Length > 0) result.AppendLine();
            result.Append(word).Append('\t').Append(translation);
        }
        return result.ToString();
    }

    // An empty translation leaves the item without a hint. Original answers remain unchanged.
    public static Dictionary<string, string> ParseEditorText(string text, IReadOnlyList<string> words)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(words);
        var knownWords = new HashSet<string>(words, StringComparer.Ordinal);
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = new StringReader(text);
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            var separator = line.IndexOf('\t');
            if (separator < 0 || line.IndexOf('\t', separator + 1) >= 0)
                throw new InvalidDataException($"第 {lineNumber} 行格式无效，请使用“原词 + Tab + 译文”两列。");
            var word = line[..separator];
            var translation = line[(separator + 1)..].Trim();
            ValidateCell(word, $"第 {lineNumber} 行词条");
            if (!knownWords.Contains(word))
                throw new InvalidDataException($"第 {lineNumber} 行的原词不在当前词表中，请保留原词的大小写和标点。");
            if (translation.Length > 0) ValidateCell(translation, $"第 {lineNumber} 行译文");
            if (rows.TryGetValue(word, out var existing) && existing != translation)
                throw new InvalidDataException($"第 {lineNumber} 行与前面的重复词条译文不同，请为同一词条保留一份译文。");
            rows[word] = translation;
        }

        return rows.Where(pair => pair.Value.Length > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static void ValidateTranslations(IReadOnlyDictionary<string, string> translations)
    {
        foreach (var (word, translation) in translations)
        {
            ValidateCell(word, "词条");
            ValidateCell(translation, "译文");
        }
    }

    private static void ValidateCell(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(['\t', '\r', '\n', '\0']) >= 0)
            throw new InvalidDataException($"{name}不能为空，也不能包含 Tab、换行或空字符。");
    }
}
