// Source distributed under the AGPL-3.0-or-later terms of DictationAssistant.
// Modified 2026-10-20: optional, cancellable DeepL pretranslation without retaining credentials.
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DictationAssistant.Practices.Typing;

internal sealed class DeepLTranslator
{
    private const int MaximumTextsPerRequest = 50;
    private const int MaximumRequestBytes = 128 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    // Redirects are unnecessary for these fixed HTTPS endpoints. Do not forward credentials.
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    public static async Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts,
        string apiKey, bool useFreeApi, string? sourceLanguage, string targetLanguage,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ct.ThrowIfCancellationRequested();
        var key = apiKey?.Trim();
        if (string.IsNullOrEmpty(key) || key.Any(c => c is <= ' ' or >= '\u007f'))
            throw new ArgumentException("请填写有效的 DeepL API 密钥。", nameof(apiKey));

        var source = string.IsNullOrWhiteSpace(sourceLanguage) ? null : sourceLanguage.Trim().ToUpperInvariant();
        var target = targetLanguage?.Trim().ToUpperInvariant();
        if (source is not (null or "EN" or "ZH"))
            throw new ArgumentException("DeepL 源语言请选择英语、中文或自动检测。", nameof(sourceLanguage));
        if (target is not ("EN" or "EN-US" or "EN-GB" or "ZH" or "ZH-HANS" or "ZH-HANT"))
            throw new ArgumentException("DeepL 目标语言请选择英语或中文。", nameof(targetLanguage));
        if (texts.Count == 0) return Array.Empty<string>();

        // Validate every item before sending anything. Incomplete results never reach the caller.
        var batches = await Task.Run(() => PrepareBatches(texts, source, target, ct), ct).ConfigureAwait(false);
        var translated = new List<string>(texts.Count);
        var endpoint = useFreeApi ? "https://api-free.deepl.com/v2/translate" : "https://api.deepl.com/v2/translate";
        try
        {
            foreach (var batch in batches)
            {
                ct.ThrowIfCancellationRequested();
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", key);
                request.Content = new ByteArrayContent(batch.Body);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
                using var response = await Client.SendAsync(request, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException(StatusMessage(response.StatusCode));
                await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("translations", out var items)
                    || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() != batch.Count)
                    throw new InvalidOperationException("DeepL 返回的译文数量不匹配，本次结果未保存。请稍后重试。");
                foreach (var item in items.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("text", out var value)
                        || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                        throw new InvalidOperationException("DeepL 返回了无效译文，本次结果未保存。请稍后重试。");
                    translated.Add(value.GetString()!);
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("DeepL 请求超时，请检查网络后重试。");
        }
        catch (HttpRequestException)
        {
            // Never include headers, response bodies, keys or provider exception details in logs/UI.
            throw new InvalidOperationException("无法连接 DeepL，请检查网络及所选 API Free／Pro 服务。");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("DeepL 返回了无法读取的数据，本次结果未保存。请稍后重试。");
        }
        catch (IOException)
        {
            throw new InvalidOperationException("读取 DeepL 响应失败，请检查网络后重试。");
        }
        ct.ThrowIfCancellationRequested();
        return translated;
    }

    private static List<RequestBatch> PrepareBatches(IReadOnlyList<string> texts, string? source,
        string target, CancellationToken ct)
    {
        var result = new List<RequestBatch>();
        var current = new List<string>(MaximumTextsPerRequest);
        byte[] body = [];
        for (var index = 0; index < texts.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var text = texts[index];
            if (string.IsNullOrWhiteSpace(text) || text.Contains('\0'))
                throw new ArgumentException($"第 {index + 1} 条词条为空或无效，请检查词表。", nameof(texts));
            if (current.Count == MaximumTextsPerRequest)
            {
                result.Add(new RequestBatch(body, current.Count));
                current.Clear();
            }
            current.Add(text);
            var candidate = Serialize(current, source, target);
            if (candidate.Length > MaximumRequestBytes)
            {
                current.RemoveAt(current.Count - 1);
                if (current.Count > 0) result.Add(new RequestBatch(body, current.Count));
                current.Clear();
                current.Add(text);
                candidate = Serialize(current, source, target);
                if (candidate.Length > MaximumRequestBytes)
                    throw new ArgumentException($"第 {index + 1} 条词条超过 DeepL 单次请求大小限制，请缩短后重试。", nameof(texts));
            }
            body = candidate;
        }
        if (current.Count > 0) result.Add(new RequestBatch(body, current.Count));
        return result;
    }

    private static byte[] Serialize(List<string> texts, string? source, string target) =>
        JsonSerializer.SerializeToUtf8Bytes(new TranslationRequest(texts, source, target), JsonOptions);

    private static string StatusMessage(HttpStatusCode status) => (int)status switch
    {
        401 or 403 => "DeepL 授权失败，请检查密钥及所选 API Free／Pro 服务。",
        429 => "DeepL 请求过于频繁，请稍后重试。",
        456 => "DeepL 翻译额度已用完，请检查账户用量或费用上限。",
        400 => "DeepL 拒绝了翻译参数，请检查词条及语言方向。",
        413 => "DeepL 请求内容过大，请减少词条长度后重试。",
        >= 500 => "DeepL 服务暂时不可用，请稍后重试。",
        _ => $"DeepL 翻译失败（HTTP {(int)status}），本次结果未保存。"
    };

    private sealed record RequestBatch(byte[] Body, int Count);
    private sealed record TranslationRequest(
        [property: JsonPropertyName("text")] List<string> Texts,
        [property: JsonPropertyName("source_lang")] string? SourceLanguage,
        [property: JsonPropertyName("target_lang")] string TargetLanguage);
}
