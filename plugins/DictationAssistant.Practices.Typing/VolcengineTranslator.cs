// Source distributed under the AGPL-3.0-or-later terms of DictationAssistant.
// Modified 2026-10-20: cancellable, signed Volcengine text pretranslation.
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DictationAssistant.Practices.Typing;

internal static class VolcengineTranslator
{
    private const string Host = "translate.volcengineapi.com";
    private const string Query = "Action=TranslateText&Version=2020-06-01";
    private const string SignedHeaders = "content-type;host;x-content-sha256;x-date";
    private const int MaximumTextsPerRequest = 16;
    private const int MaximumCharactersPerRequest = 5000;

    // Endpoint, body and limits: https://www.volcengine.com/docs/4640/65067
    // Request authentication: https://www.volcengine.com/docs/6369/67269
    public static async Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts,
        TranslationEngineBinding binding, string sourceLanguage, string targetLanguage, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(texts);
        TranslationEngineProviders.Validate(binding);
        if (binding.Provider != "volcengine")
            throw new ArgumentException("请选择火山引擎翻译配置。", nameof(binding));
        ct.ThrowIfCancellationRequested();
        var source = NormalizeLanguage(sourceLanguage);
        var target = NormalizeLanguage(targetLanguage);
        if (texts.Count == 0) return Array.Empty<string>();

        // Validate the complete input before sending any paid request.
        var batches = await Task.Run(() => PrepareBatches(texts, source, target, ct), ct).ConfigureAwait(false);
        var translated = new List<string>(texts.Count);
        foreach (var batch in batches)
        {
            ct.ThrowIfCancellationRequested();
            using var request = CreateRequest(batch.Body, binding);
            using var document = await TranslationHttp.SendAsync(request, "火山引擎", ct).ConfigureAwait(false);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw InvalidResponse();
            if (root.TryGetProperty("ResponseMetadata", out var metadata)
                && metadata.ValueKind == JsonValueKind.Object
                && metadata.TryGetProperty("Error", out var error)
                && error.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                throw new InvalidOperationException("火山引擎拒绝了翻译请求，请检查密钥、服务区域、权限和账户额度。");
            if (!root.TryGetProperty("TranslationList", out var items)
                || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() != batch.Count)
                throw InvalidResponse();
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("Translation", out var value)
                    || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                    throw InvalidResponse();
                translated.Add(value.GetString()!);
            }
        }
        ct.ThrowIfCancellationRequested();
        return translated;
    }

    private static string NormalizeLanguage(string language) => language?.Trim().ToUpperInvariant() switch
    {
        "EN" => "en",
        "ZH" => "zh",
        _ => throw new ArgumentException("火山引擎翻译语言请选择英语或中文。")
    };

    private static List<RequestBatch> PrepareBatches(IReadOnlyList<string> texts, string source,
        string target, CancellationToken ct)
    {
        var batches = new List<RequestBatch>();
        var current = new List<string>(MaximumTextsPerRequest);
        var characters = 0;
        for (var index = 0; index < texts.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var text = texts[index];
            if (string.IsNullOrWhiteSpace(text) || text.Contains('\0'))
                throw new ArgumentException($"第 {index + 1} 条词条为空或无效，请检查词表。", nameof(texts));
            if (text.Length > MaximumCharactersPerRequest)
                throw new ArgumentException($"第 {index + 1} 条词条超过火山引擎单次 5000 字符限制，请缩短后重试。", nameof(texts));
            if (current.Count == MaximumTextsPerRequest || characters + text.Length > MaximumCharactersPerRequest)
            {
                batches.Add(Serialize(current, source, target));
                current.Clear();
                characters = 0;
            }
            current.Add(text);
            characters += text.Length;
        }
        if (current.Count > 0) batches.Add(Serialize(current, source, target));
        return batches;
    }

    private static RequestBatch Serialize(List<string> texts, string source, string target) => new(
        JsonSerializer.SerializeToUtf8Bytes(new { SourceLanguage = source, TargetLanguage = target, TextList = texts }),
        texts.Count);

    private static HttpRequestMessage CreateRequest(byte[] body, TranslationEngineBinding binding)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var date = timestamp[..8];
        var region = string.IsNullOrEmpty(binding.Region) ? "cn-north-1" : binding.Region;
        var scope = $"{date}/{region}/translate/request";
        var bodyHash = Hex(SHA256.HashData(body));
        var canonicalHeaders = $"content-type:application/json\nhost:{Host}\nx-content-sha256:{bodyHash}\nx-date:{timestamp}\n";
        var canonicalRequest = $"POST\n/\n{Query}\n{canonicalHeaders}\n{SignedHeaders}\n{bodyHash}";
        var stringToSign = $"HMAC-SHA256\n{timestamp}\n{scope}\n{Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)))}";
        var secret = Encoding.UTF8.GetBytes(binding.SecretKey!);
        byte[]? signingKey = null;
        try
        {
            signingKey = Hmac(secret, date);
            foreach (var component in new[] { region, "translate", "request" })
            {
                var previousKey = signingKey;
                signingKey = Hmac(previousKey, component);
                CryptographicOperations.ZeroMemory(previousKey);
            }
            var signature = Hex(Hmac(signingKey, stringToSign));
            var request = new HttpRequestMessage(HttpMethod.Post, $"https://{Host}/?{Query}");
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Headers.Host = Host;
            request.Headers.TryAddWithoutValidation("X-Date", timestamp);
            request.Headers.TryAddWithoutValidation("X-Content-Sha256", bodyHash);
            request.Headers.TryAddWithoutValidation("Authorization",
                $"HMAC-SHA256 Credential={binding.ApiKey}/{scope}, SignedHeaders={SignedHeaders}, Signature={signature}");
            return request;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
            if (signingKey is not null) CryptographicOperations.ZeroMemory(signingKey);
        }
    }

    private static byte[] Hmac(byte[] key, string value) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value));
    private static string Hex(byte[] value) => Convert.ToHexStringLower(value);
    private static InvalidOperationException InvalidResponse() =>
        new("火山引擎返回的译文无效或数量不匹配，本次结果未保存。请稍后重试。");
    private sealed record RequestBatch(byte[] Body, int Count);
}
