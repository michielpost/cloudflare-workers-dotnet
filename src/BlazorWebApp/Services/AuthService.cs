using Shared;

namespace BlazorWebApp.Services;

/// <summary>Outcome of a login or registration attempt, for the login page.</summary>
public sealed record AuthResult(bool Ok, AuthUser? User, string? Error);

/// <summary>
/// Frontend wrapper around the worker's /api/auth/* endpoints. It talks through
/// the shared <see cref="WorkerApi"/> (so CORS and the base URL are handled
/// once) and, after a successful sign-in or logout, tells the
/// <see cref="WorkerAuthStateProvider"/> to refresh the app's auth state.
/// </summary>
public sealed class AuthService
{
    readonly WorkerApi _api;
    readonly AuthTokenStore _store;
    readonly WorkerAuthStateProvider _state;

    public AuthService(WorkerApi api, AuthTokenStore store, WorkerAuthStateProvider state)
    {
        _api = api;
        _store = store;
        _state = state;
    }

    public async Task<AuthResult> RegisterAsync(string email, string password)
    {
        // Display name is intentionally not collected at sign-up; it is set
        // later from the account page (the worker accepts an empty name).
        var res = await _api.PostJsonAsync<AuthResponse>(
            "/api/auth/register",
            new RegisterRequest(email, password, ""));

        return await ApplyAsync(res);
    }

    public async Task<AuthResult> LoginAsync(string email, string password)
    {
        var res = await _api.PostJsonAsync<AuthResponse>(
            "/api/auth/login",
            new LoginRequest(email, password));

        return await ApplyAsync(res);
    }

    async Task<AuthResult> ApplyAsync(ApiResult<AuthResponse> res)
    {
        if (res.Ok && res.Value is not null)
        {
            await _store.SetAsync(res.Value.Token);
            _state.NotifySignIn(res.Value.User);
            return new AuthResult(true, res.Value.User, null);
        }

        return new AuthResult(false, null, res.Error ?? res.Reason ?? "Something went wrong");
    }

    public async Task LogoutAsync()
    {
        await _api.PostAsync<object>("/api/auth/logout");
        await _store.ClearAsync();
        _state.NotifySignOut();
    }

    public Task<ApiResult<UsersSnapshot>> GetUsersAsync()
        => _api.GetAsync<UsersSnapshot>("/api/auth/users");

    public Task<ApiResult<AuthUser>> AssignRolesAsync(string email, IReadOnlyList<string> roles)
        => _api.PostJsonAsync<AuthUser>("/api/auth/roles", new AssignRolesRequest(email, roles));

    /// <summary>
    /// Updates the caller's own display name on the worker and refreshes the
    /// app's auth state so the new name shows up everywhere immediately.
    /// </summary>
    public async Task<ApiResult<AuthUser>> UpdateProfileAsync(string displayName)
    {
        var res = await _api.PostJsonAsync<AuthUser>(
            "/api/auth/profile",
            new UpdateProfileRequest(displayName));

        if (res.Ok && res.Value is not null)
        {
            _state.NotifySignIn(res.Value);
        }

        return res;
    }
}
