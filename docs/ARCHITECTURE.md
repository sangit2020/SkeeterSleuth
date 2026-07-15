# Architecture

This document describes how SkeeterSleuth is actually built, as of the
current codebase — scene structure, script responsibilities, the AR
detection pipeline, and how a scan turns into saved data and UI. It's meant
to let a new contributor find the right file for a change without having to
reverse-engineer the scene first.

## 1. Scenes

Three scenes, in build order (`ProjectSettings/EditorBuildSettings.asset`):

| # | Scene | Purpose | Root-level script |
|---|---|---|---|
| 0 | `Assets/Scenes/homeScreen.unity` | Branded splash screen, fixed duration | `SplashScreenManager` |
| 1 | `Assets/Scenes/OnboardingScreen.unity` | First-run 3-panel carousel with a live camera-preview background | `OnboardingManager` |
| 2 | `Assets/Scenes/ARScreen.unity` | The main app — AR camera, scanning, all UI panels | Multiple manager objects (see §3) |

**`SplashScreenManager`** (`Assets/Scripts/SplashScreenManager.cs`) waits
`splashDuration` seconds (default 3s), then checks
`PlayerPrefs.GetInt("OnboardingComplete", 0)`. If set, it loads `ARScreen`
directly; otherwise it loads `OnboardingScreen`.

**`OnboardingManager`** (`Assets/Scripts/OnboardingManager.cs`) drives a
3-panel swipe-by-button carousel with a dot indicator, requests the WebCam
permission via `Application.RequestUserAuthorization` to show a live camera
background behind the cards, and on **Get Started** sets
`PlayerPrefs["OnboardingComplete"] = 1` and loads `ARScreen`.

Note: the WebCam-based camera preview here is separate from AR
Foundation's `ARCameraManager`/`ARCameraBackground` used in `ARScreen` —
onboarding does not use AR Foundation at all.

## 2. ARScreen — panel map

`ARScreen`'s `Canvas` has these direct children (some are static hand-built
hierarchies, some are prefab instances — noted below):

```
Canvas
├── Drawer v3                  hand-built hierarchy (not an instance of Drawer v3.prefab — see Known Issues)
│   ├── OverlayDim, DrawerBg, Header (logo/app name/exit)
│   ├── CardContainer
│   │   └── Background         [LastScanCardController] — "Last Scan" summary card
│   └── NavHome / NavHistory / NavPrevention / NavAbout / NavSettings
├── MenuButton                 hamburger toggle that opens Drawer v3
├── BeginScanButton            primary scan CTA
├── ScanningIndicator          shown while a scan is active
├── ScanCompletePanel          [ScanCompleteController] — post-scan summary overlay
├── BoundingBoxContainer       empty container; see Known Issues (currently unused)
├── ScanStatusChip
│   └── PulseDot               [PulseAnimator]
├── FixSheetPanel              per-detection bottom sheet
├── PageAbout                  hand-built About page
├── PageSettings 1             prefab instance — Settings page
└── PageTips                   prefab instance — Prevention Tips page
```

The drawer's nav rows (`NavHome`, `NavHistory`, `NavPrevention`, `NavAbout`,
`NavSettings`) show/hide the corresponding page. `ScanCompletePanel` and
`FixSheetPanel` are separate overlays triggered by scan completion / pin
taps, not drawer pages.

## 3. Root-level manager GameObjects (ARScreen)

| GameObject | Script | Singleton? | Role |
|---|---|---|---|
| `ScanManager` | `ScanManager.cs` | yes (`Instance`, scene-scoped) | Scan lifecycle: start/stop, aggregate detections, save report, trigger downstream refreshes |
| `DatabaseManager` (prefab instance) | `DatabaseManager.cs` | yes (`Instance`, `DontDestroyOnLoad`) | Local SQLite access + seed data |
| `YOLOInference` | `YOLOInference.cs` | no | Runs the ONNX model against camera frames |
| `ARPinManager` | `ARPinManager.cs` | no | Confirms detections and places 3D pins |
| `ReportUIManager` (prefab instance) | `ReportUIBuilder.cs` | yes (`Instance`) | Full Report list + item detail screens; also the canonical risk/icon lookup used by other scripts |
| `ScanHistoryUIManager` | `ScanHistoryUIBuilder.cs` | yes (`Instance`, `DontDestroyOnLoad`) | Scan History screen |
| `FixSheetManager` | `FixSheetManager.cs` | yes (`Instance`) | Per-detection "why it's a risk / what to do" bottom sheet |
| `NotificationManager` | `NotificationManager.cs` | yes (`Instance`, `DontDestroyOnLoad`) | Weekly local-reminder scheduling |

`AR Session` and `XR Origin (AR Rig)` (with `ARSession`, `ARRaycastManager`,
`ARPlaneManager`, `ARCameraManager`, `ARCameraBackground`) are the AR
Foundation objects the above managers read from.

