using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Shared;

namespace BlazorWebApp.Services;

/// <summary>
/// One line in the "calls" table every sample page shows. Keeping the status,
/// duration and the interesting response header makes it obvious what the worker
/// actually answered.
/// </summary>
public sealed record ApiCallEntry(
    DateTimeOffset At,
    string Label,
    string Method,
    string Path,
    int Status,
    string Reason,
    double Milliseconds,
    string? Highlight,
    bool Ok);

/// <summary>Outcome of a single call to the worker, parsed for the UI.</summary>
public sealed class ApiResult<T>
{
    public bool Ok { get; init; }

    public int Status { get; init; }

    public string Reason { get; init; } = "";

    /// <summary>Deserialized payload, when the worker answered with JSON.</summary>
    public T? Value { get; init; }

    /// <summary>Raw response body, so a page can always show what came back.</summary>
    public string Raw { get; init; } = "";

    /// <summary>Validation message from <see cref="ErrorPayload"/>, if any.</summary>
    public string? Error { get; init; }

    /// <summary>Set when the request never reached the worker (worker not running, CORS, ...).</summary>
    public string? TransportError { get; init; }

    public double Milliseconds { get; init; }

    /// <summary>Value of the header the caller asked to highlight, e.g. <c>x-cache</c>.</summary>
    public string? Highlight { get; init; }

    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>();

    /// <summary>True when the worker answered 2xx and the body parsed.</summary>
    public bool HasValue => Ok && Value is not null;

    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;

    public ApiCallEntry ToLogEntry(string label, string method, string path)
        => new(
            DateTimeOffset.Now,
            label,
            method,
            path,
            Status,
            TransportError is null ? Reason : "no response",
            Milliseconds,
            Highlight,
            Ok);
}

/// <summary>Result of a raw download: the bytes plus what the worker said about them.</summary>
public sealed record DownloadResult<T>(
    ApiResult<T> Result,
    byte[] Bytes,
    string ContentType,
    string? FileName,
    string Text);

/// <summary>
/// Thin wrapper around <see cref="HttpClient"/> that talks to the Cloudflare
/// Worker. It centralizes the base URL, timing, header capture and JSON parsing
/// so the individual sample pages stay small.
/// </summary>
public sealed class WorkerApi
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    readonly HttpClient _http;

    public WorkerApi(HttpClient http, IConfiguration config, IWebAssemblyHostEnvironment hostEnvironment)
    {
        _http = http;
        BaseUrl = (config["ApiBaseUrl"] ?? "").TrimEnd('/');
        IsPrerendering = string.Equals(hostEnvironment.Environment, "Prerendering", StringComparison.Ordinal);
    }

    /// <summary>Base URL of the worker, e.g. http://localhost:8787 during local dev.</summary>
    public string BaseUrl { get; }

    /// <summary>True while the build-time prerenderer is rendering static pages.</summary>
    public bool IsPrerendering { get; }

    /// <summary>Base URL shown in the UI; an empty base means "same origin as this page".</summary>
    public string DisplayBaseUrl => BaseUrl.Length == 0
        ? $"{_http.BaseAddress?.ToString().TrimEnd('/')} (same origin)"
        : BaseUrl;

    /// <summary>Builds the absolute URL of a worker path.</summary>
    public string Url(string path) => BaseUrl + path;

    public Task<ApiResult<T>> GetAsync<T>(
        string path,
        string? highlightHeader = null,
        CancellationToken cancellationToken = default)
        => SendAsync<T>(new HttpRequestMessage(HttpMethod.Get, Url(path)), highlightHeader, cancellationToken);

    public Task<ApiResult<T>> PostJsonAsync<T>(
        string path,
        object payload,
        string? highlightHeader = null,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Url(path))
        {
            Content = JsonContent.Create(payload, options: Json)
        };

        return SendAsync<T>(request, highlightHeader, cancellationToken);
    }

    public Task<ApiResult<T>> PostAsync<T>(
        string path,
        string? highlightHeader = null,
        CancellationToken cancellationToken = default)
        => SendAsync<T>(new HttpRequestMessage(HttpMethod.Post, Url(path)), highlightHeader, cancellationToken);

    /// <summary>POSTs a raw body, used by the R2 upload sample.</summary>
    public Task<ApiResult<T>> PostRawAsync<T>(
        string path,
        HttpContent content,
        string? highlightHeader = null,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Url(path)) { Content = content };
        return SendAsync<T>(request, highlightHeader, cancellationToken);
    }

    /// <summary>Downloads the response body as bytes, used by the R2 download sample.</summary>
    public async Task<DownloadResult<T>> DownloadAsync<T>(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (IsPrerendering)
            return new DownloadResult<T>(SkippedResult<T>(), [], "", null, "");

        var request = new HttpRequestMessage(HttpMethod.Get, Url(path));
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            stopwatch.Stop();

            var contentType = response.Content.Headers.ContentType?.ToString() ?? "";
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

            var result = new ApiResult<T>
            {
                Ok = response.IsSuccessStatusCode,
                Status = (int)response.StatusCode,
                Reason = response.ReasonPhrase ?? "",
                Milliseconds = stopwatch.Elapsed.TotalMilliseconds,
                Headers = CollectHeaders(response)
            };

            var text = LooksLikeText(contentType) ? Encoding.UTF8.GetString(bytes) : "";

            return new DownloadResult<T>(result, bytes, contentType, fileName, text);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new DownloadResult<T>(
                new ApiResult<T>
                {
                    TransportError = ex.Message,
                    Milliseconds = stopwatch.Elapsed.TotalMilliseconds
                },
                [],
                "",
                null,
                "");
        }
    }

    async Task<ApiResult<T>> SendAsync<T>(
        HttpRequestMessage request,
        string? highlightHeader,
        CancellationToken cancellationToken)
    {
        if (IsPrerendering)
            return SkippedResult<T>();

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();

            var headers = CollectHeaders(response);

            return new ApiResult<T>
            {
                Ok = response.IsSuccessStatusCode,
                Status = (int)response.StatusCode,
                Reason = response.ReasonPhrase ?? "",
                Raw = raw,
                Milliseconds = stopwatch.Elapsed.TotalMilliseconds,
                Headers = headers,
                Value = TryParse<T>(raw),
                Error = TryParse<ErrorPayload>(raw)?.Error,
                Highlight = highlightHeader is not null && headers.TryGetValue(highlightHeader, out var value)
                    ? value
                    : null
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ApiResult<T>
            {
                TransportError = ex.Message,
                Milliseconds = stopwatch.Elapsed.TotalMilliseconds
            };
        }
    }

    static ApiResult<T> SkippedResult<T>() => new()
    {
        Reason = "Skipped during prerendering"
    };

    static IReadOnlyDictionary<string, string> CollectHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in response.Headers)
            headers[header.Key] = string.Join(", ", header.Value);

        foreach (var header in response.Content.Headers)
            headers[header.Key] = string.Join(", ", header.Value);

        return headers;
    }

    static bool LooksLikeText(string contentType)
        => contentType.Length == 0
           || contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
           || contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
           || contentType.Contains("xml", StringComparison.OrdinalIgnoreCase);

    static T? TryParse<T>(string raw)
    {
        if (raw.Length == 0)
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(raw, Json);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
