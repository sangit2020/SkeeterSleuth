# Known Issues & Technical Debt

This is a list of verified, concrete issues in the current codebase — not
speculation. Each item was confirmed by reading the actual source, scene
YAML, or prefab files listed. Intended for whoever picks this project up
next, so these aren't rediscovered by accident.

## 1. The current model can never detect `ss_pot` ("Plant Pot")

`ss_pot` is fully wired everywhere *except* the model:

- Seeded in the database (`DatabaseManager.cs:137-143`).
- Present in `FixSheetManager`'s hardcoded dictionaries (`FixSheetManager.cs:54,71,88`).
- Present in `ScanManager`'s `displayNameMap`/`mitigationMap` and its label
  normalization switch (`ScanManager.cs:49,66,466`).
- Present in both risk-classification tables (`ReportUIBuilder.cs:1784,1882`,
  `ScanHistoryUIBuilder.cs:86`).
- Has an icon (`Resources/Icons/icon_pot_bg_2e583d.png`).

But `YOLOInference.cs`'s hardcoded label array (`YOLOInference.cs:300-313`)
has **12 entries and explicitly excludes `ss_pot`**, per the inline comment:
> "The current best.onnx has 12 classes and does NOT include ss_pot. Keeping
> ss_pot here would shift every later label backward by one."

**Effect**: "Plant Pot" can never appear as a detection with the
`Assets/Models/best.onnx` file currently in the repo, even though the rest
of the stack is ready for it. If a retrained model that includes `ss_pot`
is dropped in, the label array in `YOLOInference.cs` needs to be updated to
match its exact class order — otherwise every class after the insertion
point will be mislabeled.

## 2. Two risk-classification tables disagree with each other

Per-object and per-scan risk level ("Low"/"Moderate"/"High") is computed
independently in two places, using two different label→risk mappings:

**`ReportUIBuilder.GetRiskLevel`** (substring match via `.Contains()`):
- High: `ss_bucket`, `ss_tire`, `ss_bromiliad`, `ss_inflatablepool`, `ss_waterhyacinth`, `ss_waterlettuce`
- Moderate: `ss_birdbath`, `ss_pot`, `ss_trashcan`, `ss_treehole`, `ss_wheelbarrow`, `ss_wateringcan`, `ss_grill`

**`ScanHistoryUIBuilder.ResolveRisk`** (exact `HashSet` match):
- High: `ss_tire`, `ss_bucket`, `ss_trashcan`, `ss_wheelbarrow`, `ss_inflatablepool`, `ss_grill`
- Med: `ss_birdbath`, `ss_pot`, `ss_wateringcan`, `ss_treehole`, `ss_bromiliad`, `ss_waterhyacinth`, `ss_waterlettuce`

These disagree on `ss_trashcan`, `ss_wheelbarrow`, `ss_grill` (Moderate in
one, High in the other) and on `ss_bromiliad`, `ss_waterhyacinth`,
`ss_waterlettuce` (High in one, Moderate/Med in the other). The overall-scan
escalation rule also differs — `ReportUIBuilder.ComputeOverallRisk`
escalates a scan to High if it contains **2 or more** Moderate items, while
`ScanHistoryUIBuilder` has no such rule.

**Effect**: the same scan can show a different risk badge depending on
whether the user is looking at the Scan History list versus the Full
Report / Scan Complete screen. `ScanCompleteController` and
`LastScanCardController` both mirror `ReportUIBuilder`'s table (copy-pasted
locally, not shared), and `NotificationManager` also calls
`ReportUIBuilder.GetRiskLevelPublic` — so `ReportUIBuilder`'s table is the
majority/default one. If unifying these, `ScanHistoryUIBuilder.ResolveRisk`
is the one that should be brought in line with
`ReportUIBuilder.GetRiskLevel`, and ideally both should call one shared
method instead of each maintaining a private copy.

## 3. Object copy (names/descriptions/mitigations) is duplicated in three places

The same content exists independently in:
1. `DatabaseManager.SeedObjectTypesAndMitigations()` (the actual source of
   truth for the database — re-applied on every launch).
2. `FixSheetManager`'s static `displayNames`/`descriptions`/`mitigations`
   dictionaries.
3. `ScanManager`'s `displayNameMap`/`mitigationMap` dictionaries.

None of these read from each other or from the database. Editing mitigation
copy in one place (e.g. to fix a typo) silently does not update the other
two. If you're changing user-facing copy for an object type, grep for the
label string across all three files.

## 4. "Location Tagging" toggle in Settings has no backing implementation

