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

    GameObject _reportListPanel;
    GameObject _itemDetailPanel;

    TextMeshProUGUI _listReportSubtitle;
    Transform _listItemsContainer;
    TextMeshProUGUI _listEmptyMessage;
    TextMeshProUGUI _listScanDate;
    TextMeshProUGUI _listScanDuration;
    TextMeshProUGUI _listScanItems;
    RectTransform _listRiskFill;
    TextMeshProUGUI _listRiskBadgeText;
    Image _listRiskBadgeBg;
    GameObject _listRiskSummaryCard;
    GameObject _listScanInfoCard;

    TextMeshProUGUI _detailTitle;
    TextMeshProUGUI _detailSubtitle;
    TextMeshProUGUI _detailCounter;
    TextMeshProUGUI _detailWhyRisk;
    TextMeshProUGUI _detailWhatToDo;
    RectTransform _detailConfFill;
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
            : primary + "\n<size=12><color=#A7C4AE>" + secondary + "</color></size>";

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

        string overallRisk = ComputeOverallRisk(_currentDetections);
        Color riskBadgeColor = RiskColor(overallRisk);
        float riskRatio = overallRisk switch
        {
            "High" => 0.85f,
            "Moderate" => 0.55f,
            _ => 0.25f
        };

        _listRiskBadgeText.text = overallRisk;
        _listRiskBadgeBg.color = riskBadgeColor;
        _listRiskFill.sizeDelta = new Vector2(riskRatio * 260f, 0);

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
        _detailCounter.text = "Detection " + (index + 1) + " of " + _currentDetections.Count;

        _detailWhyRisk.text = string.IsNullOrWhiteSpace(d.object_description)
            ? "No risk description available for this item."
            : d.object_description;

        if (!string.IsNullOrWhiteSpace(d.mitigation_description))
        {
            string[] lines = d.mitigation_description.Split(
                new[] { '\n', ';' },
                StringSplitOptions.RemoveEmptyEntries
            );

            var sb = new System.Text.StringBuilder();
            int n = 1;

            foreach (string line in lines)
            {
                string trimmed = line.Trim();

                if (trimmed.Length > 0)
                {
                    sb.AppendLine(n + ". " + trimmed);
                    n++;
                }
            }

            _detailWhatToDo.text = sb.ToString().TrimEnd();
        }
        else
        {
            _detailWhatToDo.text = "No mitigation information available for this item.";
        }

        string risk = GetRiskLevel(d.label);

        _detailRiskBadgeText.text = risk + " risk";
        _detailRiskBadgeBg.color = RiskColor(risk);
        _detailConfFill.sizeDelta = new Vector2(PLACEHOLDER_CONFIDENCE * 200f, 0);
    }

    void BuildReportListPanel()
    {
        Transform canvasTransform = targetCanvas.transform;

        _reportListPanel = CreateFullScreenPanel("FullReportPanel_Built", canvasTransform, C_BG);
        RectTransform root = _reportListPanel.GetComponent<RectTransform>();

        Transform scroll = CreateScrollView("ReportScroll", root);
        RectTransform content = scroll.Find("Viewport/Content").GetComponent<RectTransform>();

        var hBlock = MakeLayoutBlock("Header", content, 135, C_BG);
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
            C_WHITE,
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
            14,
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

        MakeSectionLabel("RISK SUMMARY", content);

        _listRiskSummaryCard = MakeCard("RiskSummaryCard", content, 105);
        RectTransform riskRect = _listRiskSummaryCard.GetComponent<RectTransform>();

        MakeText(
            "OverallLabel",
            riskRect,
            "Overall risk level",
            14,
            C_SUBTEXT,
            FontStyles.Normal,
            new Vector2(0, 1),
            new Vector2(0, 1),
            new Vector2(18, -19),
            new Vector2(175, -2)
        );

        _listRiskBadgeBg = MakeBadge(
            "RiskBadge",
            riskRect,
            "Moderate",
            C_ORANGE,
            new Vector2(1, 1),
            new Vector2(-18, -14)
        );

        _listRiskBadgeText = _listRiskBadgeBg.GetComponentInChildren<TextMeshProUGUI>();

        var barBg = new GameObject("ProgBg", typeof(RectTransform), typeof(Image));
        barBg.transform.SetParent(riskRect, false);

        RectTransform barBgRect = barBg.GetComponent<RectTransform>();
        SetAnchors(
            barBgRect,
            new Vector2(0, 1),
            new Vector2(1, 1),
            new Vector2(18, -64),
            new Vector2(-18, -48)
        );

        barBg.GetComponent<Image>().color = C_PROG_BG;
        SetRounded(barBg.GetComponent<Image>(), 8);

        var barFill = new GameObject("ProgFill", typeof(RectTransform), typeof(Image));
        barFill.transform.SetParent(barBgRect, false);

        _listRiskFill = barFill.GetComponent<RectTransform>();
        SetAnchors(_listRiskFill, new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, Vector2.zero);
        _listRiskFill.sizeDelta = new Vector2(140f, 0);

        barFill.GetComponent<Image>().color = C_PROG_RISK;
        SetRounded(barFill.GetComponent<Image>(), 8);

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
        vlg.spacing = 12;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.padding = new RectOffset(20, 20, 0, 0);

        itemsHolder.GetComponent<ContentSizeFitter>().verticalFit =
            ContentSizeFitter.FitMode.PreferredSize;

        _listItemsContainer = itemsHolder.transform;

        MakeSectionLabel("SCAN INFO", content);

        _listScanInfoCard = MakeCard("ScanInfoCard", content, 120);
        RectTransform infoRect = _listScanInfoCard.GetComponent<RectTransform>();

        BuildInfoRow(infoRect, "Date", ref _listScanDate, -20);
        BuildInfoRow(infoRect, "Duration", ref _listScanDuration, -55);
        BuildInfoRow(infoRect, "Items found", ref _listScanItems, -90);

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

        var hBlock = MakeLayoutBlock("Header", content, 135, C_BG);
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
            C_WHITE,
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
            14,
            C_SUBTEXT,
            FontStyles.Normal,
            new Vector2(0, 1),
            new Vector2(1, 1),
            new Vector2(24, -127),
            new Vector2(-24, -100)
        );

        var imgCard = MakeCard("ImageCard", content, 170);
        RectTransform imgRect = imgCard.GetComponent<RectTransform>();

        _detailRiskBadgeBg = MakeBadge(
            "DetailRiskBadge",
            imgRect,
            "High risk",
            C_RED,
            new Vector2(0, 1),
            new Vector2(18, -14)
        );

        _detailRiskBadgeText = _detailRiskBadgeBg.GetComponentInChildren<TextMeshProUGUI>();

        _detailCounter = MakeText(
            "CounterText",
            imgRect,
            "",
            12,
            C_SUBTEXT,
            FontStyles.Normal,
            new Vector2(1, 0),
            new Vector2(1, 0),
            new Vector2(-140, 12),
            new Vector2(-18, 30)
        );

        MakeText(
            "ImgPlaceholder",
            imgRect,
            "🔍",
            50,
            C_WHITE,
            FontStyles.Normal,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(-24, 10),
            new Vector2(24, 60)
        );

        MakeSectionLabel("WHY IT'S A RISK", content);

        var whyCard = MakeAutoSizeCard("WhyRiskCard", content);
        RectTransform whyRect = whyCard.GetComponent<RectTransform>();

        _detailWhyRisk = MakeText(
            "WhyText",
            whyRect,
            "",
            14,
            C_MINT,
            FontStyles.Normal,
            new Vector2(0, 1),
            new Vector2(1, 0),
            new Vector2(18, -15),
            new Vector2(-18, 15)
        );

        _detailWhyRisk.enableWordWrapping = true;

        MakeSectionLabel("WHAT TO DO", content);

        var todoCard = MakeAutoSizeCard("WhatToDoCard", content);
        RectTransform todoRect = todoCard.GetComponent<RectTransform>();

        _detailWhatToDo = MakeText(
            "TodoText",
            todoRect,
            "",
            14,
            C_MINT,
            FontStyles.Normal,
            new Vector2(0, 1),
            new Vector2(1, 0),
            new Vector2(18, -15),
            new Vector2(-18, 15)
        );

        _detailWhatToDo.enableWordWrapping = true;

        MakeSectionLabel("DETECTION CONFIDENCE", content);

        var confCard = MakeCard("ConfidenceCard", content, 70);
        RectTransform confRect = confCard.GetComponent<RectTransform>();

        MakeText(
            "ModelLabel",
            confRect,
            "Model",
            14,
            C_SUBTEXT,
            FontStyles.Normal,
            new Vector2(0, 0.5f),
            new Vector2(0, 0.5f),
            new Vector2(18, -10),
            new Vector2(75, 10)
        );

        var confBg = new GameObject("ConfBg", typeof(RectTransform), typeof(Image));
        confBg.transform.SetParent(confRect, false);

        RectTransform confBgRect = confBg.GetComponent<RectTransform>();
        SetAnchors(
            confBgRect,
            new Vector2(0, 0.5f),
            new Vector2(1, 0.5f),
            new Vector2(85, -8),
            new Vector2(-80, 8)
        );

        confBg.GetComponent<Image>().color = C_PROG_BG;
        SetRounded(confBg.GetComponent<Image>(), 8);

        var confFill = new GameObject("ConfFill", typeof(RectTransform), typeof(Image));
        confFill.transform.SetParent(confBgRect, false);

        _detailConfFill = confFill.GetComponent<RectTransform>();
        SetAnchors(_detailConfFill, new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, Vector2.zero);
        _detailConfFill.sizeDelta = new Vector2(PLACEHOLDER_CONFIDENCE * 200f, 0);

        confFill.GetComponent<Image>().color = C_PROG_CONF;
        SetRounded(confFill.GetComponent<Image>(), 8);

        MakeText(
            "PercLabel",
            confRect,
            ((int)(PLACEHOLDER_CONFIDENCE * 100)) + "%",
            14,
            C_WHITE,
            FontStyles.Bold,
            new Vector2(1, 0.5f),
            new Vector2(1, 0.5f),
            new Vector2(-58, -10),
            new Vector2(-18, 10)
        );

        var navRow = new GameObject(
            "NavRow",
            typeof(RectTransform),
            typeof(HorizontalLayoutGroup),
            typeof(LayoutElement)
        );

        navRow.transform.SetParent(content, false);
        navRow.GetComponent<LayoutElement>().preferredHeight = 60;

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
            C_WHITE,
            14,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            new Vector2(0, 42),
            C_CARD_LIGHT
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
            C_BG,
            14,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            new Vector2(0, 42),
            C_TEAL_BTN
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
        card.GetComponent<LayoutElement>().preferredHeight = 78;

        Image img = card.GetComponent<Image>();
        img.color = C_CARD;
        SetRounded(img, 16);

        RectTransform rect = card.GetComponent<RectTransform>();

        var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        icon.transform.SetParent(rect, false);

        RectTransform iRect = icon.GetComponent<RectTransform>();
        iRect.anchorMin = new Vector2(0, 0.5f);
        iRect.anchorMax = new Vector2(0, 0.5f);
        iRect.pivot = new Vector2(0, 0.5f);
        iRect.anchoredPosition = new Vector2(16, 0);
        iRect.sizeDelta = new Vector2(48, 48);

        icon.GetComponent<Image>().color = C_CARD_LIGHT;
        SetRounded(icon.GetComponent<Image>(), 24);

        var emojiGo = new GameObject("Emoji", typeof(RectTransform), typeof(TextMeshProUGUI));
        emojiGo.transform.SetParent(icon.transform, false);

        RectTransform eRect = emojiGo.GetComponent<RectTransform>();
        eRect.anchorMin = Vector2.zero;
        eRect.anchorMax = Vector2.one;
        eRect.sizeDelta = Vector2.zero;

        TextMeshProUGUI eTM = emojiGo.GetComponent<TextMeshProUGUI>();
        eTM.text = GetIconForLabel(d.label);
        eTM.fontSize = 21;
        eTM.alignment = TextAlignmentOptions.Center;

        var nameGo = new GameObject("ItemName", typeof(RectTransform), typeof(TextMeshProUGUI));
        nameGo.transform.SetParent(rect, false);

        RectTransform nRect = nameGo.GetComponent<RectTransform>();
        nRect.anchorMin = new Vector2(0, 1);
        nRect.anchorMax = new Vector2(1, 1);
        nRect.pivot = new Vector2(0, 1);
        nRect.anchoredPosition = new Vector2(75, -13);
        nRect.sizeDelta = new Vector2(-155, 26);

        TextMeshProUGUI nTM = nameGo.GetComponent<TextMeshProUGUI>();
        nTM.text = string.IsNullOrWhiteSpace(d.display_name) ? d.label : d.display_name;
        nTM.fontSize = 16;
        nTM.color = C_WHITE;
        nTM.fontStyle = FontStyles.Bold;

        var subGo = new GameObject("ItemSub", typeof(RectTransform), typeof(TextMeshProUGUI));
        subGo.transform.SetParent(rect, false);

        RectTransform sRect = subGo.GetComponent<RectTransform>();
        sRect.anchorMin = new Vector2(0, 0);
        sRect.anchorMax = new Vector2(1, 0);
        sRect.pivot = new Vector2(0, 0);
        sRect.anchoredPosition = new Vector2(75, 15);
        sRect.sizeDelta = new Vector2(-155, 20);

        TextMeshProUGUI sTM = subGo.GetComponent<TextMeshProUGUI>();
        sTM.text = CountInstances(d.label) + " detected · " + GetRiskLevel(d.label) + " risk";
        sTM.fontSize = 12;
        sTM.color = C_SUBTEXT;

        MakeBadge(
            "RiskBadge",
            rect,
            GetRiskLevel(d.label),
            RiskColor(GetRiskLevel(d.label)),
            new Vector2(1, 0.5f),
            new Vector2(-46, 0)
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
        button.targetGraphic = img;
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

        Image img = go.GetComponent<Image>();
        img.color = C_CARD;
        SetRounded(img, 16);

        go.GetComponent<LayoutElement>().preferredHeight = fixedHeight;

        return go;
    }

    static GameObject MakeAutoSizeCard(string name, Transform parent)
    {
        var go = new GameObject(
            name,
            typeof(RectTransform),
            typeof(Image),
            typeof(LayoutElement),
            typeof(ContentSizeFitter)
        );

        go.transform.SetParent(parent, false);

        Image img = go.GetComponent<Image>();
        img.color = C_CARD;
        SetRounded(img, 16);

        go.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        go.GetComponent<LayoutElement>().minHeight = 65;

        return go;
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
        go.GetComponent<LayoutElement>().preferredHeight = 32;

        TextMeshProUGUI tm = go.GetComponent<TextMeshProUGUI>();
        tm.text = text;
        tm.fontSize = 12;
        tm.color = C_SUBTEXT;
        tm.fontStyle = FontStyles.Bold;
        tm.margin = new Vector4(20, 10, 20, 0);
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
        Vector2 anchor,
        Vector2 anchoredPos
    )
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = new Vector2(92, 26);

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
        tm.fontSize = 12;
        tm.color = Color.white;
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
        vlg.spacing = 13;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.padding = new RectOffset(16, 16, 20, 20);

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
            14,
            C_SUBTEXT,
            FontStyles.Normal,
            new Vector2(0, 1),
            new Vector2(0, 1),
            new Vector2(18, yOffset - 16),
            new Vector2(135, yOffset)
        );

        valueRef = MakeText(
            labelText + "_V",
            parent,
            "—",
            14,
            C_WHITE,
            FontStyles.Bold,
            new Vector2(1, 1),
            new Vector2(1, 1),
            new Vector2(-140, yOffset - 16),
            new Vector2(-18, yOffset)
        );

        valueRef.alignment = TextAlignmentOptions.Right;
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

    static string GetRiskLevel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return "Low";
        }

        label = label.ToLowerInvariant();

        // High-risk standing-water containers from current YAML labels.
        if (
            label.Contains("ss_tire") ||
            label.Contains("ss_bucket") ||
            label.Contains("ss_trashcan") ||
            label.Contains("ss_wheelbarrow")
        )
        {
            return "High";
        }

        // Moderate-risk yard/water-holding objects from current YAML labels.
        if (
            label.Contains("ss_birdbath") ||
            label.Contains("ss_pot") ||
            label.Contains("ss_wateringcan") ||
            label.Contains("ss_treehole") ||
            label.Contains("ss_bromiliad") ||
            label.Contains("ss_waterhyacinth") ||
            label.Contains("ss_waterlettuce")
        )
        {
            return "Moderate";
        }

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

    static Color RiskColor(string risk)
    {
        return risk switch
        {
            "High" => C_RED,
            "Moderate" => C_ORANGE,
            _ => C_GREEN_BADGE
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