using System.Collections.Generic;
using System.Threading.Tasks;
using Shared;
using Workers;
using WorkersDotNet.Services;

namespace WorkersDotNet.Auth
{
    /// <summary>
    /// All D1 access for the auth feature: accounts, roles, the user-role mapping
    /// and the session tokens. This is the auth-specific business logic; the D1
    /// binding is injected and the SQL is centralised here. The SQL matches
    /// migrations/0002_auth.sql. Token expiry is stored as unix milliseconds so
    /// it can be compared without parsing a timestamp string.
    /// </summary>
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

    public sealed class AuthService
    {
        readonly string UserColumns =
            "id, email, display_name AS displayName, " +
            "password_hash AS passwordHash, created_at AS createdAt, updated_at AS updatedAt";

        private readonly D1Database _db;
        private readonly string _defaultAdminEmail;

        public AuthService(D1Database db, string defaultAdminEmail)
        {
            _db = db;
            _defaultAdminEmail = defaultAdminEmail;
        }

        public async Task<UserRow?> FindUserByEmailAsync(string email)
        {
            return await _db.FirstAsync<UserRow>(
                _db.Prepare($"SELECT {UserColumns} FROM users WHERE email = ?").Bind(email));
        }

        public async Task<UserRow?> FindUserByIdAsync(long id)
        {
            return await _db.FirstAsync<UserRow>(
                _db.Prepare($"SELECT {UserColumns} FROM users WHERE id = ?").Bind(id));
        }

        /// <summary>
        /// Inserts a user and reads the row back so the caller has its id. Returns
        /// null if the insert raced a duplicate email (the UNIQUE constraint fails).
        /// </summary>
        public async Task<UserRow?> CreateUserAsync(string email, string displayName, string passwordHash)
        {
            var now = DateTimeOffset.UtcNow.ToString("O");

            var ok = await _db.ExecuteSucceededAsync(
                _db.Prepare("INSERT INTO users (email, display_name, password_hash, created_at, updated_at) VALUES (?, ?, ?, ?, ?)")
                    .Bind(email, displayName, passwordHash, now, now));

            if (!ok)
                return null;

            return await _db.FirstAsync<UserRow>(
                _db.Prepare($"SELECT {UserColumns} FROM users ORDER BY id DESC LIMIT 1"));
        }

        public async Task UpdateDisplayNameAsync(long userId, string displayName)
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            await _db.ExecuteAsync(
                _db.Prepare("UPDATE users SET display_name = ?, updated_at = ? WHERE id = ?")
                    .Bind(displayName, now, userId));
        }

        public async Task<List<string>> GetRolesAsync(long userId)
        {
            var rows = await _db.AllAsync<NameRow>(
                _db.Prepare("SELECT r.name FROM roles r " +
                "INNER JOIN user_roles ur ON ur.role_id = r.id " +
                "WHERE ur.user_id = ? ORDER BY r.id").Bind(userId));
            return Names(rows);
        }

        static List<string> Names(IReadOnlyList<NameRow> rows)
        {
            var list = new List<string>();
            foreach (var row in rows)
                list.Add(row.Name);
            return list;
        }

        /// <summary>
        /// Replaces a user's roles with the given set. Unknown role names are
        /// skipped because the INSERT...SELECT only inserts rows whose role
        /// actually exists.
        /// </summary>
        public async Task AssignRolesAsync(long userId, IReadOnlyList<string> roles)
        {
            await _db.ExecuteAsync(_db.Prepare("DELETE FROM user_roles WHERE user_id = ?").Bind(userId));

            foreach (var role in roles)
            {
                await _db.ExecuteAsync(
                    _db.Prepare("INSERT OR IGNORE INTO user_roles (user_id, role_id) SELECT ?, id FROM roles WHERE name = ?")
                        .Bind(userId, role));
            }
        }

        public async Task<bool> RoleExistsAsync(string name)
        {
            var row = await _db.FirstAsync<RoleRow>(
                _db.Prepare("SELECT id, name FROM roles WHERE name = ?").Bind(name));
            return row is not null;
        }

