// SPDX-License-Identifier: AGPL-3.0-or-later
// Modified 2026-10-20: prepare bounded translation requests before sending any text.
namespace DictationAssistant.Practices.Typing;

internal static class TranslationBatches
{
    internal sealed record Batch(byte[] Body, int Count);

    public static IReadOnlyList<Batch> Prepare(IReadOnlyList<string> texts, int maximumCount,
        int maximumCharacters, Func<IReadOnlyList<string>, byte[]> serialize, string provider,
        CancellationToken cancellationToken)
    {
        const int maximumBytes = 128 * 1024;
        var batches = new List<Batch>();
        var current = new List<string>();
        byte[] body = [];
        var characters = 0;
        for (var index = 0; index < texts.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = texts[index];
            if (string.IsNullOrWhiteSpace(text) || text.Contains('\0') || text.Length > maximumCharacters)
                throw new InvalidOperationException($"第 {index + 1} 条词条为空、无效或超出{provider}长度限制。");
            if (current.Count == maximumCount || characters + text.Length > maximumCharacters)
            {
                batches.Add(new Batch(body, current.Count));
                current.Clear();
                characters = 0;
            }
            current.Add(text);
            var candidate = serialize(current);
            if (candidate.Length > maximumBytes)
            {
                current.RemoveAt(current.Count - 1);
                if (current.Count > 0) batches.Add(new Batch(body, current.Count));
                current.Clear();
                current.Add(text);
                characters = 0;
                candidate = serialize(current);
                if (candidate.Length > maximumBytes)
                    throw new InvalidOperationException($"第 {index + 1} 条词条超出{provider}单次请求大小限制。");
            }
            characters += text.Length;
            body = candidate;
        }
        if (current.Count > 0) batches.Add(new Batch(body, current.Count));
        return batches;
    }
}
