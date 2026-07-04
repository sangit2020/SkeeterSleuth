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
    static readonly Color C_ICON_BG        = HexColor("#2E583D"); // Icon tile background; matched to the icon PNG background
    static readonly Color C_ICON_BORDER    = HexColor("#2E583D"); // Icon tile border; matched to icon PNG background
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

    // Runtime rounded-rectangle fallback. This makes cards/badges round even if
    // the project does not have a Resources/UI/Rounded sprite assigned.
    static readonly Dictionary<int, Sprite> _runtimeRoundedSprites = new Dictionary<int, Sprite>();

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
    Image _listRiskBarFill;
    GameObject _listRiskSummaryCard;
    GameObject _listScanInfoCard;

    TextMeshProUGUI _detailTitle;
    TextMeshProUGUI _detailSubtitle;
    TextMeshProUGUI _detailWhyRisk;
    TextMeshProUGUI _detailWhatToDo;
    Image _detailRiskBadgeBg;
    TextMeshProUGUI _detailRiskBadgeText;
    Image _detailObjectIconImage;
    GameObject _detailObjectIconImageObject;
    TextMeshProUGUI _detailObjectIconFallback;

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

        if (_listRiskSummaryCard != null)
        {
            _listRiskSummaryCard.SetActive(false);
        }

        if (_listScanInfoCard != null)
        {
            _listScanInfoCard.SetActive(false);
        }

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

        if (_listRiskSummaryCard != null)
        {
            _listRiskSummaryCard.SetActive(true);
        }

        // Scan Info was intentionally removed from the report summary screen.
        // The header already contains the date, duration, and item count.

        DateTime dt = ParseDate(_currentReport.scanned_at);
        int mins = _currentReport.duration_seconds / 60;
        int secs = _currentReport.duration_seconds % 60;
        string dur = mins > 0 ? mins + "m " + secs + "s" : secs + " sec";

        _listReportSubtitle.text =
            dt.ToString("MMMM d") + " • " + dur + " • " + _currentReport.total_objects_detected + " items found";

        // ── Risk summary: badge + progress-style risk bar ──
        string overallRisk = ComputeOverallRisk(_currentDetections);
        var (badgeBg, badgeText) = RiskBadgeColors(overallRisk);

        _listRiskBadgeText.text = overallRisk;
        _listRiskBadgeBg.color = badgeBg;
        _listRiskBadgeText.color = badgeText;

        float fillAmount = overallRisk switch
        {
            "High" => 1.0f,
            "Moderate" => 0.6667f,
            _ => 0.3334f
        };

        if (_listRiskBarFill != null)
        {
            _listRiskBarFill.color = badgeText;

            RectTransform fillRect = _listRiskBarFill.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(fillAmount, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
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
            List<string> steps = SplitMitigationSteps(d.mitigation_description);

            if (steps.Count > 0)
            {
                var sb = new System.Text.StringBuilder();

                for (int i = 0; i < steps.Count; i++)
                {
                    // Keep the fix text white, but make the bullet itself green
                    // to match the reference style more closely.
                    sb.Append("<color=#5ED9C0>•</color> ").Append(steps[i]);

                    if (i < steps.Count - 1)
                    {
                        sb.Append("\n");
                    }
                }

                _detailWhatToDo.text = sb.ToString();
            }
            else
            {
                _detailWhatToDo.text = "No mitigation information available for this item.";
            }
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


        if (_detailObjectIconImageObject != null && _detailObjectIconImage != null && _detailObjectIconFallback != null)
        {
            Sprite objectSprite = LoadObjectIcon(d.label);

            if (objectSprite != null)
            {
                _detailObjectIconImage.sprite = objectSprite;
                _detailObjectIconImageObject.SetActive(true);
                _detailObjectIconFallback.gameObject.SetActive(false);
            }
            else
            {
                _detailObjectIconImageObject.SetActive(false);
                _detailObjectIconFallback.text = GetIconFallbackText(d.label);
                _detailObjectIconFallback.gameObject.SetActive(true);
            }
        }
    }


    // Full-width fixed header used by report screens.
    // Built outside the ScrollRect so the green header reaches the screen edges
    // instead of inheriting the scroll content's side padding.
    void MakeFixedHeader(
        string name,
        RectTransform root,
        string backLabel,
        Action backAction,
        string titleText,
        out TextMeshProUGUI titleRef,
        out TextMeshProUGUI subtitleRef
    )
    {
        var hBlock = new GameObject(name, typeof(RectTransform), typeof(Image));
        hBlock.transform.SetParent(root, false);

        RectTransform hRect = hBlock.GetComponent<RectTransform>();
        hRect.anchorMin = new Vector2(0, 1);
        hRect.anchorMax = new Vector2(1, 1);
        hRect.pivot = new Vector2(0.5f, 1f);
        hRect.offsetMin = new Vector2(0, -135);
        hRect.offsetMax = new Vector2(0, 0);

        hBlock.GetComponent<Image>().color = C_HEADER_BG;

        float backWidth = backLabel.Length > 8 ? 135f : 95f;

        var backBtn = MakeButton(
            "BackBtn",
            hRect,
            backLabel,
            C_SUBTEXT,
            15,
            new Vector2(0, 1),
            new Vector2(0, 1),
            // Moved slightly down while keeping both report-screen back controls aligned.
            // This gives the back label more clearance from mobile status bars / safe areas.
            new Vector2(10, -58),
            new Vector2(10 + backWidth, -28),
            Color.clear
        );

        // Keep all report-screen back controls visually aligned.
        // The first report page uses "← Back" and the detail page uses "← Full report";
        // left-aligning the label makes both start in the exact same place.
        TextMeshProUGUI backBtnLabel = backBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (backBtnLabel != null)
        {
            backBtnLabel.alignment = TextAlignmentOptions.MidlineLeft;
            backBtnLabel.fontStyle = FontStyles.Normal;

            RectTransform backLabelRect = backBtnLabel.GetComponent<RectTransform>();
            if (backLabelRect != null)
            {
                backLabelRect.anchorMin = Vector2.zero;
                backLabelRect.anchorMax = Vector2.one;
                backLabelRect.offsetMin = Vector2.zero;
                backLabelRect.offsetMax = Vector2.zero;
            }
        }

        backBtn.GetComponent<Button>().onClick.AddListener(() =>
        {
            backAction?.Invoke();
        });

        titleRef = MakeText(
            "TitleText",
            hRect,
            titleText,
            31,
            C_TEXT_PRIMARY,
            FontStyles.Bold,
            new Vector2(0, 1),
            new Vector2(1, 1),
            new Vector2(24, -96),
            new Vector2(-24, -58)
        );

        subtitleRef = MakeText(
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
    }

    void BuildReportListPanel()
    {
        Transform canvasTransform = targetCanvas.transform;

        _reportListPanel = CreateFullScreenPanel("FullReportPanel_Built", canvasTransform, C_BG);
        RectTransform root = _reportListPanel.GetComponent<RectTransform>();

        TextMeshProUGUI unusedTitle;
        MakeFixedHeader(
            "Header",
            root,
            "← Back",
            () =>
            {
                _reportListPanel.SetActive(false);
            },
            "Full report",
            out unusedTitle,
            out _listReportSubtitle
        );

        Transform scroll = CreateScrollView("ReportScroll", root);
        RectTransform scrollRect = scroll.GetComponent<RectTransform>();
        scrollRect.offsetMax = new Vector2(0, -135);

        RectTransform content = scroll.Find("Viewport/Content").GetComponent<RectTransform>();

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

        // Wrap the risk card in a padded holder so it lines up with the detected item cards.
        // The detected items container has 20px left/right padding, so this wrapper gives
        // Risk Summary the same visual width without changing the spacing of the whole page.
        var riskCardWrapper = new GameObject(
            "RiskSummaryCardWrapper",
            typeof(RectTransform),
            typeof(LayoutElement),
            typeof(HorizontalLayoutGroup)
        );

        riskCardWrapper.transform.SetParent(content, false);
        riskCardWrapper.GetComponent<LayoutElement>().preferredHeight = 82;

        var riskWrapperLayout = riskCardWrapper.GetComponent<HorizontalLayoutGroup>();
        riskWrapperLayout.padding = new RectOffset(20, 20, 0, 0);
        riskWrapperLayout.childControlWidth = true;
        riskWrapperLayout.childControlHeight = true;
        riskWrapperLayout.childForceExpandWidth = true;
        riskWrapperLayout.childForceExpandHeight = true;

        _listRiskSummaryCard = riskCardWrapper;

        var riskCard = MakeCard("RiskSummaryCard", riskCardWrapper.transform, 82);
        riskCard.GetComponent<LayoutElement>().flexibleWidth = 1;
        RectTransform riskRect = riskCard.GetComponent<RectTransform>();

        var overallLabel = MakeText(
            "OverallLabel",
            riskRect,
            "Overall risk level",
            13,
            C_TEXT_PRIMARY,
            FontStyles.Bold,
            new Vector2(0, 1),
            new Vector2(0, 1),
            new Vector2(18, -30),
            new Vector2(230, -8)
        );
        overallLabel.alignment = TextAlignmentOptions.Left;

        _listRiskBadgeBg = MakeBadge(
            "RiskBadge",
            riskRect,
            "Moderate",
            C_MOD_BADGE_BG,
            C_MOD_BADGE_TEXT,
            new Vector2(1, 1),
            new Vector2(-18, -12),
            new Vector2(104, 24),
            12
        );

        _listRiskBadgeText = _listRiskBadgeBg.GetComponentInChildren<TextMeshProUGUI>();

        // Progress risk bar:
        // Low = 1/3 filled, Moderate = 2/3 filled, High = full.
        var riskBarTrack = new GameObject(
            "RiskBarTrack",
            typeof(RectTransform),
            typeof(Image)
        );

        riskBarTrack.transform.SetParent(riskRect, false);

        RectTransform trackRect = riskBarTrack.GetComponent<RectTransform>();
        SetAnchors(
            trackRect,
            new Vector2(0, 1),
            new Vector2(1, 1),
            new Vector2(18, -58),
            new Vector2(-18, -48)
        );

        Image trackImg = riskBarTrack.GetComponent<Image>();
        trackImg.color = C_RISK_BAR_BG;
        SetRounded(trackImg, 5);

        var riskBarFill = new GameObject(
            "RiskBarFill",
            typeof(RectTransform),
            typeof(Image)
        );

        riskBarFill.transform.SetParent(riskBarTrack.transform, false);

        RectTransform fillRect = riskBarFill.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(0.6667f, 1f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        _listRiskBarFill = riskBarFill.GetComponent<Image>();
        _listRiskBarFill.color = C_MOD_BADGE_TEXT;
        SetRounded(_listRiskBarFill, 5);

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

        var spacer = new GameObject("BottomSpacer", typeof(RectTransform), typeof(LayoutElement));
        spacer.transform.SetParent(content, false);
        spacer.GetComponent<LayoutElement>().preferredHeight = 55;
    }

    void BuildItemDetailPanel()
    {
        Transform canvasTransform = targetCanvas.transform;

        _itemDetailPanel = CreateFullScreenPanel("ItemDetailPanel_Built", canvasTransform, C_BG);
        RectTransform root = _itemDetailPanel.GetComponent<RectTransform>();

        MakeFixedHeader(
            "Header",
            root,
            "← Full report",
            () =>
            {
                _itemDetailPanel.SetActive(false);
                _reportListPanel.SetActive(true);
            },
            "",
            out _detailTitle,
            out _detailSubtitle
        );

        Transform scroll = CreateScrollView("DetailScroll", root);
        RectTransform scrollRect = scroll.GetComponent<RectTransform>();
        scrollRect.offsetMax = new Vector2(0, -135);

        // Leave room at the bottom for the fixed Prev / Next navigation bar.
        // This keeps the buttons visible while the item details scroll above them.
        scrollRect.offsetMin = new Vector2(0, 104);

        RectTransform content = scroll.Find("Viewport/Content").GetComponent<RectTransform>();

        // ─────────────────────────────────────────────────────────────────
        // ITEM SUMMARY / RISK LEVEL
        // ─────────────────────────────────────────────────────────────────
        // Two-card summary row:
        //   • left: large #2E583D object icon tile so the PNG background blends in
        //   • right: clean risk-level tile with only the risk label and badge
        var summaryRow = new GameObject(
            "ItemSummaryRow",
            typeof(RectTransform),
            typeof(LayoutElement),
            typeof(HorizontalLayoutGroup)
        );

        summaryRow.transform.SetParent(content, false);
        summaryRow.GetComponent<LayoutElement>().preferredHeight = 128;

        var summaryLayout = summaryRow.GetComponent<HorizontalLayoutGroup>();
        summaryLayout.spacing = 12;
        summaryLayout.padding = new RectOffset(0, 0, 0, 0);
        summaryLayout.childControlWidth = true;
        summaryLayout.childControlHeight = true;
        summaryLayout.childForceExpandWidth = false;
        summaryLayout.childForceExpandHeight = true;

        // Large icon tile. It intentionally uses #2E583D to match the icon PNG background.
        var detailIcon = new GameObject(
            "DetailObjectIconTile",
            typeof(RectTransform),
            typeof(Image),
            typeof(LayoutElement)
        );

        detailIcon.transform.SetParent(summaryRow.transform, false);

        var detailIconLayout = detailIcon.GetComponent<LayoutElement>();
        detailIconLayout.preferredWidth = 128;
        detailIconLayout.preferredHeight = 128;
        detailIconLayout.flexibleWidth = 0;

        Image detailIconBg = detailIcon.GetComponent<Image>();
        detailIconBg.color = C_ICON_BG;
        detailIconBg.raycastTarget = false;
        SetRounded(detailIconBg, 18);

        var detailSpriteGo = new GameObject("DetailObjectIconImage", typeof(RectTransform), typeof(Image));
        detailSpriteGo.transform.SetParent(detailIcon.transform, false);

        RectTransform detailSpriteRect = detailSpriteGo.GetComponent<RectTransform>();
        SetAnchors(
            detailSpriteRect,
            Vector2.zero,
            Vector2.one,
            new Vector2(3f, 3f),
            new Vector2(-3f, -3f)
        );

        _detailObjectIconImageObject = detailSpriteGo;
        _detailObjectIconImage = detailSpriteGo.GetComponent<Image>();
        _detailObjectIconImage.color = Color.white;
        _detailObjectIconImage.preserveAspect = true;
        _detailObjectIconImage.raycastTarget = false;

        var detailFallbackGo = new GameObject("DetailObjectIconFallback", typeof(RectTransform), typeof(TextMeshProUGUI));
        detailFallbackGo.transform.SetParent(detailIcon.transform, false);

        RectTransform detailFallbackRect = detailFallbackGo.GetComponent<RectTransform>();
        detailFallbackRect.anchorMin = Vector2.zero;
        detailFallbackRect.anchorMax = Vector2.one;
        detailFallbackRect.sizeDelta = Vector2.zero;

        _detailObjectIconFallback = detailFallbackGo.GetComponent<TextMeshProUGUI>();
        _detailObjectIconFallback.text = "?";
        _detailObjectIconFallback.fontSize = 22;
        _detailObjectIconFallback.color = C_SECTION_HEADER;
        _detailObjectIconFallback.fontStyle = FontStyles.Bold;
        _detailObjectIconFallback.alignment = TextAlignmentOptions.Center;
        _detailObjectIconFallback.raycastTarget = false;
        detailFallbackGo.SetActive(false);

        // Separate risk tile. This keeps the right side from feeling like empty space
        // while keeping the content simple: just the label and the risk badge.
        var riskTile = MakeCard("DetailRiskTile", summaryRow.transform, 128);
        riskTile.GetComponent<LayoutElement>().flexibleWidth = 1;
        RectTransform riskTileRect = riskTile.GetComponent<RectTransform>();

        var riskLabel = MakeText(
            "DetailRiskLabel",
            riskTileRect,
            "RISK LEVEL",
            12,
            C_SECTION_HEADER,
            FontStyles.Bold,
            new Vector2(0, 1),
            new Vector2(1, 1),
            new Vector2(16, -42),
            new Vector2(-16, -20)
        );
        riskLabel.alignment = TextAlignmentOptions.Center;
        riskLabel.characterSpacing = 2f;

        _detailRiskBadgeBg = MakeBadge(
            "DetailRiskBadge",
            riskTileRect,
            "High risk",
            C_HIGH_BADGE_BG,
            C_HIGH_BADGE_TEXT,
            new Vector2(0.5f, 0.5f),
            new Vector2(0, -10),
            new Vector2(150, 36),
            13
        );

        _detailRiskBadgeText = _detailRiskBadgeBg.GetComponentInChildren<TextMeshProUGUI>();

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
        _detailWhatToDo.lineSpacing = 12;
        _detailWhatToDo.richText = true;

        var spacer = new GameObject("BottomSpacer", typeof(RectTransform), typeof(LayoutElement));
        spacer.transform.SetParent(content, false);
        spacer.GetComponent<LayoutElement>().preferredHeight = 24;

        // ─────────────────────────────────────────────────────────────────
        // FIXED PREV / NEXT NAVIGATION
        // ─────────────────────────────────────────────────────────────────
        // Built outside the ScrollRect so these controls stay pinned near the
        // bottom of the screen while the item details scroll above them.
        var navRow = new GameObject(
            "FixedNavRow",
            typeof(RectTransform),
            typeof(HorizontalLayoutGroup)
        );

        navRow.transform.SetParent(root, false);

        RectTransform navRect = navRow.GetComponent<RectTransform>();
        SetAnchors(
            navRect,
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(0, 28),
            new Vector2(0, 92)
        );

        var hlg = navRow.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 12;
        hlg.padding = new RectOffset(36, 36, 8, 8);
        hlg.childControlWidth = true;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = false;

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

        // Match the original green used behind the imported object icons (#2E583D).
        // Shape, size, and outline stay the same.
        StyleButtonAsCard(prevBtn, C_ICON_BG, C_TEXT_PRIMARY);

        prevBtn.GetComponent<Button>().onClick.AddListener(() =>
        {
            if (_detailIndex > 0)
            {
                ShowItemDetail(_detailIndex - 1);
            }
        });

        var nextBtn = MakeButton(
            "NextBtn",
            navRect,
            "Next Item →",
            C_BTN_FILLED_BG,
            15,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            new Vector2(0, 48),
            C_CARD_BORDER
        );

        // Keep the green card-style outline, but bring back the original teal fill
        // so Next reads as the primary action again.
        StyleButtonAsCard(nextBtn, C_BTN_FILLED_BG, C_BTN_FILLED_TEXT);

        nextBtn.GetComponent<Button>().onClick.AddListener(() =>
        {
            if (_detailIndex < _currentDetections.Count - 1)
            {
                ShowItemDetail(_detailIndex + 1);
            }
        });
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
        // Slightly larger icon badge with a visible green ring so the imported
        // object art feels integrated with the card instead of pasted on top.
        iRect.anchoredPosition = new Vector2(14, 0);
        iRect.sizeDelta = new Vector2(56, 56);

        Image iconBorder = icon.GetComponent<Image>();
        iconBorder.color = C_ICON_BORDER;
        iconBorder.raycastTarget = false;
        SetRounded(iconBorder, 28);

        // Clip icon art to the circular badge shape. The mask still shows the
        // green border graphic, so the icon reads as part of the app UI.
        Mask iconMask = icon.AddComponent<Mask>();
        iconMask.showMaskGraphic = true;

        Image iconFill = AddCardFill(icon.transform, 2f, C_ICON_BG);
        iconFill.raycastTarget = false;

        // Load the actual object icon from Assets/Resources/Icons.
        // These paths use the current file names you imported, without the .png extension.
        Sprite objectSprite = LoadObjectIcon(d.label);

        if (objectSprite != null)
        {
            var spriteGo = new GameObject("ObjectIcon", typeof(RectTransform), typeof(Image));
            spriteGo.transform.SetParent(icon.transform, false);

            RectTransform spriteRect = spriteGo.GetComponent<RectTransform>();
            SetAnchors(
                spriteRect,
                Vector2.zero,
                Vector2.one,
                new Vector2(7f, 7f),
                new Vector2(-7f, -7f)
            );

            Image spriteImg = spriteGo.GetComponent<Image>();
            spriteImg.sprite = objectSprite;

            // Leave the sprite untinted so the object art keeps its original colors.
            // The icon tile background above uses #2E583D, which matches the edited
            // icon PNG background so any remaining square blends into the circle.
            spriteImg.color = Color.white;
            spriteImg.preserveAspect = true;
            spriteImg.raycastTarget = false;
        }
        else
        {
            // Text fallback so the UI still works if an icon is missing or not imported as a Sprite.
            var fallbackGo = new GameObject("IconFallback", typeof(RectTransform), typeof(TextMeshProUGUI));
            fallbackGo.transform.SetParent(icon.transform, false);

            RectTransform fallbackRect = fallbackGo.GetComponent<RectTransform>();
            fallbackRect.anchorMin = Vector2.zero;
            fallbackRect.anchorMax = Vector2.one;
            fallbackRect.sizeDelta = Vector2.zero;

            TextMeshProUGUI fallbackText = fallbackGo.GetComponent<TextMeshProUGUI>();
            fallbackText.text = GetIconFallbackText(d.label);
            fallbackText.fontSize = 14;
            fallbackText.color = C_SECTION_HEADER;
            fallbackText.fontStyle = FontStyles.Bold;
            fallbackText.alignment = TextAlignmentOptions.Center;
            fallbackText.raycastTarget = false;
        }

        var nameGo = new GameObject("ItemName", typeof(RectTransform), typeof(TextMeshProUGUI));
        nameGo.transform.SetParent(rect, false);

        RectTransform nRect = nameGo.GetComponent<RectTransform>();
        nRect.anchorMin = new Vector2(0, 1);
        nRect.anchorMax = new Vector2(1, 1);
        nRect.pivot = new Vector2(0, 1);
        nRect.anchoredPosition = new Vector2(86, -14);
        nRect.sizeDelta = new Vector2(-166, 26);

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
        sRect.anchoredPosition = new Vector2(86, 16);
        sRect.sizeDelta = new Vector2(-166, 20);

        TextMeshProUGUI sTM = subGo.GetComponent<TextMeshProUGUI>();
        sTM.text = CountInstances(d.label) + " detected";
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
            typeof(Outline),
            typeof(LayoutElement),
            typeof(ContentSizeFitter),
            typeof(VerticalLayoutGroup)
        );

        go.transform.SetParent(parent, false);

        Image img = go.GetComponent<Image>();
        img.color = C_CARD;
        SetRounded(img, 16);

        // Match the Risk Level card style by giving auto-size text cards
        // the same green outline treatment without changing their layout.
        Outline outline = go.GetComponent<Outline>();
        outline.effectColor = C_CARD_BORDER;
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        outline.useGraphicAlpha = false;

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
        return AddInsetFill(cardTransform, "Fill", inset, fillColor, 14.5f);
    }

    static Image AddInsetFill(Transform targetTransform, string name, float inset, Color fillColor, float radius)
    {
        var fill = new GameObject(name, typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(targetTransform, false);

        RectTransform fRect = fill.GetComponent<RectTransform>();
        SetAnchors(fRect, Vector2.zero, Vector2.one, new Vector2(inset, inset), new Vector2(-inset, -inset));

        Image img = fill.GetComponent<Image>();
        img.color = fillColor;
        SetRounded(img, radius);

        return img;
    }

    static void StyleButtonAsCard(GameObject buttonGo, Color fillColor, Color textColor)
    {
        if (buttonGo == null)
        {
            return;
        }

        Image border = buttonGo.GetComponent<Image>();
        if (border != null)
        {
            border.color = C_CARD_BORDER;
            SetRounded(border, 12);
        }

        Image fill = AddInsetFill(buttonGo.transform, "ButtonFill", 1.5f, fillColor, 10.5f);
        fill.raycastTarget = true;
        fill.transform.SetAsFirstSibling();

        Button btn = buttonGo.GetComponent<Button>();
        if (btn != null)
        {
            btn.targetGraphic = fill;
        }

        TextMeshProUGUI label = buttonGo.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null)
        {
            label.color = textColor;
            label.fontStyle = FontStyles.Normal;
        }
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
        if (img == null)
        {
            return;
        }

        Sprite sprite = Resources.Load<Sprite>("UI/Rounded");

        if (sprite == null)
        {
            int roundedRadius = Mathf.Max(1, Mathf.RoundToInt(radius));

            if (!_runtimeRoundedSprites.TryGetValue(roundedRadius, out sprite))
            {
                sprite = CreateRuntimeRoundedSprite(64, roundedRadius);
                _runtimeRoundedSprites[roundedRadius] = sprite;
            }
        }

        if (sprite != null)
        {
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
        }
    }

    static Sprite CreateRuntimeRoundedSprite(int size, int radius)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "RuntimeRoundedRect_" + radius;
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;

        Color solid = Color.white;
        Color clear = new Color(1f, 1f, 1f, 0f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;
                bool inside = IsInsideRoundedRect(px, py, size, size, radius);
                texture.SetPixel(x, y, inside ? solid : clear);
            }
        }

        texture.Apply();

        Vector4 border = new Vector4(radius, radius, radius, radius);

        return Sprite.Create(
            texture,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            border
        );
    }

    static bool IsInsideRoundedRect(float x, float y, float width, float height, float radius)
    {
        float innerLeft = radius;
        float innerRight = width - radius;
        float innerBottom = radius;
        float innerTop = height - radius;

        float closestX = Mathf.Clamp(x, innerLeft, innerRight);
        float closestY = Mathf.Clamp(y, innerBottom, innerTop);

        float dx = x - closestX;
        float dy = y - closestY;

        return dx * dx + dy * dy <= radius * radius;
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
    
    static List<string> SplitMitigationSteps(string raw)
    {
        var steps = new List<string>();

        if (string.IsNullOrWhiteSpace(raw))
        {
            return steps;
        }

        // Mitigation text can come from the database as separate lines, semicolon-separated
        // phrases, or one paragraph with multiple sentences. Split all of those so each
        // fix becomes its own bullet instead of one long bullet.
        string normalized = raw.Replace("\r", "\n");

        string[] chunks = normalized.Split(
            new[] { '\n', ';' },
            StringSplitOptions.RemoveEmptyEntries
        );

        foreach (string chunk in chunks)
        {
            string cleanedChunk = CleanMitigationStep(chunk);

            if (cleanedChunk.Length == 0)
            {
                continue;
            }

            string[] sentencePieces = cleanedChunk.Split(
                new[] { ". " },
                StringSplitOptions.RemoveEmptyEntries
            );

            foreach (string piece in sentencePieces)
            {
                string step = CleanMitigationStep(piece);

                if (step.Length == 0)
                {
                    continue;
                }

                if (!step.EndsWith(".") && !step.EndsWith("!") && !step.EndsWith("?"))
                {
                    step += ".";
                }

                steps.Add(step);
            }
        }

        return steps;
    }

    static string CleanMitigationStep(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return value
            .Trim()
            .TrimStart('•', '-', '*', '1', '2', '3', '4', '5', '.', ')')
            .Trim();
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

    static Sprite LoadObjectIcon(string label)
    {
        string path = GetIconResourcePath(label);

        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        Sprite sprite = Resources.Load<Sprite>(path);

        if (sprite == null)
        {
            Debug.LogWarning("[ReportUIBuilder] Missing icon Sprite at Resources/" + path + " for label: " + label);
        }

        return sprite;
    }

    static string GetIconResourcePath(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return "";
        }

        label = label.ToLowerInvariant();

        if (label.Contains("ss_birdbath")) return "Icons/icon_birdbath";
        if (label.Contains("ss_bucket")) return "Icons/icon_bucket";
        if (label.Contains("ss_grill")) return "Icons/icon_grill";
        if (label.Contains("ss_inflatablepool")) return "Icons/icon_pool";
        if (label.Contains("ss_pot")) return "Icons/icon_pot";
        if (label.Contains("ss_tire")) return "Icons/icon_tire";
        if (label.Contains("ss_trashcan")) return "Icons/icon_trash";
        if (label.Contains("ss_treehole")) return "Icons/icon_treehole";
        if (label.Contains("ss_wateringcan")) return "Icons/icon_wateringcan";
        if (label.Contains("ss_wheelbarrow")) return "Icons/icon_wheelbarrow";

        // These three do not have separate imported icons yet, so use the plant pot icon as a clean fallback.
        if (label.Contains("ss_bromiliad")) return "Icons/icon_pot";
        if (label.Contains("ss_waterhyacinth")) return "Icons/icon_pot";
        if (label.Contains("ss_waterlettuce")) return "Icons/icon_pot";

        return "";
    }

    static string GetIconFallbackText(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return "?";
        }

        label = label.ToLowerInvariant();

        if (label.Contains("birdbath")) return "BB";
        if (label.Contains("bucket")) return "B";
        if (label.Contains("grill")) return "G";
        if (label.Contains("inflatablepool")) return "P";
        if (label.Contains("pot")) return "P";
        if (label.Contains("tire")) return "T";
        if (label.Contains("trashcan")) return "TC";
        if (label.Contains("treehole")) return "TH";
        if (label.Contains("wateringcan")) return "WC";
        if (label.Contains("wheelbarrow")) return "W";
        if (label.Contains("bromiliad")) return "BR";
        if (label.Contains("waterhyacinth")) return "WH";
        if (label.Contains("waterlettuce")) return "WL";

        return "?";
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