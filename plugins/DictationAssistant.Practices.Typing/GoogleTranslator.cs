// SPDX-License-Identifier: AGPL-3.0-or-later
// Modified 2026-10-20: official Google Cloud Translation Basic v2.
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DictationAssistant.Practices.Typing;

internal static class GoogleTranslator
{
    // https://cloud.google.com/translate/docs/reference/rest/v2/translate
    // https://cloud.google.com/apis/docs/system-parameters (X-Goog-Api-Key)
    public static async Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts,
        TranslationEngineBinding binding, string sourceLanguage, string targetLanguage,
        CancellationToken cancellationToken)
    {
        var source = sourceLanguage == "EN" ? "en" : "zh-CN";
        var target = targetLanguage == "EN" ? "en" : "zh-CN";
        var batches = await Task.Run(() => TranslationBatches.Prepare(texts, 100, 20000,
            batch => JsonSerializer.SerializeToUtf8Bytes(new { q = batch, source, target, format = "text" }),
            "Google 翻译", cancellationToken), cancellationToken).ConfigureAwait(false);
        var result = new List<string>(texts.Count);
        foreach (var batch in batches)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                "https://translation.googleapis.com/language/translate/v2");
            request.Headers.Add("X-Goog-Api-Key", binding.ApiKey);
            request.Content = new ByteArrayContent(batch.Body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            using var response = await TranslationHttp.SendAsync(request, "Google 翻译", cancellationToken).ConfigureAwait(false);
            if (response.RootElement.ValueKind != JsonValueKind.Object
                || !response.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("translations", out var translations) || translations.ValueKind != JsonValueKind.Array
                || translations.GetArrayLength() != batch.Count)
                throw InvalidResponse();
            foreach (var item in translations.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("translatedText", out var text)
                    || text.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(text.GetString()))
                    throw InvalidResponse();
                result.Add(WebUtility.HtmlDecode(text.GetString()!));
            }
        }
        return result;
    }

    private static InvalidOperationException InvalidResponse() =>
        new("Google 翻译返回的译文无效或数量不匹配，本次结果未保存。");
}
