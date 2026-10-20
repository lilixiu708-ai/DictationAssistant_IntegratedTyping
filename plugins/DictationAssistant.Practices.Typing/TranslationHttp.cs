// SPDX-License-Identifier: AGPL-3.0-or-later
// Modified 2026-10-20: bounded, cancellable requests to official translation APIs.
using System.Net;
using System.Text.Json;

namespace DictationAssistant.Practices.Typing;

internal static class TranslationHttp
{
    private const int MaximumResponseBytes = 4 * 1024 * 1024;
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    public static async Task<JsonDocument> SendAsync(HttpRequestMessage request, string providerName,
        CancellationToken cancellationToken, Func<HttpStatusCode, string>? statusMessage = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        try
        {
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(statusMessage?.Invoke(response.StatusCode)
                    ?? StatusMessage(providerName, response.StatusCode));
            if (response.Content.Headers.ContentLength > MaximumResponseBytes)
                throw new InvalidOperationException($"{providerName} 响应过大，本次结果未保存。");

            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var body = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                if (body.Length + read > MaximumResponseBytes)
                    throw new InvalidOperationException($"{providerName} 响应过大，本次结果未保存。");
                body.Write(buffer, 0, read);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return JsonDocument.Parse(body.GetBuffer().AsMemory(0, checked((int)body.Length)));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"{providerName} 请求超时，请检查网络后重试。");
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException($"无法连接{providerName}，请检查网络及服务配置。");
        }
        catch (IOException)
        {
            throw new InvalidOperationException($"读取{providerName}响应失败，请稍后重试。");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"{providerName} 返回了无法读取的数据，本次结果未保存。");
        }
    }

    private static string StatusMessage(string provider, HttpStatusCode status) => (int)status switch
    {
        401 or 403 => $"{provider}授权失败，请检查凭据、服务权限及区域配置。",
        429 => $"{provider}请求过于频繁或额度不足，请检查账户用量后重试。",
        400 => $"{provider}拒绝了请求，请检查词条、语言方向及配置。",
        413 => $"{provider}请求内容过大，请缩短词条后重试。",
        >= 500 => $"{provider}服务暂时不可用，请稍后重试。",
        _ => $"{provider}翻译失败（HTTP {(int)status}），本次结果未保存。"
    };
}
