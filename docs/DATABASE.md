# Database

SkeeterSleuth persists all scan data locally in a SQLite file — there is no
server and no remote sync. This document describes the schema exactly as
defined in `Assets/Scripts/DatabaseManager.cs`.

## Storage

- **Engine**: SQLite, via `com.gilzoide.sqlite-net` (git package, pinned to
  tag `1.3.2` in `Packages/manifest.json`) — a Unity-friendly wrapper around
  `sqlite-net-pcl`.
- **File location**: `Application.persistentDataPath/skeeter_sleuth.db`
  (created on first access if missing).
- **Access point**: `DatabaseManager.Instance` — a `DontDestroyOnLoad`
  singleton that opens the connection in `Awake()` and creates all four
  tables via `CreateTable<T>()` if they don't already exist.
- Every public method on `DatabaseManager` calls `EnsureDatabaseInitialized()`
  first, so callers don't need to worry about initialization order.

## Schema

### `ObjectType`

The catalog of detectable breeding-site categories.

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | int | Primary key, autoincrement | |
| `label` | string | Unique, not null | Raw YOLO class string, e.g. `ss_tire` — must match the model's label exactly |
| `display_name` | string | Not null | Human-readable name, e.g. `"Tire"` |
| `description` | string | | "Why this is a risk" copy |
| `icon_asset_path` | string | | Path under `Resources/`, e.g. `Icons/ss_tire` |

### `ScanReport`

One row per completed scan.

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | int | Primary key, autoincrement | |
| `scanned_at` | string | Not null | `DateTime.UtcNow.ToString("o")` — **always UTC**; every reader in the codebase converts to local time before displaying |
| `duration_seconds` | int | | Scan duration |
| `total_objects_detected` | int | | Total confirmed detections across all labels |
| `notes` | string | | Currently always `"Generated from AR scan."` (set by `ScanManager`) |

### `Detection`

One row per confirmed detection instance within a scan.

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | int | Primary key, autoincrement | |
| `report_id` | int | Indexed | FK → `ScanReport.id` (not DB-enforced — sqlite-net doesn't declare a foreign key constraint here) |
| `object_type_id` | int | Indexed | FK → `ObjectType.id` |
| `bbox_x`, `bbox_y`, `bbox_w`, `bbox_h` | float | | Normalized (0–1) bounding box of the *best* (highest-confidence) example detection for that label in the scan |
| `screenshot_path` | string | | Always empty string in the current pipeline — no screenshot capture is implemented |
| `detected_at` | string | Not null | UTC ISO-8601, same format as `scanned_at` |

### `Mitigation`

One row per object type, holding the "what to do about it" copy.

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | int | Primary key, autoincrement | |
| `object_type_id` | int | Indexed | FK → `ObjectType.id` (1:1 in practice — one mitigation row per object type) |
| `description` | string | Not null | Mitigation steps, as free text (later split into bullets by the UI — see `ReportUIBuilder.SplitMitigationSteps`) |

### `DetectionWithDetails` (read-only projection, not a table)

The shape returned by `GetDetectionsForReport`, produced by a hand-written
SQL join (not sqlite-net's LINQ layer):

```sql
SELECT
    Detection.id AS detection_id,
    Detection.report_id AS report_id,
    ObjectType.display_name AS display_name,
    ObjectType.label AS label,
    ObjectType.description AS object_description,
    Mitigation.description AS mitigation_description,
    Detection.screenshot_path AS screenshot_path,
    Detection.detected_at AS detected_at
FROM Detection
INNER JOIN ObjectType ON Detection.object_type_id = ObjectType.id
LEFT JOIN Mitigation ON Mitigation.object_type_id = ObjectType.id
WHERE Detection.report_id = ?
```

The `LEFT JOIN` on `Mitigation` means `mitigation_description` can be
`null`/empty if a mitigation row is somehow missing for that object type —
every consumer of this data checks for that and falls back to placeholder
text.

## Relationships

```
ObjectType 1 ── 1 Mitigation      (via object_type_id)
ObjectType 1 ── * Detection       (via object_type_id)
ScanReport 1 ── * Detection       (via report_id)
```

## Seed data

`DatabaseManager.SeedObjectTypesAndMitigations()` runs on **every app
launch** (called from `InitializeDatabase()`), not just on first install. It
upserts by `label`: if an `ObjectType` row with that label already exists,
its `display_name`/`description`/`icon_asset_path` are overwritten with the
hardcoded values in `DatabaseManager.cs`; otherwise a new row is inserted.
The same upsert pattern applies to each type's `Mitigation` row. In
practice this means **the hardcoded seed list in `DatabaseManager.cs` is
the source of truth** — any hand-edited row in the live DB gets overwritten
on next launch.

13 object types are currently seeded:

| Label | Display name |
|---|---|
| `ss_birdbath` | Bird Bath |
| `ss_bromiliad` | Bromeliad |
| `ss_bucket` | Bucket |
| `ss_grill` | Grill |
| `ss_inflatablepool` | Inflatable Pool |
| `ss_pot` | Plant Pot |
| `ss_tire` | Tire |
| `ss_trashcan` | Trash Can |
| `ss_treehole` | Tree Hole |
| `ss_waterhyacinth` | Water Hyacinth |
| `ss_wateringcan` | Watering Can |
| `ss_waterlettuce` | Water Lettuce |
| `ss_wheelbarrow` | Wheelbarrow |

**`ss_pot` is seeded and fully supported in the database and UI, but the
currently assigned ONNX model cannot output it** — see
[`KNOWN_ISSUES.md`](KNOWN_ISSUES.md) for details.

## Query patterns already implemented

Reuse these rather than writing new raw queries — they're the canonical
patterns other scripts (`LastScanCardController`, `NotificationManager`,
`ScanHistoryUIBuilder`, `ReportUIBuilder`) already build on:

- `GetAllReports()` — all `ScanReport` rows, **ordered newest-first by
  `id`**. `.FirstOrDefault()` on the result is the standard "most recent
  scan" lookup used throughout the codebase.
- `GetReportById(int reportId)` — single report lookup.
- `GetDetectionsForReport(int reportId)` — the joined `DetectionWithDetails`
  query above.
- `GetObjectTypes()` — all `ObjectType` rows, alphabetized by
  `display_name`.
- `ClearScanHistory()` — deletes **all** `Detection` rows, then **all**
  `ScanReport` rows (detections first, since they reference reports).
  `ObjectType` and `Mitigation` rows are untouched. Wired to the Settings
  page's "Clear Scan History" confirmation button (see
  [`ARCHITECTURE.md`](ARCHITECTURE.md) §7).

## Known duplication

The display name / description / mitigation text seeded here is
**independently duplicated** in `FixSheetManager.cs` (static dictionaries)
and partially in `ScanManager.cs` (`displayNameMap`, `mitigationMap`). None
of these read from the database — editing copy in one place does not
propagate to the others. See [`KNOWN_ISSUES.md`](KNOWN_ISSUES.md).