## 4. Script catalog

### AR detection pipeline

**`YOLOInference.cs`** — subscribes to `ARCameraManager.frameReceived`.
Throttled to run at most every `inferenceIntervalSeconds` (default 0.12s,
~8 Hz), and only while `ScanManager.IsScanning()` is true. Per frame:

1. Acquires the latest CPU camera image and converts/resizes it to a
   640×640 RGB24 buffer.
2. **Rotates the buffer 90° clockwise before inference.** The iOS camera
   sensor is mounted landscape regardless of device orientation; since the
   app is used in portrait, the raw buffer would otherwise be sideways and
   produce zero detections. This is a manual pixel remap
   (`YOLOInference.cs:121-135`), not a Unity API call — if a swapped model
   produces mirrored/rotated results on-device, the fix is described inline
   at `YOLOInference.cs:119-120`.
3. Runs the model via `Unity.InferenceEngine.Worker` (CPU backend) and
   reads back an `(1, 4+numClasses, numDetections)` output tensor. The
   class/anchor counts are read from the tensor shape at runtime rather
   than hardcoded, specifically so a different ONNX file doesn't silently
   index out of bounds.
4. Filters by `rawConfidenceThreshold` (default **0.80**), converts
   center-box coordinates to normalized `x, y, w, h`, then applies
   greedy non-max suppression (`ApplyNMS`) with `nmsIouThreshold` (default
   **0.45**). `classAgnosticNms = true` means overlapping boxes of
   *different* classes also compete for the same spot.
5. Publishes the result via `currentDetections` and increments
   `DetectionFrameId` — a monotonically increasing counter consumers use to
   process each inference result exactly once (see below), instead of
   coupling to Unity's per-`Update()` frame rate.

Class labels are hardcoded in `YOLOInference.cs:300-313` and **must match
the class order the currently-assigned ONNX model was exported with** —
this list currently has 12 entries and does not include `ss_pot` (see
[Known Issues](KNOWN_ISSUES.md)).

**`ARPinManager.cs`** — the confirmation/placement layer. Every `Update()`,
it checks `YOLOInference.DetectionFrameId` and only processes a given
inference result once. For each detected label above `minimumPinConfidence`
(default **0.88** — deliberately stricter than `YOLOInference`'s raw
threshold), it tracks a per-label `CandidateState`:

- The candidate's box must overlap the previous box for that label by at
  least `minimumTrackingIoU` (0.25), **and**
- the AR camera must not have rotated more than `maximumRotationBetweenFrames`
  (12°) or moved more than `maximumTranslationBetweenFrames` (0.15m) between
  inference results,

otherwise the consecutive-frame counter resets to 1. Once a label reaches
`requiredConsecutiveInferenceFrames` (default **3**) consecutive stable
hits, it's "confirmed": a 3D pin (`DetectionPin.prefab`, controlled by
`PinController.cs`) is placed in world space, and
`ScanManager.RegisterDetection(detection)` is called — **this is the only
path by which a detection reaches the database.** Raw per-frame detections
from `YOLOInference` are never saved directly.

Pin placement math (`PlaceConfirmedPin`) has to correct for the fact that
`ARCameraBackground` **cover-crops** the camera image to fill the screen
rather than letterboxing it — the bbox center (normalized against the raw
camera image) is remapped into visible-screen space using the
image/screen aspect ratio before a screen-to-world ray is cast from the
camera pose *at the time that detection frame was captured* (not the
current camera pose), using a hidden disabled `Camera` component
(`poseRayCamera`) purely as a `ScreenPointToRay` calculator.

**`BoundingBoxDrawer.cs`** — a fully implemented on-screen 2D bounding-box
renderer (corner brackets, label, confidence %) with the same
consecutive-frame/IoU stability logic as `ARPinManager`. **It is not
attached to any GameObject in the live scenes** — see
[Known Issues](KNOWN_ISSUES.md).

**`DetectionResult.cs`** — plain data class (`label`, `bbox_x/y/w/h`,
`confidence`), not a `MonoBehaviour`. Shared value type passed between
`YOLOInference`, `ARPinManager`, and `BoundingBoxDrawer`.

**`PinController.cs`** — lives on the instantiated `DetectionPin` prefab.
Billboards to face the AR camera every frame, and on tap
(`IPointerClickHandler`) opens `FixSheetManager.OpenForLabel(label,
confidence, countThisScan)`.

### Scan lifecycle & persistence

**`ScanManager.cs`** — singleton, owns scan state. Key methods:

- `OnBeginScanPressed()` — flips scanning on, resets per-scan detection
  aggregates (`detectedCounts`, `bestDetectionByLabel`), updates scan UI.
