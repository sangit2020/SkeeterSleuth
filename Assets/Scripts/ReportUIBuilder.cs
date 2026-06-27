// ═══════════════════════════════════════════════════════════════════════════════
// ReportUIBuilder.cs
// Attach to an empty GameObject in the ARScreen scene.
//
// Builds two UI panels under the existing Canvas at runtime:
//   • Full Report List Screen
//   • Item Detail Screen
//
// How to open from another script:
//   ReportUIBuilder.Instance.ShowLatestReport();
//
// Or, if you already have a specific report ID:
//   ReportUIBuilder.Instance.ShowReport(reportId);
// ═══════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ReportUIBuilder : MonoBehaviour
{
    public static ReportUIBuilder Instance { get; private set; }

    [Header("Canvas — auto-found if left empty")]
    public Canvas targetCanvas;

    [Header("Debug / Authoring Only")]
    [Tooltip("Enable ONLY for UI layout testing when no real scans exist in the database.")]
    public bool useMockDataForUIDebugging = false;

    const float PLACEHOLDER_CONFIDENCE = 0.85f;

    // ─── Color palette ────────────────────────────────────────────────────────
    static readonly Color C_BG             = HexColor("#1D2E1F"); // Main app background
    static readonly Color C_HEADER_BG      = HexColor("#2C5A3D"); // Top/header background
    static readonly Color C_CARD           = HexColor("#243F2C"); // Card background
    static readonly Color C_CARD_BORDER    = HexColor("#3C6B4C"); // Card border / outline
    static readonly Color C_ICON_BG        = HexColor("#2D593D"); // Icon tile background
    static readonly Color C_ICON_BORDER    = HexColor("#3C6B4C"); // Icon tile border
    static readonly Color C_TEXT_PRIMARY   = HexColor("#F0F0F0"); // Primary text
    static readonly Color C_SUBTEXT        = HexColor("#7EA88C"); // Secondary / muted text
    static readonly Color C_SECTION_HEADER = HexColor("#A1D4C0"); // Section header text
    static readonly Color C_RISK_BAR_BG     = HexColor("#3C6B4C"); // Risk bar / track background
    static readonly Color C_HIGH_BADGE_BG   = HexColor("#6B4A2E"); // High badge background
    static readonly Color C_HIGH_BADGE_TEXT = HexColor("#FF6B57"); // High badge text
    static readonly Color C_MOD_BADGE_BG    = HexColor("#73652C"); // Moderate badge background
    static readonly Color C_MOD_BADGE_TEXT  = HexColor("#EE9F28"); // Moderate badge text
    static readonly Color C_LOW_BADGE_BG    = HexColor("#315B3F"); // Low badge background
    static readonly Color C_LOW_BADGE_TEXT  = HexColor("#6BE089"); // Low badge text
    static readonly Color C_BTN_FILLED_BG   = HexColor("#5ED9C0"); // Filled button background
    static readonly Color C_BTN_FILLED_TEXT = HexColor("#143220"); // Filled button text
    static readonly Color C_DIVIDER        = HexColor("#386C4B"); // Divider line

    GameObject _reportListPanel;
    GameObject _itemDetailPanel;

    TextMeshProUGUI _listReportSubtitle;
    Transform _listItemsContainer;
    TextMeshProUGUI _listEmptyMessage;
    TextMeshProUGUI _listScanDate;
    TextMeshProUGUI _listScanDuration;
    TextMeshProUGUI _listScanItems;
    TextMeshProUGUI _listRiskBadgeText;
    Image _listRiskBadgeBg;
    TextMeshProUGUI _listRiskExplanation;
    Image[] _listRiskSegmentBg = new Image[3];
    TextMeshProUGUI[] _listRiskSegmentText = new TextMeshProUGUI[3];
    GameObject _listRiskSummaryCard;
    GameObject _listScanInfoCard;

    TextMeshProUGUI _detailTitle;
    TextMeshProUGUI _detailSubtitle;
    TextMeshProUGUI _detailWhyRisk;
    TextMeshProUGUI _detailWhatToDo;
    Image _detailRiskBadgeBg;
    TextMeshProUGUI _detailRiskBadgeText;

    List<DetectionWithDetails> _currentDetections = new();
    ScanReport _currentReport;
    int _detailIndex;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    void Start()
    {
        if (targetCanvas == null)
        {
            targetCanvas = FindFirstObjectByType<Canvas>();
        }

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

    public void ShowLatestReport()
    {
        if (useMockDataForUIDebugging)
        {
            LoadMockData(out var mockReport, out var mockDetections);
            DisplayReport(mockReport, mockDetections);
            return;
        }

        List<ScanReport> allReports = null;

        try
        {
            allReports = DatabaseManager.Instance.GetAllReports();
        }
        catch (Exception e)
        {
            Debug.LogError("[ReportUIBuilder] GetAllReports() threw an exception: " + e.Message);
            ShowEmptyState("Could not load reports.", "Check the database connection.");
            return;
        }

        if (allReports == null || allReports.Count == 0)
        {
            ShowEmptyState("No scan reports available yet.", "Complete a scan to generate a report.");
            return;
        }

        // DatabaseManager.GetAllReports() orders by newest first, so index 0 is the latest report.
        ShowReport(allReports[0].id);
    }

    public void ShowReport(int reportId)
    {
        if (useMockDataForUIDebugging)
        {
            LoadMockData(out var mockReport, out var mockDetections);
            DisplayReport(mockReport, mockDetections);
            return;
        }

        ScanReport report = null;
        List<DetectionWithDetails> detections = null;

        try
        {
            report = DatabaseManager.Instance.GetReportById(reportId);
            detections = DatabaseManager.Instance.GetDetectionsForReport(reportId);
        }
        catch (Exception e)
        {
            Debug.LogError("[ReportUIBuilder] Database read failed for report " + reportId + ": " + e.Message);
            ShowEmptyState("Could not load report.", "A database error occurred.");
            return;
        }

        if (report == null)
        {
            Debug.LogWarning("[ReportUIBuilder] No report found with ID " + reportId + ".");
            ShowEmptyState("Report not found.", "No report exists with ID " + reportId + ".");
            return;
        }

        detections ??= new List<DetectionWithDetails>();

        DisplayReport(report, detections);
    }

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

    void DisplayReport(ScanReport report, List<DetectionWithDetails> detections)
    {
        _currentReport = report;
        _currentDetections = detections;

        PopulateReportListPanel();

        _itemDetailPanel.SetActive(false);
        _reportListPanel.SetActive(true);
    }

    void ShowEmptyState(string primary, string secondary = "")
    {
        _currentReport = null;
        _currentDetections = new List<DetectionWithDetails>();

        _listRiskSummaryCard.SetActive(false);
        _listScanInfoCard.SetActive(false);

        foreach (Transform child in _listItemsContainer)
        {
            Destroy(child.gameObject);
        }

        _listReportSubtitle.text = "";

        _listEmptyMessage.text = string.IsNullOrEmpty(secondary)
            ? primary
            : primary + "\n<size=12><color=#7EA88C>" + secondary + "</color></size>";

        _listEmptyMessage.gameObject.SetActive(true);

        _itemDetailPanel.SetActive(false);
        _reportListPanel.SetActive(true);
    }

    void PopulateReportListPanel()
    {
        _listEmptyMessage.gameObject.SetActive(false);
        _listRiskSummaryCard.SetActive(true);
        _listScanInfoCard.SetActive(true);

        DateTime dt = ParseDate(_currentReport.scanned_at);
        int mins = _currentReport.duration_seconds / 60;
        int secs = _currentReport.duration_seconds % 60;
        string dur = mins > 0 ? mins + "m " + secs + "s" : secs + " sec";

        _listReportSubtitle.text =
            dt.ToString("MMMM d") + " • " + dur + " • " + _currentReport.total_objects_detected + " items found";

        _listScanDate.text = dt.ToString("MMMM d, yyyy");
        _listScanDuration.text = dur;
        _listScanItems.text = _currentReport.total_objects_detected + " total";

        // ── Risk summary: pill badge + explanation + segmented indicator ──
        string overallRisk = ComputeOverallRisk(_currentDetections);
        var (badgeBg, badgeText) = RiskBadgeColors(overallRisk);

        _listRiskBadgeText.text = overallRisk;
        _listRiskBadgeBg.color = badgeBg;
        _listRiskBadgeText.color = badgeText;

        int detectedCount = _currentReport.total_objects_detected;

        _listRiskExplanation.text =
            "Based on " + detectedCount + " detected mosquito breeding risk" +
            (detectedCount == 1 ? "" : "s") + ".";

        int activeSegment = overallRisk switch
        {
            "High" => 2,
            "Moderate" => 1,
            _ => 0
        };

        string[] segmentRiskKeys = { "Low", "Moderate", "High" };

        for (int i = 0; i < 3; i++)
        {
            if (i == activeSegment)
            {
                var (segBg, segText) = RiskBadgeColors(segmentRiskKeys[i]);
                _listRiskSegmentBg[i].color = segBg;
                _listRiskSegmentText[i].color = segText;
                _listRiskSegmentText[i].fontStyle = FontStyles.Bold;
            }
            else
            {
                _listRiskSegmentBg[i].color = C_RISK_BAR_BG;
                _listRiskSegmentText[i].color = C_SUBTEXT;
                _listRiskSegmentText[i].fontStyle = FontStyles.Normal;
            }
        }

        foreach (Transform child in _listItemsContainer)
        {
            Destroy(child.gameObject);
        }

        if (_currentDetections.Count == 0)
        {
            var noItems = new GameObject("NoItemsLabel",
                typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));

            noItems.transform.SetParent(_listItemsContainer, false);
            noItems.GetComponent<LayoutElement>().preferredHeight = 50;

            var t = noItems.GetComponent<TextMeshProUGUI>();
            t.text = "No detected items for this report.";
            t.fontSize = 14;
            t.color = C_SUBTEXT;
            t.alignment = TextAlignmentOptions.Center;
            t.margin = new Vector4(20, 0, 20, 0);
        }
        else
        {
            for (int i = 0; i < _currentDetections.Count; i++)
            {
                CreateDetectedItemCard(_listItemsContainer, _currentDetections[i], i);
            }
        }
    }

    void PopulateItemDetailPanel(int index)
    {
        DetectionWithDetails d = _currentDetections[index];

        _detailTitle.text = string.IsNullOrWhiteSpace(d.display_name) ? d.label : d.display_name;
        _detailSubtitle.text = InstanceCountText(d.label);

        _detailWhyRisk.text = string.IsNullOrWhiteSpace(d.object_description)
            ? "No risk description available for this item."
            : d.object_description;

        if (!string.IsNullOrWhiteSpace(d.mitigation_description))
        {
            string[] lines = d.mitigation_description.Split(
                new[] { '\n', ';' },
                StringSplitOptions.RemoveEmptyEntries
            );

            var steps = new List<string>();

            foreach (string line in lines)
            {
                string trimmed = line.Trim();

                if (trimmed.Length > 0)
                {
                    steps.Add(trimmed);
                }
            }

            var sb = new System.Text.StringBuilder();

            for (int i = 0; i < steps.Count; i++)
            {
                sb.Append("• ").Append(steps[i]);

                if (i < steps.Count - 1)
                {
                    sb.Append("\n\n");
                }
            }

            _detailWhatToDo.text = sb.ToString();
        }
        else
        {
            _detailWhatToDo.text = "No mitigation information available for this item.";
        }

        string risk = GetRiskLevel(d.label);
        var (riskBg, riskText) = RiskBadgeColors(risk);

        _detailRiskBadgeText.text = risk + " risk";
        _detailRiskBadgeBg.color = riskBg;
        _detailRiskBadgeText.color = riskText;
    }

    void BuildReportListPanel()
    {
        Transform canvasTransform = targetCanvas.transform;

        _reportListPanel = CreateFullScreenPanel("FullReportPanel_Built", canvasTransform, C_BG);
        RectTransform root = _reportListPanel.GetComponent<RectTransform>();

        Transform scroll = CreateScrollView("ReportScroll", root);
        RectTransform content = scroll.Find("Viewport/Content").GetComponent<RectTransform>();

        var hBlock = MakeLayoutBlock("Header", content, 135, C_HEADER_BG);
        RectTransform hRect = hBlock.GetComponent<RectTransform>();

        var backBtn = MakeButton(
            "BackBtn",
            hRect,
            "← Back",
            C_SUBTEXT,
            15,
            new Vector2(0, 1),
            new Vector2(0, 1),
            new Vector2(0, -58),
            new Vector2(95, 30),
            Color.clear
        );

        backBtn.GetComponent<Button>().onClick.AddListener(() =>
        {
            _reportListPanel.SetActive(false);
        });

        MakeText(
            "TitleText",
            hRect,
            "Full report",
            31,
            C_TEXT_PRIMARY,
            FontStyles.Bold,
            new Vector2(0, 1),
            new Vector2(1, 1),
            new Vector2(24, -96),
            new Vector2(-24, -58)
        );

        _listReportSubtitle = MakeText(
            "SubtitleText",
            hRect,
            "",
            15,
            C_SUBTEXT,
            FontStyles.Normal,
            new Vector2(0, 1),
            new Vector2(1, 1),
            new Vector2(24, -127),
            new Vector2(-24, -100)
        );

        var emptyGo = new GameObject(
            "EmptyStateLabel",
            typeof(RectTransform),
            typeof(TextMeshProUGUI),
            typeof(LayoutElement)
        );

        emptyGo.transform.SetParent(content, false);
        emptyGo.GetComponent<LayoutElement>().preferredHeight = 100;

        _listEmptyMessage = emptyGo.GetComponent<TextMeshProUGUI>();
        _listEmptyMessage.text = "";
        _listEmptyMessage.fontSize = 16;
        _listEmptyMessage.color = C_SUBTEXT;
        _listEmptyMessage.alignment = TextAlignmentOptions.Center;
        _listEmptyMessage.margin = new Vector4(24, 20, 24, 0);
        _listEmptyMessage.enableWordWrapping = true;
        emptyGo.SetActive(false);

        // ─────────────────────────────────────────────────────────────────
        // RISK SUMMARY
        // ─────────────────────────────────────────────────────────────────
        MakeSectionLabel("RISK SUMMARY", content);

        _listRiskSummaryCard = MakeCard("RiskSummaryCard", content, 140);
        RectTransform riskRect = _listRiskSummaryCard.GetComponent<RectTransform>();

        var overallLabel = MakeText(
            "OverallLabel",
            riskRect,
            "Overall risk level",
            16,
            C_TEXT_PRIMARY,
            FontStyles.Normal,
            new Vector2(0, 1),
            new Vector2(0, 1),
            new Vector2(18, -50),
            new Vector2(230, -16)
        );
        overallLabel.alignment = TextAlignmentOptions.Left;

        _listRiskBadgeBg = MakeBadge(
            "RiskBadge",
            riskRect,
            "Moderate",
            C_MOD_BADGE_BG,
            C_MOD_BADGE_TEXT,
            new Vector2(1, 1),
            new Vector2(-18, -16),
            new Vector2(104, 34),
            14
        );

        _listRiskBadgeText = _listRiskBadgeBg.GetComponentInChildren<TextMeshProUGUI>();

        _listRiskExplanation = MakeText(
            "RiskExplanation",
            riskRect,
            "",
            13,
            C_SUBTEXT,
            FontStyles.Normal,
            new Vector2(0, 1),
            new Vector2(1, 1),
            new Vector2(18, -90),
            new Vector2(-18, -54)
        );
        _listRiskExplanation.alignment = TextAlignmentOptions.TopLeft;
        _listRiskExplanation.enableWordWrapping = true;

        // Segmented Low / Med / High indicator — active level is highlighted.
        var segmentsRow = new GameObject(
            "RiskSegmentsRow",
            typeof(RectTransform),
            typeof(HorizontalLayoutGroup)
        );

        segmentsRow.transform.SetParent(riskRect, false);

        RectTransform segRowRect = segmentsRow.GetComponent<RectTransform>();
        SetAnchors(
            segRowRect,
            new Vector2(0, 1),
            new Vector2(1, 1),
            new Vector2(18, -124),
            new Vector2(-18, -98)
        );

        var segHlg = segmentsRow.GetComponent<HorizontalLayoutGroup>();
        segHlg.spacing = 8;
        segHlg.childControlWidth = true;
        segHlg.childControlHeight = true;
        segHlg.childForceExpandWidth = true;
        segHlg.childForceExpandHeight = true;

        string[] segmentLabels = { "Low", "Med", "High" };

        for (int i = 0; i < 3; i++)
        {
            var seg = new GameObject("RiskSegment_" + segmentLabels[i], typeof(RectTransform), typeof(Image));
            seg.transform.SetParent(segmentsRow.transform, false);

            Image segImg = seg.GetComponent<Image>();
            segImg.color = C_RISK_BAR_BG;
            SetRounded(segImg, 8);

            _listRiskSegmentBg[i] = segImg;

            var segTextGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            segTextGo.transform.SetParent(seg.transform, false);

            RectTransform stRect = segTextGo.GetComponent<RectTransform>();
            stRect.anchorMin = Vector2.zero;
            stRect.anchorMax = Vector2.one;
            stRect.sizeDelta = Vector2.zero;

            TextMeshProUGUI stTm = segTextGo.GetComponent<TextMeshProUGUI>();
            stTm.text = segmentLabels[i];
            stTm.fontSize = 12;
            stTm.color = C_SUBTEXT;
            stTm.alignment = TextAlignmentOptions.Center;

            _listRiskSegmentText[i] = stTm;
        }

        // ─────────────────────────────────────────────────────────────────
        // DETECTED ITEMS
        // ─────────────────────────────────────────────────────────────────
        MakeSectionLabel("DETECTED ITEMS", content);

        var itemsHolder = new GameObject(
            "DetectedItemsContainer",
            typeof(RectTransform),
            typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter)
        );

        itemsHolder.transform.SetParent(content, false);

        RectTransform ihRect = itemsHolder.GetComponent<RectTransform>();
        ihRect.anchorMin = new Vector2(0, 1);
        ihRect.anchorMax = new Vector2(1, 1);
        ihRect.pivot = new Vector2(0.5f, 1f);
        ihRect.sizeDelta = Vector2.zero;

        var vlg = itemsHolder.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 14;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.padding = new RectOffset(20, 20, 0, 0);

        itemsHolder.GetComponent<ContentSizeFitter>().verticalFit =
            ContentSizeFitter.FitMode.PreferredSize;

        _listItemsContainer = itemsHolder.transform;

        // ─────────────────────────────────────────────────────────────────
        // SCAN INFO
        // ─────────────────────────────────────────────────────────────────
        MakeSectionLabel("SCAN INFO", content);

        _listScanInfoCard = MakeCard("ScanInfoCard", content, 140);
        RectTransform infoRect = _listScanInfoCard.GetComponent<RectTransform>();

        BuildInfoRow(infoRect, "Date", ref _listScanDate, -20);
        AddDivider(infoRect, -50);
        BuildInfoRow(infoRect, "Duration", ref _listScanDuration, -60);
        AddDivider(infoRect, -90);
        BuildInfoRow(infoRect, "Items found", ref _listScanItems, -100);

        var spacer = new GameObject("BottomSpacer", typeof(RectTransform), typeof(LayoutElement));
        spacer.transform.SetParent(content, false);
        spacer.GetComponent<LayoutElement>().preferredHeight = 55;
    }

    void BuildItemDetailPanel()
    {
        Transform canvasTransform = targetCanvas.transform;

        _itemDetailPanel = CreateFullScreenPanel("ItemDetailPanel_Built", canvasTransform, C_BG);
        RectTransform root = _itemDetailPanel.GetComponent<RectTransform>();

        Transform scroll = CreateScrollView("DetailScroll", root);
        RectTransform content = scroll.Find("Viewport/Content").GetComponent<RectTransform>();

        // ─────────────────────────────────────────────────────────────────
        // HEADER
        // ─────────────────────────────────────────────────────────────────
        var hBlock = MakeLayoutBlock("Header", content, 135, C_HEADER_BG);
        RectTransform hRect = hBlock.GetComponent<RectTransform>();

        var backBtn = MakeButton(
            "BackBtn",
            hRect,
            "← Full report",
            C_SUBTEXT,
            14,
            new Vector2(0, 1),
            new Vector2(0, 1),
            new Vector2(0, -58),
            new Vector2(135, 30),
            Color.clear
        );

        backBtn.GetComponent<Button>().onClick.AddListener(() =>
        {
            _itemDetailPanel.SetActive(false);
            _reportListPanel.SetActive(true);
        });

        _detailTitle = MakeText(
            "DetailTitle",
            hRect,
            "",
            31,
            C_TEXT_PRIMARY,
            FontStyles.Bold,
            new Vector2(0, 1),
            new Vector2(1, 1),
            new Vector2(24, -96),
            new Vector2(-24, -58)
        );

        _detailSubtitle = MakeText(
            "DetailSubtitle",
            hRect,
            "",
            15,
            C_SUBTEXT,
            FontStyles.Normal,
            new Vector2(0, 1),
            new Vector2(1, 1),
            new Vector2(24, -127),
            new Vector2(-24, -100)
        );

        // ─────────────────────────────────────────────────────────────────
        // RISK LEVEL
        // ─────────────────────────────────────────────────────────────────
        MakeSectionLabel("RISK LEVEL", content);

        var riskCard = MakeCard("RiskLevelCard", content, 112);
        RectTransform riskCardRect = riskCard.GetComponent<RectTransform>();

        var overallItemLabel = MakeText(
            "OverallItemLabel",
            riskCardRect,
            "Overall item risk",
            16,
            C_TEXT_PRIMARY,
            FontStyles.Normal,
            new Vector2(0, 1),
            new Vector2(0, 1),
            new Vector2(20, -52),
            new Vector2(190, -18)
        );
        overallItemLabel.alignment = TextAlignmentOptions.Left;

        _detailRiskBadgeBg = MakeBadge(
            "DetailRiskBadge",
            riskCardRect,
            "High risk",
            C_HIGH_BADGE_BG,
            C_HIGH_BADGE_TEXT,
            new Vector2(1, 1),
            new Vector2(-20, -18),
            new Vector2(130, 34),
            13
        );

        _detailRiskBadgeText = _detailRiskBadgeBg.GetComponentInChildren<TextMeshProUGUI>();

        var detailRiskExplanation = MakeText(
            "DetailRiskExplanation",
            riskCardRect,
            "Based on this object's potential to hold standing water.",
            13,
            C_SUBTEXT,
            FontStyles.Normal,
            new Vector2(0, 1),
            new Vector2(1, 1),
            new Vector2(20, -94),
            new Vector2(-20, -58)
        );
        detailRiskExplanation.alignment = TextAlignmentOptions.TopLeft;
        detailRiskExplanation.enableWordWrapping = true;

        // ─────────────────────────────────────────────────────────────────
        // WHY IT'S A RISK
        // ─────────────────────────────────────────────────────────────────
        MakeSectionLabel("WHY IT'S A RISK", content);

        var whyCard = MakeAutoSizeCard("WhyRiskCard", content);
        _detailWhyRisk = GetAutoSizeCardText(whyCard);
        _detailWhyRisk.lineSpacing = 4;

        // ─────────────────────────────────────────────────────────────────
        // WHAT TO DO
        // ─────────────────────────────────────────────────────────────────
        MakeSectionLabel("WHAT TO DO", content);

        var todoCard = MakeAutoSizeCard("WhatToDoCard", content);
        _detailWhatToDo = GetAutoSizeCardText(todoCard);
        _detailWhatToDo.lineSpacing = 6;

        // ─────────────────────────────────────────────────────────────────
        // PREV / NEXT NAVIGATION
        // ─────────────────────────────────────────────────────────────────
        var navRow = new GameObject(
            "NavRow",
            typeof(RectTransform),
            typeof(HorizontalLayoutGroup),
            typeof(LayoutElement)
        );

        navRow.transform.SetParent(content, false);
        navRow.GetComponent<LayoutElement>().preferredHeight = 64;

        var hlg = navRow.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 12;
        hlg.padding = new RectOffset(20, 20, 8, 8);
        hlg.childControlWidth = true;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = false;

        RectTransform navRect = navRow.GetComponent<RectTransform>();

        var prevBtn = MakeButton(
            "PrevBtn",
            navRect,
            "← Prev Item",
            C_TEXT_PRIMARY,
            15,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            new Vector2(0, 48),
            C_CARD_BORDER
        );

        prevBtn.GetComponent<Button>().onClick.AddListener(() =>
        {
            if (_detailIndex > 0)
            {
                ShowItemDetail(_detailIndex - 1);
            }
        });

        SetRounded(prevBtn.GetComponent<Image>(), 12);

        var nextBtn = MakeButton(
            "NextBtn",
            navRect,
            "Next Item →",
            C_BTN_FILLED_TEXT,
            15,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            new Vector2(0, 48),
            C_BTN_FILLED_BG
        );

        nextBtn.GetComponent<Button>().onClick.AddListener(() =>
        {
            if (_detailIndex < _currentDetections.Count - 1)
            {
                ShowItemDetail(_detailIndex + 1);
            }
        });

        SetRounded(nextBtn.GetComponent<Image>(), 12);

        var spacer = new GameObject("BottomSpacer", typeof(RectTransform), typeof(LayoutElement));
        spacer.transform.SetParent(content, false);
        spacer.GetComponent<LayoutElement>().preferredHeight = 55;
    }

    void CreateDetectedItemCard(Transform parent, DetectionWithDetails d, int index)
    {
        var card = new GameObject(
            "ItemCard_" + index,
            typeof(RectTransform),
            typeof(Image),
            typeof(Button),
            typeof(LayoutElement)
        );

        card.transform.SetParent(parent, false);
        card.GetComponent<LayoutElement>().preferredHeight = 80;

        Image border = card.GetComponent<Image>();
        border.color = C_CARD_BORDER;
        SetRounded(border, 16);

        Image fill = AddCardFill(card.transform, 1.5f, C_CARD);

        RectTransform rect = card.GetComponent<RectTransform>();

        var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        icon.transform.SetParent(rect, false);

        RectTransform iRect = icon.GetComponent<RectTransform>();
        iRect.anchorMin = new Vector2(0, 0.5f);
        iRect.anchorMax = new Vector2(0, 0.5f);
        iRect.pivot = new Vector2(0, 0.5f);
        iRect.anchoredPosition = new Vector2(16, 0);
        iRect.sizeDelta = new Vector2(48, 48);

        Image iconBorder = icon.GetComponent<Image>();
        iconBorder.color = C_ICON_BORDER;
        SetRounded(iconBorder, 24);

        AddCardFill(icon.transform, 1.5f, C_ICON_BG);

        var emojiGo = new GameObject("Emoji", typeof(RectTransform), typeof(TextMeshProUGUI));
        emojiGo.transform.SetParent(icon.transform, false);

        RectTransform eRect = emojiGo.GetComponent<RectTransform>();
        eRect.anchorMin = Vector2.zero;
        eRect.anchorMax = Vector2.one;
        eRect.sizeDelta = Vector2.zero;

        TextMeshProUGUI eTM = emojiGo.GetComponent<TextMeshProUGUI>();
        eTM.text = GetIconForLabel(d.label);
        eTM.fontSize = 22;
        eTM.alignment = TextAlignmentOptions.Center;

        var nameGo = new GameObject("ItemName", typeof(RectTransform), typeof(TextMeshProUGUI));
        nameGo.transform.SetParent(rect, false);

        RectTransform nRect = nameGo.GetComponent<RectTransform>();
        nRect.anchorMin = new Vector2(0, 1);
        nRect.anchorMax = new Vector2(1, 1);
        nRect.pivot = new Vector2(0, 1);
        nRect.anchoredPosition = new Vector2(75, -14);
        nRect.sizeDelta = new Vector2(-155, 26);

        TextMeshProUGUI nTM = nameGo.GetComponent<TextMeshProUGUI>();
        nTM.text = string.IsNullOrWhiteSpace(d.display_name) ? d.label : d.display_name;
        nTM.fontSize = 17;
        nTM.color = C_TEXT_PRIMARY;
        nTM.fontStyle = FontStyles.Bold;

        var subGo = new GameObject("ItemSub", typeof(RectTransform), typeof(TextMeshProUGUI));
        subGo.transform.SetParent(rect, false);

        RectTransform sRect = subGo.GetComponent<RectTransform>();
        sRect.anchorMin = new Vector2(0, 0);
        sRect.anchorMax = new Vector2(1, 0);
        sRect.pivot = new Vector2(0, 0);
        sRect.anchoredPosition = new Vector2(75, 16);
        sRect.sizeDelta = new Vector2(-155, 20);

        TextMeshProUGUI sTM = subGo.GetComponent<TextMeshProUGUI>();
        sTM.text = CountInstances(d.label) + " detected · " + GetRiskLevel(d.label) + " risk";
        sTM.fontSize = 13;
        sTM.color = C_SUBTEXT;

        string riskLevel = GetRiskLevel(d.label);
        var (badgeBg, badgeText) = RiskBadgeColors(riskLevel);

        MakeBadge(
            "RiskBadge",
            rect,
            riskLevel,
            badgeBg,
            badgeText,
            new Vector2(1, 0.5f),
            new Vector2(-46, 0),
            new Vector2(92, 28),
            12
        );

        var arrowGo = new GameObject("Arrow", typeof(RectTransform), typeof(TextMeshProUGUI));
        arrowGo.transform.SetParent(rect, false);

        RectTransform aRect = arrowGo.GetComponent<RectTransform>();
        aRect.anchorMin = new Vector2(1, 0.5f);
        aRect.anchorMax = new Vector2(1, 0.5f);
        aRect.pivot = new Vector2(1, 0.5f);
        aRect.anchoredPosition = new Vector2(-14, 0);
        aRect.sizeDelta = new Vector2(22, 22);

        TextMeshProUGUI aTM = arrowGo.GetComponent<TextMeshProUGUI>();
        aTM.text = "›";
        aTM.fontSize = 25;
        aTM.color = C_SUBTEXT;
        aTM.alignment = TextAlignmentOptions.Center;

        Button button = card.GetComponent<Button>();
        button.onClick.AddListener(() => ShowItemDetail(index));
        button.targetGraphic = fill;
    }

    static GameObject CreateFullScreenPanel(string name, Transform parent, Color bg)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        go.GetComponent<Image>().color = bg;

        return go;
    }

    static GameObject MakeLayoutBlock(string name, Transform parent, int height, Color bg)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        go.GetComponent<Image>().color = bg;
        go.GetComponent<LayoutElement>().preferredHeight = height;

        return go;
    }

    static GameObject MakeCard(string name, Transform parent, int fixedHeight)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        Image border = go.GetComponent<Image>();
        border.color = C_CARD_BORDER;
        SetRounded(border, 16);

        AddCardFill(go.transform, 1.5f, C_CARD);

        go.GetComponent<LayoutElement>().preferredHeight = fixedHeight;

        return go;
    }

    // Auto-size card. Height is driven by a VerticalLayoutGroup with 20px padding on all
    // sides, which avoids stretch-anchor issues when ContentSizeFitter is active.
    // Always call GetAutoSizeCardText() immediately after to create the body text element.
    static GameObject MakeAutoSizeCard(string name, Transform parent)
    {
        var go = new GameObject(
            name,
            typeof(RectTransform),
            typeof(Image),
            typeof(LayoutElement),
            typeof(ContentSizeFitter),
            typeof(VerticalLayoutGroup)
        );

        go.transform.SetParent(parent, false);

        Image img = go.GetComponent<Image>();
        img.color = C_CARD;
        SetRounded(img, 16);

        var vlg = go.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(20, 20, 20, 20);
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        go.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        go.GetComponent<LayoutElement>().minHeight = 72;

        return go;
    }

    // Creates the body-text child inside a card built with MakeAutoSizeCard.
    // The LayoutElement lets ContentSizeFitter measure the TMP preferred height.
    static TextMeshProUGUI GetAutoSizeCardText(GameObject card)
    {
        var go = new GameObject(
            "BodyText",
            typeof(RectTransform),
            typeof(TextMeshProUGUI),
            typeof(LayoutElement)
        );

        go.transform.SetParent(card.transform, false);
        go.GetComponent<LayoutElement>().flexibleWidth = 1;

        TextMeshProUGUI tm = go.GetComponent<TextMeshProUGUI>();
        tm.text = "";
        tm.fontSize = 15;
        tm.color = C_TEXT_PRIMARY;
        tm.fontStyle = FontStyles.Normal;
        tm.enableWordWrapping = true;

        return tm;
    }

    // Adds an inset "fill" image on top of a card's border-colored background,
    // creating a thin border/outline effect around the card edges.
    static Image AddCardFill(Transform cardTransform, float inset, Color fillColor)
    {
        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(cardTransform, false);

        RectTransform fRect = fill.GetComponent<RectTransform>();
        SetAnchors(fRect, Vector2.zero, Vector2.one, new Vector2(inset, inset), new Vector2(-inset, -inset));

        Image img = fill.GetComponent<Image>();
        img.color = fillColor;
        SetRounded(img, 14.5f);

        return img;
    }

    static void MakeSectionLabel(string text, Transform parent)
    {
        var go = new GameObject(
            text + "_Label",
            typeof(RectTransform),
            typeof(TextMeshProUGUI),
            typeof(LayoutElement)
        );

        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = 34;

        TextMeshProUGUI tm = go.GetComponent<TextMeshProUGUI>();
        tm.text = text;
        tm.fontSize = 13;
        tm.color = C_SECTION_HEADER;
        tm.fontStyle = FontStyles.Bold;
        tm.characterSpacing = 2f;
        tm.margin = new Vector4(20, 14, 20, 0);
    }

    static TextMeshProUGUI MakeText(
        string name,
        Transform parent,
        string text,
        float size,
        Color color,
        FontStyles style,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax
    )
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0, 1);
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        TextMeshProUGUI tm = go.GetComponent<TextMeshProUGUI>();
        tm.text = text;
        tm.fontSize = size;
        tm.color = color;
        tm.fontStyle = style;

        return tm;
    }

    static Image MakeBadge(
        string name,
        Transform parent,
        string label,
        Color bg,
        Color textColor,
        Vector2 anchor,
        Vector2 anchoredPos,
        Vector2 size,
        float fontSize
    )
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;

        Image img = go.GetComponent<Image>();
        img.color = bg;
        SetRounded(img, 13);

        var textGo = new GameObject("BadgeText", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, false);

        RectTransform tRect = textGo.GetComponent<RectTransform>();
        tRect.anchorMin = Vector2.zero;
        tRect.anchorMax = Vector2.one;
        tRect.sizeDelta = Vector2.zero;

        TextMeshProUGUI tm = textGo.GetComponent<TextMeshProUGUI>();
        tm.text = label;
        tm.fontSize = fontSize;
        tm.color = textColor;
        tm.fontStyle = FontStyles.Bold;
        tm.alignment = TextAlignmentOptions.Center;

        return img;
    }

    static GameObject MakeButton(
        string name,
        Transform parent,
        string label,
        Color textColor,
        float fontSize,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax,
        Color bgColor
    )
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0, 1);
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        rect.sizeDelta = offsetMax - offsetMin;

        Image img = go.GetComponent<Image>();
        img.color = bgColor;

        if (bgColor != Color.clear)
        {
            SetRounded(img, 12);
        }

        go.GetComponent<Button>().targetGraphic = img;

        var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, false);

        RectTransform tRect = textGo.GetComponent<RectTransform>();
        tRect.anchorMin = Vector2.zero;
        tRect.anchorMax = Vector2.one;
        tRect.sizeDelta = Vector2.zero;

        TextMeshProUGUI tm = textGo.GetComponent<TextMeshProUGUI>();
        tm.text = label;
        tm.fontSize = fontSize;
        tm.color = textColor;
        tm.alignment = TextAlignmentOptions.Center;

        return go;
    }

    static Transform CreateScrollView(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(ScrollRect));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        SetAnchors(rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        ScrollRect sr = go.GetComponent<ScrollRect>();
        sr.horizontal = false;
        sr.movementType = ScrollRect.MovementType.Elastic;
        sr.elasticity = 0.1f;
        sr.scrollSensitivity = 30;

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(go.transform, false);

        SetAnchors(
            viewport.GetComponent<RectTransform>(),
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );

        viewport.GetComponent<Image>().color = new Color(0, 0, 0, 0.01f);
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        var content = new GameObject(
            "Content",
            typeof(RectTransform),
            typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter)
        );

        content.transform.SetParent(viewport.transform, false);

        RectTransform cRect = content.GetComponent<RectTransform>();
        cRect.anchorMin = new Vector2(0, 1);
        cRect.anchorMax = new Vector2(1, 1);
        cRect.pivot = new Vector2(0.5f, 1f);
        cRect.sizeDelta = Vector2.zero;

        VerticalLayoutGroup vlg = content.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 18;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.padding = new RectOffset(16, 16, 24, 28);

        content.GetComponent<ContentSizeFitter>().verticalFit =
            ContentSizeFitter.FitMode.PreferredSize;

        sr.viewport = viewport.GetComponent<RectTransform>();
        sr.content = content.GetComponent<RectTransform>();

        return go.transform;
    }

    void BuildInfoRow(Transform parent, string labelText, ref TextMeshProUGUI valueRef, float yOffset)
    {
        MakeText(
            labelText + "_L",
            parent,
            labelText,
            15,
            C_SUBTEXT,
            FontStyles.Normal,
            new Vector2(0, 1),
            new Vector2(0, 1),
            new Vector2(18, yOffset - 20),
            new Vector2(135, yOffset)
        );

        valueRef = MakeText(
            labelText + "_V",
            parent,
            "—",
            15,
            C_TEXT_PRIMARY,
            FontStyles.Bold,
            new Vector2(1, 1),
            new Vector2(1, 1),
            new Vector2(-140, yOffset - 20),
            new Vector2(-18, yOffset)
        );

        valueRef.alignment = TextAlignmentOptions.Right;
    }

    // Thin horizontal divider line, vertically centered at yPos (relative to the
    // parent's top edge, e.g. -50 means 50px below the top of the parent rect).
    static void AddDivider(Transform parent, float yPos)
    {
        var go = new GameObject("Divider", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(18, yPos - 0.5f);
        rect.offsetMax = new Vector2(-18, yPos + 0.5f);

        go.GetComponent<Image>().color = C_DIVIDER;
    }

    static void SetAnchors(
        RectTransform r,
        Vector2 min,
        Vector2 max,
        Vector2 offMin,
        Vector2 offMax
    )
    {
        r.anchorMin = min;
        r.anchorMax = max;
        r.pivot = new Vector2(0.5f, 0.5f);
        r.offsetMin = offMin;
        r.offsetMax = offMax;
    }

    static void SetRounded(Image img, float radius)
    {
        Sprite sprite = Resources.Load<Sprite>("UI/Rounded");

        if (sprite != null)
        {
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
        }
    }

    static Color HexColor(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color c);
        return c;
    }

    static DateTime ParseDate(string s)
    {
        return DateTime.TryParse(s, out DateTime dt) ? dt : DateTime.Now;
    }

    int CountInstances(string label)
    {
        return _currentDetections.FindAll(d => d.label == label).Count;
    }

    string InstanceCountText(string label)
    {
        int c = CountInstances(label);
        return c == 1 ? "1 instance detected" : c + " instances detected";
    }
    
    public static string GetRiskLevelPublic(string label)
    {
        return GetRiskLevel(label);
    }

    static string GetRiskLevel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return "Low";
        }

        label = label.ToLowerInvariant();

        // High based on the table:
        // Bromiliads has a High row for "Remove".
        if (
            label.Contains("ss_bromiliad")
        )
        {
            return "High";
        }

        // Medium based on the table:
        // Tires = Medium
        // Planters/empty pots = Medium for drill drainage hole
        // Water Hyacinths = Medium
        // Water Lettuce = Medium
        // Trash cans = Medium for drill drainage hole
        // Grill = Medium
        if (
            label.Contains("ss_tire") ||
            label.Contains("ss_pot") ||
            label.Contains("ss_waterhyacinth") ||
            label.Contains("ss_waterlettuce") ||
            label.Contains("ss_trashcan") ||
            label.Contains("ss_grill")
        )
        {
            return "Moderate";
        }

        // Low based on the table:
        // Treehole = Low
        // Pool, inflatable = Low
        // Birdbaths = Low
        // Wheelbarrows = Low
        // Watering cans = Low
        if (
            label.Contains("ss_treehole") ||
            label.Contains("ss_inflatablepool") ||
            label.Contains("ss_birdbath") ||
            label.Contains("ss_wheelbarrow") ||
            label.Contains("ss_wateringcan")
        )
        {
            return "Low";
        }

        // Bucket is not visible in the table screenshot, so leave unknowns as Low.
        return "Low";
    }

    static string ComputeOverallRisk(List<DetectionWithDetails> detections)
    {
        if (detections == null || detections.Count == 0)
        {
            return "Low";
        }

        bool anyHigh = false;
        bool anyModerate = false;

        foreach (DetectionWithDetails d in detections)
        {
            string risk = GetRiskLevel(d.label);

            if (risk == "High")
            {
                anyHigh = true;
            }
            else if (risk == "Moderate")
            {
                anyModerate = true;
            }
        }

        if (anyHigh)
        {
            return "High";
        }

        if (anyModerate)
        {
            return "Moderate";
        }

        return "Low";
    }

    // Returns (background, text) colors for a risk badge / pill of the given level.
    static (Color bg, Color text) RiskBadgeColors(string risk)
    {
        return risk switch
        {
            "High" => (C_HIGH_BADGE_BG, C_HIGH_BADGE_TEXT),
            "Moderate" => (C_MOD_BADGE_BG, C_MOD_BADGE_TEXT),
            _ => (C_LOW_BADGE_BG, C_LOW_BADGE_TEXT)
        };
    }

    static string GetIconForLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return "🦟";
        }

        label = label.ToLowerInvariant();

        if (label.Contains("birdbath")) return "🐦";
        if (label.Contains("bucket")) return "🪣";
        if (label.Contains("tire")) return "🛞";
        if (label.Contains("trashcan")) return "🗑";
        if (label.Contains("treehole")) return "🌳";
        if (label.Contains("wateringcan")) return "💧";
        if (label.Contains("wheelbarrow")) return "🛒";
        if (label.Contains("pot")) return "🪴";
        if (label.Contains("waterlettuce")) return "🌿";
        if (label.Contains("waterhyacinth")) return "🌿";
        if (label.Contains("bromiliad")) return "🌱";

        return "🦟";
    }

    static void LoadMockData(out ScanReport report, out List<DetectionWithDetails> detections)
    {
        report = new ScanReport
        {
            id = -1,
            scanned_at = new DateTime(2026, 6, 7).ToString("o"),
            duration_seconds = 47,
            total_objects_detected = 3,
            notes = "[UI debug mock — not a real scan]"
        };

        detections = new List<DetectionWithDetails>
        {
            new DetectionWithDetails
            {
                detection_id = 1,
                report_id = -1,
                display_name = "Bucket",
                label = "ss_bucket",
                object_description = "Buckets can collect rainwater and become mosquito breeding sites when left outside.",
                mitigation_description = "Empty the bucket after rain\nStore it upside down\nKeep it covered when not in use",
                screenshot_path = "",
                detected_at = DateTime.Now.ToString("o")
            },
            new DetectionWithDetails
            {
                detection_id = 2,
                report_id = -1,
                display_name = "Tire",
                label = "ss_tire",
                object_description = "Tires can trap rainwater and are one of the most common outdoor mosquito breeding sites.",
                mitigation_description = "Drain all standing water\nStore tires indoors or under cover\nDispose of unused tires properly",
                screenshot_path = "",
                detected_at = DateTime.Now.ToString("o")
            },
            new DetectionWithDetails
            {
                detection_id = 3,
                report_id = -1,
                display_name = "Bird Bath",
                label = "ss_birdbath",
                object_description = "Bird baths can hold standing water and become mosquito breeding sites if the water is not changed regularly.",
                mitigation_description = "Empty and scrub the bird bath regularly\nChange the water at least once a week\nKeep the basin clean to prevent mosquito larvae",
                screenshot_path = "",
                detected_at = DateTime.Now.ToString("o")
            }
        };
    }
}