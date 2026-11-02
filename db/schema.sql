-- Database schema for the ZAI measurement series project in PostgreSQL.

-- administrator table containing administrator accounts (password stored only as a bcrypt hash).
CREATE TABLE IF NOT EXISTS admin_users (
    id            integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    username      text        NOT NULL UNIQUE,
    password_hash text        NOT NULL,
    created_at    timestamptz NOT NULL DEFAULT now()
);

-- administrator session table containing session tokens. Only the SHA-256 hash of a token is stored.
CREATE TABLE IF NOT EXISTS admin_sessions (
    token_hash char(64)    PRIMARY KEY,
    admin_id   integer     NOT NULL REFERENCES admin_users(id) ON DELETE CASCADE,
    created_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_admin_sessions_admin   ON admin_sessions (admin_id);
CREATE INDEX IF NOT EXISTS ix_admin_sessions_expires ON admin_sessions (expires_at);

-- measurement series table. The name is not unique (the API contract does not require it).
CREATE TABLE IF NOT EXISTS series (
    id        integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name      text             NOT NULL CHECK (char_length(name) BETWEEN 1 AND 100),
    min_value double precision NOT NULL,
    max_value double precision NOT NULL,
    color     text             NOT NULL CHECK (color ~ '^#[0-9A-Fa-f]{6}$'),
    icon      text,
    unit      text             CHECK (unit IS NULL OR char_length(unit) <= 20),
    CONSTRAINT series_range_chk CHECK (min_value < max_value)
);

-- sensors table, unregistering a sensor = deleting its row (the key stops working,
-- but its measurements stay: measurements.sensor_id -> ON DELETE SET NULL).
-- Deleting a series deletes its sensors (ON DELETE CASCADE).
CREATE TABLE IF NOT EXISTS sensors (
    id         integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name       text        NOT NULL CHECK (char_length(name) BETWEEN 1 AND 100),
    series_id  integer     NOT NULL REFERENCES series(id) ON DELETE CASCADE,
    key_hash   char(64)    NOT NULL UNIQUE,   -- SHA-256 (hex) of the API key
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_sensors_series ON sensors (series_id);

-- measurements table, primary key includes the time column (required by hypertables).
CREATE TABLE IF NOT EXISTS measurements (
    id        bigint GENERATED ALWAYS AS IDENTITY,
    ts        timestamptz      NOT NULL,
    series_id integer          NOT NULL REFERENCES series(id)  ON DELETE CASCADE,
    sensor_id integer          REFERENCES sensors(id)          ON DELETE SET NULL,
    value     double precision NOT NULL,
    PRIMARY KEY (id, ts)
);

CREATE INDEX IF NOT EXISTS ix_measurements_series_ts ON measurements (series_id, ts DESC);
CREATE INDEX IF NOT EXISTS ix_measurements_id        ON measurements (id);
CREATE INDEX IF NOT EXISTS ix_measurements_sensor_ts ON measurements (sensor_id, ts DESC);

-- Turn measurements table into a TimescaleDB hypertable, but only when the extension is
-- installed (the timescale/timescaledb image creates it automatically in POSTGRES_DB).
-- On plain PostgreSQL this block does nothing and "measurements" stays a regular table.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'timescaledb') THEN
        PERFORM create_hypertable('measurements', 'ts',
                                  chunk_time_interval => INTERVAL '30 days',
                                  if_not_exists => TRUE);
    END IF;
END
$$;
