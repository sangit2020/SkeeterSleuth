using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;

public class ARPinManager : MonoBehaviour
{
    [Header("References")]
    public YOLOInference yoloInference;
    public ARRaycastManager raycastManager;
    public Camera arCamera;
    public GameObject pinPrefab;
    public ScanManager scanManager;

    [Header("Settings")]
    public float confidenceThreshold = 0.70f;
    public float pinDepth = 1.5f;
    public bool verboseLogging = true;

    private struct CameraPose
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    private Dictionary<string, GameObject> activePins = new Dictionary<string, GameObject>();
    private List<ARRaycastHit> hits = new List<ARRaycastHit>();
    private Dictionary<string, int> detectionFrameCount = new Dictionary<string, int>();
    private Dictionary<string, DetectionResult> latestDetection = new Dictionary<string, DetectionResult>();
    private Dictionary<string, CameraPose> latestDetectionCameraPose = new Dictionary<string, CameraPose>();
    // Cumulative sightings needed before placing a pin - NOT consecutive frames.
    // ScanManager saves a detection to the database on its very first sighting
    // (no stability check at all), so any debounce here that is stricter than
    // "seen a few times total" will place fewer pins than the database has rows.
    // Lowered from 5 (and switched from consecutive to cumulative, see Update())
    // to close that gap while still filtering out single-frame noise.
    private int requiredFrames = 3;

    // Hidden, never-rendered camera used purely to reproduce Camera.ScreenPointToRay's
    // unprojection math against a *stored* pose instead of arCamera's live transform.
    // CopyFrom() mirrors FOV/aspect/near-far/projection (including any asymmetric AR
    // frustum ARFoundation may set), so this matches what arCamera itself would have
    // produced at that earlier instant.
    private Camera poseRayCamera;

    void Awake()
    {
        var go = new GameObject("ARPinManager_PoseRayCamera");
        go.hideFlags = HideFlags.HideInHierarchy;
        go.transform.SetParent(transform, false);
        poseRayCamera = go.AddComponent<Camera>();
        poseRayCamera.enabled = false;
    }

