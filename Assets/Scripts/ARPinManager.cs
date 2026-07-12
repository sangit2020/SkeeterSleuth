using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections.Generic;

public class ARPinManager : MonoBehaviour
{
    [Header("References")]
    public YOLOInference yoloInference;
    public ARRaycastManager raycastManager;
    public Camera arCamera;
    public GameObject pinPrefab;
    public ScanManager scanManager;

    [Header("Confirmation Filtering")]
    [Tooltip("A detection must meet this confidence before it can become a pin or saved report item.")]
    [Range(0f, 1f)]
    public float minimumPinConfidence = 0.88f;

    [Tooltip("Number of consecutive YOLO inference results required before placing a pin.")]
    [Min(1)]
    public int requiredConsecutiveInferenceFrames = 3;

    [Tooltip("The same label must overlap its previous box by at least this amount.")]
    [Range(0f, 1f)]
    public float minimumTrackingIoU = 0.25f;

    [Tooltip("If the camera rotates faster than this between inference results, confirmation restarts.")]
    [Min(0f)]
    public float maximumRotationBetweenFrames = 12f;

    [Tooltip("If the camera moves farther than this between inference results, confirmation restarts.")]
    [Min(0f)]
    public float maximumTranslationBetweenFrames = 0.15f;

    [Header("Pin Placement")]
    public float pinDepth = 1.5f;
    public bool verboseLogging = false;

    private struct CameraPose
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    private class CandidateState
    {
        public DetectionResult latestDetection;
        public CameraPose latestPose;
        public int consecutiveFrames;
    }

    private readonly Dictionary<string, GameObject> activePins = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, CandidateState> candidates = new Dictionary<string, CandidateState>();

    private Camera poseRayCamera;
    private int lastProcessedDetectionFrameId = -1;

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
        if (yoloInference == null || arCamera == null) return;

        if (scanManager != null && !scanManager.IsScanning())
        {
            candidates.Clear();
            return;
        }

        // Process each completed YOLO result exactly once. The old code counted
        // Unity Update frames, so one inference result could satisfy "3 frames."
        int detectionFrameId = yoloInference.DetectionFrameId;
        if (detectionFrameId == lastProcessedDetectionFrameId) return;
        lastProcessedDetectionFrameId = detectionFrameId;

