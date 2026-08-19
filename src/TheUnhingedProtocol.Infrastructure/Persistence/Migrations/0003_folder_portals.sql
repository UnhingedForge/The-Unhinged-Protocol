CREATE TABLE IF NOT EXISTS folder_portals (
    id TEXT PRIMARY KEY NOT NULL,
    schema_version INTEGER NOT NULL,
    payload_json TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_folder_portals_updated_utc
    ON folder_portals(updated_utc);

INSERT OR IGNORE INTO schema_info (version, applied_utc)
VALUES (3, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
