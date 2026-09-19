namespace Shared;

// Auth feature: the table names from migrations/0002_auth.sql, the role names
// seeded there and the limits the API enforces and the UI shows.
public static partial class SampleConfig
{
    /// <summary>D1 table holding the accounts (see migrations/0002_auth.sql).</summary>
    public const string AuthUsersTable = "users";

    /// <summary>D1 table holding the assignable roles.</summary>
    public const string AuthRolesTable = "roles";

    /// <summary>D1 join table mapping users to roles.</summary>
    public const string AuthUserRolesTable = "user_roles";

    /// <summary>D1 table holding the issued session tokens.</summary>
    public const string AuthTokensTable = "auth_tokens";

    /// <summary>Environment variable holding the default admin email.</summary>
    public const string DefaultAdminEmailVariable = "DEFAULT_ADMIN_EMAIL";

    /// <summary>Role every new account gets by default.</summary>
    public const string AuthDefaultRole = "user";

    /// <summary>Role of the configured default admin (and of anyone an admin promotes).</summary>
    public const string AuthAdminRole = "admin";

    /// <summary>Longest email the API accepts.</summary>
    public const int AuthMaxEmailLength = 160;

    /// <summary>Longest display name the API accepts.</summary>
    public const int AuthMaxDisplayNameLength = 80;

    /// <summary>Shortest password the API accepts.</summary>
    public const int AuthMinPasswordLength = 6;

    /// <summary>How long an issued bearer token stays valid.</summary>
    public const int AuthTokenLifetimeHours = 720; // 30 days
}
