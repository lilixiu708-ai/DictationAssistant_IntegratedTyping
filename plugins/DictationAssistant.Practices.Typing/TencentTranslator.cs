// Source distributed under the AGPL-3.0-or-later terms of DictationAssistant.
// Modified 2026-10-20: Tencent TMT requests with TC3-HMAC-SHA256 authentication.
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DictationAssistant.Practices.Typing;

internal static class TencentTranslator
{
    private const string Host = "tmt.tencentcloudapi.com";
    private const string ContentType = "application/json";

    // API fields/limits: https://github.com/TencentCloud/tencentcloud-sdk-python/blob/3.0.1400/tencentcloud/tmt/v20180321/models.py
    // TC3 authentication: https://cloud.tencent.com/document/api/551/30648
    public static async Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts,
        TranslationEngineBinding binding, string sourceLanguage, string targetLanguage,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(texts);
        TranslationEngineProviders.Validate(binding);
        if (binding.Provider != "tencent")
            throw new ArgumentException("请选择腾讯云翻译配置。", nameof(binding));
        var source = sourceLanguage?.Trim().ToLowerInvariant();
        var target = targetLanguage?.Trim().ToLowerInvariant();
        if (!((source == "en" && target == "zh") || (source == "zh" && target == "en")))
            throw new ArgumentException("腾讯云翻译请选择英语与中文之间的翻译方向。");
        ct.ThrowIfCancellationRequested();

        // Validate the complete input before the first potentially billable request.
        var snapshot = await Task.Run(() =>
        {
            var values = new string[texts.Count];
            for (var index = 0; index < texts.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var text = texts[index];
                if (string.IsNullOrWhiteSpace(text) || text.Contains('\0') || text.Length >= 6000)
                    throw new ArgumentException($"第 {index + 1} 条词条为空、无效或达到腾讯云 6000 字符限制，请检查词表。");
                values[index] = text;
            }
            return values;
        }, ct).ConfigureAwait(false);

        var results = new List<string>(snapshot.Length);
        foreach (var text in snapshot)
        {
            if (results.Count > 0) await Task.Delay(TimeSpan.FromMilliseconds(220), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var body = JsonSerializer.SerializeToUtf8Bytes(new
            {
                SourceText = text, Source = source, Target = target, ProjectId = 0
            });
            var now = DateTimeOffset.UtcNow;
            var timestamp = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var date = now.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://" + Host + "/");
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(ContentType);
            request.Headers.Host = Host;
            request.Headers.TryAddWithoutValidation("X-TC-Action", "TextTranslate");
            request.Headers.TryAddWithoutValidation("X-TC-Version", "2018-03-21");
            request.Headers.TryAddWithoutValidation("X-TC-Region", binding.Region);
            request.Headers.TryAddWithoutValidation("X-TC-Timestamp", timestamp);
            request.Headers.TryAddWithoutValidation("Authorization", Authorize(binding, body, timestamp, date));
            using var response = await TranslationHttp.SendAsync(request, "腾讯云", ct).ConfigureAwait(false);
            var root = response.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Response", out var result)
                || result.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("腾讯云返回了无效数据，本次结果未保存。");
            if (result.TryGetProperty("Error", out _))
                throw new InvalidOperationException("腾讯云翻译失败，请检查凭据、服务权限、区域、额度及请求频率后重试。");
            if (!result.TryGetProperty("TargetText", out var translated)
                || translated.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(translated.GetString()))
                throw new InvalidOperationException("腾讯云返回了无效译文，本次结果未保存。");
            results.Add(translated.GetString()!);
        }
        ct.ThrowIfCancellationRequested();
        return results;
    }

    private static string Authorize(TranslationEngineBinding binding, byte[] body, string timestamp, string date)
    {
        var scope = date + "/tmt/tc3_request";
        var canonicalRequest = "POST\n/\n\ncontent-type:" + ContentType + "\nhost:" + Host
            + "\n\ncontent-type;host\n" + Convert.ToHexStringLower(SHA256.HashData(body));
        var stringToSign = "TC3-HMAC-SHA256\n" + timestamp + "\n" + scope + "\n"
            + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)));
        var secret = Encoding.UTF8.GetBytes("TC3" + binding.SecretKey);
        byte[] dateKey = [], serviceKey = [], signingKey = [];
        try
        {
            dateKey = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(date));
            serviceKey = HMACSHA256.HashData(dateKey, "tmt"u8);
            signingKey = HMACSHA256.HashData(serviceKey, "tc3_request"u8);
            var signature = Convert.ToHexStringLower(HMACSHA256.HashData(signingKey, Encoding.UTF8.GetBytes(stringToSign)));
            return "TC3-HMAC-SHA256 Credential=" + binding.ApiKey + "/" + scope
                + ", SignedHeaders=content-type;host, Signature=" + signature;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
            CryptographicOperations.ZeroMemory(dateKey);
            CryptographicOperations.ZeroMemory(serviceKey);
            CryptographicOperations.ZeroMemory(signingKey);
        }
    }
}
