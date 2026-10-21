// SPDX-License-Identifier: AGPL-3.0-or-later
// Modified 2026-10-21: persist only explicitly confirmed, question-specific Chinese answers.
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DictationAssistant.Practices.Typing;

internal sealed record AnswerIdentity(string Prompt, string Expected);

internal static class PracticeAcceptedAnswers
{
    public const int MaximumAliases = 8;
    public const int MaximumAnswerLength = 512;
    public const int MaximumPromptLength = 2048;
    private const int MaximumEntries = 5000;
    private const int MaximumFileBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 8,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private static string StoragePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "DictationAssistant", "Plugins", "SynchronousTyping", "accepted-answers.json");

    public static bool Supports(AnswerIdentity identity) => identity is not null
        && IsValidText(identity.Prompt, MaximumPromptLength, allowEmpty: true)
        && IsValidText(identity.Expected, MaximumAnswerLength, allowEmpty: false)
        && ChineseAnswerEvaluation.ContainsChinese(identity.Expected);

    public static void ValidateAlias(AnswerIdentity identity, string alias)
    {
        if (!Supports(identity))
            throw new InvalidDataException("同义答案仅支持有效的中文标准答案，答案最多 512 字，提示最多 2048 字。");
        if (!IsValidText(alias, MaximumAnswerLength, allowEmpty: false)
            || !ChineseAnswerEvaluation.ContainsChinese(alias))
            throw new InvalidDataException("同义答案需要包含中文，最多 512 字，不能包含换行、Tab 或隐藏控制字符。");
    }

    public static void Validate(IReadOnlyDictionary<AnswerIdentity, IReadOnlyList<string>> answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        if (answers.Count > MaximumEntries)
            throw new InvalidDataException("已保存的同义答案超过允许的题目数量，请先整理答案。");
        long maximumEncodedBytes = 512;
        foreach (var (identity, aliases) in answers)
        {
            if (!Supports(identity) || aliases is null || aliases.Count is < 1 or > MaximumAliases)
                throw new InvalidDataException("每题需要保存 1–8 个已确认的中文同义答案；不再需要时请删除该题的保存项。");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            maximumEncodedBytes += 512L + 6L * (identity.Prompt.Length + identity.Expected.Length);
            foreach (var alias in aliases)
            {
                ValidateAlias(identity, alias);
                if (!seen.Add(alias)) throw new InvalidDataException("同一题的同义答案不能重复。");
                // The default JSON encoder escapes each UTF-16 code unit into
                // at most six bytes. Bound the allocation before serialization.
                maximumEncodedBytes += 16L + 6L * alias.Length;
            }
            if (maximumEncodedBytes > MaximumFileBytes)
                throw new InvalidDataException("已保存的同义答案过多，请先整理后再保存。");
        }
    }

    public static IReadOnlyDictionary<AnswerIdentity, IReadOnlyList<string>> Snapshot(
        IReadOnlyDictionary<AnswerIdentity, IReadOnlyList<string>> answers)
    {
        Validate(answers);
        return new ReadOnlyDictionary<AnswerIdentity, IReadOnlyList<string>>(answers.ToDictionary(
            pair => pair.Key, pair => (IReadOnlyList<string>)Array.AsReadOnly(pair.Value.ToArray())));
    }

    public static bool IsAccepted(string prompt, string expected, string entered, bool ignoreCase,
        IReadOnlyDictionary<AnswerIdentity, IReadOnlyList<string>> answers)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(entered);
        ArgumentNullException.ThrowIfNull(answers);
        var identity = new AnswerIdentity(prompt, expected);
        if (!Supports(identity) || !ChineseAnswerEvaluation.ContainsChinese(entered))
            return TypingEvaluation.IsCorrect(expected, entered, ignoreCase);
        if (ChineseAnswerEvaluation.IsCorrect(expected, entered, ignoreCase)) return true;
        return answers.TryGetValue(identity, out var aliases)
            && aliases.Any(alias => ChineseAnswerEvaluation.IsCorrect(alias, entered, ignoreCase));
    }

    public static async Task<Dictionary<AnswerIdentity, IReadOnlyList<string>>> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = StoragePath;
        StoredFile stored;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous);
            if (stream.Length is <= 0 or > MaximumFileBytes) throw InvalidFile();
            var bytes = new byte[(int)stream.Length];
            await stream.ReadExactlyAsync(bytes, cancellationToken);
            var tail = new byte[1];
            if (await stream.ReadAsync(tail, cancellationToken) != 0) throw InvalidFile();
            stored = JsonSerializer.Deserialize<StoredFile>(bytes, JsonOptions) ?? throw InvalidFile();
        }
        catch (FileNotFoundException) { return new Dictionary<AnswerIdentity, IReadOnlyList<string>>(); }
        catch (DirectoryNotFoundException) { return new Dictionary<AnswerIdentity, IReadOnlyList<string>>(); }
        catch (JsonException) { throw InvalidFile(); }
        catch (EndOfStreamException) { throw InvalidFile(); }
        if (stored.Version != 1 || stored.Entries is null || stored.Entries.Count > MaximumEntries)
            throw InvalidFile();
        var result = new Dictionary<AnswerIdentity, IReadOnlyList<string>>();
        foreach (var entry in stored.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry is null || entry.Prompt is null || entry.Expected is null || entry.Aliases is null)
                throw InvalidFile();
            var identity = new AnswerIdentity(entry.Prompt, entry.Expected);
            if (!result.TryAdd(identity, Array.AsReadOnly(entry.Aliases.ToArray()))) throw InvalidFile();
        }
        Validate(result);
        return result;
    }

    // The caller must merge its confirmed selections with this complete
    // snapshot. Candidates have no storage API and are not saved automatically.
    public static async Task SaveAsync(IReadOnlyDictionary<AnswerIdentity, IReadOnlyList<string>> answers,
        IReadOnlyDictionary<AnswerIdentity, IReadOnlyList<string>> expectedSnapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = Snapshot(answers);
        var expected = Snapshot(expectedSnapshot);
        cancellationToken.ThrowIfCancellationRequested();
        var path = StoragePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        FileStream saveLock;
        try { saveLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new IOException("另一个窗口正在保存同义答案，请稍后重试。"); }
        await using var heldLock = saveLock;
        var current = await LoadAsync(cancellationToken);
        if (current.Count != expected.Count || expected.Any(pair => !current.TryGetValue(pair.Key, out var value)
            || !pair.Value.SequenceEqual(value, StringComparer.Ordinal)))
            throw new InvalidDataException("同义答案已在其他窗口修改，请关闭并重新打开练习后再保存。");
        var stored = new StoredFile(1, snapshot.OrderBy(pair => pair.Key.Prompt, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Expected, StringComparer.Ordinal)
            .Select(pair => new StoredEntry(pair.Key.Prompt, pair.Key.Expected, pair.Value.ToList())).ToList());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(stored, JsonOptions);
        if (bytes.Length > MaximumFileBytes) throw new InvalidDataException("同义答案文件超过允许大小，未替换原有文件。");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(bytes, cancellationToken);
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

    private static bool IsValidText(string? value, int maximumLength, bool allowEmpty)
    {
        if (value is null || value.Length > maximumLength || !allowEmpty && string.IsNullOrWhiteSpace(value)) return false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsControl(character) || char.GetUnicodeCategory(character) == UnicodeCategory.Format) return false;
            if (char.IsHighSurrogate(character))
            {
                if (++index >= value.Length || !char.IsLowSurrogate(value[index])) return false;
                if (!Rune.TryCreate(character, value[index], out var rune)
                    || Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format) return false;
            }
            else if (char.IsLowSurrogate(character)) return false;
        }
        return true;
    }

    private static InvalidDataException InvalidFile() => new("保存的同义答案文件格式不正确，请检查 accepted-answers.json；原文件未被覆盖。");
    private sealed record StoredFile(int Version, List<StoredEntry>? Entries);
    private sealed record StoredEntry(string? Prompt, string? Expected, List<string>? Aliases);
}
