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
    public static class AuthEndpoint
    {
        const string BearerPrefix = "Bearer ";

        static ID1Database Db(Env environment) => environment.D1("DB");

        static string DefaultAdminEmail(Env environment)
            => environment.Variable(SampleConfig.DefaultAdminEmailVariable);

        /// <summary>POST /api/auth/register - creates an account and signs it in.</summary>
        public static async Task<Response> RegisterAsync(Request request, Env environment)
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

            var db = Db(environment);
            var defaultAdmin = DefaultAdminEmail(environment);

            var email = AuthService.Trim(input.Email);
            var displayName = AuthService.Trim(input.DisplayName);
            var password = input.Password ?? "";

            var problem = AuthService.ValidateRegistration(email, displayName, password);
            if (problem is not null)
                return Results.Error(problem, 400);

            var existing = await AuthService.FindUserByEmailAsync(db, email);
            if (existing is not null)
                return Results.Error("An account with that email already exists. Sign in instead.", 409);

            var hash = await AuthPassword.HashAsync(password);
            var user = await AuthService.CreateUserAsync(db, email, displayName, hash);
            if (user is null)
                return Results.Error("Could not create the account. Try a different email.", 409);

            // Everyone starts with "user"; the configured default admin email is
            // promoted to "admin" the moment that account registers.
            var roles = new List<string>();
            roles.Add(SampleConfig.AuthDefaultRole);
            if (AuthService.IsDefaultAdmin(defaultAdmin, email))
                roles.Add(SampleConfig.AuthAdminRole);

            await AuthService.AssignRolesAsync(db, user.Id, roles);

            var token = await AuthService.CreateTokenAsync(db, user.Id);
            var authUser = await AuthService.ToAuthUserAsync(db, user);
            return Response.Json(new AuthResponse(token, authUser), 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>POST /api/auth/login - verifies credentials and signs the user in.</summary>
        public static async Task<Response> LoginAsync(Request request, Env environment)
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

            var db = Db(environment);

            var email = AuthService.Trim(input.Email);
            var password = input.Password ?? "";

            if (email.Length == 0 || password.Length == 0)
                return Results.Error("An email and a password are required", 400);

            var user = await AuthService.FindUserByEmailAsync(db, email);
            if (user is null)
                return Results.Error("Invalid email or password", 401);

            var valid = await AuthPassword.VerifyAsync(password, user.PasswordHash);
            if (!valid)
                return Results.Error("Invalid email or password", 401);

            var token = await AuthService.CreateTokenAsync(db, user.Id);
            var authUser = await AuthService.ToAuthUserAsync(db, user);
            return Response.Json(new AuthResponse(token, authUser), 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>GET /api/auth/me - returns the caller's identity from their bearer token.</summary>
        public static async Task<Response> MeAsync(Request request, Env environment)
        {
            if (request.Method != "GET")
                return Results.Error("Only GET is supported on /api/auth/me", 405);

            var db = Db(environment);

            var token = BearerToken(request);
            var user = await AuthService.FindUserByTokenAsync(db, token);
            if (user is null)
                return Results.Error("Not signed in", 401);

            var authUser = await AuthService.ToAuthUserAsync(db, user);
            return Response.Json(authUser, 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>POST /api/auth/profile - updates the caller's own display name.</summary>
        public static async Task<Response> UpdateProfileAsync(Request request, Env environment)
        {
            if (request.Method != "POST")
                return Results.Error("Only POST is supported on /api/auth/profile", 405);

            var db = Db(environment);

            var token = BearerToken(request);
            var user = await AuthService.FindUserByTokenAsync(db, token);
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

            await AuthService.UpdateDisplayNameAsync(db, user.Id, displayName);
            var fresh = await AuthService.FindUserByIdAsync(db, user.Id);
            var authUser = await AuthService.ToAuthUserAsync(db, fresh!);
            return Response.Json(authUser, 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>POST /api/auth/logout - invalidates the caller's session token.</summary>
        public static async Task<Response> LogoutAsync(Request request, Env environment)
        {
            if (request.Method != "POST")
                return Results.Error("Only POST is supported on /api/auth/logout", 405);

            var token = BearerToken(request);
            await AuthService.DeleteTokenAsync(Db(environment), token);
            return Results.Ok("Signed out.", request.Path);
        }

        /// <summary>GET /api/auth/users - admin only: all users with their roles.</summary>
        public static async Task<Response> UsersAsync(Request request, Env environment)
        {
            if (request.Method != "GET")
                return Results.Error("Only GET is supported on /api/auth/users", 405);

            var denied = await RequireAdminAsync(request, environment);
            if (denied is not null)
                return denied;

            var db = Db(environment);
            var users = await AuthService.GetAllUsersAsync(db);
            var roles = await AuthService.GetAllRolesAsync(db);
            return Response.Json(new UsersSnapshot(users, roles), 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>POST /api/auth/roles - admin only: replaces one user's roles.</summary>
        public static async Task<Response> AssignRolesAsync(Request request, Env environment)
        {
            if (request.Method != "POST")
                return Results.Error("Only POST is supported on /api/auth/roles", 405);

            var denied = await RequireAdminAsync(request, environment);
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

            var db = Db(environment);

            var email = AuthService.Trim(input?.Email);
            if (input is null || email.Length == 0)
                return Results.Error("A JSON body with \"email\" and \"roles\" is required", 400);

            var target = await AuthService.FindUserByEmailAsync(db, email);
            if (target is null)
                return Results.Error($"No account found for {email}", 404);

            var roles = input.Roles ?? new List<string>();
            foreach (var role in roles)
            {
                if (!await AuthService.RoleExistsAsync(db, role))
                    return Results.Error($"Unknown role \"{role}\"", 400);
            }

            await AuthService.AssignRolesAsync(db, target.Id, roles);

            var updated = await AuthService.ToAuthUserAsync(db, target);
            return Response.Json(updated, 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>
        /// The error response when the request is not an admin, or null when the
        /// caller is an authenticated admin.
        /// </summary>
        static async Task<Response?> RequireAdminAsync(Request request, Env environment)
        {
            var db = Db(environment);

            var token = BearerToken(request);
            var caller = await AuthService.FindUserByTokenAsync(db, token);
            if (caller is null)
                return Results.Error("Not signed in", 401);

            if (!await AuthService.IsAdminAsync(db, caller.Id))
                return Results.Error("Administrator role required", 403);

            return null;
        }

        /// <summary>The raw bearer token from the Authorization header, or "".</summary>
        static string BearerToken(Request request)
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
