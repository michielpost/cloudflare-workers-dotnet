using Shared;
using Workers;

namespace WorkersDotNet
{
    /// <summary>
    /// All D1 access for the auth feature: accounts, roles, the user-role mapping
    /// and the session tokens. Everything goes through <c>env.D1("DB")</c>, the
    /// same database as the other samples, and the SQL matches
    /// migrations/0002_auth.sql. Token expiry is stored as unix milliseconds so
    /// it can be compared without parsing a timestamp string.
    /// </summary>
    public static class AuthService
    {
        const string Binding = "DB";

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

        public static async Task<UserRow?> FindUserByEmailAsync(Env environment, string email)
        {
            var db = environment.D1(Binding);
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

        public static async Task<UserRow?> FindUserByIdAsync(Env environment, long id)
        {
            var db = environment.D1(Binding);
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
        public static async Task<UserRow?> CreateUserAsync(Env environment, string email, string displayName, string passwordHash)
        {
            var db = environment.D1(Binding);
            var now = DateTimeOffset.UtcNow.ToString("O");

            var result = await db.Prepare("INSERT INTO users (email, display_name, password_hash, created_at, updated_at) VALUES (?, ?, ?, ?, ?)")
                .Bind(email, displayName, passwordHash, now, now)
                .RunAsync();

            if (result is null || !result.Success)
                return null;

            return await db.Prepare($"SELECT {UserColumns} FROM users ORDER BY id DESC LIMIT 1")
                .FirstAsync<UserRow>();
        }

        public static async Task UpdateDisplayNameAsync(Env environment, long userId, string displayName)
        {
            var db = environment.D1(Binding);
            var now = DateTimeOffset.UtcNow.ToString("O");
            await db.Prepare("UPDATE users SET display_name = ?, updated_at = ? WHERE id = ?")
                .Bind(displayName, now, userId)
                .RunAsync();
        }

        public static async Task<List<string>> GetRolesAsync(Env environment, long userId)
        {
            var db = environment.D1(Binding);
            var rows = await db.Prepare(
                    "SELECT r.name FROM roles r " +
                    "INNER JOIN user_roles ur ON ur.role_id = r.id " +
                    "WHERE ur.user_id = ? ORDER BY r.id")
                .Bind(userId)
                .AllAsync<NameRow>();

            var list = new List<string>();
            if (rows is not null && rows.Results is not null)
            {
                foreach (var row in rows.Results)
                    list.Add(row.Name);
            }

            return list;
        }

        /// <summary>
        /// Replaces a user's roles with the given set. Unknown role names are
        /// skipped because the INSERT...SELECT only inserts rows whose role
        /// actually exists.
        /// </summary>
        public static async Task AssignRolesAsync(Env environment, long userId, IReadOnlyList<string> roles)
        {
            var db = environment.D1(Binding);

            await db.Prepare("DELETE FROM user_roles WHERE user_id = ?")
                .Bind(userId)
                .RunAsync();

            foreach (var role in roles)
            {
                await db.Prepare("INSERT OR IGNORE INTO user_roles (user_id, role_id) SELECT ?, id FROM roles WHERE name = ?")
                    .Bind(userId, role)
                    .RunAsync();
            }
        }

        public static async Task<bool> RoleExistsAsync(Env environment, string name)
        {
            var db = environment.D1(Binding);
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

        public static async Task<bool> IsAdminAsync(Env environment, long userId)
        {
            var roles = await GetRolesAsync(environment, userId);
            foreach (var role in roles)
            {
                if (role == SampleConfig.AuthAdminRole)
                    return true;
            }

            return false;
        }

        public static async Task<List<string>> GetAllRolesAsync(Env environment)
        {
            var db = environment.D1(Binding);
            var rows = await db.Prepare("SELECT name FROM roles ORDER BY id").AllAsync<NameRow>();

            var list = new List<string>();
            if (rows is not null && rows.Results is not null)
            {
                foreach (var row in rows.Results)
                    list.Add(row.Name);
            }

            return list;
        }

        public static async Task<List<AdminUser>> GetAllUsersAsync(Env environment)
        {
            var db = environment.D1(Binding);
            var rows = await db.Prepare("SELECT id, email, display_name AS displayName FROM users ORDER BY email")
                .AllAsync<AdminUserRow>();

            var users = new List<AdminUser>();
            if (rows is not null && rows.Results is not null)
            {
                foreach (var row in rows.Results)
                {
                    var roles = await GetRolesAsync(environment, row.Id);
                    users.Add(new AdminUser(row.Id, row.Email, row.DisplayName, roles));
                }
            }

            return users;
        }

        /// <summary>Creates a session token and stores its hash; returns the raw bearer token.</summary>
        public static async Task<string> CreateTokenAsync(Env environment, long userId)
        {
            var raw = Hex.Encode(Crypto.RandomBytes(32));
            var hash = await HashTokenAsync(raw);
            var now = DateTimeOffset.UtcNow.ToString("O");
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(SampleConfig.AuthTokenLifetimeHours * 3600.0).ToUnixTimeMilliseconds();

            var db = environment.D1(Binding);
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
        public static async Task<UserRow?> FindUserByTokenAsync(Env environment, string rawToken)
        {
            if (rawToken is null || rawToken.Length == 0)
                return null;

            var hash = await HashTokenAsync(rawToken);
            var db = environment.D1(Binding);

            TokenRow? token;
            try
            {
                token = await db.Prepare("SELECT token_hash AS tokenHash, user_id AS userId, created_at AS createdAt, expires_at AS expiresAt FROM auth_tokens WHERE token_hash = ?")
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

            return await FindUserByIdAsync(environment, token.UserId);
        }

        public static async Task DeleteTokenAsync(Env environment, string rawToken)
        {
            if (rawToken is null || rawToken.Length == 0)
                return;

            var hash = await HashTokenAsync(rawToken);
            var db = environment.D1(Binding);
            await db.Prepare("DELETE FROM auth_tokens WHERE token_hash = ?").Bind(hash).RunAsync();
        }

        public static async Task<AuthUser> ToAuthUserAsync(Env environment, UserRow user)
        {
            var roles = await GetRolesAsync(environment, user.Id);
            return new AuthUser(user.Id, user.Email, user.DisplayName, roles);
        }

        static async Task<string> HashTokenAsync(string rawToken)
        {
            var bytes = await Crypto.DigestTextAsync(DigestAlgorithm.Sha256, rawToken);
            return Hex.Encode(bytes);
        }
    }
}
