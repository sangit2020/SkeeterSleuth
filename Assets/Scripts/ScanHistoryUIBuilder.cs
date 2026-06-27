using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// ScanHistoryUIBuilder - Builds and manages the Scan History screen at runtime.
///
/// SETUP:
///   1. Attach this script to any persistent GameObject (e.g. "ScanHistoryManager").
///   2. Wire NavHistory button OnClick -> ScanHistoryUIBuilder.Instance.ShowScanHistory()
///   3. Assign targetCanvas in Inspector, or leave null to auto-find.
///
/// DEPENDENCIES:
///   - DatabaseManager.Instance  (GetAllReports, GetDetectionsForReport)
///   - ReportUIBuilder.Instance  (ShowReport)
///   - TextMeshPro
/// </summary>
public class ScanHistoryUIBuilder : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Singleton
    // ──────────────────────────────────────────────
    public static ScanHistoryUIBuilder Instance { get; private set; }

    // ──────────────────────────────────────────────
    //  Inspector
    // ──────────────────────────────────────────────
    [Header("Canvas")]
    [Tooltip("Assign the root Canvas. Leave null to auto-find.")]
    public Canvas targetCanvas;

    [Header("Debug")]
    [Tooltip("When true, injects fake rows so you can preview without a real DB.")]
    public bool useMockDataForDebugging = false;

    // ──────────────────────────────────────────────
    //  Brand palette  (matches Prevention Tips screen)
    // ──────────────────────────────────────────────
    static readonly Color AppBg          = HexColor("#F5F2EC");
    static readonly Color HeaderBg       = HexColor("#2D5A3D");
    static readonly Color HeaderBorder   = HexColor("#3A6E4D");
    static readonly Color CardBg         = HexColor("#FFFFFF");
    static readonly Color CardBorder     = HexColor("#C8DBC0");

    static readonly Color TitleWhite     = HexColor("#FFFFFF");
    static readonly Color SubtitleGreen  = HexColor("#9FE1CB");
    static readonly Color BackLabel      = HexColor("#9FE1CB");
    static readonly Color CardDateColor  = HexColor("#173404");
    static readonly Color CardMetaColor  = HexColor("#3B6D11");
    static readonly Color ChevronColor   = HexColor("#639922");

    // Badge colors
    static readonly Color BadgeHighBg    = HexColor("#FAECE7");
    static readonly Color BadgeHighText  = HexColor("#993C1D");
    static readonly Color BadgeMedBg     = HexColor("#FAEEDA");
    static readonly Color BadgeMedText   = HexColor("#854F0B");
    static readonly Color BadgeLowBg     = HexColor("#EAF3DE");
    static readonly Color BadgeLowText   = HexColor("#3B6D11");

    // Empty state
    static readonly Color EmptyIconColor = HexColor("#C8DBC0");
    static readonly Color EmptyTextColor = HexColor("#3B6D11");

    // ──────────────────────────────────────────────
    //  Layout constants
    // ──────────────────────────────────────────────
    // Header height matches Prevention Tips screen header
    const float HeaderHeight   = 148f;
    // Side margin for cards (matches Prevention Tips card margins)
    const float CardSideMargin = 20f;
    // Vertical gap between cards
    const float CardSpacing    = 12f;
    // Inner padding inside each card
    const float CardPadH       = 20f;  // horizontal
    const float CardPadV       = 16f;  // vertical

    // ──────────────────────────────────────────────
    //  Risk label sets
    // ──────────────────────────────────────────────
    static readonly HashSet<string> HighLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ss_tire", "ss_bucket", "ss_trashcan",
        "ss_wheelbarrow", "ss_inflatablepool", "ss_grill"
    };

    static readonly HashSet<string> MedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ss_birdbath", "ss_pot", "ss_wateringcan",
        "ss_treehole", "ss_bromiliad", "ss_waterhyacinth", "ss_waterlettuce"
    };

    // ──────────────────────────────────────────────
    //  Private state
    // ──────────────────────────────────────────────
    GameObject      _panel;
    TextMeshProUGUI _subtitleText;
    Transform       _cardContainer;
    GameObject      _emptyState;
    bool            _built = false;

    // Rounded sprite loaded once (optional — falls back to square if missing)
    Sprite _roundedSprite;

    // ══════════════════════════════════════════════
    //  Unity lifecycle
    // ══════════════════════════════════════════════
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        // Create the rounded-rect sprite once so cards and badges can have
        // rounded corners without needing any new Unity editor assets.
        LoadRoundedSprite();

        if (targetCanvas == null)
        {
            targetCanvas = FindObjectOfType<Canvas>();
            if (targetCanvas == null)
            {
                Debug.LogWarning("[ScanHistoryUIBuilder] No Canvas found in scene.");
                return;
            }
        }
        BuildPanel();
    }

    // ══════════════════════════════════════════════
    //  Public API
    // ══════════════════════════════════════════════

    /// <summary>Show the Scan History panel and refresh the list from DB.</summary>
    public void ShowScanHistory()
    {
        if (!_built)
        {
            if (targetCanvas == null)
                targetCanvas = FindObjectOfType<Canvas>();
            // Create rounded sprite if Start() hasn't run yet.
            LoadRoundedSprite();
            BuildPanel();
        }

        if (_panel == null)
        {
            Debug.LogWarning("[ScanHistoryUIBuilder] Panel is null — cannot show.");
            return;
        }

        _panel.SetActive(true);
        RefreshHistory();
    }

    /// <summary>Hide the Scan History panel.</summary>
    public void HideScanHistory()
    {
        if (_panel != null)
            _panel.SetActive(false);
    }

    /// <summary>Re-query the database and rebuild the card list.</summary>
    public void RefreshHistory()
    {
        if (_cardContainer == null) return;

        // Clear old cards
        foreach (Transform child in _cardContainer)
            Destroy(child.gameObject);

        List<ScanReport> reports = FetchReports();

        // Sort newest first
        reports.Sort((a, b) =>
        {
            DateTime dtA, dtB;
            DateTime.TryParse(a.scanned_at, out dtA);
            DateTime.TryParse(b.scanned_at, out dtB);
            return dtB.CompareTo(dtA);
        });

        // Update subtitle
        if (_subtitleText != null)
        {
            int count = reports.Count;
            _subtitleText.text = count == 1 ? "1 scan recorded" : $"{count} scans recorded";
        }

        // Show/hide empty state
        bool isEmpty = reports.Count == 0;
        if (_emptyState != null)
            _emptyState.SetActive(isEmpty);

        if (!isEmpty)
        {
            foreach (var report in reports)
                CreateReportCard(report);
        }
    }

    // ══════════════════════════════════════════════
    //  Panel construction
    // ══════════════════════════════════════════════

    void BuildPanel()
    {
        if (_built) return;
        if (targetCanvas == null) return;

        _built = true;

        // ── Root panel (full screen) ──────────────────────────
        _panel = new GameObject("ScanHistoryPanel");
        _panel.transform.SetParent(targetCanvas.transform, false);

        RectTransform panelRect = _panel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        Image panelBg = _panel.AddComponent<Image>();
        panelBg.color = AppBg;

        _panel.transform.SetAsLastSibling();

        BuildHeader(_panel.transform);
        BuildScrollArea(_panel.transform);
        BuildEmptyState(_panel.transform);

        _panel.SetActive(false);
    }

    // ──────────────────────────────────────────────
    //  Header  (mirrors Prevention Tips header style)
    // ──────────────────────────────────────────────
    void BuildHeader(Transform parent)
    {
        GameObject header = CreateObject("Header", parent);

        // Pin to top, full width, fixed height
        RectTransform hr = header.AddComponent<RectTransform>();
        hr.anchorMin        = new Vector2(0f, 1f);
        hr.anchorMax        = new Vector2(1f, 1f);
        hr.pivot            = new Vector2(0.5f, 1f);
        hr.anchoredPosition = Vector2.zero;
        hr.sizeDelta        = new Vector2(0f, HeaderHeight);

        Image headerBg = header.AddComponent<Image>();
        headerBg.color = HeaderBg;

        // Thin bottom border accent
        GameObject borderLine = CreateObject("HeaderBorder", header.transform);
        RectTransform blRect  = borderLine.AddComponent<RectTransform>();
        blRect.anchorMin        = new Vector2(0f, 0f);
        blRect.anchorMax        = new Vector2(1f, 0f);
        blRect.pivot            = new Vector2(0.5f, 0f);
        blRect.anchoredPosition = Vector2.zero;
        blRect.sizeDelta        = new Vector2(0f, 2f);
        borderLine.AddComponent<Image>().color = HeaderBorder;

        // ── Back button row  (top of header) ─────────────────
        // Positioned manually so it sits near the safe-area top
        // and the title/subtitle can breathe in the lower portion.

        GameObject backBtn         = CreateObject("BackButton", header.transform);
        RectTransform backBtnRect  = backBtn.AddComponent<RectTransform>();
        backBtnRect.anchorMin      = new Vector2(0f, 1f);
        backBtnRect.anchorMax      = new Vector2(0.6f, 1f);
        backBtnRect.pivot          = new Vector2(0f, 1f);
        backBtnRect.anchoredPosition = new Vector2(CardSideMargin, -18f);
        backBtnRect.sizeDelta      = new Vector2(0f, 28f);

        Image backHitImg            = backBtn.AddComponent<Image>();
        backHitImg.color            = Color.clear;
        backHitImg.raycastTarget    = true;

        Button backButton           = backBtn.AddComponent<Button>();
        backButton.targetGraphic    = backHitImg;
        ColorBlock bcb              = backButton.colors;
        bcb.normalColor             = Color.white;
        bcb.highlightedColor        = new Color(1f, 1f, 1f, 0.12f);
        bcb.pressedColor            = new Color(1f, 1f, 1f, 0.20f);
        backButton.colors           = bcb;
        backButton.onClick.AddListener(HideScanHistory);

        TextMeshProUGUI backLabel   = CreateTMP("BackLabel", backBtn.transform);
        RectTransform blr           = backLabel.GetComponent<RectTransform>();
        blr.anchorMin               = Vector2.zero;
        blr.anchorMax               = Vector2.one;
        blr.offsetMin               = Vector2.zero;
        blr.offsetMax               = Vector2.zero;
        backLabel.text              = "← Menu";
        backLabel.fontSize          = 15f;
        backLabel.color             = BackLabel;
        backLabel.alignment         = TextAlignmentOptions.MidlineLeft;
        backLabel.fontStyle         = FontStyles.Normal;
        backLabel.raycastTarget     = false;

        // ── Title ─────────────────────────────────────────────
        // Sits in the lower 60 % of the header, left-aligned with same margin
        TextMeshProUGUI title       = CreateTMP("Title", header.transform);
        RectTransform titleRect     = title.GetComponent<RectTransform>();
        titleRect.anchorMin         = new Vector2(0f, 0f);
        titleRect.anchorMax         = new Vector2(1f, 1f);
        titleRect.offsetMin         = new Vector2(CardSideMargin, 36f);
        titleRect.offsetMax         = new Vector2(-CardSideMargin, -52f);
        title.text                  = "Scan history";
        title.fontSize              = 32f;
        title.color                 = TitleWhite;
        title.fontStyle             = FontStyles.Bold;
        title.alignment             = TextAlignmentOptions.BottomLeft;
        title.enableWordWrapping    = false;
        title.raycastTarget         = false;

        // ── Subtitle ──────────────────────────────────────────
        _subtitleText               = CreateTMP("Subtitle", header.transform);
        RectTransform subRect       = _subtitleText.GetComponent<RectTransform>();
        subRect.anchorMin           = new Vector2(0f, 0f);
        subRect.anchorMax           = new Vector2(1f, 0f);
        subRect.pivot               = new Vector2(0f, 0f);
        subRect.anchoredPosition    = new Vector2(CardSideMargin, 14f);
        subRect.sizeDelta           = new Vector2(-CardSideMargin * 2f, 22f);
        _subtitleText.text          = "0 scans recorded";
        _subtitleText.fontSize      = 14f;
        _subtitleText.color         = SubtitleGreen;
        _subtitleText.alignment     = TextAlignmentOptions.BottomLeft;
        _subtitleText.raycastTarget = false;
    }

    // ──────────────────────────────────────────────
    //  Scroll area
    // ──────────────────────────────────────────────
    void BuildScrollArea(Transform parent)
    {
        GameObject scrollObj        = CreateObject("ScrollView", parent);
        RectTransform scrollRect    = scrollObj.AddComponent<RectTransform>();
        scrollRect.anchorMin        = new Vector2(0f, 0f);
        scrollRect.anchorMax        = new Vector2(1f, 1f);
        scrollRect.offsetMin        = new Vector2(0f, 0f);
        scrollRect.offsetMax        = new Vector2(0f, -HeaderHeight);

        ScrollRect sr               = scrollObj.AddComponent<ScrollRect>();
        sr.horizontal               = false;
        sr.vertical                 = true;
        sr.scrollSensitivity        = 40f;
        sr.movementType             = ScrollRect.MovementType.Elastic;
        sr.elasticity               = 0.1f;
        sr.inertia                  = true;
        sr.decelerationRate         = 0.135f;

        // Viewport
        GameObject viewport         = CreateObject("Viewport", scrollObj.transform);
        RectTransform vpRect        = viewport.AddComponent<RectTransform>();
        vpRect.anchorMin            = Vector2.zero;
        vpRect.anchorMax            = Vector2.one;
        vpRect.offsetMin            = Vector2.zero;
        vpRect.offsetMax            = Vector2.zero;

        // IMPORTANT: Mask graphics cannot be fully transparent in some Unity UI setups.
        // A tiny alpha keeps the mask working without making the viewport visible.
        Image vpImg                 = viewport.AddComponent<Image>();
        vpImg.color                 = new Color(0f, 0f, 0f, 0.01f);

        Mask vpMask                 = viewport.AddComponent<Mask>();
        vpMask.showMaskGraphic      = false;

        // Content container
        GameObject content          = CreateObject("Content", viewport.transform);
        RectTransform contentRect   = content.AddComponent<RectTransform>();
        contentRect.anchorMin       = new Vector2(0f, 1f);
        contentRect.anchorMax       = new Vector2(1f, 1f);
        contentRect.pivot           = new Vector2(0.5f, 1f);
        contentRect.offsetMin       = Vector2.zero;
        contentRect.offsetMax       = Vector2.zero;

        VerticalLayoutGroup vlg     = content.AddComponent<VerticalLayoutGroup>();
        vlg.padding                 = new RectOffset(
            (int)CardSideMargin,
            (int)CardSideMargin,
            20,
            32
        );
        vlg.spacing                 = CardSpacing;
        vlg.childAlignment          = TextAnchor.UpperCenter;
        vlg.childControlWidth       = true;
        vlg.childControlHeight      = true;
        vlg.childForceExpandWidth   = true;
        vlg.childForceExpandHeight  = false;

        ContentSizeFitter csf       = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit             = ContentSizeFitter.FitMode.PreferredSize;

        sr.viewport                 = vpRect;
        sr.content                  = contentRect;

        _cardContainer              = content.transform;
    }

    // ──────────────────────────────────────────────
    //  Empty state
    // ──────────────────────────────────────────────
    void BuildEmptyState(Transform parent)
    {
        _emptyState                 = CreateObject("EmptyState", parent);
        RectTransform eRect         = _emptyState.AddComponent<RectTransform>();
        eRect.anchorMin             = new Vector2(0f, 0f);
        eRect.anchorMax             = new Vector2(1f, 1f);
        eRect.offsetMin             = new Vector2(0f, 0f);
        eRect.offsetMax             = new Vector2(0f, -HeaderHeight);

        VerticalLayoutGroup vlg     = _emptyState.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment          = TextAnchor.MiddleCenter;
        vlg.childControlWidth       = true;
        vlg.childControlHeight      = true;
        vlg.childForceExpandWidth   = false;
        vlg.childForceExpandHeight  = false;
        vlg.spacing                 = 16f;
        vlg.padding                 = new RectOffset(40, 40, 0, 0);

        // Large soft circle placeholder icon
        GameObject iconHolder       = CreateObject("EmptyIconHolder", _emptyState.transform);
        LayoutElement iconLE        = iconHolder.AddComponent<LayoutElement>();
        iconLE.preferredWidth       = 72f;
        iconLE.preferredHeight      = 72f;
        iconLE.flexibleWidth        = 0f;

        Image iconCircle            = iconHolder.AddComponent<Image>();
        iconCircle.color            = HexColor("#E8F0E4");
        if (_roundedSprite != null)
        {
            iconCircle.sprite       = _roundedSprite;
            iconCircle.type         = Image.Type.Sliced;
        }

        TextMeshProUGUI iconGlyph   = CreateTMP("IconGlyph", iconHolder.transform);
        RectTransform igRect        = iconGlyph.GetComponent<RectTransform>();
        igRect.anchorMin            = Vector2.zero;
        igRect.anchorMax            = Vector2.one;
        igRect.offsetMin            = Vector2.zero;
        igRect.offsetMax            = Vector2.zero;
        iconGlyph.text              = "-";
        iconGlyph.fontSize          = 36f;
        iconGlyph.color             = EmptyIconColor;
        iconGlyph.alignment         = TextAlignmentOptions.Center;

        // Message
        TextMeshProUGUI msg         = CreateTMP("EmptyMessage", _emptyState.transform);
        LayoutElement msgLE         = msg.gameObject.AddComponent<LayoutElement>();
        msgLE.preferredWidth        = 280f;
        msg.text                    = "No scans yet — tap Begin Scan to check your yard.";
        msg.fontSize                = 16f;
        msg.color                   = EmptyTextColor;
        msg.alignment               = TextAlignmentOptions.Center;
        msg.enableWordWrapping      = true;

        _emptyState.SetActive(false);
    }

    // ══════════════════════════════════════════════
    //  Card creation  (Prevention Tips card style)
    // ══════════════════════════════════════════════

    void CreateReportCard(ScanReport report)
    {
        RiskLevel risk   = ResolveRisk(report.id);
        string dateStr   = FormatDate(report.scanned_at);
        string meta      = FormatMeta(report.duration_seconds, report.total_objects_detected);

        // Fixed-height card. This is more reliable than ContentSizeFitter for
        // dynamically generated cards because the parent VerticalLayoutGroup needs
        // a clear preferred height to render each row. The height is kept compact to match the Prevention-style cards.
        GameObject card             = CreateObject($"Card_{report.id}", _cardContainer);
        RectTransform cardRect      = card.AddComponent<RectTransform>();

        LayoutElement cardLE        = card.AddComponent<LayoutElement>();
        cardLE.preferredHeight      = 86f;
        cardLE.minHeight            = 86f;
        cardLE.flexibleWidth        = 1f;

        Image cardBorderImg         = card.AddComponent<Image>();
        cardBorderImg.color         = CardBorder;
        if (_roundedSprite != null)
        {
            cardBorderImg.sprite    = _roundedSprite;
            cardBorderImg.type      = Image.Type.Sliced;
        }

        // White fill inset so the border stays visible.
        GameObject cardInner        = CreateObject("CardInner", card.transform);
        RectTransform innerRect     = cardInner.AddComponent<RectTransform>();
        innerRect.anchorMin         = Vector2.zero;
        innerRect.anchorMax         = Vector2.one;
        innerRect.offsetMin         = new Vector2(1.5f, 1.5f);
        innerRect.offsetMax         = new Vector2(-1.5f, -1.5f);

        Image innerImg              = cardInner.AddComponent<Image>();
        innerImg.color              = CardBg;
        if (_roundedSprite != null)
        {
            innerImg.sprite         = _roundedSprite;
            innerImg.type           = Image.Type.Sliced;
        }

        Button btn                  = card.AddComponent<Button>();
        btn.targetGraphic           = innerImg;
        ColorBlock cb               = btn.colors;
        cb.normalColor              = Color.white;
        cb.highlightedColor         = new Color(0.96f, 0.99f, 0.96f, 1f);
        cb.pressedColor             = new Color(0.90f, 0.96f, 0.90f, 1f);
        cb.fadeDuration             = 0.08f;
        btn.colors                  = cb;

        int capturedId              = report.id;
        btn.onClick.AddListener(() =>
        {
            HideScanHistory();
            if (ReportUIBuilder.Instance == null)
            {
                Debug.LogWarning("[ScanHistoryUIBuilder] ReportUIBuilder.Instance is null.");
                return;
            }
            ReportUIBuilder.Instance.ShowReport(capturedId);
        });

        // Date title
        TextMeshProUGUI dateTMP     = CreateTMP("DateLabel", cardInner.transform);
        RectTransform dateRect      = dateTMP.GetComponent<RectTransform>();
        dateRect.anchorMin          = new Vector2(0f, 1f);
        dateRect.anchorMax          = new Vector2(1f, 1f);
        dateRect.pivot              = new Vector2(0f, 1f);
        dateRect.offsetMin          = new Vector2(18f, -36f);
        dateRect.offsetMax          = new Vector2(-158f, -10f);
        dateTMP.text                = dateStr;
        dateTMP.fontSize            = 16.5f;
        dateTMP.color               = CardDateColor;
        dateTMP.fontStyle           = FontStyles.Bold;
        dateTMP.alignment           = TextAlignmentOptions.Left;
        dateTMP.enableWordWrapping  = false;

        // Duration + item count
        TextMeshProUGUI metaTMP     = CreateTMP("MetaLabel", cardInner.transform);
        RectTransform metaRect      = metaTMP.GetComponent<RectTransform>();
        metaRect.anchorMin          = new Vector2(0f, 1f);
        metaRect.anchorMax          = new Vector2(1f, 1f);
        metaRect.pivot              = new Vector2(0f, 1f);
        metaRect.offsetMin          = new Vector2(18f, -59f);
        metaRect.offsetMax          = new Vector2(-158f, -36f);
        metaTMP.text                = meta;
        metaTMP.fontSize            = 14f;
        metaTMP.color               = CardMetaColor;
        metaTMP.alignment           = TextAlignmentOptions.Left;
        metaTMP.enableWordWrapping  = false;


        // Risk badge centered vertically on the right, matching the cleaner mobile card style.
        BuildBadge(cardInner.transform, risk);

        // Chevron on the right.
        TextMeshProUGUI chevron     = CreateTMP("Chevron", cardInner.transform);
        RectTransform chevRect      = chevron.GetComponent<RectTransform>();
        chevRect.anchorMin          = new Vector2(1f, 0.5f);
        chevRect.anchorMax          = new Vector2(1f, 0.5f);
        chevRect.pivot              = new Vector2(0.5f, 0.5f);
        chevRect.anchoredPosition   = new Vector2(-20f, 0f);
        chevRect.sizeDelta          = new Vector2(20f, 32f);
        chevron.text                = "›";
        chevron.fontSize            = 24f;
        chevron.color               = ChevronColor;
        chevron.alignment           = TextAlignmentOptions.Center;
        chevron.fontStyle           = FontStyles.Normal;
        chevron.enableWordWrapping  = false;
    }

    // ──────────────────────────────────────────────
    //  Badge pill
    // ──────────────────────────────────────────────
    void BuildBadge(Transform parent, RiskLevel risk)
    {
        string label;
        Color  bgColor, textColor;

        switch (risk)
        {
            case RiskLevel.High:
                label = "High risk"; bgColor = BadgeHighBg; textColor = BadgeHighText; break;
            case RiskLevel.Med:
                label = "Med risk";  bgColor = BadgeMedBg;  textColor = BadgeMedText;  break;
            default:
                label = "Low risk";  bgColor = BadgeLowBg;  textColor = BadgeLowText;  break;
        }

        GameObject badge            = CreateObject($"Badge_{label}", parent);
        RectTransform badgeRect     = badge.AddComponent<RectTransform>();
        badgeRect.anchorMin         = new Vector2(1f, 0.5f);
        badgeRect.anchorMax         = new Vector2(1f, 0.5f);
        badgeRect.pivot             = new Vector2(1f, 0.5f);
        badgeRect.anchoredPosition  = new Vector2(-52f, 0f);
        badgeRect.sizeDelta         = new Vector2(88f, 28f);

        // Subtle outer border so the badge feels more intentional and less like
        // a plain template pill. The inner fill keeps the soft risk color.
        Color borderColor           = textColor;
        borderColor.a               = 0.20f;

        Image badgeBorder           = badge.AddComponent<Image>();
        badgeBorder.color           = borderColor;
        if (_roundedSprite != null)
        {
            badgeBorder.sprite      = _roundedSprite;
            badgeBorder.type        = Image.Type.Sliced;
        }

        GameObject badgeFill        = CreateObject("BadgeFill", badge.transform);
        RectTransform fillRect      = badgeFill.AddComponent<RectTransform>();
        fillRect.anchorMin          = Vector2.zero;
        fillRect.anchorMax          = Vector2.one;
        fillRect.offsetMin          = new Vector2(1.2f, 1.2f);
        fillRect.offsetMax          = new Vector2(-1.2f, -1.2f);

        Image fillImg               = badgeFill.AddComponent<Image>();
        fillImg.color               = bgColor;
        if (_roundedSprite != null)
        {
            fillImg.sprite          = _roundedSprite;
            fillImg.type            = Image.Type.Sliced;
        }

        TextMeshProUGUI badgeTMP    = CreateTMP("BadgeText", badgeFill.transform);
        RectTransform btRect        = badgeTMP.GetComponent<RectTransform>();
        btRect.anchorMin            = Vector2.zero;
        btRect.anchorMax            = Vector2.one;
        btRect.offsetMin            = new Vector2(6f, 1.5f);
        btRect.offsetMax            = new Vector2(-6f, -1.5f);
        badgeTMP.text               = label;
        badgeTMP.fontSize           = 12.5f;
        badgeTMP.color              = textColor;
        badgeTMP.alignment          = TextAlignmentOptions.Center;
        badgeTMP.fontStyle          = FontStyles.Bold;
        badgeTMP.enableWordWrapping = false;
    }

    // ══════════════════════════════════════════════
    //  Data helpers  (unchanged)
    // ══════════════════════════════════════════════

    List<ScanReport> FetchReports()
    {
        if (useMockDataForDebugging)
            return GetMockReports();

        if (DatabaseManager.Instance == null)
        {
            Debug.LogWarning("[ScanHistoryUIBuilder] DatabaseManager.Instance is null.");
            return new List<ScanReport>();
        }

        var reports = DatabaseManager.Instance.GetAllReports();
        return reports ?? new List<ScanReport>();
    }

    RiskLevel ResolveRisk(int reportId)
    {
        if (DatabaseManager.Instance == null)
            return RiskLevel.Low;

        List<DetectionWithDetails> detections;
        try
        {
            detections = DatabaseManager.Instance.GetDetectionsForReport(reportId);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ScanHistoryUIBuilder] Failed to get detections for {reportId}: {e.Message}");
            return RiskLevel.Low;
        }

        if (detections == null || detections.Count == 0)
            return RiskLevel.Low;

        bool anyHigh = false;
        bool anyMed  = false;

        foreach (var d in detections)
        {
            if (string.IsNullOrEmpty(d.label)) continue;
            if (HighLabels.Contains(d.label)) { anyHigh = true; break; }
            if (MedLabels.Contains(d.label))    anyMed = true;
        }

        if (anyHigh) return RiskLevel.High;
        if (anyMed)  return RiskLevel.Med;
        return RiskLevel.Low;
    }

    static string FormatDate(string scannedAt)
    {
        if (DateTime.TryParse(scannedAt, out DateTime dt))
            return dt.ToString("MMMM d, yyyy");
        return scannedAt ?? "Unknown date";
    }

    static string FormatDuration(int seconds)
    {
        if (seconds < 60)
            return $"{seconds} sec";
        int m = seconds / 60;
        int s = seconds % 60;
        return s == 0 ? $"{m}m" : $"{m}m {s}s";
    }

    static string FormatMeta(int durationSeconds, int itemCount)
    {
        string durStr  = FormatDuration(durationSeconds);
        string itemStr = itemCount == 1 ? "1 item" : $"{itemCount} items";
        return $"{durStr} \u2022 {itemStr}";
    }

    // ══════════════════════════════════════════════
    //  Mock data (debug only — useMockDataForDebugging = false by default)
    // ══════════════════════════════════════════════

    List<ScanReport> GetMockReports()
    {
        return new List<ScanReport>
        {
            new ScanReport { id = 1, scanned_at = "2026-06-07T10:30:00", duration_seconds = 47, total_objects_detected = 3 },
            new ScanReport { id = 2, scanned_at = "2026-05-30T14:22:00", duration_seconds = 52, total_objects_detected = 2 },
            new ScanReport { id = 3, scanned_at = "2026-05-22T09:11:00", duration_seconds = 38, total_objects_detected = 1 },
            new ScanReport { id = 4, scanned_at = "2026-05-15T16:45:00", duration_seconds = 61, total_objects_detected = 4 },
        };
    }

    // ══════════════════════════════════════════════
    //  UI factory helpers
    // ══════════════════════════════════════════════

    void LoadRoundedSprite()
    {
        if (_roundedSprite != null)
        {
            return;
        }

        // Generate the rounded rectangle at runtime so the card corners
        // work even if the project does not already have a UI/Rounded sprite.
        _roundedSprite = CreateRuntimeRoundedSprite(64, 18);
    }

    static Sprite CreateRuntimeRoundedSprite(int size, int radius)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "RuntimeRoundedRect";
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

    static GameObject CreateObject(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go;
    }

    static TextMeshProUGUI CreateTMP(string name, Transform parent)
    {
        var go  = CreateObject(name, parent);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.raycastTarget = false;
        return tmp;
    }

    static Color HexColor(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color c);
        return c;
    }

    // ══════════════════════════════════════════════
    //  Nested types
    // ══════════════════════════════════════════════

    enum RiskLevel { Low, Med, High }
}