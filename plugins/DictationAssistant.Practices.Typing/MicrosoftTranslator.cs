// SPDX-License-Identifier: AGPL-3.0-or-later
// Modified 2026-10-20: official Microsoft Translator text translation API.
using System.Net.Http.Headers;
using System.Text.Json;

namespace DictationAssistant.Practices.Typing;

internal static class MicrosoftTranslator
{
    // https://learn.microsoft.com/azure/ai-services/translator/text-translation/reference/v3/translate
    public static async Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts,
        TranslationEngineBinding binding, string sourceLanguage, string targetLanguage,
        CancellationToken cancellationToken)
    {
        var source = sourceLanguage == "EN" ? "en" : "zh-Hans";
        var target = targetLanguage == "EN" ? "en" : "zh-Hans";
        var batches = await Task.Run(() => TranslationBatches.Prepare(texts, 100, 45000,
            batch => JsonSerializer.SerializeToUtf8Bytes(batch.Select(text => new { Text = text })),
            "必应 / Microsoft 翻译", cancellationToken), cancellationToken).ConfigureAwait(false);
        var result = new List<string>(texts.Count);
        foreach (var batch in batches)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://api.cognitive.microsofttranslator.com/translate?api-version=3.0&from={source}&to={target}&textType=plain");
            request.Headers.Add("Ocp-Apim-Subscription-Key", binding.ApiKey);
            if (!string.IsNullOrEmpty(binding.Region))
                request.Headers.Add("Ocp-Apim-Subscription-Region", binding.Region);
            request.Content = new ByteArrayContent(batch.Body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            using var response = await TranslationHttp.SendAsync(request, "必应 / Microsoft 翻译", cancellationToken).ConfigureAwait(false);
            if (response.RootElement.ValueKind != JsonValueKind.Array
                || response.RootElement.GetArrayLength() != batch.Count)
                throw InvalidResponse();
            foreach (var item in response.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("translations", out var translations)
                    || translations.ValueKind != JsonValueKind.Array || translations.GetArrayLength() != 1)
                    throw InvalidResponse();
                var translation = translations[0];
                if (translation.ValueKind != JsonValueKind.Object || !translation.TryGetProperty("text", out var text)
                    || text.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(text.GetString()))
                    throw InvalidResponse();
                result.Add(text.GetString()!);
            }
        }
        return result;
    }

    private static InvalidOperationException InvalidResponse() =>
        new("必应 / Microsoft 翻译返回的译文无效或数量不匹配，本次结果未保存。");
}
