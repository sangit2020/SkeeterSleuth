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

    [Header("Integration References")]
    [Tooltip("Optional. If left empty, ScanManager will try to find YOLOInference in the scene.")]
    public YOLOInference yoloInference;

    [Tooltip("If true, the full report opens immediately after stopping the scan.")]
    public bool openReportImmediatelyAfterScan = false;

    private bool isScanning = false;
    private float scanStartTime;

    private int lastSavedReportId = -1;

    // Stores the max number of each label seen in one scan frame.
    // This avoids counting the same object hundreds of times across camera frames.
    private Dictionary<string, int> detectedCounts = new Dictionary<string, int>();

    // Stores the best example detection for each label so we have bbox data to save.
    private Dictionary<string, DetectionResult> bestDetectionByLabel = new Dictionary<string, DetectionResult>();

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
        if (yoloInference == null)
        {
            yoloInference = UnityEngine.Object.FindFirstObjectByType<YOLOInference>();
        }

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

        // Grab whatever YOLO currently sees before stopping.
        PullLatestYoloDetectionsOnce();

        int duration = Mathf.RoundToInt(Time.time - scanStartTime);

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

        Debug.Log("[ScanManager] Scan stopped. Last saved report ID: " + lastSavedReportId);

        if (openReportImmediatelyAfterScan)
        {
            OpenGeneratedReport();
        }
    }

    private void PullLatestYoloDetectionsOnce()
    {
        if (yoloInference == null)
        {
            yoloInference = UnityEngine.Object.FindFirstObjectByType<YOLOInference>();
        }

        if (yoloInference == null)
        {
            Debug.LogWarning("[ScanManager] No YOLOInference found. No YOLO detections were pulled.");
            return;
        }

        if (yoloInference.currentDetections == null)
        {
            Debug.LogWarning("[ScanManager] YOLO currentDetections list is null.");
            return;
        }

        RegisterDetections(yoloInference.currentDetections);
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

            if (string.IsNullOrWhiteSpace(normalizedLabel) || normalizedLabel == "unknown")
                continue;

            if (frameCounts.ContainsKey(normalizedLabel))
                frameCounts[normalizedLabel]++;
            else
                frameCounts[normalizedLabel] = 1;

            if (!bestDetectionByLabel.ContainsKey(normalizedLabel))
            {
                bestDetectionByLabel[normalizedLabel] = detection;
            }
            else if (detection.confidence > bestDetectionByLabel[normalizedLabel].confidence)
            {
                bestDetectionByLabel[normalizedLabel] = detection;
            }
        }

        foreach (KeyValuePair<string, int> kvp in frameCounts)
        {
            if (!detectedCounts.ContainsKey(kvp.Key))
            {
                detectedCounts[kvp.Key] = kvp.Value;
            }
            else
            {
                detectedCounts[kvp.Key] = Mathf.Max(detectedCounts[kvp.Key], kvp.Value);
            }
        }
    }

    public void RegisterDetection(DetectionResult detection)
    {
        if (detection == null) return;

        List<DetectionResult> singleDetectionList = new List<DetectionResult>();
        singleDetectionList.Add(detection);

        RegisterDetections(singleDetectionList);
    }

    // Backward-compatible method in case another script is already calling RegisterDetection(label).
    public void RegisterDetection(string label)
    {
        if (!isScanning) return;

        string normalizedLabel = NormalizeLabel(label);

        if (string.IsNullOrWhiteSpace(normalizedLabel) || normalizedLabel == "unknown")
            return;

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
    }

    // Used by the Fix Sheet to show "X found this scan" for one specific label.
    public int GetCountForLabel(string label)
    {
        string normalizedLabel = NormalizeLabel(label);
        return detectedCounts.ContainsKey(normalizedLabel) ? detectedCounts[normalizedLabel] : 1;
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
            {
                bestDetection = bestDetectionByLabel[label];
            }

            for (int i = 0; i < count; i++)
            {
                if (bestDetection != null)
                {
                    DatabaseManager.Instance.SaveDetection(
                        reportId: reportId,
                        objectLabel: label,
                        bboxX: bestDetection.bbox_x,
                        bboxY: bestDetection.bbox_y,
                        bboxW: bestDetection.bbox_w,
                        bboxH: bestDetection.bbox_h,
                        screenshotPath: ""
                    );
                }
                else
                {
                    DatabaseManager.Instance.SaveDetection(
                        reportId: reportId,
                        objectLabel: label,
                        bboxX: 0f,
                        bboxY: 0f,
                        bboxW: 0f,
                        bboxH: 0f,
                        screenshotPath: ""
                    );
                }
            }
        }

        Debug.Log("[ScanManager] Saved report " + reportId + " with " + totalCount + " total detections.");

        return reportId;
    }

    private void ShowScanComplete(int duration)
    {
        if (scanCompletePanel != null)
            scanCompletePanel.SetActive(true);

        int totalCount = GetTotalDetectedCount();

        if (breedingSitesText != null)
            breedingSitesText.text = totalCount.ToString();

        if (scanDurationText != null)
            scanDurationText.text = duration + "s";

        if (detectedCounts.Count == 0)
        {
            if (itemsDetectedText != null)
                itemsDetectedText.text = "No items detected";

            if (mitigationPreviewText != null)
                mitigationPreviewText.text = "";

            return;
        }

        string itemList = "";
        string mitList = "";

        foreach (KeyValuePair<string, int> kvp in detectedCounts)
        {
            string label = kvp.Key;
            string displayName = GetDisplayName(label);

            itemList += displayName + " x" + kvp.Value + "\n";

            if (mitigationMap.ContainsKey(label))
            {
                mitList += "• " + mitigationMap[label] + "\n";
            }
        }

        if (itemsDetectedText != null)
            itemsDetectedText.text = itemList;

        if (mitigationPreviewText != null)
            mitigationPreviewText.text = mitList;
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

    private int GetTotalDetectedCount()
    {
        int total = 0;

        foreach (int count in detectedCounts.Values)
        {
            total += count;
        }

        return total;
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

        // If it already matches the YOLO/database format, keep it.
        if (label.StartsWith("ss_"))
            return label;

        // Backward compatibility for older scripts that may still pass old labels.
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