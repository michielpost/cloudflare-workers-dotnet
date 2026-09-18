-- D1 schema of the "dotnet" database.
--
-- It holds two unrelated samples, which is deliberate: one database, two sets of
-- tables, so the second sample does not need a database of its own.
--
--   items    - the D1 CRUD sample (GET/POST /api/items and friends)
--   sensors  - the weather sensor telemetry pipeline: which sensors exist and
--   readings   when each one is polled next, plus the reading itself
--   jobs     - one row per telemetry job, so the pipeline's progress is visible
--
-- Wrangler tracks this migration and applies it with
--   npx wrangler d1 migrations apply dotnet --local
--   npx wrangler d1 migrations apply dotnet --remote
-- The statements are also idempotent for safe recovery/replay.

-- ---------------------------------------------------------------------------
-- D1 CRUD sample
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS items (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    title      TEXT    NOT NULL,
    notes      TEXT    NOT NULL DEFAULT '',
    status     TEXT    NOT NULL DEFAULT 'open',
    created_at TEXT    NOT NULL,
    updated_at TEXT    NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_items_updated ON items (updated_at DESC);

-- ---------------------------------------------------------------------------
-- Weather sensor telemetry pipeline
-- ---------------------------------------------------------------------------

-- The sensors the cron trigger polls. Latitude/longitude are the coordinates
-- handed to the weather API, interval_minutes is how often each one is read and
-- next_read_at (unix milliseconds) is the due time the cron trigger compares
-- against. Reading a sensor moves next_read_at forward by interval_minutes.
CREATE TABLE IF NOT EXISTS sensors (
    sensor_id        TEXT    PRIMARY KEY,
    name             TEXT    NOT NULL,
    latitude         REAL    NOT NULL,
    longitude        REAL    NOT NULL,
    interval_minutes INTEGER NOT NULL DEFAULT 5,
    next_read_at     INTEGER NOT NULL DEFAULT 0,
    active           INTEGER NOT NULL DEFAULT 1
);

CREATE INDEX IF NOT EXISTS idx_sensors_due ON sensors (active, next_read_at);

-- One row per finished reading. id is the job id, so re-running a job replaces
-- its previous reading instead of duplicating it.
CREATE TABLE IF NOT EXISTS readings (
    id           TEXT    PRIMARY KEY,
    sensor_id    TEXT    NOT NULL,
    temperature  REAL    NOT NULL,
    air_quality  REAL    NOT NULL DEFAULT 0,
    condition    TEXT    NOT NULL,
    source       TEXT    NOT NULL,
    reading_at   INTEGER NOT NULL,
    processed_at INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_readings_recent ON readings (processed_at DESC);

-- One row per telemetry job. This is what makes the pipeline observable: a job
-- is 'queued' when the cron trigger puts it on the queue, and the consumer moves
-- it to 'done' (with a reading) or to 'failed' after the last retry. attempts
-- and last_error show the retry path.
CREATE TABLE IF NOT EXISTS jobs (
    id           TEXT    PRIMARY KEY,
    sensor_id    TEXT    NOT NULL,
    status       TEXT    NOT NULL,
    attempts     INTEGER NOT NULL DEFAULT 0,
    last_error   TEXT    NOT NULL DEFAULT '',
    queued_at    INTEGER NOT NULL,
    processed_at INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_jobs_status ON jobs (status, queued_at DESC);

-- The three sensors this sample ships with. INSERT OR IGNORE keeps the seed
-- idempotent, and next_read_at = 0 means "due immediately", so the very first
-- run of the pipeline has work to do.
INSERT OR IGNORE INTO sensors (sensor_id, name, latitude, longitude, interval_minutes, next_read_at, active)
VALUES
    ('sensor-amsterdam', 'Amsterdam canal house',  52.3740,   4.8897, 5, 0, 1),
    ('sensor-london',    'London rooftop',         51.5074,  -0.1278, 5, 0, 1),
    ('sensor-reykjavik', 'Reykjavik weather mast', 64.1466, -21.9426, 5, 0, 1);
