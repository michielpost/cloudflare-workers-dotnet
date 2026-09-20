using Shared;
using Workers;

namespace WorkersDotNet.Auth
{
    /// <summary>
    /// All D1 access for the auth feature: accounts, roles, the user-role mapping
    /// and the session tokens. This is the auth-specific business logic; every
    /// method takes the D1 binding it needs and calls the SDK directly (the
    /// worker transpiler does not support user-defined instance methods). The SQL
    /// matches migrations/0002_auth.sql. Token expiry is stored as unix
    /// milliseconds so it can be compared without parsing a timestamp string.
    /// </summary>
    public static class AuthService
    {
        /// <summary>One row of the users table (columns aliased to the property names).</summary>
        public sealed record UserRow(long Id, string Email, string DisplayName, string PasswordHash, string CreatedAt, string UpdatedAt);

        /// <summary>One row of the auth_tokens table.</summary>
        public sealed record TokenRow(string TokenHash, long UserId, string CreatedAt, long ExpiresAt);

        /// <summary>One row of the roles table.</summary>
        public sealed record RoleRow(long Id, string Name);

        /// <summary>A single name column, used when reading a list of role names.</summary>
        public sealed record NameRow(string Name);

        /// <summary>A user without the password, for the admin user list.</summary>
        public sealed record AdminUserRow(long Id, string Email, string DisplayName);

        const string UserColumns =
            "id, email, display_name AS displayName, " +
            "password_hash AS passwordHash, created_at AS createdAt, updated_at AS updatedAt";

