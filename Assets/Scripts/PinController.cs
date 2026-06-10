using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PinController : MonoBehaviour
{
    public TextMeshProUGUI labelText;
    public TextMeshProUGUI confidenceText;
    public Image barFill;

    private Camera arCamera;

    void Start()
    {
        arCamera = Camera.main;
    }

    public void SetData(string label, float confidence)
    {
        labelText.text = FormatLabel(label);
        confidenceText.text = Mathf.RoundToInt(confidence * 100) + "%";
        barFill.fillAmount = confidence;
    }

    void Update()
    {
        // Billboard — always face camera
        if (arCamera != null)
        {
            transform.LookAt(arCamera.transform);
            transform.Rotate(0, 180, 0);
        }
    }

    string FormatLabel(string label)
    {
        return label.Replace("ss_", "")
                    .Replace("_", " ")
                    .ToUpper();
    }
}