- `RegisterDetection(DetectionResult)` / `RegisterDetections(List<...>)` —
  called by `ARPinManager` for each *confirmed* detection; tracks the max
  count seen per label in a single camera frame (to avoid counting the same
  physical object hundreds of times across frames) and keeps the
  highest-confidence example detection per label for its bbox data.
- `OnStopScanPressed()` — the scan-completion pipeline:
  1. Stops scanning, clears AR pins (`ARPinManager.ClearAllPins()`).
  2. `SaveCurrentScanToDatabase()` — writes one `ScanReport` row, then one
     `Detection` row per counted instance (via
     `DatabaseManager.SaveReport` / `SaveDetection`).
  3. `ShowScanComplete()` — loads the just-saved report's detections and
     hands them to `ScanCompleteController.Show(...)`.
  4. `LastScanCardController.Instance.RefreshLastScanCard()` — updates the
     drawer's "Last Scan" summary card.
  5. `NotificationManager.Instance.OnScanCompleted()` — reschedules the
     weekly reminder if it's currently enabled (see §6).
- `OpenGeneratedReport()` — hands off to `ReportUIBuilder.Instance.ShowReport(reportId)`.

`ScanManager` also owns its own `displayNameMap`/`mitigationMap`
dictionaries and a `NormalizeLabel` compatibility shim for older label
spellings — see the duplication note in
[Known Issues](KNOWN_ISSUES.md).

**`DatabaseManager.cs`** — see [`DATABASE.md`](DATABASE.md) for the full
schema. Singleton, `DontDestroyOnLoad`. Opens/creates a SQLite file at
`Application.persistentDataPath/skeeter_sleuth.db` on `Awake()`, creates the
four tables if missing, and **re-seeds `ObjectType`/`Mitigation` rows on
every launch** (upsert by label — see `SeedObjectType`).

### Report / history UI

**`ReportUIBuilder.cs`** (1,924 lines) — the largest script in the project.
Builds two full-screen panels entirely at runtime (no static prefab
layout): a scrollable **Full Report** list (one card per detected item,
overall risk badge + bar) and an **Item Detail** screen (per-item risk
badge, icon, "why it's a risk", "what to do" steps, Prev/Next navigation).
Also exposes the two static helpers other scripts treat as the canonical
risk/icon lookup:

- `ReportUIBuilder.GetRiskLevelPublic(string label)` — used by
  `NotificationManager`, `LastScanCardController`, `ScanCompleteController`,
  and `FixSheetManager`.
- `ReportUIBuilder.LoadObjectIconPublic(string label)` — used by
  `ScanCompleteController`.

See [Known Issues](KNOWN_ISSUES.md) for how this risk table diverges from
`ScanHistoryUIBuilder`'s independent one.

**`ScanHistoryUIBuilder.cs`** (842 lines) — builds a single runtime "Scan
History" panel: header + scrollable list of past `ScanReport`s, each with
date, duration/item-count, and a risk badge computed via its **own**,
differently-thresholded classifier (`ResolveRisk`). Tapping a card calls
`ReportUIBuilder.Instance.ShowReport(reportId)`.

**`LastScanCardController.cs`** — drives the "Last Scan" card inside
Drawer v3. `RefreshLastScanCard()` pulls the newest `ScanReport` via
`DatabaseManager.Instance.GetAllReports().FirstOrDefault()` (the query
already orders newest-first) and mirrors `ReportUIBuilder`'s overall-risk
logic locally to color the card. Called on `Start`/`OnEnable` and by
`ScanManager` after every completed scan.

**`ScanCompleteController.cs`** — drives the post-scan overlay. Groups the
saved detections by label into "chips" (`Tire ×3`) and per-item "what to
do" cards, computes overall risk with its own copy of the same
majority/escalation logic used elsewhere, and wires **Full Report** /
**Scan Again** / **Close** buttons.

**`FixSheetManager.cs`** — the per-detection bottom sheet
(`OpenForLabel(label, confidence, countThisScan)`), animated open/closed
via a coroutine that slides `sheetRect` in/out. Holds its **own** hardcoded
`displayNames`/`descriptions`/`mitigations` dictionaries, duplicated from
`DatabaseManager`'s seed data (see [Known Issues](KNOWN_ISSUES.md)). While
open, it polls `ScanManager` every 0.5s to show a live "still scanning"
row with a running item count and timer.

### Notifications