        public static async Task<UserRow?> FindUserByEmailAsync(ID1Database db, string email)
        {
            try
            {
                return await db.Prepare($"SELECT {UserColumns} FROM users WHERE email = ?")
                    .Bind(email)
                    .FirstAsync<UserRow>();
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static async Task<UserRow?> FindUserByIdAsync(ID1Database db, long id)
        {
            try
            {
                return await db.Prepare($"SELECT {UserColumns} FROM users WHERE id = ?")
                    .Bind(id)
                    .FirstAsync<UserRow>();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Inserts a user and reads the row back so the caller has its id. Returns
        /// null if the insert raced a duplicate email (the UNIQUE constraint fails).
        /// </summary>
        public static async Task<UserRow?> CreateUserAsync(ID1Database db, string email, string displayName, string passwordHash)
        {
            var now = DateTimeOffset.UtcNow.ToString("O");

            var result = await db.Prepare(
                "INSERT INTO users (email, display_name, password_hash, created_at, updated_at) VALUES (?, ?, ?, ?, ?)")
                .Bind(email, displayName, passwordHash, now, now)
                .RunAsync();

            if (result is null || !result.Success)
                return null;

            return await db.Prepare($"SELECT {UserColumns} FROM users ORDER BY id DESC LIMIT 1")
                .FirstAsync<UserRow>();
        }

        public static async Task UpdateDisplayNameAsync(ID1Database db, long userId, string displayName)
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            await db.Prepare("UPDATE users SET display_name = ?, updated_at = ? WHERE id = ?")
                .Bind(displayName, now, userId)
                .RunAsync();
        }

        public static async Task<List<string>> GetRolesAsync(ID1Database db, long userId)
        {
            var result = await db.Prepare(
                "SELECT r.name FROM roles r " +
                "INNER JOIN user_roles ur ON ur.role_id = r.id " +
                "WHERE ur.user_id = ? ORDER BY r.id")
                .Bind(userId)
                .AllAsync<NameRow>();

            var list = new List<string>();
            if (result is not null && result.Results is not null)
            {
                foreach (var row in result.Results)
                    list.Add(row.Name);
            }

            return list;
        }

        /// <summary>
        /// Replaces a user's roles with the given set. Unknown role names are
        /// skipped because the INSERT...SELECT only inserts rows whose role
        /// actually exists.
        /// </summary>
        public static async Task AssignRolesAsync(ID1Database db, long userId, IReadOnlyList<string> roles)
        {
            await db.Prepare("DELETE FROM user_roles WHERE user_id = ?").Bind(userId).RunAsync();

            foreach (var role in roles)
            {
                await db.Prepare(
                    "INSERT OR IGNORE INTO user_roles (user_id, role_id) SELECT ?, id FROM roles WHERE name = ?")
                    .Bind(userId, role)
                    .RunAsync();
            }
        }

        public static async Task<bool> RoleExistsAsync(ID1Database db, string name)
        {
            try
            {
                var row = await db.Prepare("SELECT id, name FROM roles WHERE name = ?")
                    .Bind(name)
                    .FirstAsync<RoleRow>();
                return row is not null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static async Task<bool> IsAdminAsync(ID1Database db, long userId)
        {
            var roles = await GetRolesAsync(db, userId);
            foreach (var role in roles)
            {
                if (role == SampleConfig.AuthAdminRole)
                    return true;
            }

            return false;
        }

        public static async Task<List<string>> GetAllRolesAsync(ID1Database db)
        {
            var result = await db.Prepare("SELECT name FROM roles ORDER BY id").AllAsync<NameRow>();

            var list = new List<string>();
            if (result is not null && result.Results is not null)
            {
                foreach (var row in result.Results)
                    list.Add(row.Name);
            }

            return list;
        }

        public static async Task<List<AdminUser>> GetAllUsersAsync(ID1Database db)
        {
            var result = await db.Prepare("SELECT id, email, display_name AS displayName FROM users ORDER BY email")
                .AllAsync<AdminUserRow>();

            var users = new List<AdminUser>();
            if (result is not null && result.Results is not null)
            {
                foreach (var row in result.Results)
                {
                    var roles = await GetRolesAsync(db, row.Id);
                    users.Add(new AdminUser(row.Id, row.Email, row.DisplayName, roles));
                }
            }

            return users;
        }

        /// <summary>Creates a session token and stores its hash; returns the raw bearer token.</summary>
        public static async Task<string> CreateTokenAsync(ID1Database db, long userId)
        {
            var raw = Hex.Encode(Crypto.RandomBytes(32));
            var hash = await HashTokenAsync(raw);
            var now = DateTimeOffset.UtcNow.ToString("O");
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(SampleConfig.AuthTokenLifetimeHours * 3600.0).ToUnixTimeMilliseconds();

            await db.Prepare("INSERT INTO auth_tokens (token_hash, user_id, created_at, expires_at) VALUES (?, ?, ?, ?)")
                .Bind(hash, userId, now, expiresAt)
                .RunAsync();

            return raw;
        }

        /// <summary>
        /// Resolves a raw bearer token to its user, or null when the token is
        /// unknown or expired. Expired tokens are deleted on the way out. Valid
        /// tokens that are past the halfway point of their lifetime get their
        /// expiry extended (sliding session), so an active user is never logged
        /// out while they keep using the app.
        /// </summary>
        public static async Task<UserRow?> FindUserByTokenAsync(ID1Database db, string rawToken)
        {
            if (rawToken is null || rawToken.Length == 0)
                return null;

            var hash = await HashTokenAsync(rawToken);

            TokenRow? token;
            try
            {
                token = await db.Prepare(
                    "SELECT token_hash AS tokenHash, user_id AS userId, created_at AS createdAt, expires_at AS expiresAt FROM auth_tokens WHERE token_hash = ?")
                    .Bind(hash)
                    .FirstAsync<TokenRow>();
            }
            catch (Exception)
            {
                return null;
            }

            if (token is null)
                return null;

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (token.ExpiresAt <= nowMs)
            {
                await db.Prepare("DELETE FROM auth_tokens WHERE token_hash = ?").Bind(hash).RunAsync();
                return null;
            }

            // Sliding refresh: when a valid token is about to expire (within the
            // next day) it is rolled forward by a full lifetime again, so an
            // active user is never logged out. This uses only method calls and a
            // plain long comparison - the worker transpiler rejects cast and
            // arithmetic (+/-) expressions.
            var refreshThreshold = DateTimeOffset.UtcNow.AddSeconds(24 * 3600.0).ToUnixTimeMilliseconds();
            if (token.ExpiresAt < refreshThreshold)
            {
                var refreshedExpiry = DateTimeOffset.UtcNow
                    .AddSeconds(SampleConfig.AuthTokenLifetimeHours * 3600.0)
                    .ToUnixTimeMilliseconds();
                await db.Prepare("UPDATE auth_tokens SET expires_at = ? WHERE token_hash = ?")
                    .Bind(refreshedExpiry, hash)
                    .RunAsync();
            }

            return await FindUserByIdAsync(db, token.UserId);
        }

        public static async Task DeleteTokenAsync(ID1Database db, string rawToken)
        {
            if (rawToken is null || rawToken.Length == 0)
                return;

            var hash = await HashTokenAsync(rawToken);
            await db.Prepare("DELETE FROM auth_tokens WHERE token_hash = ?").Bind(hash).RunAsync();
        }

        public static async Task<AuthUser> ToAuthUserAsync(ID1Database db, UserRow user)
        {
            var roles = await GetRolesAsync(db, user.Id);
            return new AuthUser(user.Id, user.Email, user.DisplayName, roles);
        }

        /// <summary>True when the email matches the configured default admin.</summary>
        public static bool IsDefaultAdmin(string defaultAdminEmail, string email)
        {
            var configured = defaultAdminEmail;
            if (configured is null || configured.Length == 0)
                return false;

            return configured.Trim().ToLowerInvariant() == email.Trim().ToLowerInvariant();
        }

        /// <summary>Returns the problem with the registration input, or null when it is valid.</summary>
        public static string? ValidateRegistration(string email, string displayName, string password)
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

        public static string Trim(string? value)
        {
            if (value is null)
                return "";

            return value.Trim();
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

        static async Task<string> HashTokenAsync(string rawToken)
        {
            var bytes = await Crypto.DigestTextAsync(DigestAlgorithm.Sha256, rawToken);
            return Hex.Encode(bytes);
        }
    }
}