        public async Task<bool> IsAdminAsync(long userId)
        {
            var roles = await GetRolesAsync(userId);
            foreach (var role in roles)
            {
                if (role == SampleConfig.AuthAdminRole)
                    return true;
            }

            return false;
        }

        public async Task<List<string>> GetAllRolesAsync()
        {
            var result = await _db.AllAsync<NameRow>(_db.Prepare("SELECT name FROM roles ORDER BY id"));
            return Names(result);
        }

        public async Task<List<AdminUser>> GetAllUsersAsync()
        {
            var rows = await _db.AllAsync<AdminUserRow>(
                _db.Prepare("SELECT id, email, display_name AS displayName FROM users ORDER BY email"));

            var users = new List<AdminUser>();
            foreach (var row in rows)
            {
                var roles = await GetRolesAsync(row.Id);
                users.Add(new AdminUser(row.Id, row.Email, row.DisplayName, roles));
            }

            return users;
        }

        /// <summary>Creates a session token and stores its hash; returns the raw bearer token.</summary>
        public async Task<string> CreateTokenAsync(long userId)
        {
            var raw = Hex.Encode(Crypto.RandomBytes(32));
            var hash = await HashTokenAsync(raw);
            var now = DateTimeOffset.UtcNow.ToString("O");
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(SampleConfig.AuthTokenLifetimeHours * 3600.0).ToUnixTimeMilliseconds();

            await _db.ExecuteAsync(
                _db.Prepare("INSERT INTO auth_tokens (token_hash, user_id, created_at, expires_at) VALUES (?, ?, ?, ?)")
                    .Bind(hash, userId, now, expiresAt));

            return raw;
        }

        /// <summary>
        /// Resolves a raw bearer token to its user, or null when the token is
        /// unknown or expired. Expired tokens are deleted on the way out. Valid
        /// tokens that are past the halfway point of their lifetime get their
        /// expiry extended (sliding session), so an active user is never logged
        /// out while they keep using the app.
        /// </summary>
        public async Task<UserRow?> FindUserByTokenAsync(string rawToken)
        {
            if (rawToken is null || rawToken.Length == 0)
                return null;

            var hash = await HashTokenAsync(rawToken);

            var token = await _db.FirstAsync<TokenRow>(
                _db.Prepare("SELECT token_hash AS tokenHash, user_id AS userId, created_at AS createdAt, expires_at AS expiresAt FROM auth_tokens WHERE token_hash = ?")
                    .Bind(hash));

            if (token is null)
                return null;

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (token.ExpiresAt <= nowMs)
            {
                await _db.ExecuteAsync(_db.Prepare("DELETE FROM auth_tokens WHERE token_hash = ?").Bind(hash));
                return null;
            }

            // Sliding refresh: when a valid token is about to expire (within the
            // next day) it is rolled forward by a full lifetime again, so an
            // active user is never logged out.
            var refreshThreshold = DateTimeOffset.UtcNow.AddSeconds(24 * 3600.0).ToUnixTimeMilliseconds();
            if (token.ExpiresAt < refreshThreshold)
            {
                var refreshedExpiry = DateTimeOffset.UtcNow
                    .AddSeconds(SampleConfig.AuthTokenLifetimeHours * 3600.0)
                    .ToUnixTimeMilliseconds();
                await _db.ExecuteAsync(_db.Prepare("UPDATE auth_tokens SET expires_at = ? WHERE token_hash = ?").Bind(refreshedExpiry, hash));
            }

            return await FindUserByIdAsync(token.UserId);
        }

        public async Task DeleteTokenAsync(string rawToken)
        {
            if (rawToken is null || rawToken.Length == 0)
                return;

            var hash = await HashTokenAsync(rawToken);
            await _db.ExecuteAsync(_db.Prepare("DELETE FROM auth_tokens WHERE token_hash = ?").Bind(hash));
        }

        public async Task<AuthUser> ToAuthUserAsync(UserRow user)
        {
            var roles = await GetRolesAsync(user.Id);
            return new AuthUser(user.Id, user.Email, user.DisplayName, roles);
        }

        /// <summary>True when the email matches the configured default admin.</summary>
        public bool IsDefaultAdmin(string email)
        {
            var configured = _defaultAdminEmail;
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
