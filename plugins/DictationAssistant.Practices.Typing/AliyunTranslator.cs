// Source distributed under the AGPL-3.0-or-later terms of DictationAssistant.
// Modified 2026-10-20: Aliyun TranslateGeneral requests with RPC HMAC-SHA1 authentication.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DictationAssistant.Practices.Typing;

internal static class AliyunTranslator
{
    // API: https://help.aliyun.com/zh/machine-translation/developer-reference/api-alimt-2018-10-12-translategeneral
    // RPC signing: https://help.aliyun.com/zh/sdk/product-overview/rpc-mechanism
    public static async Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts,
        TranslationEngineBinding binding, string sourceLanguage, string targetLanguage,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(texts);
        TranslationEngineProviders.Validate(binding);
        if (binding.Provider != "aliyun")
            throw new ArgumentException("请选择阿里云翻译配置。", nameof(binding));
        var source = sourceLanguage?.Trim().ToLowerInvariant();
        var target = targetLanguage?.Trim().ToLowerInvariant();
        if (!((source == "en" && target == "zh") || (source == "zh" && target == "en")))
            throw new ArgumentException("阿里云翻译请选择英语与中文之间的翻译方向。");
        ct.ThrowIfCancellationRequested();
        var snapshot = await Task.Run(() =>
        {
            var values = new string[texts.Count];
            for (var index = 0; index < texts.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var text = texts[index];
                if (string.IsNullOrWhiteSpace(text) || text.Contains('\0') || text.Length > 5000)
                    throw new ArgumentException($"第 {index + 1} 条词条为空、无效或超过阿里云 5000 字符限制，请检查词表。");
                values[index] = text;
            }
            return values;
        }, ct).ConfigureAwait(false);

        // Match the official SDK endpoint map; never construct an authority from user text.
        var endpoint = binding.Region switch
        {
            "cn-hangzhou" => "https://mt.cn-hangzhou.aliyuncs.com/",
            "ap-southeast-1" => "https://mt.ap-southeast-1.aliyuncs.com/",
            _ => "https://mt.aliyuncs.com/"
        };
        var results = new List<string>(snapshot.Length);
        foreach (var text in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["Action"] = "TranslateGeneral",
                ["Version"] = "2018-10-12",
                ["Format"] = "JSON",
                ["AccessKeyId"] = binding.ApiKey,
                ["SignatureMethod"] = "HMAC-SHA1",
                ["SignatureVersion"] = "1.0",
                ["SignatureNonce"] = Guid.NewGuid().ToString("N"),
                ["Timestamp"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                ["FormatType"] = "text",
                ["SourceLanguage"] = source,
                ["TargetLanguage"] = target,
                ["SourceText"] = text,
                ["Scene"] = "general"
            };
            parameters["Signature"] = Sign(parameters, binding.SecretKey!);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                // RPC accepts form parameters; keep credentials, signatures and source text out of URLs.
                Content = new FormUrlEncodedContent(parameters)
            };
            using var response = await TranslationHttp.SendAsync(request, "阿里云", ct).ConfigureAwait(false);
            var root = response.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("阿里云返回了无效数据，本次结果未保存。");
            if (!root.TryGetProperty("Code", out var code) || !IsSuccess(code))
                throw new InvalidOperationException("阿里云翻译失败，请检查凭据、服务权限、区域、额度及请求频率后重试。");
            if (!root.TryGetProperty("Data", out var data) || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("Translated", out var translated)
                || translated.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(translated.GetString()))
                throw new InvalidOperationException("阿里云返回了无效译文，本次结果未保存。");
            results.Add(translated.GetString()!);
        }
        ct.ThrowIfCancellationRequested();
        return results;
    }

    private static bool IsSuccess(JsonElement code) => code.ValueKind switch
    {
        JsonValueKind.Number => code.TryGetInt32(out var value) && value == 200,
        JsonValueKind.String => code.GetString() == "200",
        _ => false
    };

    private static string Sign(SortedDictionary<string, string> parameters, string secret)
    {
        var canonical = string.Join("&", parameters.Select(parameter =>
            Uri.EscapeDataString(parameter.Key) + "=" + Uri.EscapeDataString(parameter.Value)));
        var stringToSign = "POST&%2F&" + Uri.EscapeDataString(canonical);
        var secretBytes = Encoding.UTF8.GetBytes(secret + "&");
        try
        {
            return Convert.ToBase64String(HMACSHA1.HashData(secretBytes, Encoding.UTF8.GetBytes(stringToSign)));
        }
        finally { CryptographicOperations.ZeroMemory(secretBytes); }
    }
}
