using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class BoundingBoxDrawer : MonoBehaviour
{
    public YOLOInference yoloInference;
    public RectTransform boundingBoxContainer;

    [Header("Display Filtering")]
    [Tooltip("Boxes below this confidence are never shown.")]
    [Range(0f, 1f)]
    public float minimumDisplayConfidence = 0.88f;

    [Tooltip("A box must survive this many consecutive YOLO inference results before it is shown.")]
    [Min(1)]
    public int requiredConsecutiveInferenceFrames = 2;

    [Tooltip("The box must overlap its previous location by at least this amount.")]
    [Range(0f, 1f)]
    public float minimumTrackingIoU = 0.25f;

    private readonly List<GameObject> activeBoxes = new List<GameObject>();

    const float CORNER_SIZE = 28f;
    const float CORNER_THICKNESS = 3f;
    const float BAR_HEIGHT = 26f;
    const float SMOOTH_SPEED = 8f;

    private class CandidateState
    {
        public DetectionResult latest;
        public int consecutiveFrames;
    }

    private readonly Dictionary<string, CandidateState> candidates = new Dictionary<string, CandidateState>();
    private List<DetectionResult> smoothedDetections = new List<DetectionResult>();
    private int lastProcessedDetectionFrameId = -1;

    void Update()
    {
        if (yoloInference == null || boundingBoxContainer == null) return;

        int frameId = yoloInference.DetectionFrameId;
        if (frameId != lastProcessedDetectionFrameId)
        {
            lastProcessedDetectionFrameId = frameId;
            UpdateStableDetections(yoloInference.currentDetections);
        }

        DrawBoxes(smoothedDetections);
    }

    private void UpdateStableDetections(List<DetectionResult> newDetections)
    {
        var bestByLabel = new Dictionary<string, DetectionResult>();

        if (newDetections != null)
        {
            foreach (DetectionResult det in newDetections)
            {
                if (det == null || det.confidence < minimumDisplayConfidence) continue;
                if (string.IsNullOrWhiteSpace(det.label) || det.label == "unknown") continue;

                if (!bestByLabel.TryGetValue(det.label, out DetectionResult currentBest) ||
                    det.confidence > currentBest.confidence)
                {
                    bestByLabel[det.label] = det;
                }
            }
        }

        var seenThisFrame = new HashSet<string>();

        foreach (KeyValuePair<string, DetectionResult> pair in bestByLabel)
        {
            string label = pair.Key;
            DetectionResult det = pair.Value;
            seenThisFrame.Add(label);

            if (!candidates.TryGetValue(label, out CandidateState state))
            {
                state = new CandidateState
                {
                    latest = CloneDetection(det),
                    consecutiveFrames = 1
                };
                candidates[label] = state;
            }
            else
            {
                state.consecutiveFrames = IoU(state.latest, det) >= minimumTrackingIoU
                    ? state.consecutiveFrames + 1
                    : 1;

                state.latest = CloneDetection(det);
            }
        }

        var staleLabels = new List<string>();
        foreach (string label in candidates.Keys)
        {
            if (!seenThisFrame.Contains(label)) staleLabels.Add(label);
        }

        foreach (string label in staleLabels)
        {
            candidates.Remove(label);
        }

        var stable = new List<DetectionResult>();
        foreach (CandidateState state in candidates.Values)
        {
            if (state.consecutiveFrames >= requiredConsecutiveInferenceFrames)
            {
                stable.Add(CloneDetection(state.latest));
            }
        }

        SmoothDetections(stable);
    }

    private void SmoothDetections(List<DetectionResult> newDetections)
    {
        var updated = new List<DetectionResult>();

        foreach (DetectionResult newDet in newDetections)
        {
            DetectionResult match = null;
            foreach (DetectionResult existing in smoothedDetections)
            {
                if (existing.label == newDet.label)
                {
                    match = existing;
                    break;
                }
            }

            if (match != null)
            {
                float t = Mathf.Clamp01(Time.deltaTime * SMOOTH_SPEED);
                match.bbox_x = Mathf.Lerp(match.bbox_x, newDet.bbox_x, t);
                match.bbox_y = Mathf.Lerp(match.bbox_y, newDet.bbox_y, t);
                match.bbox_w = Mathf.Lerp(match.bbox_w, newDet.bbox_w, t);
                match.bbox_h = Mathf.Lerp(match.bbox_h, newDet.bbox_h, t);
                match.confidence = newDet.confidence;
                updated.Add(match);
            }
            else
            {
                updated.Add(CloneDetection(newDet));
            }
        }

        smoothedDetections = updated;
    }

    private void DrawBoxes(List<DetectionResult> detections)
    {
        foreach (GameObject box in activeBoxes)
        {
            Destroy(box);
        }
        activeBoxes.Clear();

        if (detections == null) return;

        float screenW = boundingBoxContainer.rect.width;
        float screenH = boundingBoxContainer.rect.height;

        foreach (DetectionResult det in detections)
        {
            float x = det.bbox_x * screenW;
            float y = (1f - det.bbox_y - det.bbox_h) * screenH;
            float w = det.bbox_w * screenW;
            float h = det.bbox_h * screenH;

            CreateDetectionBox(x, y, w, h, det.label, det.confidence);
        }
    }

    private void CreateDetectionBox(float x, float y, float w, float h, string label, float confidence)
    {
        var root = new GameObject("DetectionBox");
        root.transform.SetParent(boundingBoxContainer, false);
        RectTransform rootRect = root.AddComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.zero;
        rootRect.pivot = Vector2.zero;
        rootRect.anchoredPosition = new Vector2(x, y);
        rootRect.sizeDelta = new Vector2(w, h);
        activeBoxes.Add(root);

        Color green = new Color(0.29f, 0.87f, 0.50f, 1f);
        Color greenDim = new Color(0.29f, 0.87f, 0.50f, 0.15f);

        CreateRect(root, "Fill", 0, 0, w, h, greenDim);
        CreateRect(root, "TL_H", 0, h - CORNER_THICKNESS, CORNER_SIZE, CORNER_THICKNESS, green);
        CreateRect(root, "TL_V", 0, h - CORNER_SIZE, CORNER_THICKNESS, CORNER_SIZE, green);
        CreateRect(root, "TR_H", w - CORNER_SIZE, h - CORNER_THICKNESS, CORNER_SIZE, CORNER_THICKNESS, green);
        CreateRect(root, "TR_V", w - CORNER_THICKNESS, h - CORNER_SIZE, CORNER_THICKNESS, CORNER_SIZE, green);
        CreateRect(root, "BL_H", 0, BAR_HEIGHT, CORNER_SIZE, CORNER_THICKNESS, green);
        CreateRect(root, "BL_V", 0, BAR_HEIGHT, CORNER_THICKNESS, CORNER_SIZE, green);
        CreateRect(root, "BR_H", w - CORNER_SIZE, BAR_HEIGHT, CORNER_SIZE, CORNER_THICKNESS, green);
        CreateRect(root, "BR_V", w - CORNER_THICKNESS, BAR_HEIGHT, CORNER_THICKNESS, CORNER_SIZE, green);
        CreateRect(root, "Bar", 0, 0, w, BAR_HEIGHT, greenDim);

        GameObject dot = CreateRect(root, "Dot", 10, (BAR_HEIGHT - 8) / 2f, 8, 8, green);
        dot.GetComponent<RectTransform>().pivot = Vector2.zero;

        var labelObj = new GameObject("Label");
        labelObj.transform.SetParent(root.transform, false);
        RectTransform labelRect = labelObj.AddComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.zero;
        labelRect.anchoredPosition = new Vector2(24, 0);
        labelRect.sizeDelta = new Vector2(w - 60, BAR_HEIGHT);
        TextMeshProUGUI labelText = labelObj.AddComponent<TextMeshProUGUI>();
        labelText.text = Capitalize(label);
        labelText.fontSize = 11;
        labelText.fontStyle = FontStyles.Bold;
        labelText.color = green;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;

        var confObj = new GameObject("Confidence");
        confObj.transform.SetParent(root.transform, false);
        RectTransform confRect = confObj.AddComponent<RectTransform>();
        confRect.anchorMin = Vector2.zero;
        confRect.anchorMax = Vector2.zero;
        confRect.anchoredPosition = new Vector2(0, 0);
        confRect.sizeDelta = new Vector2(w - 8, BAR_HEIGHT);
        TextMeshProUGUI confText = confObj.AddComponent<TextMeshProUGUI>();
        confText.text = Mathf.RoundToInt(confidence * 100) + "%";
        confText.fontSize = 10;
        confText.color = new Color(0.53f, 0.94f, 0.69f, 1f);
        confText.alignment = TextAlignmentOptions.MidlineRight;
    }

    private GameObject CreateRect(GameObject parent, string name, float x, float y, float w, float h, Color color)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent.transform, false);
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(w, h);
        Image img = obj.AddComponent<Image>();
        img.color = color;
        return obj;
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

    private string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return char.ToUpper(value[0]) + value.Substring(1).Replace("_", " ");
    }
}