**`NotificationManager.cs`** — singleton, `DontDestroyOnLoad`. Wraps
`Unity.Notifications.iOS` (guarded by `#if UNITY_IOS || UNITY_EDITOR`,
matching that package's asmdef platform restriction). See §6 below for the
full data flow; in short, it schedules a single non-repeating
`iOSNotificationTimeIntervalTrigger` identified by `"weekly_reminder"`,
always cancel-and-reschedule rather than relying on a repeating trigger, so
the reminder date rolls forward from the actual last scan rather than
drifting on a fixed calendar cadence.

### Onboarding / splash

Covered in §1 above (`SplashScreenManager.cs`, `OnboardingManager.cs`).

### Miscellaneous

- **`PulseAnimator.cs`** — generic sine-wave alpha pulse on an `Image`,
  used on the "recording" dot in `ScanStatusChip` and inside
  `FixSheetPanel`'s scan-status row.
- **`SlidingSwitch.cs`** — purely cosmetic toggle-switch animation (moves a
  handle, recolors a background based on `Toggle.isOn`). Used by both
  toggles on the Settings page. It does **not** implement any app behavior
  itself — it only listens to the `Toggle` it's attached to and animates.
  Functional behavior has to be wired separately onto the `Toggle`'s
  `OnValueChanged` event (see §7).

## 5. Scan data flow (end to end)

```
User taps Begin Scan
   ScanManager.OnBeginScanPressed()
        │
        ▼  every ~120ms while scanning, per AR camera frame
   YOLOInference.RunInference()
        → currentDetections, DetectionFrameId++
        │
        ▼  ARPinManager.Update() diffs DetectionFrameId
   ARPinManager.ProcessDetectionFrame()
        → per label: track confidence/IoU/camera-stability across
          consecutive inference results
        → once confirmed (3 consecutive stable hits):
             place 3D pin  +  ScanManager.RegisterDetection(detection)
        │
User taps Stop Scan
        ▼
   ScanManager.OnStopScanPressed()
        → ARPinManager.ClearAllPins()
        → DatabaseManager.SaveReport(...) + SaveDetection(...) per item
        → ScanCompleteController.Show(reportId, duration, detections)
        → LastScanCardController.RefreshLastScanCard()
        → NotificationManager.OnScanCompleted()  (reschedules reminder if enabled)
```

## 6. Notification system data flow

`NotificationManager` persists reminder state under the PlayerPrefs key
`WeeklyReminderEnabled` (0/1) and always schedules under the fixed iOS
notification identifier `weekly_reminder`, so scheduling is idempotent —
cancel-then-reschedule, never additive.

- **Toggle on** (`OnReminderToggleChanged(true)`) → requests notification
  authorization (a no-op prompt if the user already answered) → on grant,
  schedules; on denial, reverts the toggle in the UI via
  `Toggle.SetIsOnWithoutNotify(false)` and clears the PlayerPrefs flag, so
  the switch doesn't show "on" while nothing is actually scheduled.
- **Toggle off** → cancels the scheduled notification, persists off.
- **Scan completed** (`OnScanCompleted`, called from `ScanManager`) → if
  enabled and still authorized, reschedules from the report that was just
  saved.
- **App launch** (`Start()` → `ResyncOnLaunch()`) → if PlayerPrefs says
  "on" but nothing is actually scheduled (fresh install, or permission was
  granted/revoked outside the app since the flag was last saved),
  reschedules or reverts the toggle to match reality.
- **Fire time**: date is 7 days from the most recent `ScanReport.scanned_at`
  (or from "now" if there are no scans yet), but the **time-of-day is
  clamped to a fixed 10:00 AM local** rather than inheriting whatever time
  the scan happened to occur — see `ComputeFireTimeUtc` in
  `NotificationManager.cs`.
- **Content**: pulled from the most recent scan's risk level (via
  `ReportUIBuilder.GetRiskLevelPublic`, computed locally the same way
  `LastScanCardController` does) and detection count — e.g. *"Your last
  scan found 3 High Risk items 7 days ago — check your yard again."* Falls
  back to a generic message only when there's no prior scan to reference.

## 7. Settings page wiring (as currently configured in the scene)

`PageSettings 1.prefab` defines three sections: **Location** (a
"Location Tagging" toggle, "Save GPS info with each scan"), **Data**
("Clear Scan History", with a confirm/cancel popup), and **Notifications**
(the "Weekly Reminders" toggle). Because a prefab asset can't hold a
reference to a specific scene object, the prefab's own template button/toggle
events use inert placeholder bindings — the *scene's* `PrefabInstance`
override block is what actually wires each control to real behavior. As of
the current `ARScreen.unity`:

| Control | Wired to | Status |
|---|---|---|
| Weekly Reminders toggle | `NotificationManager.OnReminderToggleChanged` | **Wired** |
| Clear Scan History → Confirm | `DatabaseManager.ClearScanHistory` | **Wired** |
| Location Tagging toggle | *(none)* | **Not wired** — see [Known Issues](KNOWN_ISSUES.md) |

If you're adding a new Settings control that needs code behind it, this is
the pattern to follow: leave the prefab's own event binding empty/inert,
then wire the real target and method from within `ARScreen.unity`'s
Inspector (Toggle/Button component → `OnValueChanged`/`OnClick` → drag the
scene's manager GameObject → pick the method).
