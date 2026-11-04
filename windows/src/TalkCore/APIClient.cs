using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HydayTalk.Core;

public static class APIClient
{
    public static readonly HttpClient Shared = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly string[] LocalHosts = ["localhost", "127.0.0.1", "::1", "[::1]"];

    public static bool IsLocal(Uri url) => LocalHosts.Contains(url.Host) || LocalHosts.Contains(url.IdnHost);

    public static Uri Endpoint(string baseURL, string path)
    {
        if (!Uri.TryCreate(baseURL.Trim(), UriKind.Absolute, out var url) || url.Host.Length == 0
            || url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0
            || !(url.Scheme == "https" || (url.Scheme == "http" && IsLocal(url))))
            throw new TalkException("服務網址需使用 HTTPS；本機服務可使用 http://localhost。請填入包含 /v1 的基底網址。");
        var text = url.ToString();
        return new Uri(text.EndsWith('/') ? text + path : text + "/" + path);
    }

    public static async Task<string> TranscriptionAsync(byte[] audio, APISettings settings, string key, string language,
        IReadOnlyList<string> dictionary, HttpClient? client = null, CancellationToken cancellation = default)
    {
        if (settings.Model.Trim().Length == 0) throw new TalkException("請填入辨識模型名稱。");
        using var form = new MultipartFormDataContent("HydayTalk-" + Guid.NewGuid());
        form.Add(new StringContent(settings.Model), "model");
        form.Add(new StringContent("json"), "response_format");
        // New gpt-transcribe uses a languages array; older compatible models use language.
        form.Add(new StringContent(language), settings.Model == "gpt-transcribe" ? "languages[]" : "language");
        if (dictionary.Count > 0)
        {
            var prompt = string.Join(", ", dictionary);
            form.Add(new StringContent(prompt.Length > 800 ? prompt[..800] : prompt), "prompt");
        }
        var file = new ByteArrayContent(audio);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", "dictation.wav");
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(settings.BaseURL, "audio/transcriptions")) { Content = form };
        Authorize(request, key);
        var data = await SendAsync(request, client, TimeSpan.FromSeconds(120), cancellation);
        string? text = null;
        try { text = JsonNode.Parse(data)?["text"]?.GetValue<string>(); } catch (Exception e) when (e is JsonException or InvalidOperationException) { }
        if (string.IsNullOrWhiteSpace(text)) throw new TalkException("辨識服務未回傳文字。請確認模型與音訊辨識端點。");
        return text;
    }

    public static async Task<string> CompleteAsync(string prompt, APISettings settings, string key,
        HttpClient? client = null, CancellationToken cancellation = default)
    {
        if (settings.Model.Trim().Length == 0) throw new TalkException("請填入文字模型名稱。");
        var body = new JsonObject
        {
            ["model"] = settings.Model,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = "You are a careful writing assistant. Follow the task and return only the requested text." },
                new JsonObject { ["role"] = "user", ["content"] = prompt }),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(settings.BaseURL, "chat/completions"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        Authorize(request, key);
        var data = await SendAsync(request, client, TimeSpan.FromSeconds(60), cancellation);
        string? text = null;
        try { text = JsonNode.Parse(data)?["choices"]?[0]?["message"]?["content"]?.GetValue<string>()?.Trim(); }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { }
        if (string.IsNullOrEmpty(text)) throw new TalkException("文字服務未回傳內容。請確認模型支援 chat/completions。");
        return text;
    }

    private static void Authorize(HttpRequestMessage request, string key)
    {
        if (key.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
    }

    private static async Task<byte[]> SendAsync(HttpRequestMessage request, HttpClient? client, TimeSpan timeout, CancellationToken cancellation)
    {
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timer.CancelAfter(timeout);
        HttpResponseMessage response;
        try { response = await (client ?? Shared).SendAsync(request, timer.Token); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new TalkException("服務回應逾時，請稍後重試。"); }
        catch (HttpRequestException) { throw new TalkException("無法連接服務，請確認網路與服務網址。"); }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new TalkException((int)response.StatusCode switch
                {
                    401 or 403 => "服務拒絕存取。請確認 API Key 與模型權限。",
                    404 => "找不到端點或模型。請確認服務網址包含 /v1 與模型名稱。",
                    429 => "服務額度或請求次數已達上限，請稍後重試。",
                    var code => $"服務回應 HTTP {code}，請稍後重試。",
                });
            }
            var data = await response.Content.ReadAsByteArrayAsync(timer.Token);
            if (data.Length > 2_000_000) throw new TalkException("服務回傳的內容過大。");
            return data;
        }
    }
}
