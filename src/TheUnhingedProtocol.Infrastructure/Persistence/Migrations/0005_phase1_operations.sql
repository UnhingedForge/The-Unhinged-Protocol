PRAGMA foreign_keys = ON;

CREATE INDEX IF NOT EXISTS ix_layout_snapshots_created_utc
ON layout_snapshots (created_utc DESC);

INSERT OR IGNORE INTO schema_info (version, applied_utc)
VALUES (5, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
