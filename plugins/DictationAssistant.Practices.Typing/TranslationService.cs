// SPDX-License-Identifier: AGPL-3.0-or-later
// Modified 2026-10-20: dispatch preparation requests to the selected official service.
namespace DictationAssistant.Practices.Typing;

internal static class TranslationService
{
    public static async Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts,
        TranslationEngineBinding binding, string sourceLanguage, string targetLanguage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        TranslationEngineProviders.Validate(binding);
        if (sourceLanguage is not ("EN" or "ZH") || targetLanguage is not ("EN" or "ZH"))
            throw new ArgumentException("请选择英文与中文之间的翻译方向。");
        await Task.Run(() =>
        {
            for (var index = 0; index < texts.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(texts[index]) || texts[index].Contains('\0'))
                    throw new InvalidOperationException($"第 {index + 1} 条词条为空或无效，请检查词表。");
            }
        }, cancellationToken).ConfigureAwait(false);
        if (texts.Count == 0) return Array.Empty<string>();
        var result = await (binding.Provider switch
        {
            "deepl-free" or "deepl-pro" => DeepLTranslator.TranslateAsync(texts, binding.ApiKey,
                binding.Provider == "deepl-free", sourceLanguage, targetLanguage, cancellationToken),
            "microsoft-translator" => MicrosoftTranslator.TranslateAsync(texts, binding, sourceLanguage, targetLanguage, cancellationToken),
            "google-cloud" => GoogleTranslator.TranslateAsync(texts, binding, sourceLanguage, targetLanguage, cancellationToken),
            "baidu" => BaiduTranslator.TranslateAsync(texts, binding, sourceLanguage, targetLanguage, cancellationToken),
            "youdao" => YoudaoTranslator.TranslateAsync(texts, binding, sourceLanguage, targetLanguage, cancellationToken),
            "tencent" => TencentTranslator.TranslateAsync(texts, binding, sourceLanguage, targetLanguage, cancellationToken),
            "aliyun" => AliyunTranslator.TranslateAsync(texts, binding, sourceLanguage, targetLanguage, cancellationToken),
            "volcengine" => VolcengineTranslator.TranslateAsync(texts, binding, sourceLanguage, targetLanguage, cancellationToken),
            _ => throw new InvalidOperationException("请选择受支持的翻译引擎。")
        }).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Count != texts.Count || result.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("翻译服务返回的译文数量或内容无效，本次结果未保存。");
        return result;
    }
}
