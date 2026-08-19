PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS schema_info (
    version INTEGER NOT NULL PRIMARY KEY,
    applied_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS containers (
    id TEXT NOT NULL PRIMARY KEY,
    schema_version INTEGER NOT NULL,
    payload_json TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS item_references (
    id TEXT NOT NULL PRIMARY KEY,
    canonical_path TEXT NOT NULL,
    payload_json TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS rules (
    id TEXT NOT NULL PRIMARY KEY,
    schema_version INTEGER NOT NULL,
    payload_json TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS file_transactions (
    id TEXT NOT NULL PRIMARY KEY,
    schema_version INTEGER NOT NULL,
    state TEXT NOT NULL,
    payload_json TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS layout_snapshots (
    id TEXT NOT NULL PRIMARY KEY,
    schema_version INTEGER NOT NULL,
    payload_json TEXT NOT NULL,
    created_utc TEXT NOT NULL
);

INSERT OR IGNORE INTO schema_info (version, applied_utc)
VALUES (1, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
