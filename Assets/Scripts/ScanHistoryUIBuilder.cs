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
///   2. Wire the NavHistory button's OnClick to: ScanHistoryUIBuilder.Instance.ShowScanHistory()
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
    //  Brand palette
    // ──────────────────────────────────────────────
    static readonly Color AppBg         = HexColor("#F5F2EC");
    static readonly Color HeaderBg      = HexColor("#2D5A3D");
    static readonly Color HeaderBorder  = HexColor("#3A6E4D");
    static readonly Color CardBg        = HexColor("#FFFFFF");
    static readonly Color CardBorder    = HexColor("#C8DBC0");

    static readonly Color TitleWhite    = HexColor("#FFFFFF");
    static readonly Color SubtitleGreen = HexColor("#9FE1CB");
    static readonly Color BackLabel     = HexColor("#9FE1CB");
    static readonly Color CardDateColor = HexColor("#173404");
    static readonly Color CardMetaColor = HexColor("#3B6D11");
    static readonly Color ChevronColor  = HexColor("#639922");

    // Badge colors
    static readonly Color BadgeHighBg   = HexColor("#FAECE7");
    static readonly Color BadgeHighText = HexColor("#993C1D");
    static readonly Color BadgeMedBg    = HexColor("#FAEEDA");
    static readonly Color BadgeMedText  = HexColor("#854F0B");
    static readonly Color BadgeLowBg    = HexColor("#EAF3DE");
    static readonly Color BadgeLowText  = HexColor("#3B6D11");

    // Empty state
    static readonly Color EmptyIconColor = HexColor("#C8DBC0");
    static readonly Color EmptyTextColor = HexColor("#3B6D11");

    // ──────────────────────────────────────────────
    //  Risk label sets
    // ──────────────────────────────────────────────
    static readonly HashSet<string> HighLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ss_bromiliad"
    };

    static readonly HashSet<string> MedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ss_tire",
        "ss_pot",
        "ss_waterhyacinth",
        "ss_waterlettuce",
        "ss_trashcan",
        "ss_grill"
    };

    // ──────────────────────────────────────────────
    //  Private state
    // ──────────────────────────────────────────────
    GameObject  _panel;
    TextMeshProUGUI _subtitleText;
    Transform   _cardContainer;
    GameObject  _emptyState;
    bool        _built = false;

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

        // ── Root panel (full screen) ──────────────
        _panel = new GameObject("ScanHistoryPanel");
        _panel.transform.SetParent(targetCanvas.transform, false);

        RectTransform panelRect = _panel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        Image panelBg = _panel.AddComponent<Image>();
        panelBg.color = AppBg;

        // Sit on top of everything
        _panel.transform.SetAsLastSibling();

        // ── Header ───────────────────────────────
        BuildHeader(_panel.transform);

        // ── Scroll area ──────────────────────────
        BuildScrollArea(_panel.transform);

        // ── Empty state ──────────────────────────
        BuildEmptyState(_panel.transform);

        // Start hidden
        _panel.SetActive(false);
    }

    void BuildHeader(Transform parent)
    {
        // Header container
        GameObject header = CreateObject("Header", parent);
        RectTransform headerRect = header.AddComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot     = new Vector2(0.5f, 1f);
        headerRect.sizeDelta = new Vector2(0f, 130f);
        headerRect.anchoredPosition = Vector2.zero;

        Image headerBg = header.AddComponent<Image>();
        headerBg.color = HeaderBg;

        // Bottom border line
        GameObject borderLine = CreateObject("HeaderBorder", header.transform);
        RectTransform blRect = borderLine.AddComponent<RectTransform>();
        blRect.anchorMin = new Vector2(0f, 0f);
        blRect.anchorMax = new Vector2(1f, 0f);
        blRect.pivot     = new Vector2(0.5f, 0f);
        blRect.sizeDelta = new Vector2(0f, 2f);
        blRect.anchoredPosition = Vector2.zero;
        Image blImg = borderLine.AddComponent<Image>();
        blImg.color = HeaderBorder;

        // Vertical layout inside header
        VerticalLayoutGroup vlg = header.AddComponent<VerticalLayoutGroup>();
        vlg.padding           = new RectOffset(22, 22, 12, 14);
        vlg.spacing           = 2f;
        vlg.childAlignment    = TextAnchor.LowerLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight= false;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        ContentSizeFitter csf = header.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        // ← Menu back button
        GameObject backBtn = CreateObject("BackButton", header.transform);
        LayoutElement backLE = backBtn.AddComponent<LayoutElement>();
        backLE.preferredHeight = 22f;
        backLE.flexibleWidth   = 1f;

        Button backButton = backBtn.AddComponent<Button>();
        Image backImg = backBtn.AddComponent<Image>();
        backImg.color = Color.clear;
        ColorBlock cb = backButton.colors;
        cb.highlightedColor = new Color(1f, 1f, 1f, 0.08f);
        backButton.colors   = cb;
        backButton.onClick.AddListener(HideScanHistory);

        TextMeshProUGUI backLabel = CreateTMP("BackLabel", backBtn.transform);
        RectTransform blabelRect  = backLabel.GetComponent<RectTransform>();
        blabelRect.anchorMin      = Vector2.zero;
        blabelRect.anchorMax      = Vector2.one;
        blabelRect.offsetMin      = Vector2.zero;
        blabelRect.offsetMax      = Vector2.zero;
        backLabel.text            = "← Menu";
        backLabel.fontSize        = 14f;
        backLabel.color           = BackLabel;
        backLabel.alignment       = TextAlignmentOptions.BottomLeft;
        backLabel.fontStyle       = FontStyles.Normal;

        // Title
        TextMeshProUGUI title = CreateTMP("Title", header.transform);
        LayoutElement titleLE = title.GetComponent<RectTransform>().gameObject.AddComponent<LayoutElement>();
        titleLE.preferredHeight = 40f;
        titleLE.flexibleWidth   = 1f;
        title.text      = "Scan history";
        title.fontSize  = 30f;
        title.color     = TitleWhite;
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.BottomLeft;

        // Subtitle
        _subtitleText = CreateTMP("Subtitle", header.transform);
        LayoutElement subLE = _subtitleText.GetComponent<RectTransform>().gameObject.AddComponent<LayoutElement>();
        subLE.preferredHeight = 20f;
        subLE.flexibleWidth   = 1f;
        _subtitleText.text      = "0 scans recorded";
        _subtitleText.fontSize  = 14f;
        _subtitleText.color     = SubtitleGreen;
        _subtitleText.alignment = TextAlignmentOptions.BottomLeft;
    }

    void BuildScrollArea(Transform parent)
    {
        // ScrollRect viewport
        GameObject scrollObj = CreateObject("ScrollView", parent);
        RectTransform scrollRect = scrollObj.AddComponent<RectTransform>();
        scrollRect.anchorMin        = new Vector2(0f, 0f);
        scrollRect.anchorMax        = new Vector2(1f, 1f);
        scrollRect.offsetMin        = new Vector2(0f,  0f);
        scrollRect.offsetMax        = new Vector2(0f, -130f); // leave room for header

        ScrollRect sr = scrollObj.AddComponent<ScrollRect>();
        sr.horizontal       = false;
        sr.vertical         = true;
        sr.scrollSensitivity= 30f;
        sr.movementType     = ScrollRect.MovementType.Elastic;

        // Viewport
        GameObject viewport = CreateObject("Viewport", scrollObj.transform);
        RectTransform vpRect = viewport.AddComponent<RectTransform>();
        vpRect.anchorMin = Vector2.zero;
        vpRect.anchorMax = Vector2.one;
        vpRect.offsetMin = Vector2.zero;
        vpRect.offsetMax = Vector2.zero;
        Image viewportImg = viewport.AddComponent<Image>();
        viewportImg.color = new Color(0f, 0f, 0f, 0.01f);

        Mask viewportMask = viewport.AddComponent<Mask>();
        viewportMask.showMaskGraphic = false;

        // Content container
        GameObject content = CreateObject("Content", viewport.transform);
        RectTransform contentRect = content.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot     = new Vector2(0.5f, 1f);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;

        VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.padding           = new RectOffset(20, 20, 16, 24);
        vlg.spacing           = 12f;
        vlg.childAlignment    = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight= true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        sr.viewport = vpRect;
        sr.content  = contentRect;

        _cardContainer = content.transform;
    }

    void BuildEmptyState(Transform parent)
    {
        _emptyState = CreateObject("EmptyState", parent);
        RectTransform eRect = _emptyState.AddComponent<RectTransform>();
        eRect.anchorMin        = new Vector2(0f, 0f);
        eRect.anchorMax        = new Vector2(1f, 1f);
        eRect.offsetMin        = new Vector2(0f,   0f);
        eRect.offsetMax        = new Vector2(0f, -130f);

        VerticalLayoutGroup vlg = _emptyState.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment    = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight= true;
        vlg.childForceExpandWidth  = false;
        vlg.childForceExpandHeight = false;
        vlg.spacing = 12f;

        // Icon (simple mosquito / droplet placeholder as text glyph)
        TextMeshProUGUI icon = CreateTMP("EmptyIcon", _emptyState.transform);
        icon.text      = "○";        // neutral circle; swap for a sprite/glyph if desired
        icon.fontSize  = 56f;
        icon.color     = EmptyIconColor;
        icon.alignment = TextAlignmentOptions.Center;

        // Message
        TextMeshProUGUI msg = CreateTMP("EmptyMessage", _emptyState.transform);
        msg.text      = "No scans yet — tap Begin Scan to check your yard.";
        msg.fontSize  = 16f;
        msg.color     = EmptyTextColor;
        msg.alignment = TextAlignmentOptions.Center;
        msg.enableWordWrapping = true;

        RectTransform msgRect = msg.GetComponent<RectTransform>();
        LayoutElement msgLE   = msg.gameObject.AddComponent<LayoutElement>();
        msgLE.preferredWidth  = 260f;

        _emptyState.SetActive(false);
    }

    // ══════════════════════════════════════════════
    //  Card creation
    // ══════════════════════════════════════════════

    void CreateReportCard(ScanReport report)
    {
        // Resolve risk level from detections
        RiskLevel risk = ResolveRisk(report.id);

        // Format date
        string dateStr = FormatDate(report.scanned_at);

        // Format meta
        string meta = FormatMeta(report.duration_seconds, report.total_objects_detected);

        // ── Card outer (handles border via a nested approach) ──
        GameObject card = CreateObject($"Card_{report.id}", _cardContainer);

        LayoutElement cardLE = card.AddComponent<LayoutElement>();
        cardLE.preferredHeight = 76f;
        cardLE.flexibleWidth   = 1f;

        // Border image on the card root
        Image cardBorderImg = card.AddComponent<Image>();
        cardBorderImg.color  = CardBorder;
        cardBorderImg.type   = Image.Type.Sliced;

        // Rounded corners via sprite — fallback to solid if none available
        // We'll simulate rounded card with a child white fill inset 1px
        GameObject cardInner = CreateObject("CardInner", card.transform);
        RectTransform innerRect = cardInner.AddComponent<RectTransform>();
        innerRect.anchorMin = Vector2.zero;
        innerRect.anchorMax = Vector2.one;
        innerRect.offsetMin = new Vector2(1.5f, 1.5f);
        innerRect.offsetMax = new Vector2(-1.5f, -1.5f);

        Image innerImg = cardInner.AddComponent<Image>();
        innerImg.color = CardBg;

        // Button on card root
        Button btn = card.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.normalColor      = Color.white;
        cb.highlightedColor = new Color(0.95f, 0.98f, 0.95f, 1f);
        cb.pressedColor     = new Color(0.88f, 0.94f, 0.88f, 1f);
        btn.colors          = cb;
        btn.targetGraphic   = innerImg;

        int capturedId = report.id;
        btn.onClick.AddListener(() =>
        {
            HideScanHistory();

            if (ReportUIBuilder.Instance == null)
            {
                Debug.LogWarning("[ScanHistoryUIBuilder] ReportUIBuilder.Instance is null. Cannot open report.");
                return;
            }
            ReportUIBuilder.Instance.ShowReport(capturedId);
        });

        // ── Horizontal layout inside inner card ──
        HorizontalLayoutGroup hlg = cardInner.AddComponent<HorizontalLayoutGroup>();
        hlg.padding           = new RectOffset(16, 12, 0, 0);
        hlg.spacing           = 8f;
        hlg.childAlignment    = TextAnchor.MiddleLeft;
        hlg.childControlWidth = false;
        hlg.childControlHeight= true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = true;

        // ── Left text column ──────────────────────
        GameObject textCol = CreateObject("TextCol", cardInner.transform);
        LayoutElement textLE = textCol.AddComponent<LayoutElement>();
        textLE.flexibleWidth = 1f;

        VerticalLayoutGroup textVLG = textCol.AddComponent<VerticalLayoutGroup>();
        textVLG.childAlignment    = TextAnchor.MiddleLeft;
        textVLG.childControlWidth = true;
        textVLG.childControlHeight= true;
        textVLG.childForceExpandWidth  = true;
        textVLG.childForceExpandHeight = false;
        textVLG.spacing = 2f;
        textVLG.padding = new RectOffset(0, 0, 0, 0);

        TextMeshProUGUI dateTMP = CreateTMP("DateLabel", textCol.transform);
        dateTMP.text      = dateStr;
        dateTMP.fontSize  = 15f;
        dateTMP.color     = CardDateColor;
        dateTMP.fontStyle = FontStyles.Bold;
        dateTMP.alignment = TextAlignmentOptions.Left;

        TextMeshProUGUI metaTMP = CreateTMP("MetaLabel", textCol.transform);
        metaTMP.text      = meta;
        metaTMP.fontSize  = 13f;
        metaTMP.color     = CardMetaColor;
        metaTMP.alignment = TextAlignmentOptions.Left;

        // ── Risk badge ────────────────────────────
        GameObject badge = BuildBadge(cardInner.transform, risk);
        LayoutElement badgeLE = badge.AddComponent<LayoutElement>();
        badgeLE.preferredWidth  = 52f;
        badgeLE.preferredHeight = 26f;
        badgeLE.flexibleWidth   = 0f;

        // ── Chevron ───────────────────────────────
        TextMeshProUGUI chevron = CreateTMP("Chevron", cardInner.transform);
        LayoutElement chevLE = chevron.gameObject.AddComponent<LayoutElement>();
        chevLE.preferredWidth  = 18f;
        chevLE.flexibleWidth   = 0f;
        chevron.text      = "›";
        chevron.fontSize  = 24f;
        chevron.color     = ChevronColor;
        chevron.alignment = TextAlignmentOptions.Center;
        chevron.fontStyle = FontStyles.Bold;
    }

    GameObject BuildBadge(Transform parent, RiskLevel risk)
    {
        string label;
        Color bgColor, textColor;

        switch (risk)
        {
            case RiskLevel.High:
                label = "High"; bgColor = BadgeHighBg; textColor = BadgeHighText;
                break;
            case RiskLevel.Med:
                label = "Med";  bgColor = BadgeMedBg;  textColor = BadgeMedText;
                break;
            default:
                label = "Low";  bgColor = BadgeLowBg;  textColor = BadgeLowText;
                break;
        }

        GameObject badge = CreateObject($"Badge_{label}", parent);

        Image badgeImg = badge.AddComponent<Image>();
        badgeImg.color = bgColor;
        // Note: rounded pill corners require a rounded sprite; using solid fallback.
        // In Unity, assign a rounded-rect sprite to badgeImg.sprite for the pill look.

        TextMeshProUGUI badgeTMP = CreateTMP("BadgeText", badge.transform);
        RectTransform btRect     = badgeTMP.GetComponent<RectTransform>();
        btRect.anchorMin = Vector2.zero;
        btRect.anchorMax = Vector2.one;
        btRect.offsetMin = new Vector2(4f, 2f);
        btRect.offsetMax = new Vector2(-4f, -2f);
        badgeTMP.text      = label;
        badgeTMP.fontSize  = 12f;
        badgeTMP.color     = textColor;
        badgeTMP.alignment = TextAlignmentOptions.Center;
        badgeTMP.fontStyle = FontStyles.Bold;

        return badge;
    }

    // ══════════════════════════════════════════════
    //  Data helpers
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
            Debug.LogWarning($"[ScanHistoryUIBuilder] Failed to get detections for report {reportId}: {e.Message}");
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
            return dt.ToString("MMMM d, yyyy");   // e.g. "June 7, 2026"
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
        return $"{durStr} • {itemStr}";
    }

    // ══════════════════════════════════════════════
    //  Mock data (debug only)
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
