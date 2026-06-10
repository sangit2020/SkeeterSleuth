using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class BoundingBoxDrawer : MonoBehaviour
{
    public YOLOInference yoloInference;
    public RectTransform boundingBoxContainer;

    private List<GameObject> activeBoxes = new List<GameObject>();

    const float CORNER_SIZE = 28f;
    const float CORNER_THICKNESS = 3f;
    const float BAR_HEIGHT = 26f;

    // Smoothing
    private List<DetectionResult> smoothedDetections = new List<DetectionResult>();
    const float SMOOTH_SPEED = 8f;

    void Update()
    {
        SmoothDetections(yoloInference.currentDetections);
        DrawBoxes(smoothedDetections);
    }

    void SmoothDetections(List<DetectionResult> newDetections)
    {
        var updated = new List<DetectionResult>();

        foreach (var newDet in newDetections)
        {
            // Find matching smoothed detection by label
            DetectionResult match = null;
            foreach (var s in smoothedDetections)
            {
                if (s.label == newDet.label)
                { match = s; break; }
            }

            if (match != null)
            {
                // Lerp toward new position
                float t = Time.deltaTime * SMOOTH_SPEED;
                match.bbox_x = Mathf.Lerp(match.bbox_x, newDet.bbox_x, t);
                match.bbox_y = Mathf.Lerp(match.bbox_y, newDet.bbox_y, t);
                match.bbox_w = Mathf.Lerp(match.bbox_w, newDet.bbox_w, t);
                match.bbox_h = Mathf.Lerp(match.bbox_h, newDet.bbox_h, t);
                match.confidence = newDet.confidence;
                updated.Add(match);
            }
            else
            {
                updated.Add(new DetectionResult
                {
                    label = newDet.label,
                    bbox_x = newDet.bbox_x,
                    bbox_y = newDet.bbox_y,
                    bbox_w = newDet.bbox_w,
                    bbox_h = newDet.bbox_h,
                    confidence = newDet.confidence
                });
            }
        }

        smoothedDetections = updated;
    }

    void DrawBoxes(List<DetectionResult> detections)
    {
        foreach (var box in activeBoxes)
            Destroy(box);
        activeBoxes.Clear();

        if (detections == null) return;

        float screenW = boundingBoxContainer.rect.width;
        float screenH = boundingBoxContainer.rect.height;

        foreach (var det in detections)
        {
            // Fixed coordinate mapping
            float x = det.bbox_x * screenW;
            float y = (1f - det.bbox_y - det.bbox_h) * screenH;
            float w = det.bbox_w * screenW;
            float h = det.bbox_h * screenH;

            CreateDetectionBox(x, y, w, h, det.label, det.confidence);
        }
    }

    void CreateDetectionBox(float x, float y, float w, float h, string label, float confidence)
    {
        var root = new GameObject("DetectionBox");
        root.transform.SetParent(boundingBoxContainer, false);
        var rootRect = root.AddComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.zero;
        rootRect.pivot = Vector2.zero;
        rootRect.anchoredPosition = new Vector2(x, y);
        rootRect.sizeDelta = new Vector2(w, h);
        activeBoxes.Add(root);

        Color green = new Color(0.29f, 0.87f, 0.50f, 1f);
        Color greenDim = new Color(0.29f, 0.87f, 0.50f, 0.15f);

        CreateRect(root, "Fill", 0, 0, w, h, greenDim);

        // Corners
        CreateRect(root, "TL_H", 0, h - CORNER_THICKNESS, CORNER_SIZE, CORNER_THICKNESS, green);
        CreateRect(root, "TL_V", 0, h - CORNER_SIZE, CORNER_THICKNESS, CORNER_SIZE, green);
        CreateRect(root, "TR_H", w - CORNER_SIZE, h - CORNER_THICKNESS, CORNER_SIZE, CORNER_THICKNESS, green);
        CreateRect(root, "TR_V", w - CORNER_THICKNESS, h - CORNER_SIZE, CORNER_THICKNESS, CORNER_SIZE, green);
        CreateRect(root, "BL_H", 0, BAR_HEIGHT, CORNER_SIZE, CORNER_THICKNESS, green);
        CreateRect(root, "BL_V", 0, BAR_HEIGHT, CORNER_THICKNESS, CORNER_SIZE, green);
        CreateRect(root, "BR_H", w - CORNER_SIZE, BAR_HEIGHT, CORNER_SIZE, CORNER_THICKNESS, green);
        CreateRect(root, "BR_V", w - CORNER_THICKNESS, BAR_HEIGHT, CORNER_THICKNESS, CORNER_SIZE, green);

        CreateRect(root, "Bar", 0, 0, w, BAR_HEIGHT, greenDim);

        var dot = CreateRect(root, "Dot", 10, (BAR_HEIGHT - 8) / 2f, 8, 8, green);
        dot.GetComponent<RectTransform>().pivot = Vector2.zero;

        var labelObj = new GameObject("Label");
        labelObj.transform.SetParent(root.transform, false);
        var labelRect = labelObj.AddComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.zero;
        labelRect.anchoredPosition = new Vector2(24, 0);
        labelRect.sizeDelta = new Vector2(w - 60, BAR_HEIGHT);
        var labelText = labelObj.AddComponent<TextMeshProUGUI>();
        labelText.text = Capitalize(label);
        labelText.fontSize = 11;
        labelText.fontStyle = FontStyles.Bold;
        labelText.color = green;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;

        var confObj = new GameObject("Confidence");
        confObj.transform.SetParent(root.transform, false);
        var confRect = confObj.AddComponent<RectTransform>();
        confRect.anchorMin = Vector2.zero;
        confRect.anchorMax = Vector2.zero;
        confRect.anchoredPosition = new Vector2(0, 0);
        confRect.sizeDelta = new Vector2(w - 8, BAR_HEIGHT);
        var confText = confObj.AddComponent<TextMeshProUGUI>();
        confText.text = Mathf.RoundToInt(confidence * 100) + "%";
        confText.fontSize = 10;
        confText.color = new Color(0.53f, 0.94f, 0.69f, 1f);
        confText.alignment = TextAlignmentOptions.MidlineRight;
    }

    GameObject CreateRect(GameObject parent, string name, float x, float y, float w, float h, Color color)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent.transform, false);
        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(w, h);
        var img = obj.AddComponent<Image>();
        img.color = color;
        return obj;
    }

    string Capitalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpper(s[0]) + s.Substring(1).Replace("_", " ");
    }
}