        ProcessDetectionFrame(yoloInference.currentDetections);
    }

    private void ProcessDetectionFrame(List<DetectionResult> detections)
    {
        // Keep only the strongest box for each label in this inference result.
        // This matches the existing one-pin-per-label behavior and prevents two
        // same-label boxes in one result from incrementing confirmation twice.
        var bestByLabel = new Dictionary<string, DetectionResult>();

        if (detections != null)
        {
            foreach (DetectionResult det in detections)
            {
                if (det == null || det.confidence < minimumPinConfidence) continue;
                if (string.IsNullOrWhiteSpace(det.label) || det.label == "unknown") continue;
                if (activePins.ContainsKey(det.label)) continue;

                if (!bestByLabel.TryGetValue(det.label, out DetectionResult currentBest) ||
                    det.confidence > currentBest.confidence)
                {
                    bestByLabel[det.label] = det;
                }
            }
        }

        var seenThisFrame = new HashSet<string>();
        CameraPose currentPose = new CameraPose
        {
            position = arCamera.transform.position,
            rotation = arCamera.transform.rotation
        };

        foreach (KeyValuePair<string, DetectionResult> pair in bestByLabel)
        {
            string label = pair.Key;
            DetectionResult det = pair.Value;
            seenThisFrame.Add(label);

            if (!candidates.TryGetValue(label, out CandidateState state))
            {
                state = new CandidateState
                {
                    latestDetection = CloneDetection(det),
                    latestPose = currentPose,
                    consecutiveFrames = 1
                };
                candidates[label] = state;
            }
            else
            {
                float overlap = IoU(state.latestDetection, det);
                float rotationDelta = Quaternion.Angle(state.latestPose.rotation, currentPose.rotation);
                float translationDelta = Vector3.Distance(state.latestPose.position, currentPose.position);

                bool sameScreenObject = overlap >= minimumTrackingIoU;
                bool cameraStable = rotationDelta <= maximumRotationBetweenFrames &&
                                    translationDelta <= maximumTranslationBetweenFrames;

                state.consecutiveFrames = sameScreenObject && cameraStable
                    ? state.consecutiveFrames + 1
                    : 1;

                state.latestDetection = CloneDetection(det);
                state.latestPose = currentPose;
            }

#if UNITY_EDITOR
            if (verboseLogging)
            {
                Debug.Log($"[ARPinManager] candidate={label} " +
                          $"count={state.consecutiveFrames}/{requiredConsecutiveInferenceFrames} " +
                          $"confidence={det.confidence:F2}");
            }
#endif

            if (state.consecutiveFrames >= requiredConsecutiveInferenceFrames)
            {
                PlaceConfirmedPin(label, state.latestDetection, state.latestPose);
                candidates.Remove(label);
            }
        }

        // Consecutive means consecutive: if a label disappears for even one YOLO
        // result, its confirmation count is discarded instead of accumulating forever.
        var staleLabels = new List<string>();
        foreach (string label in candidates.Keys)
        {
            if (!seenThisFrame.Contains(label)) staleLabels.Add(label);
        }

        foreach (string label in staleLabels)
        {
            candidates.Remove(label);
        }
    }

    private void PlaceConfirmedPin(string key, DetectionResult detection, CameraPose pose)
    {
        float convW = arCamera.pixelWidth;
        float convH = arCamera.pixelHeight;
        if (convW <= 0f || convH <= 0f) return;

        float bboxCenterX = detection.bbox_x + detection.bbox_w / 2f;
        float bboxCenterY = detection.bbox_y + detection.bbox_h / 2f;

        float imgAspect = 1f;
        if (yoloInference.LastCpuImageWidth > 0 && yoloInference.LastCpuImageHeight > 0)
        {
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
        float screenY = (1f - coverMappedY) * convH;

        Ray ray = ScreenPointToRayFromPose(screenX, screenY, pose.position, pose.rotation);
        Vector3 pinPosition = ray.origin + ray.direction * pinDepth;

        GameObject pin = Instantiate(pinPrefab, pinPosition, Quaternion.identity);
        PinController controller = pin.GetComponent<PinController>();
        if (controller != null)
        {
            controller.SetData(detection.label, detection.confidence);
        }

        activePins[key] = pin;

        // Only confirmed detections reach the report/database pipeline.
        if (scanManager != null)
        {
            scanManager.RegisterDetection(detection);
        }

#if UNITY_EDITOR
        if (verboseLogging)
        {
            Debug.Log($"[ARPinManager] Confirmed pin placed: {key} " +
                      $"confidence={detection.confidence:F2} " +
                      $"screen=({screenX:F0},{screenY:F0}) world={pinPosition:F3}");
        }
#endif
    }

    private Ray ScreenPointToRayFromPose(float screenX, float screenY, Vector3 pos, Quaternion rot)
    {
        poseRayCamera.CopyFrom(arCamera);
        poseRayCamera.enabled = false;
        poseRayCamera.transform.SetPositionAndRotation(pos, rot);
        return poseRayCamera.ScreenPointToRay(new Vector3(screenX, screenY, 0f));
    }

    private static DetectionResult CloneDetection(DetectionResult source)
    {
        return new DetectionResult
        {
            label = source.label,
            bbox_x = source.bbox_x,
            bbox_y = source.bbox_y,
            bbox_w = source.bbox_w,
            bbox_h = source.bbox_h,
            confidence = source.confidence
        };
    }

    private static float IoU(DetectionResult a, DetectionResult b)
    {
        if (a == null || b == null) return 0f;

        float ax2 = a.bbox_x + a.bbox_w;
        float ay2 = a.bbox_y + a.bbox_h;
        float bx2 = b.bbox_x + b.bbox_w;
        float by2 = b.bbox_y + b.bbox_h;

        float ix1 = Mathf.Max(a.bbox_x, b.bbox_x);
        float iy1 = Mathf.Max(a.bbox_y, b.bbox_y);
        float ix2 = Mathf.Min(ax2, bx2);
        float iy2 = Mathf.Min(ay2, by2);

        float interW = Mathf.Max(0f, ix2 - ix1);
        float interH = Mathf.Max(0f, iy2 - iy1);
        float intersection = interW * interH;
        float union = a.bbox_w * a.bbox_h + b.bbox_w * b.bbox_h - intersection;

        return union <= 0f ? 0f : intersection / union;
    }

    public void ClearAllPins()
    {
        foreach (KeyValuePair<string, GameObject> pair in activePins)
        {
            if (pair.Value != null) Destroy(pair.Value);
        }

        activePins.Clear();
        candidates.Clear();
        lastProcessedDetectionFrameId = -1;

#if UNITY_EDITOR
        Debug.Log("All pins cleared");
#endif
    }

    void OnDestroy()
    {
        if (poseRayCamera != null)
        {
            Destroy(poseRayCamera.gameObject);
        }
    }
}
