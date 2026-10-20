// Source distributed under the AGPL-3.0-or-later terms of DictationAssistant.
// Modified 2026-10-20: optional signed Baidu text translation over the official HTTPS API.
// Protocol reference: https://fanyi-api.baidu.com/doc/21
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DictationAssistant.Practices.Typing;

internal static class BaiduTranslator
{
    private const string Endpoint = "https://fanyi-api.baidu.com/api/trans/vip/translate";
    private const int MaximumTextBytes = 6000;

    public static async Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts,
        TranslationEngineBinding binding, string sourceLanguage, string targetLanguage, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(binding);
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(binding.ApiKey) || string.IsNullOrWhiteSpace(binding.SecretKey))
            throw new InvalidOperationException("请在百度翻译配置中填写 APP ID 和密钥。");
        var source = NormalizeLanguage(sourceLanguage);
        var target = NormalizeLanguage(targetLanguage);
        var pending = texts.ToArray();
        for (var index = 0; index < pending.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            var text = pending[index];
            if (string.IsNullOrWhiteSpace(text) || text.Contains('\0'))
                throw new InvalidOperationException($"第 {index + 1} 条词条为空或无效，请检查词表。");
            if (Encoding.UTF8.GetByteCount(text) > MaximumTextBytes)
                throw new InvalidOperationException($"第 {index + 1} 条词条超过百度翻译单次 6000 字节限制，请缩短后重试。");
        }

        var results = new List<string>(pending.Length);
        for (var index = 0; index < pending.Length; index++)
        {
            // Basic Baidu accounts permit one request per second. Individual requests also
            // preserve the outer word-to-translation mapping when a word contains newlines.
            if (index > 0) await Task.Delay(TimeSpan.FromMilliseconds(1100), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var query = pending[index];
            var salt = RandomNumberGenerator.GetInt32(1, int.MaxValue).ToString(CultureInfo.InvariantCulture);
            var signatureBytes = Encoding.UTF8.GetBytes(binding.ApiKey + query + salt + binding.SecretKey);
            string signature;
            try { signature = Convert.ToHexStringLower(MD5.HashData(signatureBytes)); }
            finally { CryptographicOperations.ZeroMemory(signatureBytes); }
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["q"] = query, ["from"] = source, ["to"] = target,
                    ["appid"] = binding.ApiKey, ["salt"] = salt, ["sign"] = signature
                })
            };
            using var document = await TranslationHttp.SendAsync(request, "百度翻译", ct).ConfigureAwait(false);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw InvalidResponse();
            if (root.TryGetProperty("error_code", out var error))
            {
                var code = error.ValueKind == JsonValueKind.String ? error.GetString()
                    : error.ValueKind == JsonValueKind.Number ? error.GetRawText() : null;
                if (code is not ("0" or "52000")) throw new InvalidOperationException(ErrorMessage(code));
            }
            if (!root.TryGetProperty("trans_result", out var translations)
                || translations.ValueKind != JsonValueKind.Array || translations.GetArrayLength() == 0)
                throw InvalidResponse();
            var paragraphs = new List<string>(translations.GetArrayLength());
            foreach (var translation in translations.EnumerateArray())
            {
                if (translation.ValueKind != JsonValueKind.Object || !translation.TryGetProperty("dst", out var value)
                    || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                    throw InvalidResponse();
                paragraphs.Add(value.GetString()!);
            }
            results.Add(string.Join("\n", paragraphs));
        }
        ct.ThrowIfCancellationRequested();
        return results;
    }

    private static string NormalizeLanguage(string language) => language?.Trim().ToUpperInvariant() switch
    {
        "EN" => "en",
        "ZH" => "zh",
        _ => throw new InvalidOperationException("百度翻译目前仅支持本插件的中文与英文互译。")
    };

    private static InvalidOperationException InvalidResponse() =>
        new("百度翻译返回了无效译文，本次结果未保存，请稍后重试。");

    private static string ErrorMessage(string? code) => code switch
    {
        "52001" => "百度翻译请求超时，请稍后重试。",
        "52002" => "百度翻译服务暂时不可用，请稍后重试。",
        "52003" or "54001" => "百度翻译授权或签名校验失败，请检查 APP ID 和密钥。",
        "54000" => "百度翻译缺少必要参数，请检查配置后重试。",
        "54003" or "54005" => "百度翻译访问频率受限，请稍后重试。",
        "54004" => "百度翻译账户余额或可用额度不足，请检查控制台用量。",
        "58000" => "百度翻译拒绝了当前网络来源，请检查账户的 IP 访问限制。",
        "58001" => "百度翻译不支持所选语言方向，请检查配置。",
        "58002" or "90107" => "百度翻译服务尚未开通或认证未完成，请在官方控制台检查。",
        _ => "百度翻译未能完成请求，请检查官方控制台的服务状态与配置后重试。"
    };
}
