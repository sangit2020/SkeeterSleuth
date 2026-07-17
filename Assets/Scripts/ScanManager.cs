using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class ScanManager : MonoBehaviour
{
    public static ScanManager Instance { get; private set; }

    [Header("UI References")]
    public GameObject beginScanButton;
    public GameObject scanningIndicator;
    public GameObject scanCompletePanel;
    public TextMeshProUGUI breedingSitesText;
    public TextMeshProUGUI scanDurationText;
    public TextMeshProUGUI itemsDetectedText;
    public TextMeshProUGUI mitigationPreviewText;
    public GameObject hamburgerButton;
    public GameObject scanStatusChip;

    [Header("Scan Complete Screen")]
    public ScanCompleteController scanCompleteController;

    [Header("Integration References")]
    [Tooltip("Optional. If left empty, ScanManager will try to find YOLOInference in the scene.")]
    public YOLOInference yoloInference;

    [Tooltip("If true, the full report opens immediately after stopping the scan.")]
    public bool openReportImmediatelyAfterScan = false;

    [Header("Detection Photo Capture")]
    [Tooltip("Save a padded JPG crop for each confirmed object label.")]
    public bool captureDetectionPhotos = true;

    [Tooltip("How many inference frames are compared, including the frame that confirmed the detection. Three means current frame plus the next two.")]
    [Range(1, 8)]
    public int cropCandidateFrameWindow = 3;

    [Tooltip("Padding added around every side of the YOLO bounding box.")]
    [Range(0f, 0.5f)]
    public float cropPaddingPercent = 0.20f;

    [Tooltip("Minimum IoU used to follow the same object across the short candidate window.")]
    [Range(0f, 1f)]
    public float cropMatchIouThreshold = 0.15f;

    [Tooltip("Saved JPG quality. 85 is a good balance for a report thumbnail.")]
    [Range(1, 100)]
    public int cropJpegQuality = 85;

    private bool isScanning = false;
    private float scanStartTime;
    private int lastSavedReportId = -1;

    // Stores the max number of each label seen in one confirmed registration call.
    // The current ARPinManager permits one active pin per label, so the photo state
    // is also tracked once per label.
    private Dictionary<string, int> detectedCounts =
        new Dictionary<string, int>();

    // Stores the best example detection for each label so bbox data can be saved.
    private Dictionary<string, DetectionResult> bestDetectionByLabel =
        new Dictionary<string, DetectionResult>();

    // Tracks the confirmed frame plus a few following inference frames and keeps
    // the highest-confidence crop. JPG bytes stay in memory only until Stop Scan.
    private Dictionary<string, CropSelectionState> cropSelectionByLabel =
        new Dictionary<string, CropSelectionState>();

    private sealed class CropSelectionState
    {
        public string label;
        public DetectionResult trackingDetection;
        public DetectionResult bestDetection;
        public float bestConfidence = -1f;
        public byte[] bestJpegBytes;
        public int framesRemaining;
        public int lastProcessedFrameId = -1;
        public bool finalized;
    }

    private Dictionary<string, string> displayNameMap = new Dictionary<string, string>()
    {
        { "ss_birdbath",       "Bird Bath" },
        { "ss_bromiliad",      "Bromeliad" },
        { "ss_bucket",         "Bucket" },
        { "ss_grill",          "Grill" },
        { "ss_inflatablepool", "Inflatable Pool" },
        { "ss_pot",            "Plant Pot" },
        { "ss_tire",           "Tire" },
        { "ss_trashcan",       "Trash Can" },
        { "ss_treehole",       "Tree Hole" },
        { "ss_waterhyacinth",  "Water Hyacinth" },
        { "ss_wateringcan",    "Watering Can" },
        { "ss_waterlettuce",   "Water Lettuce" },
        { "ss_wheelbarrow",    "Wheelbarrow" }
    };

    private Dictionary<string, string> mitigationMap = new Dictionary<string, string>()
    {
        { "ss_birdbath",       "Empty and scrub the bird bath regularly." },
        { "ss_bromiliad",      "Flush the plant regularly and remove excess standing water." },
        { "ss_bucket",         "Empty the bucket and store it upside down." },
        { "ss_grill",          "Check the grill or cover for pooled water and empty it." },
        { "ss_inflatablepool", "Drain the pool when not in use and store it properly." },
        { "ss_pot",            "Empty saucers and improve drainage." },
        { "ss_tire",           "Drain, cover, or properly dispose of unused tires." },
        { "ss_trashcan",       "Keep the lid closed and empty any pooled water." },
        { "ss_treehole",       "Fill small tree holes with sand or foam if appropriate." },
        { "ss_waterhyacinth",  "Thin or remove excess aquatic plants and monitor water." },
        { "ss_wateringcan",    "Empty after use and store upside down." },
        { "ss_waterlettuce",   "Remove excess plant growth and check for larvae." },
        { "ss_wheelbarrow",    "Empty water and store the wheelbarrow upside down." }
    };

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        EnsureYoloReference();

        if (scanCompletePanel != null)
            scanCompletePanel.SetActive(false);

        if (scanningIndicator != null)
            scanningIndicator.SetActive(false);

        if (scanStatusChip != null)
            scanStatusChip.SetActive(false);
    }

    public void OnBeginScanPressed()
    {
        isScanning = true;
        scanStartTime = Time.time;
        lastSavedReportId = -1;

        detectedCounts.Clear();
        bestDetectionByLabel.Clear();
        cropSelectionByLabel.Clear();

        if (beginScanButton != null)
            beginScanButton.SetActive(false);

        if (scanningIndicator != null)
            scanningIndicator.SetActive(true);

        if (scanCompletePanel != null)
            scanCompletePanel.SetActive(false);

        if (hamburgerButton != null)
            hamburgerButton.SetActive(false);

        if (scanStatusChip != null)
            scanStatusChip.SetActive(true);

        Debug.Log("[ScanManager] Scan started.");
    }

    public void OnStopScanPressed()
    {
        if (!isScanning)
        {
            Debug.LogWarning("[ScanManager] Stop scan pressed, but scan was not active.");
            return;
        }

        // Do not pull the latest raw YOLO frame here. Only detections confirmed
        // by ARPinManager are allowed into the report/database pipeline.
        int duration = Mathf.RoundToInt(Time.time - scanStartTime);

        // Stop accepting future inference-frame photo candidates before saving.
        isScanning = false;

        if (scanningIndicator != null)
            scanningIndicator.SetActive(false);

        if (hamburgerButton != null)
            hamburgerButton.SetActive(true);

        if (scanStatusChip != null)
            scanStatusChip.SetActive(false);

        ARPinManager pinManager = UnityEngine.Object.FindAnyObjectByType<ARPinManager>();

        if (pinManager != null)
            pinManager.ClearAllPins();

        lastSavedReportId = SaveCurrentScanToDatabase(duration);

        ShowScanComplete(duration);

        if (LastScanCardController.Instance != null)
            LastScanCardController.Instance.RefreshLastScanCard();

        if (NotificationManager.Instance != null)
            NotificationManager.Instance.OnScanCompleted();

        Debug.Log("[ScanManager] Scan stopped. Last saved report ID: " + lastSavedReportId);

        if (openReportImmediatelyAfterScan)
            OpenGeneratedReport();
    }

    private void EnsureYoloReference()
    {
        if (yoloInference == null)
            yoloInference = UnityEngine.Object.FindFirstObjectByType<YOLOInference>();
    }

    public void RegisterDetections(List<DetectionResult> detections)
    {
        if (!isScanning) return;
        if (detections == null || detections.Count == 0) return;

        Dictionary<string, int> frameCounts = new Dictionary<string, int>();

        foreach (DetectionResult detection in detections)
        {
            if (detection == null) continue;

            string normalizedLabel = NormalizeLabel(detection.label);

            if (string.IsNullOrWhiteSpace(normalizedLabel) ||
                normalizedLabel == "unknown")
            {
                continue;
            }

            if (frameCounts.ContainsKey(normalizedLabel))
                frameCounts[normalizedLabel]++;
            else
                frameCounts[normalizedLabel] = 1;

            DetectionResult normalizedDetection = CloneDetection(
                detection,
                normalizedLabel
            );

            if (!bestDetectionByLabel.ContainsKey(normalizedLabel) ||
                normalizedDetection.confidence >
                bestDetectionByLabel[normalizedLabel].confidence)
            {
                bestDetectionByLabel[normalizedLabel] = normalizedDetection;
            }

            StartPhotoSelectionIfNeeded(
                normalizedLabel,
                normalizedDetection
            );
        }

        foreach (KeyValuePair<string, int> kvp in frameCounts)
        {
            if (!detectedCounts.ContainsKey(kvp.Key))
                detectedCounts[kvp.Key] = kvp.Value;
            else
                detectedCounts[kvp.Key] = Mathf.Max(detectedCounts[kvp.Key], kvp.Value);
        }
    }

    public void RegisterDetection(DetectionResult detection)
    {
        if (detection == null) return;

        RegisterDetections(new List<DetectionResult> { detection });
    }

    // ARPinManager currently calls this string overload after it confirms and
    // places a pin. Resolve that label back to the best matching detection in the
    // latest YOLO frame so the photo pipeline still receives a real bbox.
    public void RegisterDetection(string label)
    {
        if (!isScanning) return;

        string normalizedLabel = NormalizeLabel(label);

        if (string.IsNullOrWhiteSpace(normalizedLabel) ||
            normalizedLabel == "unknown")
        {
            return;
        }

        DetectionResult currentDetection =
            FindBestCurrentYoloDetection(normalizedLabel);

        if (currentDetection != null)
        {
            RegisterDetection(currentDetection);
            return;
        }

        // Fallback preserves the original behavior if a legacy caller registers
        // only a label at a moment when the matching YOLO frame is unavailable.
        if (!detectedCounts.ContainsKey(normalizedLabel))
            detectedCounts[normalizedLabel] = 1;

        if (!bestDetectionByLabel.ContainsKey(normalizedLabel))
        {
            bestDetectionByLabel[normalizedLabel] = new DetectionResult
            {
                label = normalizedLabel,
                bbox_x = 0f,
                bbox_y = 0f,
                bbox_w = 0f,
                bbox_h = 0f,
                confidence = 0f
            };
        }

        Debug.LogWarning(
            "[ScanManager] Registered " + normalizedLabel +
            " without bbox/photo data because no matching current YOLO detection was found."
        );
    }

    /// <summary>
    /// Called once after each completed YOLO inference. This does not register raw
    /// detections. It only evaluates additional photo candidates for labels that
    /// ARPinManager has already confirmed.
    /// </summary>
    public void OnInferenceFrameCompletedForPhotoSelection(
        int frameId,
        List<DetectionResult> frameDetections
    )
    {
        if (!isScanning || !captureDetectionPhotos) return;
        if (cropSelectionByLabel.Count == 0) return;

        EnsureYoloReference();

        if (yoloInference == null) return;

        foreach (CropSelectionState state in cropSelectionByLabel.Values)
        {
            if (state == null || state.finalized) continue;
            if (frameId <= state.lastProcessedFrameId) continue;

            DetectionResult matchingDetection = FindBestTrackingMatch(
                state,
                frameDetections
            );

            if (matchingDetection != null)
            {
                // Follow the most recent box even when this frame does not beat
                // the current confidence. This keeps short-term matching stable.
                state.trackingDetection = CloneDetection(
                    matchingDetection,
                    state.label
                );

                TryStoreCandidateCrop(state, matchingDetection);
            }

            state.lastProcessedFrameId = frameId;
            state.framesRemaining--;

            if (state.framesRemaining <= 0)
            {
                state.finalized = true;

#if UNITY_EDITOR
                Debug.Log(
                    "[ScanManager] Finalized crop for " + state.label +
                    " at confidence " + state.bestConfidence.ToString("F3") + "."
                );
#endif
            }
        }
    }

    private void StartPhotoSelectionIfNeeded(
        string normalizedLabel,
        DetectionResult confirmedDetection
    )
    {
        if (!captureDetectionPhotos) return;
        if (confirmedDetection == null) return;
        if (confirmedDetection.bbox_w <= 0f || confirmedDetection.bbox_h <= 0f) return;
        if (cropSelectionByLabel.ContainsKey(normalizedLabel)) return;

        EnsureYoloReference();

        if (yoloInference == null)
        {
            Debug.LogWarning(
                "[ScanManager] Cannot start photo selection because YOLOInference is missing."
            );
            return;
        }

        CropSelectionState state = new CropSelectionState
        {
            label = normalizedLabel,
            trackingDetection = CloneDetection(
                confirmedDetection,
                normalizedLabel
            ),
            framesRemaining = Mathf.Max(1, cropCandidateFrameWindow),
            lastProcessedFrameId = yoloInference.DetectionFrameId
        };

        cropSelectionByLabel[normalizedLabel] = state;

        // Candidate 1: the frame that caused ARPinManager to confirm the object.
        TryStoreCandidateCrop(state, confirmedDetection);
        state.framesRemaining--;

        if (state.framesRemaining <= 0)
            state.finalized = true;
    }

    private void TryStoreCandidateCrop(
        CropSelectionState state,
        DetectionResult candidate
    )
    {
        if (state == null || candidate == null) return;
        if (candidate.confidence <= state.bestConfidence) return;

        EnsureYoloReference();

        if (yoloInference == null) return;

        if (yoloInference.TryCreatePaddedCropJpg(
                candidate,
                cropPaddingPercent,
                cropJpegQuality,
                out byte[] jpgBytes))
        {
            state.bestConfidence = candidate.confidence;
            state.bestJpegBytes = jpgBytes;
            state.bestDetection = CloneDetection(candidate, state.label);

            if (!bestDetectionByLabel.ContainsKey(state.label) ||
                candidate.confidence >
                bestDetectionByLabel[state.label].confidence)
            {
                bestDetectionByLabel[state.label] =
                    CloneDetection(candidate, state.label);
            }
        }
    }

    private DetectionResult FindBestCurrentYoloDetection(
        string normalizedLabel
    )
    {
        EnsureYoloReference();

        if (yoloInference == null ||
            yoloInference.currentDetections == null)
        {
            return null;
        }

        DetectionResult best = null;

        foreach (DetectionResult detection in yoloInference.currentDetections)
        {
            if (detection == null) continue;

            if (NormalizeLabel(detection.label) != normalizedLabel)
                continue;

            if (best == null || detection.confidence > best.confidence)
                best = detection;
        }

        return best == null
            ? null
            : CloneDetection(best, normalizedLabel);
    }

    private DetectionResult FindBestTrackingMatch(
        CropSelectionState state,
        List<DetectionResult> detections
    )
    {
        if (state == null ||
            detections == null ||
            detections.Count == 0)
        {
            return null;
        }

        DetectionResult bestMatch = null;
        float bestMatchConfidence = -1f;

        foreach (DetectionResult detection in detections)
        {
            if (detection == null) continue;

            if (NormalizeLabel(detection.label) != state.label)
                continue;

            float iou = IoU(state.trackingDetection, detection);

            if (iou < cropMatchIouThreshold)
                continue;

            if (detection.confidence > bestMatchConfidence)
            {
                bestMatch = detection;
                bestMatchConfidence = detection.confidence;
            }
        }

        return bestMatch;
    }

    private int SaveCurrentScanToDatabase(int durationSeconds)
    {
        int totalCount = GetTotalDetectedCount();

        if (DatabaseManager.Instance == null)
        {
            Debug.LogError("[ScanManager] Cannot save scan. DatabaseManager.Instance is null.");
            return -1;
        }

        int reportId = DatabaseManager.Instance.SaveReport(
            durationSeconds: durationSeconds,
            totalObjectsDetected: totalCount,
            notes: "Generated from AR scan."
        );

        foreach (KeyValuePair<string, int> kvp in detectedCounts)
        {
            string label = kvp.Key;
            int count = Mathf.Max(1, kvp.Value);

            DetectionResult bestDetection = null;

            if (bestDetectionByLabel.ContainsKey(label))
                bestDetection = bestDetectionByLabel[label];

            string screenshotPath = "";

            if (captureDetectionPhotos &&
                cropSelectionByLabel.TryGetValue(
                    label,
                    out CropSelectionState photoState))
            {
                if (photoState.bestDetection != null)
                    bestDetection = photoState.bestDetection;

                if (photoState.bestJpegBytes != null &&
                    photoState.bestJpegBytes.Length > 0)
                {
                    screenshotPath =
                        DatabaseManager.Instance.SaveDetectionScreenshot(
                            reportId: reportId,
                            objectLabel: label,
                            jpgBytes: photoState.bestJpegBytes,
                            imageIndex: 0
                        );
                }
            }

            for (int i = 0; i < count; i++)
            {
                DatabaseManager.Instance.SaveDetection(
                    reportId: reportId,
                    objectLabel: label,
                    bboxX: bestDetection != null ? bestDetection.bbox_x : 0f,
                    bboxY: bestDetection != null ? bestDetection.bbox_y : 0f,
                    bboxW: bestDetection != null ? bestDetection.bbox_w : 0f,
                    bboxH: bestDetection != null ? bestDetection.bbox_h : 0f,
                    screenshotPath: screenshotPath
                );
            }
        }

        Debug.Log(
            "[ScanManager] Saved report " + reportId +
            " with " + totalCount + " total detections."
        );

        return reportId;
    }

    private void ShowScanComplete(int duration)
    {
        if (scanCompleteController == null)
        {
            Debug.LogWarning("[ScanManager] scanCompleteController not assigned in Inspector.");

            if (scanCompletePanel != null)
                scanCompletePanel.SetActive(true);

            return;
        }

        if (DatabaseManager.Instance == null || lastSavedReportId < 0)
        {
            Debug.LogWarning("[ScanManager] Cannot populate Scan Complete — no saved report.");

            if (scanCompletePanel != null)
                scanCompletePanel.SetActive(true);

            return;
        }

        List<DetectionWithDetails> detections =
            DatabaseManager.Instance.GetDetectionsForReport(lastSavedReportId);

        scanCompleteController.Show(lastSavedReportId, duration, detections);
    }

    public void OpenGeneratedReport()
    {
        if (lastSavedReportId < 0)
        {
            Debug.LogWarning("[ScanManager] No saved report ID available. Complete a scan first.");
            return;
        }

        if (ReportUIBuilder.Instance == null)
        {
            Debug.LogError("[ScanManager] Cannot open report. ReportUIBuilder.Instance is null.");
            return;
        }

        ReportUIBuilder.Instance.ShowReport(lastSavedReportId);
    }

    public bool IsScanning()
    {
        return isScanning;
    }

    public int GetLastSavedReportId()
    {
        return lastSavedReportId;
    }

    public int GetTotalDetectedCountPublic()
    {
        return GetTotalDetectedCount();
    }

    public float GetElapsedSeconds()
    {
        return Time.time - scanStartTime;
    }

    // Used by the Fix Sheet to show "X found this scan" for one specific label.
    public int GetCountForLabel(string label)
    {
        string normalizedLabel = NormalizeLabel(label);

        return detectedCounts.ContainsKey(normalizedLabel)
            ? detectedCounts[normalizedLabel]
            : 1;
    }

    private int GetTotalDetectedCount()
    {
        int total = 0;

        foreach (int count in detectedCounts.Values)
            total += count;

        return total;
    }

    private DetectionResult CloneDetection(
        DetectionResult detection,
        string normalizedLabel = null
    )
    {
        if (detection == null) return null;

        return new DetectionResult
        {
            label = string.IsNullOrWhiteSpace(normalizedLabel)
                ? detection.label
                : normalizedLabel,
            bbox_x = detection.bbox_x,
            bbox_y = detection.bbox_y,
            bbox_w = detection.bbox_w,
            bbox_h = detection.bbox_h,
            confidence = detection.confidence
        };
    }

    private float IoU(DetectionResult a, DetectionResult b)
    {
        if (a == null || b == null) return 0f;

        float ax2 = a.bbox_x + a.bbox_w;
        float ay2 = a.bbox_y + a.bbox_h;
        float bx2 = b.bbox_x + b.bbox_w;
        float by2 = b.bbox_y + b.bbox_h;

        float intersectionX1 = Mathf.Max(a.bbox_x, b.bbox_x);
        float intersectionY1 = Mathf.Max(a.bbox_y, b.bbox_y);
        float intersectionX2 = Mathf.Min(ax2, bx2);
        float intersectionY2 = Mathf.Min(ay2, by2);

        float intersectionWidth =
            Mathf.Max(0f, intersectionX2 - intersectionX1);

        float intersectionHeight =
            Mathf.Max(0f, intersectionY2 - intersectionY1);

        float intersection =
            intersectionWidth * intersectionHeight;

        float union =
            a.bbox_w * a.bbox_h +
            b.bbox_w * b.bbox_h -
            intersection;

        return union <= 0f ? 0f : intersection / union;
    }

    private string GetDisplayName(string label)
    {
        if (displayNameMap.ContainsKey(label))
            return displayNameMap[label];

        return label;
    }

    private string NormalizeLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return "";

        label = label.Trim().ToLowerInvariant();

        if (label.StartsWith("ss_"))
            return label;

        switch (label)
        {
            case "birdbath":
            case "bird_bath":
            case "bird bath":
                return "ss_birdbath";

            case "bromiliad":
            case "bromeliad":
                return "ss_bromiliad";

            case "bucket":
                return "ss_bucket";

            case "grill":
                return "ss_grill";

            case "inflatablepool":
            case "inflatable_pool":
            case "inflatable pool":
                return "ss_inflatablepool";

            case "pot":
            case "plantpot":
            case "plant_pot":
            case "plant pot":
                return "ss_pot";

            case "tire":
                return "ss_tire";

            case "trashcan":
            case "trash_can":
            case "trash can":
                return "ss_trashcan";

            case "treehole":
            case "tree_hole":
            case "tree hole":
                return "ss_treehole";

            case "waterhyacinth":
            case "water_hyacinth":
            case "water hyacinth":
                return "ss_waterhyacinth";

            case "wateringcan":
            case "watering_can":
            case "watering can":
                return "ss_wateringcan";

            case "waterlettuce":
            case "water_lettuce":
            case "water lettuce":
                return "ss_waterlettuce";

            case "wheelbarrow":
                return "ss_wheelbarrow";

            default:
                return label;
        }
    }
}
