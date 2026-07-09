using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class PinController : MonoBehaviour, IPointerClickHandler
{
    public TextMeshProUGUI labelText;
    public TextMeshProUGUI confidenceText;
    public Image barFill;

    private Camera arCamera;
    private string detectionLabel;
    private float detectionConfidence;

    void Start()
    {
        arCamera = Camera.main;

        Canvas canvas = GetComponentInChildren<Canvas>();
        if (canvas != null && canvas.renderMode == RenderMode.WorldSpace)
        {
            canvas.worldCamera = arCamera;
        }
    }

    public void SetData(string label, float confidence)
    {
        detectionLabel = label;
        detectionConfidence = confidence;

        labelText.text = FormatLabel(label);
        confidenceText.text = Mathf.RoundToInt(confidence * 100) + "%";
        barFill.fillAmount = confidence;
    }

    void Update()
    {
        if (arCamera != null)
        {
            transform.LookAt(arCamera.transform);
            transform.Rotate(0, 180, 0);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
#if UNITY_EDITOR
        Debug.Log("PIN TAPPED - this should always print if tap registers");
#endif

        if (FixSheetManager.Instance == null)
        {
#if UNITY_EDITOR
            Debug.Log("PIN TAPPED but FixSheetManager.Instance is NULL");
#endif
            return;
        }

        int count = ScanManager.Instance != null
            ? ScanManager.Instance.GetCountForLabel(detectionLabel)
            : 1;

#if UNITY_EDITOR
        Debug.Log("Calling OpenForLabel with label=" + detectionLabel + " count=" + count);
#endif

        FixSheetManager.Instance.OpenForLabel(detectionLabel, detectionConfidence, count);
    }

    string FormatLabel(string label)
    {
        return label.Replace("ss_", "")
                    .Replace("_", " ")
                    .ToUpper();
    }
}