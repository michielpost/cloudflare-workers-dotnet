namespace Shared;

// Models for the auth feature, shared by the worker (which serializes them in
// its API responses) and the Blazor frontend (which deserializes them). These
// are plain records so they work in both the worker's focused compile profile
// and the Blazor WebAssembly app.

/// <summary>Request body of POST /api/auth/register.</summary>
public sealed record RegisterRequest(string Email, string Password, string DisplayName);

/// <summary>Request body of POST /api/auth/login.</summary>
public sealed record LoginRequest(string Email, string Password);

/// <summary>Request body of POST /api/auth/roles (admin assigns roles to a user).</summary>
public sealed record AssignRolesRequest(string Email, IReadOnlyList<string> Roles);

/// <summary>Request body of POST /api/auth/profile (the caller updates their own display name).</summary>
public sealed record UpdateProfileRequest(string DisplayName);

/// <summary>The caller's own identity, returned by login/register and /me.</summary>
public sealed record AuthUser(long Id, string Email, string DisplayName, IReadOnlyList<string> Roles);

/// <summary>Result of a successful login or registration: the bearer token plus the user.</summary>
public sealed record AuthResponse(string Token, AuthUser User);

/// <summary>One user in the admin user list.</summary>
public sealed record AdminUser(long Id, string Email, string DisplayName, IReadOnlyList<string> Roles);

/// <summary>
/// Everything GET /api/auth/users returns: all users with their roles plus the
/// full set of assignable roles, so the admin UI never has to hard-code either.
/// </summary>
public sealed record UsersSnapshot(IReadOnlyList<AdminUser> Users, IReadOnlyList<string> Roles);