The Settings page (`PageSettings 1.prefab`) has a "Location" section with a
toggle labeled "Location Tagging" / "Save GPS info with each scan". This
toggle's `OnValueChanged` has **no override anywhere in `ARScreen.unity`** —
verified by diffing against how the neighboring "Weekly Reminders" toggle
*is* wired (a `PrefabInstance` modification block targeting the toggle's
`onValueChanged.m_PersistentCalls`). No `LocationManager`-equivalent script
exists anywhere in `Assets/Scripts/`, and nothing in `DatabaseManager`'s
schema stores GPS coordinates. This toggle is currently decorative only —
the same state the "Weekly Reminders" toggle was in before it was wired up
to `NotificationManager`. If GPS tagging is implemented later, it needs:
a location-permission request flow, a place to store lat/lng (likely new
columns on `Detection` or `ScanReport`), and a `Toggle.OnValueChanged`
binding added the same way `NotificationManager.OnReminderToggleChanged`
was added — see `docs/ARCHITECTURE.md` §7 for the pattern.

## 5. Orphaned `runMockScanOnStart` field in `DatabaseManager.prefab`

`Assets/Prefabs/DatabaseManager.prefab` still serializes
`runMockScanOnStart: 0` on the `DatabaseManager` component, and
`ARScreen.unity` carries a stale `PrefabInstance` property override for the
same path. `DatabaseManager.cs` **no longer declares this field** — it was
removed at some point (see git history: `dcaecbd6 Removed mock scan data`,
`b3398faa Clean up mock/test data, guard debug logging...`). Unity silently
ignores unknown serialized properties on deserialize, so this is inert, not
a bug — but it will keep showing up in prefab diffs and is worth deleting
next time that prefab is opened and re-saved in the Editor.

## 6. `BoundingBoxDrawer.cs` is dead code

`BoundingBoxDrawer.cs` is a fully implemented 2D on-screen bounding-box
renderer (corner brackets, label, confidence % — visually similar to a
typical CV demo overlay), with the same consecutive-frame/IoU stability
gating as `ARPinManager`. **It is not attached to any GameObject in any
live scene or prefab.** The only reference to this script anywhere in the
repository is inside `Assets/_Recovery/0.unity`, a Unity Editor
crash-recovery autosave file — not part of the actual project content or
build. `ARScreen`'s `Canvas` does have an empty `BoundingBoxContainer`
GameObject sitting under it, matching the field this script expects, but
nothing currently populates it.

If on-screen 2D boxes (as opposed to the 3D AR pins `ARPinManager` places)
are wanted, attach `BoundingBoxDrawer` to a GameObject in `ARScreen.unity`
and wire its `yoloInference` field to the scene's `YOLOInference` manager
and its `boundingBoxContainer` field to the existing `BoundingBoxContainer`
`RectTransform`.

## 7. Several `Assets/Prefabs/` assets are unreferenced

`DataPopup.prefab`, `Drawer v3.prefab`, `FixSheetPanel.prefab`,
`FixSheetManager.prefab`, `InteractionManager.prefab`,
`MenuButton v3.prefab`, `Pin.prefab`, `PagesWrapper.prefab`, and
`ScanStatusChip.prefab` have zero references in any scene, any other
prefab, or any `Resources.Load` call in the scripts. The live
`ARScreen.unity` scene instead has independently hand-built,
identically-named GameObject hierarchies directly under `Canvas` (its own
`Drawer v3`, `FixSheetPanel`, `ScanStatusChip` trees) rather than instances
of these prefab assets. This looks like these prefabs were exported at some
point during development and the in-scene copies then diverged and lost
their prefab link, rather than a deliberate decision.

Two effects worth knowing about: (a) these prefab assets are safe to delete
if confirmed unused, and (b) any UI change made directly in the scene
hierarchy today does **not** flow back into a reusable prefab — if this UI
needs to be reused elsewhere, it would need to be re-prefabbed from the
live scene hierarchy first.

## 8. Android application identifier is left at the Unity template default

`ProjectSettings/ProjectSettings.asset` sets the Android
`applicationIdentifier` to `com.unity.template.ar_mobile` — the literal
default from Unity's Mobile AR template, never customized. The app is not
currently built or tested for Android despite `com.unity.xr.arcore`,
`com.unity.mobile.android-logcat`, and other Android-capable packages being
present in `Packages/manifest.json`. Update this identifier before
attempting any real Android build.

## 9. No Apple Developer Team ID is configured

`ProjectSettings/ProjectSettings.asset` has no `AppleDeveloperTeamID` set.
Each developer needs to select their own team in the generated Xcode
project's Signing & Capabilities pane before building to a device — this is
also reflected in the iOS bundle identifier already having a per-developer
suffix (`com.ucfg13s26.skeetersleuth.eli`), suggesting the team has been
building individually rather than with a shared signing identity. Worth
standardizing before any real TestFlight/App Store distribution.
