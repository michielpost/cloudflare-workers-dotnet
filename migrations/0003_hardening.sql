-- Indexes that keep the telemetry reads cheap.
--
-- D1 bills "rows read" per row a query has to scan, so an unindexed lookup is
-- charged for the whole table. The telemetry status read used to scan
-- `readings` once per sensor (the correlated COUNT subqueries) and again for
-- the recent list, which made one GET /api/telemetry cost O(sensors x readings).
-- These indexes let the aggregate and the ORDER BY ... LIMIT use a range scan
-- instead of a full table scan.
--
-- Apply with
--   npx wrangler d1 migrations apply dotnet --remote
-- (the deploy build in wrangler.toml does this automatically).

-- Per-sensor counts: the LEFT JOIN aggregate groups by sensor_id.
CREATE INDEX IF NOT EXISTS idx_readings_sensor ON readings (sensor_id);

-- The "latest readings" list: ORDER BY reading_at DESC LIMIT n.
CREATE INDEX IF NOT EXISTS idx_readings_reading_at ON readings (reading_at DESC);

-- Per-sensor pending counts and the status rollup.
CREATE INDEX IF NOT EXISTS idx_jobs_sensor_status ON jobs (sensor_id, status);

-- The "latest jobs" list: ORDER BY queued_at DESC LIMIT n.
CREATE INDEX IF NOT EXISTS idx_jobs_queued ON jobs (queued_at DESC);
