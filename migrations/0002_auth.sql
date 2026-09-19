-- Auth: users, roles, the user<->role mapping and the session tokens.
--
-- The auth feature keeps everything in the same "dotnet" D1 database as the
-- other samples (one database, several unrelated tables). The statements are
-- idempotent for safe replay, and the role seed is INSERT OR IGNORE so it can
-- be re-run without duplicating rows.
--
--   users        - one row per account. password_hash holds the salted SHA-256
--                  hash (format "<saltHex>:<hashHex>"), never the raw password.
--   roles        - the roles that can be assigned. Seeded with "admin" and
--                  "user"; new accounts default to "user".
--   user_roles   - which roles a user has (many-to-many).
--   auth_tokens  - one row per issued session token, keyed by the SHA-256 hash
--                  of the raw bearer token so a database leak never exposes a
--                  usable token. expires_at drives token validity.
--
-- Applied with:
--   npx wrangler d1 migrations apply dotnet --local
--   npx wrangler d1 migrations apply dotnet --remote

CREATE TABLE IF NOT EXISTS users (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    email         TEXT    NOT NULL UNIQUE,
    display_name  TEXT    NOT NULL DEFAULT '',
    password_hash TEXT    NOT NULL,
    created_at    TEXT    NOT NULL,
    updated_at    TEXT    NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_users_email ON users (email);

CREATE TABLE IF NOT EXISTS roles (
    id   INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT    NOT NULL UNIQUE
);

CREATE INDEX IF NOT EXISTS idx_roles_name ON roles (name);

INSERT OR IGNORE INTO roles (name) VALUES ('admin');
INSERT OR IGNORE INTO roles (name) VALUES ('user');

CREATE TABLE IF NOT EXISTS user_roles (
    user_id INTEGER NOT NULL,
    role_id INTEGER NOT NULL,
    PRIMARY KEY (user_id, role_id)
);

CREATE TABLE IF NOT EXISTS auth_tokens (
    token_hash TEXT    PRIMARY KEY,
    user_id    INTEGER NOT NULL,
    created_at TEXT    NOT NULL,
    expires_at INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_auth_tokens_user ON auth_tokens (user_id);
