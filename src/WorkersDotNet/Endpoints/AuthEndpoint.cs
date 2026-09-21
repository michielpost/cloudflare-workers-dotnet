using System.Collections.Generic;
using System.Threading.Tasks;
using Shared;
using Workers;
using WorkersDotNet.Auth;

namespace WorkersDotNet
{
    /// <summary>
    /// The auth API: registration, login, session lookup and logout, plus the
    /// admin-only user/role management. This is a thin controller: it reads the
    /// bearer token, parses and validates the HTTP request, then delegates to
    /// <see cref="AuthService"/> and maps the result to a response.
    /// </summary>
    public sealed class AuthEndpoint
    {
        readonly string BearerPrefix = "Bearer ";

        private readonly AuthService _auth;

        public AuthEndpoint(AuthService auth)
        {
            _auth = auth;
        }

        /// <summary>POST /api/auth/register - creates an account and signs it in.</summary>
        public async Task<Response> RegisterAsync(Request request)
        {
            if (request.Method != "POST")
                return Results.Error("Only POST is supported on /api/auth/register", 405);

            RegisterRequest? input;
            try
            {
                input = await request.JsonAsync<RegisterRequest>();
            }
            catch (Exception)
            {
                return Results.Error("Malformed JSON body", 400);
            }

            if (input is null)
                return Results.Error("A JSON body with \"email\", \"password\" and \"displayName\" is required", 400);

            var email = AuthService.Trim(input.Email);
            var displayName = AuthService.Trim(input.DisplayName);
            var password = input.Password ?? "";

            var problem = AuthService.ValidateRegistration(email, displayName, password);
            if (problem is not null)
                return Results.Error(problem, 400);

            var existing = await _auth.FindUserByEmailAsync(email);
            if (existing is not null)
                return Results.Error("An account with that email already exists. Sign in instead.", 409);

            var hash = await AuthPassword.HashAsync(password);
            var user = await _auth.CreateUserAsync(email, displayName, hash);
            if (user is null)
                return Results.Error("Could not create the account. Try a different email.", 409);

            // Everyone starts with "user"; the configured default admin email is
            // promoted to "admin" the moment that account registers.
            var roles = new List<string>();
            roles.Add(SampleConfig.AuthDefaultRole);
            if (_auth.IsDefaultAdmin(email))
                roles.Add(SampleConfig.AuthAdminRole);

            await _auth.AssignRolesAsync(user.Id, roles);

            var token = await _auth.CreateTokenAsync(user.Id);
            var authUser = await _auth.ToAuthUserAsync(user);
            return Response.Json(new AuthResponse(token, authUser), 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>POST /api/auth/login - verifies credentials and signs the user in.</summary>
        public async Task<Response> LoginAsync(Request request)
        {
            if (request.Method != "POST")
                return Results.Error("Only POST is supported on /api/auth/login", 405);

            LoginRequest? input;
            try
            {
                input = await request.JsonAsync<LoginRequest>();
            }
            catch (Exception)
            {
                return Results.Error("Malformed JSON body", 400);
            }

            if (input is null)
                return Results.Error("A JSON body with \"email\" and \"password\" is required", 400);

            var email = AuthService.Trim(input.Email);
            var password = input.Password ?? "";

            if (email.Length == 0 || password.Length == 0)
                return Results.Error("An email and a password are required", 400);

            var user = await _auth.FindUserByEmailAsync(email);
            if (user is null)
                return Results.Error("Invalid email or password", 401);

            var valid = await AuthPassword.VerifyAsync(password, user.PasswordHash);
            if (!valid)
                return Results.Error("Invalid email or password", 401);

            var token = await _auth.CreateTokenAsync(user.Id);
            var authUser = await _auth.ToAuthUserAsync(user);
            return Response.Json(new AuthResponse(token, authUser), 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>GET /api/auth/me - returns the caller's identity from their bearer token.</summary>
        public async Task<Response> MeAsync(Request request)
        {
            if (request.Method != "GET")
                return Results.Error("Only GET is supported on /api/auth/me", 405);

            var token = BearerToken(request);
            var user = await _auth.FindUserByTokenAsync(token);
            if (user is null)
                return Results.Error("Not signed in", 401);

            var authUser = await _auth.ToAuthUserAsync(user);
            return Response.Json(authUser, 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>POST /api/auth/profile - updates the caller's own display name.</summary>
        public async Task<Response> UpdateProfileAsync(Request request)
        {
            if (request.Method != "POST")
                return Results.Error("Only POST is supported on /api/auth/profile", 405);

            var token = BearerToken(request);
            var user = await _auth.FindUserByTokenAsync(token);
            if (user is null)
                return Results.Error("Not signed in", 401);

            UpdateProfileRequest? input;
            try
            {
                input = await request.JsonAsync<UpdateProfileRequest>();
            }
            catch (Exception)
            {
                return Results.Error("Malformed JSON body", 400);
            }

            if (input is null)
                return Results.Error("A JSON body with \"displayName\" is required", 400);

            var displayName = AuthService.Trim(input.DisplayName);
            if (displayName.Length > SampleConfig.AuthMaxDisplayNameLength)
                return Results.Error(
                    $"The display name is {displayName.Length} characters, the limit is {SampleConfig.AuthMaxDisplayNameLength}", 400);

            await _auth.UpdateDisplayNameAsync(user.Id, displayName);
            var fresh = await _auth.FindUserByIdAsync(user.Id);
            var authUser = await _auth.ToAuthUserAsync(fresh!);
            return Response.Json(authUser, 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>POST /api/auth/logout - invalidates the caller's session token.</summary>
        public async Task<Response> LogoutAsync(Request request)
        {
            if (request.Method != "POST")
                return Results.Error("Only POST is supported on /api/auth/logout", 405);

            var token = BearerToken(request);
            await _auth.DeleteTokenAsync(token);
            return Results.Ok("Signed out.", request.Path);
        }

        /// <summary>GET /api/auth/users - admin only: all users with their roles.</summary>
        public async Task<Response> UsersAsync(Request request)
        {
            if (request.Method != "GET")
                return Results.Error("Only GET is supported on /api/auth/users", 405);

            var denied = await RequireAdminAsync(request);
            if (denied is not null)
                return denied;

            var users = await _auth.GetAllUsersAsync();
            var roles = await _auth.GetAllRolesAsync();
            return Response.Json(new UsersSnapshot(users, roles), 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>POST /api/auth/roles - admin only: replaces one user's roles.</summary>
        public async Task<Response> AssignRolesAsync(Request request)
        {
            if (request.Method != "POST")
                return Results.Error("Only POST is supported on /api/auth/roles", 405);

            var denied = await RequireAdminAsync(request);
            if (denied is not null)
                return denied;

            AssignRolesRequest? input;
            try
            {
                input = await request.JsonAsync<AssignRolesRequest>();
            }
            catch (Exception)
            {
                return Results.Error("Malformed JSON body", 400);
            }

            var email = AuthService.Trim(input?.Email);
            if (input is null || email.Length == 0)
                return Results.Error("A JSON body with \"email\" and \"roles\" is required", 400);

            var target = await _auth.FindUserByEmailAsync(email);
            if (target is null)
                return Results.Error($"No account found for {email}", 404);

            var roles = input.Roles ?? new List<string>();
            foreach (var role in roles)
            {
                if (!await _auth.RoleExistsAsync(role))
                    return Results.Error($"Unknown role \"{role}\"", 400);
            }

            await _auth.AssignRolesAsync(target.Id, roles);

            var updated = await _auth.ToAuthUserAsync(target);
            return Response.Json(updated, 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>
        /// The error response when the request is not an admin, or null when the
        /// caller is an authenticated admin.
        /// </summary>
        async Task<Response?> RequireAdminAsync(Request request)
        {
            var token = BearerToken(request);
            var caller = await _auth.FindUserByTokenAsync(token);
            if (caller is null)
                return Results.Error("Not signed in", 401);

            if (!await _auth.IsAdminAsync(caller.Id))
                return Results.Error("Administrator role required", 403);

            return null;
        }

        /// <summary>The raw bearer token from the Authorization header, or "".</summary>
        string BearerToken(Request request)
        {
            var header = request.Headers.Get("authorization");
            if (header is null || header.Length == 0)
                return "";

            if (!header.StartsWith(BearerPrefix))
                return "";

            return header.Substring(BearerPrefix.Length).Trim();
        }
    }
}
