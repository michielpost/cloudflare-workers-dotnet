using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Shared;

namespace BlazorWebApp.Services;

/// <summary>
/// Turns the worker bearer token into a <see cref="ClaimsPrincipal"/>, the same
/// way the default Blazor WASM template wires auth. Roles come from the /me
/// call, so <c>AuthorizeView Roles="admin"</c> and <c>[Authorize(Roles)]</c>
/// work natively against the roles the admin assigns in D1.
/// </summary>
public sealed class WorkerAuthStateProvider : AuthenticationStateProvider
{
    public const string Scheme = "cf-bearer";

    readonly AuthTokenStore _store;
    readonly WorkerApi _api;

    public WorkerAuthStateProvider(AuthTokenStore store, WorkerApi api)
    {
        _store = store;
        _api = api;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        => new(await BuildPrincipalAsync());

    async Task<ClaimsPrincipal> BuildPrincipalAsync()
    {
        var token = await _store.GetAsync();
        if (string.IsNullOrEmpty(token))
            return new ClaimsPrincipal(new ClaimsIdentity());

        var me = await _api.GetAsync<AuthUser>("/api/auth/me");
        if (me.Ok && me.Value is not null)
            return PrincipalFor(me.Value);

        // The token is stale or the user was removed; drop it so the UI shows
        // signed out instead of an unauthenticated error on every page.
        await _store.ClearAsync();
        return new ClaimsPrincipal(new ClaimsIdentity());
    }

    static ClaimsPrincipal PrincipalFor(AuthUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName)
        };

        foreach (var role in user.Roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
    }

    /// <summary>Called right after login/register so the app re-renders signed in.</summary>
    public void NotifySignIn(AuthUser user)
        => NotifyAuthenticationStateChanged(
            Task.FromResult(new AuthenticationState(PrincipalFor(user))));

    /// <summary>Called after logout so the app re-renders signed out.</summary>
    public void NotifySignOut()
        => NotifyAuthenticationStateChanged(
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()))));
}
