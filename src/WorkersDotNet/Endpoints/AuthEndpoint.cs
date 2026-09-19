using Shared;
using Workers;

namespace WorkersDotNet
{
    /// <summary>
    /// The auth API: registration, login, session lookup and logout, plus the
    /// admin-only user/role management. Passwords never travel in cleartext over
    /// the wire in responses - only a random bearer token is handed back - and
    /// every protected endpoint validates that token against the D1 auth_tokens
    /// table. The "admin can assign roles" surface is POST /api/auth/roles and
    /// GET /api/auth/users, both of which require the caller to hold the admin
    /// role.
    /// </summary>
    public static class AuthEndpoint
    {
        const string BearerPrefix = "Bearer ";

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

            var email = Trim(input.Email);
            var displayName = Trim(input.DisplayName);
            var password = input.Password ?? "";

            var problem = Validate(email, displayName, password);
            if (problem is not null)
                return Results.Error(problem, 400);

            var existing = await AuthService.FindUserByEmailAsync(environment, email);
            if (existing is not null)
                return Results.Error("An account with that email already exists. Sign in instead.", 409);

            var hash = await AuthPassword.HashAsync(password);
            var user = await AuthService.CreateUserAsync(environment, email, displayName, hash);
            if (user is null)
                return Results.Error("Could not create the account. Try a different email.", 409);

            // Everyone starts with "user"; the configured default admin email is
            // promoted to "admin" the moment that account registers.
            var roles = new List<string>();
            roles.Add(SampleConfig.AuthDefaultRole);
            if (IsDefaultAdmin(environment, email))
                roles.Add(SampleConfig.AuthAdminRole);

            await AuthService.AssignRolesAsync(environment, user.Id, roles);

            var token = await AuthService.CreateTokenAsync(environment, user.Id);
            var authUser = await AuthService.ToAuthUserAsync(environment, user);
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

            var email = Trim(input.Email);
            var password = input.Password ?? "";

            if (email.Length == 0 || password.Length == 0)
                return Results.Error("An email and a password are required", 400);

            var user = await AuthService.FindUserByEmailAsync(environment, email);
            if (user is null)
                return Results.Error("Invalid email or password", 401);

            var valid = await AuthPassword.VerifyAsync(password, user.PasswordHash);
            if (!valid)
                return Results.Error("Invalid email or password", 401);

            var token = await AuthService.CreateTokenAsync(environment, user.Id);
            var authUser = await AuthService.ToAuthUserAsync(environment, user);
            return Response.Json(new AuthResponse(token, authUser), 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>GET /api/auth/me - returns the caller's identity from their bearer token.</summary>
        public static async Task<Response> MeAsync(Request request, Env environment)
        {
            if (request.Method != "GET")
                return Results.Error("Only GET is supported on /api/auth/me", 405);

            var token = BearerToken(request);
            var user = await AuthService.FindUserByTokenAsync(environment, token);
            if (user is null)
                return Results.Error("Not signed in", 401);

            var authUser = await AuthService.ToAuthUserAsync(environment, user);
            return Response.Json(authUser, 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>POST /api/auth/profile - updates the caller's own display name.</summary>
        public static async Task<Response> UpdateProfileAsync(Request request, Env environment)
        {
            if (request.Method != "POST")
                return Results.Error("Only POST is supported on /api/auth/profile", 405);

            var token = BearerToken(request);
            var user = await AuthService.FindUserByTokenAsync(environment, token);
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

            var displayName = Trim(input.DisplayName);
            if (displayName.Length > SampleConfig.AuthMaxDisplayNameLength)
                return Results.Error(
                    $"The display name is {displayName.Length} characters, the limit is {SampleConfig.AuthMaxDisplayNameLength}", 400);

            await AuthService.UpdateDisplayNameAsync(environment, user.Id, displayName);
            var fresh = await AuthService.FindUserByIdAsync(environment, user.Id);
            var authUser = await AuthService.ToAuthUserAsync(environment, fresh!);
            return Response.Json(authUser, 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>POST /api/auth/logout - invalidates the caller's session token.</summary>
        public static async Task<Response> LogoutAsync(Request request, Env environment)
        {
            if (request.Method != "POST")
                return Results.Error("Only POST is supported on /api/auth/logout", 405);

            var token = BearerToken(request);
            await AuthService.DeleteTokenAsync(environment, token);
            return Results.Ok("Signed out.", request.Path);
        }

        /// <summary>GET /api/auth/users - admin only: all users with their roles.</summary>
        public static async Task<Response> UsersAsync(Request request, Env environment)
        {
            if (request.Method != "GET")
                return Results.Error("Only GET is supported on /api/auth/users", 405);

            var caller = await RequireAdminAsync(request, environment);
            if (caller is not null)
                return caller;

            var users = await AuthService.GetAllUsersAsync(environment);
            var roles = await AuthService.GetAllRolesAsync(environment);
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

            var email = Trim(input?.Email);
            if (input is null || email.Length == 0)
                return Results.Error("A JSON body with \"email\" and \"roles\" is required", 400);

            var target = await AuthService.FindUserByEmailAsync(environment, email);
            if (target is null)
                return Results.Error($"No account found for {email}", 404);

            var roles = input.Roles ?? new List<string>();
            foreach (var role in roles)
            {
                if (!await AuthService.RoleExistsAsync(environment, role))
                    return Results.Error($"Unknown role \"{role}\"", 400);
            }

            await AuthService.AssignRolesAsync(environment, target.Id, roles);

            var updated = await AuthService.ToAuthUserAsync(environment, target);
            return Response.Json(updated, 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>
        /// The error response when the request is not an admin, or null when the
        /// caller is an authenticated admin.
        /// </summary>
        static async Task<Response?> RequireAdminAsync(Request request, Env environment)
        {
            var token = BearerToken(request);
            var caller = await AuthService.FindUserByTokenAsync(environment, token);
            if (caller is null)
                return Results.Error("Not signed in", 401);

            if (!await AuthService.IsAdminAsync(environment, caller.Id))
                return Results.Error("Administrator role required", 403);

            return null;
        }

        static bool IsDefaultAdmin(Env environment, string email)
        {
            var configured = environment.Variable(SampleConfig.DefaultAdminEmailVariable);
            if (configured is null || configured.Length == 0)
                return false;

            return configured.Trim().ToLowerInvariant() == email.Trim().ToLowerInvariant();
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

        static string? Validate(string email, string displayName, string password)
        {
            if (email.Length == 0)
                return "An email is required";

            if (email.Length > SampleConfig.AuthMaxEmailLength)
                return $"The email is {email.Length} characters, the limit is {SampleConfig.AuthMaxEmailLength}";

            if (!ContainsAt(email))
                return "The email does not look valid";

            if (displayName.Length > SampleConfig.AuthMaxDisplayNameLength)
                return $"The display name is {displayName.Length} characters, the limit is {SampleConfig.AuthMaxDisplayNameLength}";

            if (password.Length < SampleConfig.AuthMinPasswordLength)
                return $"The password must be at least {SampleConfig.AuthMinPasswordLength} characters";

            return null;
        }

        static bool ContainsAt(string value)
        {
            for (var i = 0; i < value.Length; i++)
            {
                if (value.Substring(i, 1) == "@")
                    return true;
            }

            return false;
        }

        static string Trim(string? value)
        {
            if (value is null)
                return "";

            return value.Trim();
        }
    }
}




