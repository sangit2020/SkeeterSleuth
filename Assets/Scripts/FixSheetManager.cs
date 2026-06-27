using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class FixSheetManager : MonoBehaviour
{
    public static FixSheetManager Instance { get; private set; }

    [Header("Sheet Root")]
    public GameObject sheetPanel;

    [Header("Header")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI subtitleText;
    public Image riskPill;
    public TextMeshProUGUI riskPillText;

    [Header("Body")]
    public TextMeshProUGUI whyText;
    public Transform stepsContainer;
    public GameObject stepPrefab;
    public TextMeshProUGUI confidenceText;
    public Image confidenceBarFill;

    [Header("Scan Status Row")]
    public GameObject scanStatusRow;
    public TextMeshProUGUI scanFoundCountText;
    public TextMeshProUGUI scanTimerText;

    [Header("Risk Colors")]
    public Color highBg  = new Color(216f/255f, 90f/255f, 48f/255f, 0.20f);
    public Color highTxt = new Color32(0xD8, 0x5A, 0x30, 0xFF);
    public Color medBg   = new Color(239f/255f, 159f/255f, 39f/255f, 0.20f);
    public Color medTxt  = new Color32(0xEF, 0x9F, 0x27, 0xFF);
    public Color lowBg   = new Color(93f/255f, 202f/255f, 165f/255f, 0.15f);
    public Color lowTxt  = new Color32(0x5D, 0xCA, 0xA5, 0xFF);

    [Header("Animation")]
    public RectTransform sheetRect;
    public float animDuration = 0.28f;

    private float sheetHeight;
    private bool isOpen = false;

    private static readonly Dictionary<string, string> displayNames = new Dictionary<string, string>
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

    private static readonly Dictionary<string, string> descriptions = new Dictionary<string, string>
    {
        { "ss_birdbath",       "Bird baths can hold standing water and become mosquito breeding sites if the water is not changed regularly." },
        { "ss_bromiliad",      "Bromeliads can collect water between their leaves, which may create a small mosquito breeding site." },
        { "ss_bucket",         "Buckets can collect rainwater and become mosquito breeding sites when left outside." },
        { "ss_grill",          "Outdoor grills and grill covers can trap rainwater, creating areas where mosquitoes may breed." },
        { "ss_inflatablepool", "Inflatable pools can hold large amounts of standing water and quickly become mosquito breeding sites when unused." },
        { "ss_pot",            "Plant pots and saucers can collect water after watering or rainfall, creating small mosquito breeding areas." },
        { "ss_tire",           "Tires can trap rainwater and are one of the most common outdoor mosquito breeding sites." },
        { "ss_trashcan",       "Trash cans and lids can collect standing water, especially if left uncovered or upside down incorrectly." },
        { "ss_treehole",       "Tree holes can naturally collect rainwater and may become mosquito breeding sites." },
        { "ss_waterhyacinth",  "Floating aquatic plants like water hyacinth can create sheltered areas where mosquitoes may breed." },
        { "ss_wateringcan",    "Watering cans can hold leftover water and become mosquito breeding sites when left outside." },
        { "ss_waterlettuce",   "Water lettuce can create sheltered standing-water areas that may support mosquito breeding." },
        { "ss_wheelbarrow",    "Wheelbarrows can collect rainwater when left outside upright." }
    };

    private static readonly Dictionary<string, string> mitigations = new Dictionary<string, string>
    {
        { "ss_birdbath",       "Empty and scrub the bird bath regularly. Change the water at least once a week." },
        { "ss_bromiliad",      "Flush the plant regularly with fresh water. Remove excess standing water from leaf pockets." },
        { "ss_bucket",         "Empty the bucket after rain. Store it upside down. Keep it covered when not in use." },
        { "ss_grill",          "Check the grill and cover for pooled water. Empty any standing water." },
        { "ss_inflatablepool", "Drain the pool when not in use. Store it indoors or upside down." },
        { "ss_pot",            "Empty saucers after watering. Improve drainage. Store unused pots upside down." },
        { "ss_tire",           "Empty any standing water immediately. Flip upside-down or store indoors. Drill drain holes if used as a planter." },
        { "ss_trashcan",       "Keep the lid closed. Empty any pooled water." },
        { "ss_treehole",       "Fill small tree holes with sand or expandable foam if appropriate. Monitor after rain." },
        { "ss_waterhyacinth",  "Thin or remove excess plants. Keep water moving when possible." },
        { "ss_wateringcan",    "Empty the watering can after use. Store it upside down." },
        { "ss_waterlettuce",   "Remove excess plant growth. Keep water circulating when possible." },
        { "ss_wheelbarrow",    "Empty any standing water. Store the wheelbarrow upside down or under cover." }
    };

    void Awake()
    {
        Instance = this;
        sheetPanel.SetActive(false);
    }

    void Start()
    {
        Canvas.ForceUpdateCanvases();
        sheetHeight = sheetRect.rect.height;
        sheetRect.anchoredPosition = new Vector2(0, -sheetHeight);
    }

    public void OpenForLabel(string label, float confidence, int countThisScan)
    {
        string displayName = displayNames.ContainsKey(label) ? displayNames[label] : label;
        string description = descriptions.ContainsKey(label) ? descriptions[label] : "No risk description available.";
        string mitigationText = mitigations.ContainsKey(label) ? mitigations[label] : "";

        string risk = ReportUIBuilder.GetRiskLevelPublic(label);

        titleText.text = displayName;
        subtitleText.text = countThisScan + " found this scan · " + risk + " risk";

        Color bg, txt;
        if (risk == "High") { bg = highBg; txt = highTxt; }
        else if (risk == "Moderate") { bg = medBg; txt = medTxt; }
        else { bg = lowBg; txt = lowTxt; }

        riskPill.color = bg;
        riskPillText.text = risk + " risk";
        riskPillText.color = txt;

        whyText.text = description;

        foreach (Transform child in stepsContainer)
            Destroy(child.gameObject);

        if (!string.IsNullOrWhiteSpace(mitigationText))
        {
            string[] steps = mitigationText.Split(new string[] { ". " }, System.StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < steps.Length; i++)
            {
                GameObject stepGO = Instantiate(stepPrefab, stepsContainer);
                TextMeshProUGUI[] tmps = stepGO.GetComponentsInChildren<TextMeshProUGUI>();
                if (tmps.Length >= 2)
                {
                    tmps[0].text = "•";
                    tmps[1].text = steps[i].TrimEnd('.');
                }
            }
        }

        int pct = Mathf.RoundToInt(confidence * 100f);
        confidenceText.text = pct + "%";
        confidenceBarFill.fillAmount = confidence;

        sheetPanel.SetActive(true);
        StopAllCoroutines();
        StartCoroutine(AnimateSheet(true));
        StartCoroutine(UpdateScanStatusRow());
        isOpen = true;
    }

    public void Close()
    {
        if (!isOpen) return;
        StopAllCoroutines();
        StartCoroutine(AnimateSheet(false));
        isOpen = false;
    }

    public void OpenFullReportForCurrentLabel()
    {
        Close();
        if (ScanManager.Instance != null)
            ScanManager.Instance.OpenGeneratedReport();
    }

    IEnumerator UpdateScanStatusRow()
    {
        while (isOpen)
        {
            bool scanning = ScanManager.Instance != null && ScanManager.Instance.IsScanning();
            if (scanStatusRow != null)
                scanStatusRow.SetActive(scanning);

            if (scanning && ScanManager.Instance != null)
            {
                if (scanFoundCountText != null)
                    scanFoundCountText.text = ScanManager.Instance.GetTotalDetectedCountPublic() + " found";

                if (scanTimerText != null)
                {
                    float elapsed = ScanManager.Instance.GetElapsedSeconds();
                    int m = (int)(elapsed / 60f);
                    int s = (int)(elapsed % 60f);
                    scanTimerText.text = (m > 0 ? m + "m " : "") + s.ToString("D2") + "s";
                }
            }

            yield return new WaitForSeconds(0.5f);
        }
    }

    IEnumerator AnimateSheet(bool open)
    {
        Canvas.ForceUpdateCanvases();
        sheetHeight = sheetRect.rect.height;

        float startY = sheetRect.anchoredPosition.y;
        float endY = open ? 0f : -sheetHeight;
        float elapsed = 0f;

        while (elapsed < animDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / animDuration);
            sheetRect.anchoredPosition = new Vector2(0, Mathf.Lerp(startY, endY, t));
            yield return null;
        }

        sheetRect.anchoredPosition = new Vector2(0, endY);

        if (!open)
            sheetPanel.SetActive(false);
    }
}