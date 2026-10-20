// Source distributed under the AGPL-3.0-or-later terms of DictationAssistant.
// Modified 2026-10-20: optional Youdao v3 signed text translation over the official HTTPS API.
// Protocol: https://ai.youdao.com/DOCSIRMA/html/trans/api/wbfy/index.html
// Unicode input truncation follows the official Python3 example's codepoint semantics:
// https://download.ydstatic.com/ead/TranslateDemo-py1.zip (AuthV3Util.py).
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DictationAssistant.Practices.Typing;

internal static class YoudaoTranslator
{
    private const string Endpoint = "https://openapi.youdao.com/api";
    private const int MaximumTextCharacters = 5000;

    public static async Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts,
        TranslationEngineBinding binding, string sourceLanguage, string targetLanguage, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(binding);
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(binding.ApiKey) || string.IsNullOrWhiteSpace(binding.SecretKey))
            throw new InvalidOperationException("请在有道智云配置中填写应用 ID（AppKey）和应用密钥。");
        var source = NormalizeLanguage(sourceLanguage);
        var target = NormalizeLanguage(targetLanguage);
        var pending = texts.ToArray();
        var signatureInputs = new string[pending.Length];
        for (var index = 0; index < pending.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            var text = pending[index];
            if (string.IsNullOrWhiteSpace(text) || text.Contains('\0'))
                throw new InvalidOperationException($"第 {index + 1} 条词条为空或无效，请检查词表。");
            var runes = text.EnumerateRunes().ToArray();
            if (runes.Length > MaximumTextCharacters)
                throw new InvalidOperationException($"第 {index + 1} 条词条超过有道智云单次 5000 字符限制，请缩短后重试。");
            signatureInputs[index] = BuildSignatureInput(text, runes);
        }

        var results = new List<string>(pending.Length);
        for (var index = 0; index < pending.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            var salt = Guid.NewGuid().ToString();
            var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var signatureBytes = Encoding.UTF8.GetBytes(binding.ApiKey + signatureInputs[index] + salt
                + currentTime + binding.SecretKey);
            string signature;
            try { signature = Convert.ToHexStringLower(SHA256.HashData(signatureBytes)); }
            finally { CryptographicOperations.ZeroMemory(signatureBytes); }
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["q"] = pending[index], ["from"] = source, ["to"] = target,
                    ["appKey"] = binding.ApiKey, ["salt"] = salt, ["curtime"] = currentTime,
                    ["signType"] = "v3", ["sign"] = signature, ["strict"] = "true"
                })
            };
            using var document = await TranslationHttp.SendAsync(request, "有道智云", ct).ConfigureAwait(false);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("errorCode", out var error))
                throw InvalidResponse();
            var code = error.ValueKind == JsonValueKind.String ? error.GetString()
                : error.ValueKind == JsonValueKind.Number ? error.GetRawText() : null;
            if (code != "0") throw new InvalidOperationException(ErrorMessage(code));
            if (!root.TryGetProperty("translation", out var translations)
                || translations.ValueKind != JsonValueKind.Array || translations.GetArrayLength() == 0)
                throw InvalidResponse();
            var paragraphs = new List<string>(translations.GetArrayLength());
            foreach (var translation in translations.EnumerateArray())
            {
                if (translation.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(translation.GetString()))
                    throw InvalidResponse();
                paragraphs.Add(translation.GetString()!);
            }
            results.Add(string.Join("\n", paragraphs));
        }
        ct.ThrowIfCancellationRequested();
        return results;
    }

    private static string BuildSignatureInput(string text, Rune[] runes)
    {
        if (runes.Length <= 20) return text;
        var input = new StringBuilder();
        for (var index = 0; index < 10; index++) input.Append(runes[index].ToString());
        input.Append(runes.Length.ToString(CultureInfo.InvariantCulture));
        for (var index = runes.Length - 10; index < runes.Length; index++) input.Append(runes[index].ToString());
        return input.ToString();
    }

    private static string NormalizeLanguage(string language) => language?.Trim().ToUpperInvariant() switch
    {
        "EN" => "en",
        "ZH" => "zh-CHS",
        _ => throw new InvalidOperationException("有道智云目前仅支持本插件的中文与英文互译。")
    };

    private static InvalidOperationException InvalidResponse() =>
        new("有道智云返回了无效译文，本次结果未保存，请稍后重试。");

    private static string ErrorMessage(string? code) => code switch
    {
        "108" or "111" or "202" => "有道智云授权或签名校验失败，请检查应用 ID 和应用密钥。",
        "110" or "112" or "205" => "有道智云应用未绑定有效的文本翻译服务，请检查官方控制台配置。",
        "102" => "有道智云不支持所选语言方向，请检查配置。",
        "103" => "有道智云认为文本过长，请缩短词条后重试。",
        "203" => "有道智云拒绝了当前网络来源，请检查账户的 IP 访问限制。",
        "206" => "有道智云时间戳校验失败，请同步电脑时间后重试。",
        "207" => "有道智云拒绝了重复请求，请稍后重试。",
        "401" => "有道智云账户已欠费或额度不足，请检查官方控制台用量。",
        "411" or "412" => "有道智云访问频率受限，请稍后重试。",
        "301" or "302" or "303" or "304" => "有道智云翻译服务暂时不可用，请稍后重试。",
        _ => "有道智云未能完成请求，请检查官方控制台的服务状态与配置后重试。"
    };
}
