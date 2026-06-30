using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Attach this script to the ScanCompletePanel GameObject.
// It reads real data from the database and populates every
// visual element you built by hand in the Hierarchy.

public class ScanCompleteController : MonoBehaviour
{
    [Header("Header")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI subtitleText;
    public Button closeButton;

    [Header("Risk Card")]
    public Image riskPillBg;
    public TextMeshProUGUI riskPillText;
    public RectTransform riskBarTrack;
    public RectTransform riskBarFill;
    public Image riskBarFillImage;

    [Header("Detected Items Chips")]
    // This is the Content object inside ItemChipsRow > Viewport > Content
    public Transform chipsContent;
    // This is Chip_Tire — used as a template. Script duplicates it per item.
    public GameObject chipTemplate;

    [Header("What To Do Cards")]
    // This is the Content object inside WhatToDoScrollView > Viewport > Content
    public Transform fixBlocksContent;
    // This is FixBlock_Tire — used as a template. Script duplicates it per item.
    public GameObject fixBlockTemplate;

    [Header("Buttons")]
    public Button fullReportButton;
    public Button scanAgainButton;

    [Header("Saved Confirmation")]
    public TextMeshProUGUI savedConfirmationText;

    // Risk colors matching ReportUIBuilder exactly
    static readonly Color C_HIGH_BG   = HexColor("#6B4A2E");
    static readonly Color C_HIGH_TXT  = HexColor("#FF6B57");
    static readonly Color C_MOD_BG    = HexColor("#73652C");
    static readonly Color C_MOD_TXT   = HexColor("#EE9F28");
    static readonly Color C_LOW_BG    = HexColor("#315B3F");
    static readonly Color C_LOW_TXT   = HexColor("#6BE089");

void Awake()
{
    if (chipTemplate != null)
        chipTemplate.SetActive(false);

    if (fixBlockTemplate != null)
        fixBlockTemplate.SetActive(false);
}

void Start()
{
    // Wire buttons — must be in Start so references are fully initialized
    if (closeButton != null)
        closeButton.onClick.AddListener(OnClosePressed);

    if (fullReportButton != null)
        fullReportButton.onClick.AddListener(OnFullReportPressed);

    if (scanAgainButton != null)
        scanAgainButton.onClick.AddListener(OnScanAgainPressed);
}

    // Called by ScanManager after scan is saved to DB
    public void Show(int reportId, int durationSeconds, List<DetectionWithDetails> detections)
    {
        gameObject.SetActive(true);

        // --- Subtitle: date + duration ---
        string dateLabel = DateTime.Now.ToString("MMMM d, yyyy");
        int mins = durationSeconds / 60;
        int secs = durationSeconds % 60;
        string durationLabel = mins > 0 ? mins + "m " + secs + "s" : secs + "s";

        if (subtitleText != null)
            subtitleText.text = dateLabel + " · " + durationLabel;

        // --- Risk pill + bar ---
        string overallRisk = ComputeOverallRisk(detections);
        ApplyRiskPill(overallRisk);
        ApplyRiskBar(overallRisk);

        // --- Group detections by label so each label appears once ---
        // e.g. 3 rows of ss_tire collapse into one chip "Tire ×3"
        var grouped = detections
            .GroupBy(d => d.label)
            .Select(g => new
            {
                label       = g.Key,
                displayName = g.First().display_name,
                mitigation  = g.First().mitigation_description,
                count       = g.Count()
            })
            .ToList();

        // --- Build chips ---
        BuildChips(grouped.Select(x => (x.label, x.displayName, x.count)).ToList());

        // --- Build fix blocks ---
        BuildFixBlocks(grouped.Select(x => (x.label, x.displayName, x.mitigation)).ToList());

        // --- Saved confirmation ---
        if (savedConfirmationText != null)
            savedConfirmationText.text = "✓ Saved to scan history";
    }

    // ── Risk helpers ──────────────────────────────────────────────────────────

    string ComputeOverallRisk(List<DetectionWithDetails> detections)
    {
        if (detections == null || detections.Count == 0)
            return "Low";

        bool anyHigh = false;
        bool anyMod  = false;

        foreach (var d in detections)
        {
            string r = ReportUIBuilder.GetRiskLevelPublic(d.label);
            if (r == "High")     anyHigh = true;
            else if (r == "Moderate") anyMod = true;
        }

        if (anyHigh) return "High";
        if (anyMod)  return "Moderate";
        return "Low";
    }

    void ApplyRiskPill(string risk)
    {
        Color bg, txt;
        GetRiskColors(risk, out bg, out txt);

        if (riskPillBg != null)  riskPillBg.color  = bg;
        if (riskPillText != null)
        {
            riskPillText.text  = risk;
            riskPillText.color = txt;
        }
    }

    void ApplyRiskBar(string risk)
    {
        // Fill width as a fraction of the track width
        // Low = 33%, Moderate = 62%, High = 90%
        float fraction = risk == "High" ? 0.90f : risk == "Moderate" ? 0.62f : 0.33f;

        Color _, txt;
        GetRiskColors(risk, out _, out txt);

        if (riskBarFillImage != null)
            riskBarFillImage.color = txt;

        // We need to wait one frame for the track RectTransform to have
        // its final width before we can set the fill width correctly.
        StartCoroutine(SetFillWidthNextFrame(fraction));
    }

    System.Collections.IEnumerator SetFillWidthNextFrame(float fraction)
    {
        yield return null; // wait one frame

        if (riskBarTrack == null || riskBarFill == null) yield break;

        float trackWidth = riskBarTrack.rect.width;
        riskBarFill.sizeDelta = new Vector2(trackWidth * fraction, riskBarFill.sizeDelta.y);
    }

    void GetRiskColors(string risk, out Color bg, out Color txt)
    {
        if (risk == "High")          { bg = C_HIGH_BG; txt = C_HIGH_TXT; }
        else if (risk == "Moderate") { bg = C_MOD_BG;  txt = C_MOD_TXT;  }
        else                         { bg = C_LOW_BG;  txt = C_LOW_TXT;  }
    }

    // ── Chips ─────────────────────────────────────────────────────────────────

    void BuildChips(List<(string label, string displayName, int count)> items)
    {
        // Destroy any previously built chips (keeps the template)
        foreach (Transform child in chipsContent)
        {
            if (child.gameObject == chipTemplate) continue;
            Destroy(child.gameObject);
        }

        if (items.Count == 0) return;

        foreach (var item in items)
        {
            // Duplicate the template
            GameObject chip = Instantiate(chipTemplate, chipsContent);
            chip.SetActive(true);

            // Find Label and Count text children by name
            TextMeshProUGUI labelTm = chip.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI countTm = chip.transform.Find("Count")?.GetComponent<TextMeshProUGUI>();

            if (labelTm != null) labelTm.text = item.displayName;
            if (countTm != null) countTm.text = "×" + item.count;
        }
    }

    // ── Fix Blocks ────────────────────────────────────────────────────────────

    void BuildFixBlocks(List<(string label, string displayName, string mitigation)> items)
    {
        // Destroy any previously built fix blocks (keeps the template)
        foreach (Transform child in fixBlocksContent)
        {
            if (child.gameObject == fixBlockTemplate) continue;
            Destroy(child.gameObject);
        }

        if (items.Count == 0)
        {
            // Show a fallback block
            GameObject empty = Instantiate(fixBlockTemplate, fixBlocksContent);
            empty.SetActive(true);

            TextMeshProUGUI nameT = empty.transform.Find("FixHeadRow/FixName")
                                         ?.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI bodyT = empty.transform.Find("FixText")
                                         ?.GetComponent<TextMeshProUGUI>();

            if (nameT != null) nameT.text = "No items detected";
            if (bodyT != null) bodyT.text = "Nothing found this scan — great job!";

            Image pillBg = empty.transform.Find("FixHeadRow/FixPill")?.GetComponent<Image>();
            if (pillBg != null) pillBg.color = C_LOW_BG;

            TextMeshProUGUI pillTm = empty.transform
                                          .Find("FixHeadRow/FixPill/Text")
                                          ?.GetComponent<TextMeshProUGUI>();
            if (pillTm != null) { pillTm.text = "Low"; pillTm.color = C_LOW_TXT; }

            return;
        }

        foreach (var item in items)
        {
            string risk = ReportUIBuilder.GetRiskLevelPublic(item.label);
            Color pillBgColor, pillTxtColor;
            GetRiskColors(risk, out pillBgColor, out pillTxtColor);

            GameObject block = Instantiate(fixBlockTemplate, fixBlocksContent);
            block.SetActive(true);

            // FixName
            TextMeshProUGUI fixName = block.transform.Find("FixHeadRow/FixName")
                                           ?.GetComponent<TextMeshProUGUI>();
            if (fixName != null) fixName.text = item.displayName;

            // FixPill background + text
            Image pill = block.transform.Find("FixHeadRow/FixPill")?.GetComponent<Image>();
            if (pill != null) pill.color = pillBgColor;

            TextMeshProUGUI pillText = block.transform.Find("FixHeadRow/FixPill/Text")
                                            ?.GetComponent<TextMeshProUGUI>();
            if (pillText != null)
            {
                pillText.text  = risk;
                pillText.color = pillTxtColor;
            }

            // FixText (mitigation description)
            TextMeshProUGUI fixText = block.transform.Find("FixText")
                                           ?.GetComponent<TextMeshProUGUI>();
            if (fixText != null)
            {
                fixText.text = string.IsNullOrWhiteSpace(item.mitigation)
                    ? "No fix guidance available."
                    : item.mitigation;
            }
        }
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    void OnClosePressed()
    {
        // Hide the panel. Scan is already saved. User returns to live AR screen.
        gameObject.SetActive(false);
    }

    void OnFullReportPressed()
    {
        gameObject.SetActive(false);

        if (ScanManager.Instance != null)
            ScanManager.Instance.OpenGeneratedReport();
    }

    void OnScanAgainPressed()
    {
        gameObject.SetActive(false);

        if (ScanManager.Instance != null)
            ScanManager.Instance.OnBeginScanPressed();
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    static Color HexColor(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color c);
        return c;
    }
}