    void Update()
    {
        if (yoloInference.currentDetections == null) return;
        if (yoloInference.currentDetections.Count == 0) return;

        foreach (var det in yoloInference.currentDetections)
        {
            if (det.confidence < confidenceThreshold) continue;

            string key = det.label;

            if (activePins.ContainsKey(key)) continue;

            // Snapshot the camera pose at the exact moment this detection's bbox was
            // captured. We place the pin using THIS pose, not arCamera's pose at
            // placement time (requiredFrames frames later), since the bbox is only
            // valid relative to where the camera was when YOLO produced it.
            latestDetection[key] = det;
            latestDetectionCameraPose[key] = new CameraPose
            {
                position = arCamera.transform.position,
                rotation = arCamera.transform.rotation
            };

            if (!detectionFrameCount.ContainsKey(key))
                detectionFrameCount[key] = 0;
            detectionFrameCount[key]++;

            if (verboseLogging)
            {
                Debug.Log($"[ARPinManager] update key={key} count={detectionFrameCount[key]}/{requiredFrames} " +
                          $"bbox=(x={det.bbox_x:F4},y={det.bbox_y:F4},w={det.bbox_w:F4},h={det.bbox_h:F4}) conf={det.confidence:F2} " +
                          $"camPos={arCamera.transform.position:F3} camRotEuler={arCamera.transform.rotation.eulerAngles:F1} " +
                          $"screenOrientation={Screen.orientation}");
            }

            if (detectionFrameCount[key] < requiredFrames) continue;

            // Use the LATEST detection coordinates AND the camera pose captured at
            // that same moment - not the camera's current (T+requiredFrames) transform.
            var latest = latestDetection[key];
            var pose = latestDetectionCameraPose[key];

            // arCamera.pixelWidth/pixelHeight reflect the camera's actual render
            // target resolution and are what ScreenPointToRay interprets coordinates
            // against. Screen.width/Screen.height can diverge from this (a frame of
            // lag after rotation, a camera with a non-fullscreen pixelRect, or
            // discrepancies under the Editor's Device Simulator), which would silently
            // feed ScreenPointToRay a point in the wrong coordinate space.
            float convW = arCamera.pixelWidth;
            float convH = arCamera.pixelHeight;

            float bboxCenterX = latest.bbox_x + latest.bbox_w / 2f;
            float bboxCenterY = latest.bbox_y + latest.bbox_h / 2f;

            // The bbox is normalized against the FULL sensor frame (post-rotation,
            // portrait-oriented), but ARCameraBackground displays that frame with a
            // "cover" fit: it scales the image up to fill the screen with no black
            // bars, cropping whichever axis has excess (this is the ARFoundation
            // default - passthrough camera views don't letterbox). That means only a
            // central sub-range of the bbox's normalized [0,1] space is actually
            // visible on screen. Mapping bboxCenter directly onto screen pixels
            // ignores that crop and is why the pin lands near the object but off
            // from its true center - the error grows with distance from screen
            // center, which matches "near but not centered" rather than "way off".
            float imgAspect = 1f;
            if (yoloInference.LastCpuImageWidth > 0 && yoloInference.LastCpuImageHeight > 0)
            {
                // Sensor is mounted landscape; after YOLOInference's 90-degree
                // rotation, the logical portrait width/height are swapped.
                imgAspect = (float)yoloInference.LastCpuImageHeight / yoloInference.LastCpuImageWidth;
            }
            float screenAspect = convW / convH;

            float visibleFracX = Mathf.Min(1f, screenAspect / imgAspect);
            float visibleFracY = Mathf.Min(1f, imgAspect / screenAspect);
            float offsetX = (1f - visibleFracX) / 2f;
            float offsetY = (1f - visibleFracY) / 2f;

            float coverMappedX = (bboxCenterX - offsetX) / visibleFracX;
            float coverMappedY = (bboxCenterY - offsetY) / visibleFracY;

            float screenX = coverMappedX * convW;
            // Flip Y: bbox coordinates are top-left-origin/Y-down (image space),
            // Unity screen space is bottom-left-origin/Y-up. This flip is only
            // correct if the image YOLO ran on shares the same orientation as the
            // screen - see YOLOInference: the CPU image conversion applies a
            // 90-degree rotation to handle that; if pins still land 90 degrees off,
            // that rotation direction is the next thing to check, not this flip.
            float screenY = (1f - coverMappedY) * convH;

            if (verboseLogging)
            {
                Debug.Log($"[ARPinManager] PLACEMENT key={key}\n" +
                          $"  Screen.width/height       = ({Screen.width}, {Screen.height})\n" +
                          $"  arCamera.pixelWidth/Height = ({arCamera.pixelWidth}, {arCamera.pixelHeight})\n" +
                          $"  used for conversion        = ({convW}, {convH})\n" +
                          $"  Screen.orientation         = {Screen.orientation}\n" +
                          $"  sensor size (raw, landscape) = ({yoloInference.LastCpuImageWidth}, {yoloInference.LastCpuImageHeight})\n" +
                          $"  imgAspect/screenAspect     = ({imgAspect:F4}, {screenAspect:F4})\n" +
                          $"  visibleFracX/Y, offsetX/Y  = ({visibleFracX:F4}, {visibleFracY:F4}), ({offsetX:F4}, {offsetY:F4})\n" +
                          $"  bbox center (normalized)   = ({bboxCenterX:F4}, {bboxCenterY:F4})\n" +
                          $"  cover-mapped center        = ({coverMappedX:F4}, {coverMappedY:F4})\n" +
                          $"  screenX/screenY            = ({screenX:F1}, {screenY:F1})\n" +
                          $"  stored camPos (capture T)  = {pose.position:F3}\n" +
                          $"  stored camRotEuler         = {pose.rotation.eulerAngles:F1}\n" +
                          $"  live camPos (placement)    = {arCamera.transform.position:F3}\n" +
                          $"  live camRotEuler           = {arCamera.transform.rotation.eulerAngles:F1}\n" +
                          $"  camPos drift over latency  = {Vector3.Distance(pose.position, arCamera.transform.position):F4} m\n" +
                          $"  camRot drift over latency  = {Quaternion.Angle(pose.rotation, arCamera.transform.rotation):F2} deg");
            }

            Ray ray = ScreenPointToRayFromPose(screenX, screenY, pose.position, pose.rotation);
            Vector3 pinPosition = ray.origin + ray.direction * pinDepth;

            if (verboseLogging)
            {
                Debug.Log($"[ARPinManager] ray key={key} origin={ray.origin:F3} dir={ray.direction:F3} " +
                          $"pinDepth={pinDepth:F2} -> pinPosition={pinPosition:F3}");
            }

            GameObject pin = Instantiate(pinPrefab, pinPosition, Quaternion.identity);
            var controller = pin.GetComponent<PinController>();
            if (controller != null)
                controller.SetData(latest.label, latest.confidence);
            activePins[key] = pin;

            if (scanManager != null)
                scanManager.RegisterDetection(det.label);

            Debug.Log($"Pin placed: {key} conf={latest.confidence:F2} screen=({screenX:F0},{screenY:F0}) world={pinPosition:F3}");
        }
    }

    // Reproduces Camera.ScreenPointToRay against an explicit position/rotation
    // snapshot instead of arCamera's current transform, so a detection captured at
    // time T can be placed using the camera pose that was actually true at time T.
    private Ray ScreenPointToRayFromPose(float screenX, float screenY, Vector3 pos, Quaternion rot)
    {
        poseRayCamera.CopyFrom(arCamera);
        poseRayCamera.enabled = false;
        poseRayCamera.transform.SetPositionAndRotation(pos, rot);
        return poseRayCamera.ScreenPointToRay(new Vector3(screenX, screenY, 0f));
    }

    public void ClearAllPins()
    {
        foreach (var kvp in activePins)
            if (kvp.Value != null)
                Destroy(kvp.Value);
        activePins.Clear();
        detectionFrameCount.Clear();
        latestDetection.Clear();
        latestDetectionCameraPose.Clear();
        Debug.Log("All pins cleared");
    }

    void OnDestroy()
    {
        if (poseRayCamera != null)
            Destroy(poseRayCamera.gameObject);
    }
}
