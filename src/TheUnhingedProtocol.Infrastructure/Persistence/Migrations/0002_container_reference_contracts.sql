PRAGMA foreign_keys = ON;

CREATE INDEX IF NOT EXISTS ix_containers_updated_utc
ON containers (updated_utc);

INSERT OR IGNORE INTO schema_info (version, applied_utc)
VALUES (2, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
