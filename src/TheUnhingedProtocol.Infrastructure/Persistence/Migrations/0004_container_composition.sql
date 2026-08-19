PRAGMA foreign_keys = ON;

-- PH1-003 container composition, state, and appearance are stored in the
-- versioned containers.payload_json document. This migration records the
-- database contract level without duplicating that validated document state.
INSERT OR IGNORE INTO schema_info (version, applied_utc)
VALUES (4, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
