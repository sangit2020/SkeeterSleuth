# SkeeterSleuth

An AR mobile app that scans a user's yard through the phone camera, detects
common mosquito breeding sites (standing-water containers, tires, plant
pots, etc.) using an on-device YOLOv8n object-detection model, and generates
a scored risk report with mitigation guidance — entirely offline, with no
backend server.

**Capstone context:** UCF COP 4934 (Senior Design), Group G14, Spring 2026.
Sponsored by Dr. Barbara Sharanowski, UCF Department of Biology (Entomology).
Team: Dylan Duran, Ethan Niessner, Esteban Ramírez Mejía, Elias Sanchez.

> This README is the entry point. Deeper technical references live in
> [`docs/`](docs/):
> - [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — scene structure, script
>   catalog, the AR detection pipeline, and the scan/notification data flow.
> - [`docs/DATABASE.md`](docs/DATABASE.md) — the local SQLite schema and
>   query patterns.
> - [`docs/KNOWN_ISSUES.md`](docs/KNOWN_ISSUES.md) — verified gaps,
>   inconsistencies, and dead code a new contributor should know about
>   before making changes.

## How it works, in one paragraph

The user points their phone at their yard and taps **Begin Scan**. Every
~120ms, `YOLOInference` runs a YOLOv8n model (`Assets/Models/best.onnx`) on
the AR camera feed entirely on-device via Unity's Inference Engine package.
`ARPinManager` waits for a detection to stay confident and spatially stable
across several consecutive inference results before it "confirms" it —
placing a 3D pin in the AR scene and registering the detection with
`ScanManager`. When the user taps **Stop Scan**, `ScanManager` saves a
`ScanReport` and its `Detection` rows to a local SQLite database
(`DatabaseManager`), shows a scan-complete summary with a risk score and
per-item mitigation guidance, and — if the user has opted in — reschedules a
local "check your yard again" notification for 7 days out.

## Tech stack

| Concern | Technology |
|---|---|
| Engine | Unity 6000.4.7f1 (Unity 6), URP 17.4.0 |
| AR | AR Foundation 6.4.2 + ARKit XR Plugin 6.4.2 (ARCore 6.4.2 is present in the manifest but the app is iOS-only — see [Known Issues](docs/KNOWN_ISSUES.md)) |
| On-device ML | `com.unity.ai.inference` 2.6.1 ("Inference Engine", the successor to Sentis) running a 640×640 YOLOv8n ONNX model, CPU backend, no server round-trip |
| Local persistence | SQLite via `com.gilzoide.sqlite-net` 1.3.2 (fetched from GitHub, not the Unity registry); DB file lives at `Application.persistentDataPath/skeeter_sleuth.db` |
| Local notifications | `com.unity.mobile.notifications` 2.4.3, wrapping iOS's `UNUserNotificationCenter` |
| UI | Unity UI (uGUI) + TextMeshPro; screens are largely built at runtime in C# rather than laid out as static prefabs (see [Architecture](docs/ARCHITECTURE.md)) |
| Input | Unity Input System 1.19.0 |
| Backend | **None.** The app is fully offline; all data stays in the on-device SQLite file. |

## Repository layout

```
Assets/
  Scripts/                 All gameplay/app C# (17 files) — see docs/ARCHITECTURE.md
  Scenes/                  homeScreen.unity, OnboardingScreen.unity, ARScreen.unity
  Prefabs/                 UI/manager prefabs — several are unreferenced legacy assets, see Known Issues
  Models/best.onnx         YOLOv8n weights (12-class breeding-site detector)
  Resources/Icons/         Per-object-type icon sprites (loaded via Resources.Load at runtime)
  Resources/BillingMode.json
  Settings/                URP pipeline assets, Build Profiles
  Fonts/, TextMesh Pro/, Materials/, Animation/, UI/, UI Toolkit/
                           Art and UI infrastructure (mostly Unity/package-generated)
  XR/, XRI/, MobileARTemplateAssets/
                           AR Foundation / XR Interaction Toolkit scaffolding from Unity's Mobile AR template
  Layer Lab/               Third-party UI kit asset pack
  MobileDependencyResolver/ Native Android/iOS dependency resolution plugin (EDM4U)
  Samples/                 Imported package sample content (XR Interaction Toolkit starter assets)
  _Recovery/               Unity crash-recovery autosave folder — not real project content
Packages/manifest.json     Package dependency list
ProjectSettings/           Unity project configuration (player settings, build order, etc.)
```

## Getting started

1. Install **Unity Hub**, then install Editor **6000.4.7f1** with the **iOS
   Build Support** module.
2. Clone the repo and open the project root in Unity Hub. Package resolution
   requires network access on first open — `com.gilzoide.sqlite-net` is
   fetched directly from its GitHub URL rather than the Unity package
   registry (see `Packages/manifest.json`).
3. Build Settings should already list the three scenes in the correct order
   (`homeScreen` → `OnboardingScreen` → `ARScreen`), configured in
   `ProjectSettings/EditorBuildSettings.asset`. Verify this if scenes were
   added/reordered.
4. `File > Build Settings` → switch platform to **iOS** if it isn't already
   selected.
5. Build (or **Build and Run**) to produce an Xcode project.
6. Open the generated Xcode project and select **your own Apple Developer
   Team** under Signing & Capabilities — the project does not ship with a
   team ID configured (bundle identifier is `com.ucfg13s26.skeetersleuth.eli`,
   suffixed per-developer; standardize this before any real distribution).
7. Deploy to a **physical iOS device running iOS 15.0+**. AR Foundation/
   ARKit needs a real camera and real motion tracking — the app will not
   produce detections in the iOS Simulator.
8. On first launch, grant the camera permission prompt (required for AR
   tracking). Toggling **Weekly Reminders** in Settings separately triggers
   the notification permission prompt the first time it's switched on.

## Screen flow (summary)

```
homeScreen (splash, ~3s)
   │  PlayerPrefs["OnboardingComplete"] == 1 ?
   ├── no  → OnboardingScreen (3-panel carousel, requests camera permission)
   │            │  "Get Started" → sets PlayerPrefs flag
   │            ▼
   └── yes ─────┴──────────────────────────────► ARScreen (main app)
```

`ARScreen` hosts the live AR camera view, the scan flow, and a slide-out
drawer menu ("Drawer v3") with Home, History, Prevention Tips, About, and
Settings pages. Full detail — including the object-detection pipeline, the
scan save/report flow, and how the notification system ties into it — is in
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

## Known gaps

Before extending this project, read
[`docs/KNOWN_ISSUES.md`](docs/KNOWN_ISSUES.md) — it documents verified
issues (a model/database class mismatch, two disagreeing risk-scoring
tables, duplicated content strings, an unwired Settings toggle, and unused
legacy assets) so you don't rediscover them the hard way.
