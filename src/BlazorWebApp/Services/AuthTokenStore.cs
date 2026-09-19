using Microsoft.JSInterop;

namespace BlazorWebApp.Services;

/// <summary>
/// Keeps the worker bearer token in localStorage so a page refresh keeps the
/// user signed in. The value is cached in memory so the Blazor side never has to
/// round-trip to the browser just to read the token on every request.
/// </summary>
public sealed class AuthTokenStore
{
    const string Key = "cf_auth_token";

    readonly IJSRuntime _js;
    string? _cached;
    bool _loaded;

    public AuthTokenStore(IJSRuntime js) => _js = js;

    /// <summary>The raw bearer token, or null when the user is signed out.</summary>
    public async Task<string?> GetAsync()
    {
        if (_loaded)
            return _cached;

        try
        {
            _cached = await _js.InvokeAsync<string?>("localStorage.getItem", Key);
        }
        catch
        {
            _cached = null;
        }

        _loaded = true;
        return _cached;
    }

    public async Task SetAsync(string? token)
    {
        _cached = token;
        _loaded = true;

        try
        {
            await _js.InvokeVoidAsync("localStorage.setItem", Key, token ?? "");
        }
        catch
        {
            // Storage is best-effort; a blocked browser tab should not crash auth.
        }
    }

    public Task ClearAsync() => SetAsync(null);
}
