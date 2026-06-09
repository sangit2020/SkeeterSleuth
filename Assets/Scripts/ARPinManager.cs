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

    private Dictionary<string, GameObject> activePins = new Dictionary<string, GameObject>();
    private List<ARRaycastHit> hits = new List<ARRaycastHit>();
    private Dictionary<string, int> detectionFrameCount = new Dictionary<string, int>();
    private Dictionary<string, DetectionResult> latestDetection = new Dictionary<string, DetectionResult>();
    private int requiredFrames = 5;

    void Update()
    {
        if (yoloInference.currentDetections == null) return;
        if (yoloInference.currentDetections.Count == 0) return;

        HashSet<string> detectedThisFrame = new HashSet<string>();

        foreach (var det in yoloInference.currentDetections)
        {
            if (det.confidence < confidenceThreshold) continue;

            string key = det.label;
            detectedThisFrame.Add(key);

            if (activePins.ContainsKey(key)) continue;

            // Always update to latest coordinates
            latestDetection[key] = det;

            if (!detectionFrameCount.ContainsKey(key))
                detectionFrameCount[key] = 0;
            detectionFrameCount[key]++;

            if (detectionFrameCount[key] < requiredFrames) continue;

            // Use the LATEST detection coordinates - not stale ones
            var latest = latestDetection[key];
            float screenX = (latest.bbox_x + latest.bbox_w / 2f) * Screen.width;
            float screenY = (1f - latest.bbox_y - latest.bbox_h / 2f) * Screen.height;

            Ray ray = arCamera.ScreenPointToRay(new Vector3(screenX, screenY, 0));
            Vector3 pinPosition = ray.origin + ray.direction * pinDepth;

            GameObject pin = Instantiate(pinPrefab, pinPosition, Quaternion.identity);
            var controller = pin.GetComponent<PinController>();
            if (controller != null)
                controller.SetData(latest.label, latest.confidence);
            activePins[key] = pin;

            if (scanManager != null)
                scanManager.RegisterDetection(det.label);

            Debug.Log($"Pin placed: {key} conf={latest.confidence:F2} screen=({screenX:F0},{screenY:F0})");
        }

        // Reset counts for labels not seen this frame
        List<string> toReset = new List<string>();
        foreach (var key in detectionFrameCount.Keys)
            if (!detectedThisFrame.Contains(key) && !activePins.ContainsKey(key))
                toReset.Add(key);
        foreach (var key in toReset)
        {
            detectionFrameCount[key] = 0;
            latestDetection.Remove(key);
        }
    }

    public void ClearAllPins()
    {
        foreach (var kvp in activePins)
            if (kvp.Value != null)
                Destroy(kvp.Value);
        activePins.Clear();
        detectionFrameCount.Clear();
        latestDetection.Clear();
        Debug.Log("All pins cleared");
    }
}