using System.Net.Http.Headers;

namespace BlazorWebApp.Services;

/// <summary>
/// Attaches the worker bearer token to every outbound request when the user is
/// signed in. Registered as the handler behind the shared <see cref="HttpClient"/>,
/// so the admin pages and the auth calls all authenticate automatically.
/// </summary>
public sealed class AuthTokenHandler : DelegatingHandler
{
    readonly AuthTokenStore _store;

    public AuthTokenHandler(AuthTokenStore store)
    {
        _store = store;
        InnerHandler = new HttpClientHandler();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await _store.GetAsync();
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, cancellationToken);
    }
}
