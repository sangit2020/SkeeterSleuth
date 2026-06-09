// ═══════════════════════════════════════════════════════════════════════════════
// ReportUIBuilder.cs
// Attach to an empty GameObject in the ARScreen scene.
//
// Builds two UI panels under the existing Canvas at runtime:
//   • Full Report List Screen
//   • Item Detail Screen
//
// ── HOW TO OPEN FROM ScanCompletePanel ──────────────────────────────────────
//
//   Option A — always show the most recent report (typical "Full Report" button):
//       ReportUIBuilder.Instance.ShowLatestReport();
//
//   Option B — show a specific report when you already have the ID
//   (e.g. right after DatabaseManager.Instance.SaveReport(...) returns):
//       int reportId = DatabaseManager.Instance.SaveReport(duration, count);
//       ReportUIBuilder.Instance.ShowReport(reportId);
//
// ── DATA POLICY ─────────────────────────────────────────────────────────────
//   All content (names, descriptions, mitigation, dates, counts) comes from the
//   live SQLite database via DatabaseManager.
//
//   Two fields are placeholder-only until the DB schema is extended:
//     • Risk level  → inferred from label string via GetRiskLevel(label)
//     • Confidence  → fixed at 85% via PLACEHOLDER_CONFIDENCE
//   When you add those columns, replace those two helpers and nothing else.
//
//   The optional Inspector bool `useMockDataForUIDebugging` (default false)
//   enables hardcoded Tire/Gutter data so the UI can be exercised without
//   any real scan in the database.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ReportUIBuilder : MonoBehaviour
{
    // ── Singleton ────────────────────────────────────────────────────────────
    public static ReportUIBuilder Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Canvas — auto-found if left empty")]
    public Canvas targetCanvas;

    [Header("Debug / Authoring Only")]
    [Tooltip("Enable ONLY for UI layout testing when no real scans exist in the database. " +
             "Leave false in production — the UI will use live database data.")]
    public bool useMockDataForUIDebugging = false;

    // ── Placeholder constants (replace when DB schema gains these columns) ────
    // TODO: Replace with a real confidence value from DetectionWithDetails once
    //       a 'confidence' column is added to the detections table.
    const float PLACEHOLDER_CONFIDENCE = 0.85f;

    // ── Colours ───────────────────────────────────────────────────────────────
    static readonly Color C_BG          = HexColor("#093A1B");
    static readonly Color C_CARD        = HexColor("#174E2A");
    static readonly Color C_CARD_LIGHT  = HexColor("#1E6335");
    static readonly Color C_MINT        = HexColor("#C6F5D4");
    static readonly Color C_TEAL_BTN    = HexColor("#2DD4BF");
    static readonly Color C_ORANGE      = HexColor("#F59E0B");
    static readonly Color C_RED         = HexColor("#EF4444");
    static readonly Color C_GREEN_BADGE = HexColor("#22C55E");
    static readonly Color C_WHITE       = Color.white;
    static readonly Color C_SUBTEXT     = HexColor("#A7C4AE");
    static readonly Color C_PROG_BG     = HexColor("#2D6A42");
    static readonly Color C_PROG_RISK   = HexColor("#F59E0B");
    static readonly Color C_PROG_CONF   = HexColor("#2DD4BF");

    // ── Runtime state ─────────────────────────────────────────────────────────
    GameObject _reportListPanel;
    GameObject _itemDetailPanel;

    // Report list live refs
    TextMeshProUGUI _listReportSubtitle;
    Transform       _listItemsContainer;
    TextMeshProUGUI _listEmptyMessage;       // shown when report has zero detections
    TextMeshProUGUI _listScanDate, _listScanDuration, _listScanItems;
    RectTransform   _listRiskFill;
    TextMeshProUGUI _listRiskBadgeText;
    Image           _listRiskBadgeBg;
    GameObject      _listRiskSummaryCard;   // hidden in empty state
    GameObject      _listScanInfoCard;      // hidden in empty state

    // Item detail live refs
    TextMeshProUGUI _detailTitle, _detailSubtitle, _detailCounter;
    TextMeshProUGUI _detailWhyRisk, _detailWhatToDo;
    RectTransform   _detailConfFill;
    Image           _detailRiskBadgeBg;
    TextMeshProUGUI _detailRiskBadgeText;

    // Active data
    List<DetectionWithDetails> _currentDetections = new();
    ScanReport                 _currentReport;
    int                        _detailIndex;

    // ═════════════════════════════════════════════════════════════════════════
    // UNITY LIFECYCLE
    // ═════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        if (targetCanvas == null)
            targetCanvas = FindFirstObjectByType<Canvas>();

        if (targetCanvas == null)
        {
            Debug.LogError("[ReportUIBuilder] No Canvas found in scene. Assign one in the Inspector.");
            return;
        }

        BuildReportListPanel();
        BuildItemDetailPanel();

        _reportListPanel.SetActive(false);
        _itemDetailPanel.SetActive(false);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // PUBLIC API
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Shows the most recent report from the database.
    /// Call this from ScanCompletePanel's "Full Report" button:
    ///     ReportUIBuilder.Instance.ShowLatestReport();
    /// </summary>
    public void ShowLatestReport()
    {
        // ── Debug override ──────────────────────────────────────────────────
        if (useMockDataForUIDebugging)
        {
            LoadMockData(out var mockReport, out var mockDetections);
            DisplayReport(mockReport, mockDetections);
            return;
        }

        // ── Live database path ──────────────────────────────────────────────
        List<ScanReport> allReports = null;
        try
        {
            allReports = DatabaseManager.Instance.GetAllReports();
        }
        catch (Exception e)
        {
            Debug.LogError($"[ReportUIBuilder] GetAllReports() threw an exception: {e.Message}");
            ShowEmptyState("Could not load reports.", "Check the database connection.");
            return;
        }

        if (allReports == null || allReports.Count == 0)
        {
            // No scans have been saved yet — show a clean empty state.
            ShowEmptyState("No scan reports available yet.", "Complete a scan to generate a report.");
            return;
        }

        // GetAllReports returns in insertion order; the last entry is the most recent.
        ShowReport(allReports[^1].id);
    }

    /// <summary>
    /// Shows the report for a specific report ID.
    /// Call this when you already have the ID, e.g. right after SaveReport():
    ///     int id = DatabaseManager.Instance.SaveReport(duration, count);
    ///     ReportUIBuilder.Instance.ShowReport(id);
    /// </summary>
    public void ShowReport(int reportId)
    {
        // ── Debug override ──────────────────────────────────────────────────
        if (useMockDataForUIDebugging)
        {
            LoadMockData(out var mockReport, out var mockDetections);
            DisplayReport(mockReport, mockDetections);
            return;
        }

        // ── Live database path ──────────────────────────────────────────────
        ScanReport report = null;
        List<DetectionWithDetails> detections = null;

        try
        {
            report     = DatabaseManager.Instance.GetReportById(reportId);
            detections = DatabaseManager.Instance.GetDetectionsForReport(reportId);
        }
        catch (Exception e)
        {
            Debug.LogError($"[ReportUIBuilder] Database read failed for report {reportId}: {e.Message}");
            ShowEmptyState("Could not load report.", "A database error occurred.");
            return;
        }

        if (report == null)
        {
            Debug.LogWarning($"[ReportUIBuilder] No report found with ID {reportId}.");
            ShowEmptyState("Report not found.", $"No report exists with ID {reportId}.");
            return;
        }

        // detections may legitimately be empty (report with zero detected items).
        detections ??= new List<DetectionWithDetails>();

        DisplayReport(report, detections);
    }

    /// <summary>
    /// Opens the Item Detail screen for the detection at the given index.
    /// Called automatically when a detected-item card is tapped.
    /// </summary>
    public void ShowItemDetail(int index)
    {
        if (_currentDetections == null || _currentDetections.Count == 0)
        {
            Debug.LogWarning("[ReportUIBuilder] ShowItemDetail called but no detections are loaded.");
            return;
        }

        _detailIndex = Mathf.Clamp(index, 0, _currentDetections.Count - 1);
        PopulateItemDetailPanel(_detailIndex);
        _reportListPanel.SetActive(false);
        _itemDetailPanel.SetActive(true);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // INTERNAL DISPLAY LOGIC
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Stores data and drives the report list panel.</summary>
    void DisplayReport(ScanReport report, List<DetectionWithDetails> detections)
    {
        _currentReport     = report;
        _currentDetections = detections;

        PopulateReportListPanel();
        _itemDetailPanel.SetActive(false);
        _reportListPanel.SetActive(true);
    }

    /// <summary>
    /// Shows the full report panel with an empty-state message instead of data.
    /// Used when no reports exist or a DB error occurs.
    /// </summary>
    void ShowEmptyState(string primary, string secondary = "")
    {
        _currentReport     = null;
        _currentDetections = new List<DetectionWithDetails>();

        // Header stays; hide data-dependent sections.
        _listRiskSummaryCard.SetActive(false);
        _listScanInfoCard.SetActive(false);

        // Clear item cards
        foreach (Transform child in _listItemsContainer)
            Destroy(child.gameObject);

        // Subtitle becomes the primary empty message
        _listReportSubtitle.text = "";

        // Show the dedicated empty-state label
        _listEmptyMessage.text = string.IsNullOrEmpty(secondary)
            ? primary
            : $"{primary}\n<size=12><color=#A7C4AE>{secondary}</color></size>";
        _listEmptyMessage.gameObject.SetActive(true);

        _itemDetailPanel.SetActive(false);
        _reportListPanel.SetActive(true);
    }

    // ─── Report List Population ───────────────────────────────────────────────

    void PopulateReportListPanel()
    {
        // Hide the empty-state message; show data cards.
        _listEmptyMessage.gameObject.SetActive(false);
        _listRiskSummaryCard.SetActive(true);
        _listScanInfoCard.SetActive(true);

        // ── Subtitle (date / duration / count) ── all from DB ───────────────
        var dt    = ParseDate(_currentReport.scanned_at);
        int mins  = _currentReport.duration_seconds / 60;
        int secs  = _currentReport.duration_seconds % 60;
        string dur = mins > 0 ? $"{mins}m {secs}s" : $"{secs} sec";

        _listReportSubtitle.text =
            $"{dt:MMMM d} • {dur} • {_currentReport.total_objects_detected} items found";

        // ── Scan Info card ── all from DB ────────────────────────────────────
        _listScanDate.text     = dt.ToString("MMMM d, yyyy");
        _listScanDuration.text = dur;
        _listScanItems.text    = $"{_currentReport.total_objects_detected} total";

        // ── Risk Summary ── label inferred, bar proportional to detection count
        // TODO: Replace GetRiskLevel(label) once a risk_level column exists in the DB.
        string overallRisk   = ComputeOverallRisk(_currentDetections);
        Color  riskBadgeColor = RiskColor(overallRisk);
        float  riskRatio      = overallRisk switch { "High" => 0.85f, "Moderate" => 0.55f, _ => 0.25f };

        _listRiskBadgeText.text = overallRisk;
        _listRiskBadgeBg.color  = riskBadgeColor;
        _listRiskFill.sizeDelta = new Vector2(riskRatio * 260f, 0);

        // ── Detected Item Cards ── one per DetectionWithDetails from DB ──────
        foreach (Transform child in _listItemsContainer)
            Destroy(child.gameObject);

        if (_currentDetections.Count == 0)
        {
            // Report exists but has zero detections — inline note inside the section.
            var noItems = new GameObject("NoItemsLabel",
                typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            noItems.transform.SetParent(_listItemsContainer, false);
            noItems.GetComponent<LayoutElement>().preferredHeight = 40;
            var t = noItems.GetComponent<TextMeshProUGUI>();
            t.text      = "No detected items for this report.";
            t.fontSize  = 13;
            t.color     = C_SUBTEXT;
            t.alignment = TextAlignmentOptions.Center;
            t.margin    = new Vector4(20, 0, 20, 0);
        }
        else
        {
            for (int i = 0; i < _currentDetections.Count; i++)
                CreateDetectedItemCard(_listItemsContainer, _currentDetections[i], i);
        }
    }

    // ─── Item Detail Population ───────────────────────────────────────────────

    void PopulateItemDetailPanel(int index)
    {
        var d = _currentDetections[index];

        // ── From DB ──────────────────────────────────────────────────────────
        _detailTitle.text    = string.IsNullOrWhiteSpace(d.display_name) ? d.label : d.display_name;
        _detailSubtitle.text = InstanceCountText(d.label);
        _detailCounter.text  = $"Detection {index + 1} of {_currentDetections.Count}";

        _detailWhyRisk.text = string.IsNullOrWhiteSpace(d.object_description)
            ? "No risk description available for this item."
            : d.object_description;

        // Mitigation: split on newline or semicolon and format as numbered list.
        // Content comes directly from mitigation_description in the DB.
        if (!string.IsNullOrWhiteSpace(d.mitigation_description))
        {
            var lines = d.mitigation_description.Split(
                new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new System.Text.StringBuilder();
            int n = 1;
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0)
                    sb.AppendLine($"{n++}. {trimmed}");
            }
            _detailWhatToDo.text = sb.ToString().TrimEnd();
        }
        else
        {
            _detailWhatToDo.text = "No mitigation information available for this item.";
        }

        // ── Placeholder — replace when DB gains these columns ────────────────
        // TODO: Risk level — swap GetRiskLevel(d.label) for d.risk_level once available.
        string risk = GetRiskLevel(d.label);
        _detailRiskBadgeText.text = $"{risk} risk";
        _detailRiskBadgeBg.color  = RiskColor(risk);

        // TODO: Confidence — swap PLACEHOLDER_CONFIDENCE for d.confidence once available.
        _detailConfFill.sizeDelta = new Vector2(PLACEHOLDER_CONFIDENCE * 200f, 0);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // PANEL BUILDERS
    // ═════════════════════════════════════════════════════════════════════════

    void BuildReportListPanel()
    {
        var ct = targetCanvas.transform;

        _reportListPanel = CreateFullScreenPanel("FullReportPanel_Built", ct, C_BG);
        var root = _reportListPanel.GetComponent<RectTransform>();

        var scroll  = CreateScrollView("ReportScroll", root);
        var content = scroll.Find("Viewport/Content").GetComponent<RectTransform>();

        // ── Header ────────────────────────────────────────────────────────────
        var hBlock = MakeLayoutBlock("Header", content, 110, C_BG);
        var hRect  = hBlock.GetComponent<RectTransform>();

        var backBtn = MakeButton("BackBtn", hRect, "← Back", C_SUBTEXT, 14,
                                 new Vector2(0,1), new Vector2(0,1),
                                 new Vector2(0,-48), new Vector2(80,28), Color.clear);
        backBtn.GetComponent<Button>().onClick.AddListener(() =>
        {
            _reportListPanel.SetActive(false);
            // ── HOOK: re-show ScanCompletePanel here if needed ──────────────
            // Example: ScanCompletePanel.Instance.Show();
        });

        MakeText("TitleText", hRect, "Full report", 28, C_WHITE, FontStyles.Bold,
                 new Vector2(0,1), new Vector2(1,1), new Vector2(24,-80), new Vector2(-24,-50));

        _listReportSubtitle = MakeText("SubtitleText", hRect, "", 13, C_SUBTEXT, FontStyles.Normal,
                                        new Vector2(0,1), new Vector2(1,1),
                                        new Vector2(24,-112), new Vector2(-24,-90));

        // ── Empty state label (hidden until needed) ───────────────────────────
        var emptyGo = new GameObject("EmptyStateLabel",
            typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        emptyGo.transform.SetParent(content, false);
        emptyGo.GetComponent<LayoutElement>().preferredHeight = 80;
        _listEmptyMessage = emptyGo.GetComponent<TextMeshProUGUI>();
        _listEmptyMessage.text      = "";
        _listEmptyMessage.fontSize  = 15;
        _listEmptyMessage.color     = C_SUBTEXT;
        _listEmptyMessage.alignment = TextAlignmentOptions.Center;
        _listEmptyMessage.margin    = new Vector4(24, 16, 24, 0);
        _listEmptyMessage.enableWordWrapping = true;
        emptyGo.SetActive(false);

        // ── Risk Summary Card ─────────────────────────────────────────────────
        MakeSectionLabel("RISK SUMMARY", content);
        _listRiskSummaryCard = MakeCard("RiskSummaryCard", content, 90);
        var riskRect = _listRiskSummaryCard.GetComponent<RectTransform>();

        MakeText("OverallLabel", riskRect, "Overall risk level", 13, C_SUBTEXT, FontStyles.Normal,
                 new Vector2(0,1), new Vector2(0,1), new Vector2(16,-16), new Vector2(160,-2));

        _listRiskBadgeBg = MakeBadge("RiskBadge", riskRect, "Moderate", C_ORANGE,
                                      new Vector2(1,1), new Vector2(-16,-12));
        _listRiskBadgeText = _listRiskBadgeBg.GetComponentInChildren<TextMeshProUGUI>();

        // Progress bar
        var barBg = new GameObject("ProgBg", typeof(RectTransform), typeof(Image));
        barBg.transform.SetParent(riskRect, false);
        var barBgRect = barBg.GetComponent<RectTransform>();
        SetAnchors(barBgRect, new Vector2(0,1), new Vector2(1,1),
                   new Vector2(16,-52), new Vector2(-16,-38));
        barBg.GetComponent<Image>().color = C_PROG_BG;
        SetRounded(barBg.GetComponent<Image>(), 6);

        var barFill = new GameObject("ProgFill", typeof(RectTransform), typeof(Image));
        barFill.transform.SetParent(barBgRect, false);
        _listRiskFill = barFill.GetComponent<RectTransform>();
        SetAnchors(_listRiskFill, new Vector2(0,0), new Vector2(0,1), Vector2.zero, Vector2.zero);
        _listRiskFill.sizeDelta = new Vector2(140f, 0);
        barFill.GetComponent<Image>().color = C_PROG_RISK;
        SetRounded(barFill.GetComponent<Image>(), 6);

        // ── Detected Items Section ────────────────────────────────────────────
        MakeSectionLabel("DETECTED ITEMS", content);

        var itemsHolder = new GameObject("DetectedItemsContainer",
            typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        itemsHolder.transform.SetParent(content, false);

        var ihRect = itemsHolder.GetComponent<RectTransform>();
        ihRect.anchorMin = new Vector2(0,1);
        ihRect.anchorMax = new Vector2(1,1);
        ihRect.pivot     = new Vector2(0.5f,1f);
        ihRect.sizeDelta = Vector2.zero;

        var vlg = itemsHolder.GetComponent<VerticalLayoutGroup>();
        vlg.spacing              = 10;
        vlg.childControlWidth    = true;
        vlg.childControlHeight   = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.padding = new RectOffset(20, 20, 0, 0);

        itemsHolder.GetComponent<ContentSizeFitter>().verticalFit =
            ContentSizeFitter.FitMode.PreferredSize;

        _listItemsContainer = itemsHolder.transform;

        // ── Scan Info Card ────────────────────────────────────────────────────
        MakeSectionLabel("SCAN INFO", content);
        _listScanInfoCard = MakeCard("ScanInfoCard", content, 110);
        var infoRect = _listScanInfoCard.GetComponent<RectTransform>();

        BuildInfoRow(infoRect, "Date",         ref _listScanDate,     -16);
        BuildInfoRow(infoRect, "Duration",     ref _listScanDuration, -48);
        BuildInfoRow(infoRect, "Items found",  ref _listScanItems,    -80);

        // Bottom spacer
        var spacer = new GameObject("BottomSpacer", typeof(RectTransform), typeof(LayoutElement));
        spacer.transform.SetParent(content, false);
        spacer.GetComponent<LayoutElement>().preferredHeight = 40;
    }

    void BuildItemDetailPanel()
    {
        var ct = targetCanvas.transform;
        _itemDetailPanel = CreateFullScreenPanel("ItemDetailPanel_Built", ct, C_BG);
        var root = _itemDetailPanel.GetComponent<RectTransform>();

        var scroll  = CreateScrollView("DetailScroll", root);
        var content = scroll.Find("Viewport/Content").GetComponent<RectTransform>();

        // ── Header ────────────────────────────────────────────────────────────
        var hBlock = MakeLayoutBlock("Header", content, 110, C_BG);
        var hRect  = hBlock.GetComponent<RectTransform>();

        var backBtn = MakeButton("BackBtn", hRect, "← Full report", C_SUBTEXT, 13,
                                 new Vector2(0,1), new Vector2(0,1),
                                 new Vector2(0,-48), new Vector2(120,28), Color.clear);
        backBtn.GetComponent<Button>().onClick.AddListener(() =>
        {
            _itemDetailPanel.SetActive(false);
            _reportListPanel.SetActive(true);
        });

        _detailTitle = MakeText("DetailTitle", hRect, "", 28, C_WHITE, FontStyles.Bold,
                                 new Vector2(0,1), new Vector2(1,1),
                                 new Vector2(24,-80), new Vector2(-24,-50));

        _detailSubtitle = MakeText("DetailSubtitle", hRect, "", 13, C_SUBTEXT, FontStyles.Normal,
                                    new Vector2(0,1), new Vector2(1,1),
                                    new Vector2(24,-112), new Vector2(-24,-90));

        // ── Image Placeholder Card ─────────────────────────────────────────────
        var imgCard = MakeCard("ImageCard", content, 160);
        var imgRect = imgCard.GetComponent<RectTransform>();

        _detailRiskBadgeBg = MakeBadge("DetailRiskBadge", imgRect, "High risk", C_RED,
                                        new Vector2(0,1), new Vector2(16,-12));
        _detailRiskBadgeText = _detailRiskBadgeBg.GetComponentInChildren<TextMeshProUGUI>();

        _detailCounter = MakeText("CounterText", imgRect, "", 12, C_SUBTEXT, FontStyles.Normal,
                                   new Vector2(1,0), new Vector2(1,0),
                                   new Vector2(-130,10), new Vector2(-16,28));

        MakeText("ImgPlaceholder", imgRect, "🔍", 48, C_WHITE, FontStyles.Normal,
                 new Vector2(0.5f,0.5f), new Vector2(0.5f,0.5f),
                 new Vector2(-24,12), new Vector2(24,56));

        // ── Why It's a Risk ───────────────────────────────────────────────────
        MakeSectionLabel("WHY IT'S A RISK", content);
        var whyCard = MakeAutoSizeCard("WhyRiskCard", content);
        var whyRect = whyCard.GetComponent<RectTransform>();
        _detailWhyRisk = MakeText("WhyText", whyRect, "", 13, C_MINT, FontStyles.Normal,
                                   new Vector2(0,1), new Vector2(1,0),
                                   new Vector2(16,-12), new Vector2(-16,12));
        _detailWhyRisk.enableWordWrapping = true;

        // ── What to Do ───────────────────────────────────────────────────────
        MakeSectionLabel("WHAT TO DO", content);
        var todoCard = MakeAutoSizeCard("WhatToDoCard", content);
        var todoRect = todoCard.GetComponent<RectTransform>();
        _detailWhatToDo = MakeText("TodoText", todoRect, "", 13, C_MINT, FontStyles.Normal,
                                    new Vector2(0,1), new Vector2(1,0),
                                    new Vector2(16,-12), new Vector2(-16,12));
        _detailWhatToDo.enableWordWrapping = true;

        // ── Detection Confidence ──────────────────────────────────────────────
        // TODO: Replace PLACEHOLDER_CONFIDENCE (85%) with d.confidence from DB.
        MakeSectionLabel("DETECTION CONFIDENCE", content);
        var confCard = MakeCard("ConfidenceCard", content, 60);
        var confRect = confCard.GetComponent<RectTransform>();

        MakeText("ModelLabel", confRect, "Model", 13, C_SUBTEXT, FontStyles.Normal,
                 new Vector2(0,0.5f), new Vector2(0,0.5f),
                 new Vector2(16,-10), new Vector2(70,10));

        var confBg = new GameObject("ConfBg", typeof(RectTransform), typeof(Image));
        confBg.transform.SetParent(confRect, false);
        var confBgRect = confBg.GetComponent<RectTransform>();
        SetAnchors(confBgRect, new Vector2(0,0.5f), new Vector2(1,0.5f),
                   new Vector2(80,-7), new Vector2(-80,7));
        confBg.GetComponent<Image>().color = C_PROG_BG;
        SetRounded(confBg.GetComponent<Image>(), 6);

        var confFill = new GameObject("ConfFill", typeof(RectTransform), typeof(Image));
        confFill.transform.SetParent(confBgRect, false);
        _detailConfFill = confFill.GetComponent<RectTransform>();
        SetAnchors(_detailConfFill, new Vector2(0,0), new Vector2(0,1), Vector2.zero, Vector2.zero);
        _detailConfFill.sizeDelta = new Vector2(PLACEHOLDER_CONFIDENCE * 200f, 0);
        confFill.GetComponent<Image>().color = C_PROG_CONF;
        SetRounded(confFill.GetComponent<Image>(), 6);

        MakeText("PercLabel", confRect, $"{(int)(PLACEHOLDER_CONFIDENCE * 100)}%",
                 14, C_WHITE, FontStyles.Bold,
                 new Vector2(1,0.5f), new Vector2(1,0.5f),
                 new Vector2(-56,-10), new Vector2(-16,10));

        // ── Prev / Next Navigation ────────────────────────────────────────────
        var navRow = new GameObject("NavRow",
            typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        navRow.transform.SetParent(content, false);
        navRow.GetComponent<LayoutElement>().preferredHeight = 56;

        var hlg = navRow.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing              = 12;
        hlg.padding              = new RectOffset(20,20,8,8);
        hlg.childControlWidth    = true;
        hlg.childControlHeight   = false;
        hlg.childForceExpandWidth  = true;
        hlg.childForceExpandHeight = false;

        var navRect = navRow.GetComponent<RectTransform>();

        var prevBtn = MakeButton("PrevBtn", navRect, "← Prev Item",
                                  C_WHITE, 14,
                                  Vector2.zero, Vector2.one,
                                  Vector2.zero, new Vector2(0,40), C_CARD_LIGHT);
        prevBtn.GetComponent<Button>().onClick.AddListener(() =>
        {
            if (_detailIndex > 0) ShowItemDetail(_detailIndex - 1);
        });
        SetRounded(prevBtn.GetComponent<Image>(), 12);

        var nextBtn = MakeButton("NextBtn", navRect, "Next Item →",
                                  C_BG, 14,
                                  Vector2.zero, Vector2.one,
                                  Vector2.zero, new Vector2(0,40), C_TEAL_BTN);
        nextBtn.GetComponent<Button>().onClick.AddListener(() =>
        {
            if (_detailIndex < _currentDetections.Count - 1) ShowItemDetail(_detailIndex + 1);
        });
        SetRounded(nextBtn.GetComponent<Image>(), 12);

        // Bottom spacer
        var spacer = new GameObject("BottomSpacer", typeof(RectTransform), typeof(LayoutElement));
        spacer.transform.SetParent(content, false);
        spacer.GetComponent<LayoutElement>().preferredHeight = 40;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DETECTED ITEM CARD
    // ═════════════════════════════════════════════════════════════════════════

    void CreateDetectedItemCard(Transform parent, DetectionWithDetails d, int index)
    {
        var card = new GameObject($"ItemCard_{index}",
            typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        card.transform.SetParent(parent, false);

        card.GetComponent<LayoutElement>().preferredHeight = 64;
        var img = card.GetComponent<Image>();
        img.color = C_CARD;
        SetRounded(img, 14);

        var rect = card.GetComponent<RectTransform>();

        // Icon circle
        var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        icon.transform.SetParent(rect, false);
        var iRect = icon.GetComponent<RectTransform>();
        iRect.anchorMin        = new Vector2(0,0.5f);
        iRect.anchorMax        = new Vector2(0,0.5f);
        iRect.pivot            = new Vector2(0,0.5f);
        iRect.anchoredPosition = new Vector2(14,0);
        iRect.sizeDelta        = new Vector2(40,40);
        icon.GetComponent<Image>().color = C_CARD_LIGHT;
        SetRounded(icon.GetComponent<Image>(), 20);

        var emojiGo = new GameObject("Emoji", typeof(RectTransform), typeof(TextMeshProUGUI));
        emojiGo.transform.SetParent(icon.transform, false);
        var eRect = emojiGo.GetComponent<RectTransform>();
        eRect.anchorMin = Vector2.zero; eRect.anchorMax = Vector2.one; eRect.sizeDelta = Vector2.zero;
        var eTM = emojiGo.GetComponent<TextMeshProUGUI>();
        eTM.text      = "🦟";
        eTM.fontSize  = 18;
        eTM.alignment = TextAlignmentOptions.Center;

        // Display name — from DB
        var nameGo = new GameObject("ItemName", typeof(RectTransform), typeof(TextMeshProUGUI));
        nameGo.transform.SetParent(rect, false);
        var nRect = nameGo.GetComponent<RectTransform>();
        nRect.anchorMin        = new Vector2(0,1);
        nRect.anchorMax        = new Vector2(1,1);
        nRect.pivot            = new Vector2(0,1);
        nRect.anchoredPosition = new Vector2(64,-10);
        nRect.sizeDelta        = new Vector2(-130,22);
        var nTM = nameGo.GetComponent<TextMeshProUGUI>();
        nTM.text      = string.IsNullOrWhiteSpace(d.display_name) ? d.label : d.display_name;
        nTM.fontSize  = 14;
        nTM.color     = C_WHITE;
        nTM.fontStyle = FontStyles.Bold;

        // Sub-label: instance count (from DB) + risk placeholder
        var subGo = new GameObject("ItemSub", typeof(RectTransform), typeof(TextMeshProUGUI));
        subGo.transform.SetParent(rect, false);
        var sRect = subGo.GetComponent<RectTransform>();
        sRect.anchorMin        = new Vector2(0,0);
        sRect.anchorMax        = new Vector2(1,0);
        sRect.pivot            = new Vector2(0,0);
        sRect.anchoredPosition = new Vector2(64,12);
        sRect.sizeDelta        = new Vector2(-130,18);
        var sTM = subGo.GetComponent<TextMeshProUGUI>();
        // TODO: Replace GetRiskLevel(d.label) with d.risk_level when column exists.
        sTM.text    = $"{CountInstances(d.label)} detected · {GetRiskLevel(d.label)} risk";
        sTM.fontSize = 11;
        sTM.color    = C_SUBTEXT;

        // Risk badge — placeholder label inferred from label string
        MakeBadge("RiskBadge", rect,
                  GetRiskLevel(d.label), RiskColor(GetRiskLevel(d.label)),
                  new Vector2(1,0.5f), new Vector2(-44,0));

        // Arrow
        var arrowGo = new GameObject("Arrow", typeof(RectTransform), typeof(TextMeshProUGUI));
        arrowGo.transform.SetParent(rect, false);
        var aRect = arrowGo.GetComponent<RectTransform>();
        aRect.anchorMin        = new Vector2(1,0.5f);
        aRect.anchorMax        = new Vector2(1,0.5f);
        aRect.pivot            = new Vector2(1,0.5f);
        aRect.anchoredPosition = new Vector2(-12,0);
        aRect.sizeDelta        = new Vector2(20,20);
        var aTM = arrowGo.GetComponent<TextMeshProUGUI>();
        aTM.text      = "›";
        aTM.fontSize  = 22;
        aTM.color     = C_SUBTEXT;
        aTM.alignment = TextAlignmentOptions.Center;

        card.GetComponent<Button>().onClick.AddListener(() => ShowItemDetail(index));
        card.GetComponent<Button>().targetGraphic = img;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // UI PRIMITIVE HELPERS
    // ═════════════════════════════════════════════════════════════════════════

    static GameObject CreateFullScreenPanel(string name, Transform parent, Color bg)
    {
        var go   = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        go.GetComponent<Image>().color = bg;
        return go;
    }

    /// <summary>A fixed-height block inside a scroll content, for use as a header area.</summary>
    static GameObject MakeLayoutBlock(string name, Transform parent, int height, Color bg)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = bg;
        go.GetComponent<LayoutElement>().preferredHeight = height;
        return go;
    }

    /// <summary>Dark card with fixed height.</summary>
    static GameObject MakeCard(string name, Transform parent, int fixedHeight)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = C_CARD;
        SetRounded(img, 16);
        go.GetComponent<LayoutElement>().preferredHeight = fixedHeight;
        return go;
    }

    /// <summary>Dark card that auto-sizes to its content height.</summary>
    static GameObject MakeAutoSizeCard(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image),
                                typeof(LayoutElement), typeof(ContentSizeFitter));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = C_CARD;
        SetRounded(img, 16);
        go.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        go.GetComponent<LayoutElement>().minHeight = 48;
        return go;
    }

    static void MakeSectionLabel(string text, Transform parent)
    {
        var go = new GameObject(text + "_Label",
            typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = 28;
        var tm = go.GetComponent<TextMeshProUGUI>();
        tm.text      = text;
        tm.fontSize  = 11;
        tm.color     = C_SUBTEXT;
        tm.fontStyle = FontStyles.Bold;
        tm.margin    = new Vector4(20,8,20,0);
    }

    static TextMeshProUGUI MakeText(string name, Transform parent,
                                    string text, float size, Color color, FontStyles style,
                                    Vector2 anchorMin, Vector2 anchorMax,
                                    Vector2 offsetMin, Vector2 offsetMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot     = new Vector2(0,1);
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        var tm = go.GetComponent<TextMeshProUGUI>();
        tm.text      = text;
        tm.fontSize  = size;
        tm.color     = color;
        tm.fontStyle = style;
        return tm;
    }

    static Image MakeBadge(string name, Transform parent, string label, Color bg,
                            Vector2 anchor, Vector2 anchoredPos)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin        = anchor;
        rect.anchorMax        = anchor;
        rect.pivot            = anchor;
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta        = new Vector2(90,24);
        var img = go.GetComponent<Image>();
        img.color = bg;
        SetRounded(img, 12);

        var textGo = new GameObject("BadgeText", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, false);
        var tRect = textGo.GetComponent<RectTransform>();
        tRect.anchorMin = Vector2.zero; tRect.anchorMax = Vector2.one; tRect.sizeDelta = Vector2.zero;
        var tm = textGo.GetComponent<TextMeshProUGUI>();
        tm.text      = label;
        tm.fontSize  = 12;
        tm.color     = Color.white;
        tm.fontStyle = FontStyles.Bold;
        tm.alignment = TextAlignmentOptions.Center;
        return img;
    }

    static GameObject MakeButton(string name, Transform parent,
                                  string label, Color textColor, float fontSize,
                                  Vector2 anchorMin, Vector2 anchorMax,
                                  Vector2 offsetMin, Vector2 offsetMax,
                                  Color bgColor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot     = new Vector2(0,1);
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        rect.sizeDelta = offsetMax - offsetMin;
        var img = go.GetComponent<Image>();
        img.color = bgColor;
        if (bgColor != Color.clear) SetRounded(img, 12);
        go.GetComponent<Button>().targetGraphic = img;

        var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, false);
        var tRect = textGo.GetComponent<RectTransform>();
        tRect.anchorMin = Vector2.zero; tRect.anchorMax = Vector2.one; tRect.sizeDelta = Vector2.zero;
        var tm = textGo.GetComponent<TextMeshProUGUI>();
        tm.text      = label;
        tm.fontSize  = fontSize;
        tm.color     = textColor;
        tm.alignment = TextAlignmentOptions.Center;
        return go;
    }

    static Transform CreateScrollView(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(ScrollRect));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        SetAnchors(rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var sr = go.GetComponent<ScrollRect>();
        sr.horizontal        = false;
        sr.movementType      = ScrollRect.MovementType.Elastic;
        sr.elasticity        = 0.1f;
        sr.scrollSensitivity = 30;

        var vp = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        vp.transform.SetParent(go.transform, false);
        SetAnchors(vp.GetComponent<RectTransform>(), Vector2.zero, Vector2.one,
                   Vector2.zero, Vector2.zero);
        vp.GetComponent<Image>().color = new Color(0,0,0,0.01f);
        vp.GetComponent<Mask>().showMaskGraphic = false;

        var content = new GameObject("Content",
            typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(vp.transform, false);
        var cRect = content.GetComponent<RectTransform>();
        cRect.anchorMin = new Vector2(0,1);
        cRect.anchorMax = new Vector2(1,1);
        cRect.pivot     = new Vector2(0.5f,1f);
        cRect.sizeDelta = Vector2.zero;

        var vlg = content.GetComponent<VerticalLayoutGroup>();
        vlg.spacing              = 12;
        vlg.childControlWidth    = true;
        vlg.childControlHeight   = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.padding = new RectOffset(16,16,16,16);

        content.GetComponent<ContentSizeFitter>().verticalFit =
            ContentSizeFitter.FitMode.PreferredSize;

        sr.viewport = vp.GetComponent<RectTransform>();
        sr.content  = content.GetComponent<RectTransform>();
        return go.transform;
    }

    void BuildInfoRow(Transform parent, string labelText,
                      ref TextMeshProUGUI valueRef, float yOffset)
    {
        MakeText(labelText + "_L", parent, labelText, 13, C_SUBTEXT, FontStyles.Normal,
                 new Vector2(0,1), new Vector2(0,1),
                 new Vector2(16, yOffset - 14), new Vector2(130, yOffset));

        valueRef = MakeText(labelText + "_V", parent, "—", 13, C_WHITE, FontStyles.Bold,
                            new Vector2(1,1), new Vector2(1,1),
                            new Vector2(-130, yOffset - 14), new Vector2(-16, yOffset));
        valueRef.alignment = TextAlignmentOptions.Right;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // UTILITY
    // ═════════════════════════════════════════════════════════════════════════

    static void SetAnchors(RectTransform r,
                           Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
    {
        r.anchorMin = min; r.anchorMax = max;
        r.pivot     = new Vector2(0.5f, 0.5f);
        r.offsetMin = offMin; r.offsetMax = offMax;
    }

    static void SetRounded(Image img, float radius)
    {
        // Loads Assets/Resources/UI/Rounded.png as a 9-sliced sprite for rounded corners.
        // If the sprite doesn't exist, the card stays rectangular — no errors thrown.
        var sprite = Resources.Load<Sprite>("UI/Rounded");
        if (sprite != null) { img.sprite = sprite; img.type = Image.Type.Sliced; }
    }

    static Color HexColor(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out var c);
        return c;
    }

    static DateTime ParseDate(string s)
    {
        return DateTime.TryParse(s, out var dt) ? dt : DateTime.Now;
    }

    /// <summary>How many detections in the current list share this label.</summary>
    int CountInstances(string label) =>
        _currentDetections.FindAll(d => d.label == label).Count;

    string InstanceCountText(string label)
    {
        int c = CountInstances(label);
        return c == 1 ? "1 instance detected" : $"{c} instances detected";
    }

    /// <summary>
    /// Infers a risk tier from the detection label string.
    /// TODO: Replace with a real risk_level field from the DB once that column exists.
    /// The mapping below is a temporary heuristic — update it to match your ObjectType data.
    /// </summary>
    static string GetRiskLevel(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "Low";
        label = label.ToLowerInvariant();

        // High-risk standing-water containers
        if (label.Contains("tire")    || label.Contains("barrel")  ||
            label.Contains("bucket")  || label.Contains("puddle")  ||
            label.Contains("standing water"))
            return "High";

        // Moderate-risk items
        if (label.Contains("gutter")  || label.Contains("planter") ||
            label.Contains("birdbath") || label.Contains("pot")    ||
            label.Contains("tarp")    || label.Contains("tray"))
            return "Moderate";

        return "Low";
    }

    /// <summary>Looks across all detections to derive an overall risk for the report.</summary>
    static string ComputeOverallRisk(List<DetectionWithDetails> detections)
    {
        if (detections == null || detections.Count == 0) return "Low";
        bool anyHigh = false, anyMod = false;
        foreach (var d in detections)
        {
            switch (GetRiskLevel(d.label))
            {
                case "High":     anyHigh = true; break;
                case "Moderate": anyMod  = true; break;
            }
        }
        if (anyHigh) return "High";
        if (anyMod)  return "Moderate";
        return "Low";
    }

    static Color RiskColor(string risk) => risk switch
    {
        "High"     => C_RED,
        "Moderate" => C_ORANGE,
        _          => C_GREEN_BADGE,
    };

    // ═════════════════════════════════════════════════════════════════════════
    // MOCK DATA  (useMockDataForUIDebugging = true only)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns hardcoded test data for UI layout work.
    /// Only reachable when useMockDataForUIDebugging is true in the Inspector.
    /// Never called during normal gameplay — the DB path is always used instead.
    /// </summary>
    static void LoadMockData(out ScanReport report, out List<DetectionWithDetails> detections)
    {
        report = new ScanReport
        {
            id                     = -1,
            scanned_at             = new DateTime(2026, 6, 7).ToString("o"),
            duration_seconds       = 47,
            total_objects_detected = 3,
            notes                  = "[UI debug mock — not a real scan]"
        };

        detections = new List<DetectionWithDetails>
        {
            new DetectionWithDetails
            {
                detection_id           = 1,
                report_id              = -1,
                display_name           = "Tire",
                label                  = "tire",
                object_description     = "Discarded tires collect rainwater and stay warm, " +
                                         "creating ideal conditions for mosquito larvae that " +
                                         "can hatch within days of pooling.",
                mitigation_description = "Empty all standing water immediately\n" +
                                         "Flip tires upside-down or store indoors\n" +
                                         "Drill drainage holes if used as planters",
                screenshot_path        = "",
                detected_at            = DateTime.Now.ToString("o")
            },
            new DetectionWithDetails
            {
                detection_id           = 2,
                report_id              = -1,
                display_name           = "Tire",
                label                  = "tire",
                object_description     = "Discarded tires collect rainwater and stay warm, " +
                                         "creating ideal conditions for mosquito larvae that " +
                                         "can hatch within days of pooling.",
                mitigation_description = "Empty all standing water immediately\n" +
                                         "Flip tires upside-down or store indoors\n" +
                                         "Drill drainage holes if used as planters",
                screenshot_path        = "",
                detected_at            = DateTime.Now.ToString("o")
            },
            new DetectionWithDetails
            {
                detection_id           = 3,
                report_id              = -1,
                display_name           = "Gutter",
                label                  = "gutter",
                object_description     = "Clogged gutters retain stagnant water for extended " +
                                         "periods, making them prime mosquito breeding sites " +
                                         "especially in warm weather.",
                mitigation_description = "Clear debris from gutters every season\n" +
                                         "Install gutter guards to prevent clogging\n" +
                                         "Flush gutters with water after cleaning",
                screenshot_path        = "",
                detected_at            = DateTime.Now.ToString("o")
            }
        };
    }
}
