using System;
using System.Linq;
using TMPro;
using UnityEngine;

// Attach this to the "Background" GameObject of the Last Scan card inside
// Drawer v3 (Drawer v3 > ... > Background, the same object that parents the
// Date, BadgeText and Meta labels), and wire the three TextMeshProUGUI
// fields below to those existing children in the Inspector.
public class LastScanCardController : MonoBehaviour
{
    public static LastScanCardController Instance { get; private set; }

    [Header("Last Scan Card")]
    public TextMeshProUGUI dateText;
    public TextMeshProUGUI badgeText;
    public TextMeshProUGUI metaText;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        RefreshLastScanCard();
    }

    void OnEnable()
    {
        RefreshLastScanCard();
    }

    public void RefreshLastScanCard()
    {
        if (DatabaseManager.Instance == null) return;

        ScanReport report = DatabaseManager.Instance.GetAllReports().FirstOrDefault();

        if (report == null)
        {
            if (dateText != null) dateText.text = "No scans yet";
            if (badgeText != null) badgeText.text = "–";
            if (metaText != null) metaText.text = "Run your first scan to see results";
            return;
        }

        if (dateText != null)
            dateText.text = FormatDate(report.scanned_at);

        if (badgeText != null)
        {
            int count = report.total_objects_detected;
            badgeText.text = count == 1 ? "1 site" : count + " sites";
        }

        if (metaText != null)
        {
            string risk = ComputeOverallRisk(report.id);
            metaText.text = FormatDuration(report.duration_seconds) + " | " + risk + " Risk";
        }
    }

    // Mirrors ScanCompleteController.ComputeOverallRisk: reuse
    // ReportUIBuilder's per-label risk lookup and take the highest risk
    // across every detection in the report.
    string ComputeOverallRisk(int reportId)
    {
        var detections = DatabaseManager.Instance.GetDetectionsForReport(reportId);

        if (detections == null || detections.Count == 0)
            return "Low";

        bool anyHigh = false;
        bool anyMod = false;

        foreach (var d in detections)
        {
            string r = ReportUIBuilder.GetRiskLevelPublic(d.label);
            if (r == "High") anyHigh = true;
            else if (r == "Moderate") anyMod = true;
        }

        if (anyHigh) return "High";
        if (anyMod) return "Moderate";
        return "Low";
    }

    // Same parse/format approach as ScanHistoryUIBuilder.FormatDate: scanned_at
    // is saved in UTC via DateTime.UtcNow.ToString("o"), so convert to local
    // time before displaying.
    static string FormatDate(string scannedAt)
    {
        if (string.IsNullOrWhiteSpace(scannedAt))
            return "Unknown date";

        if (DateTimeOffset.TryParse(scannedAt, out DateTimeOffset dto))
            return dto.ToLocalTime().DateTime.ToString("MMMM d, yyyy");

        if (DateTime.TryParse(scannedAt, out DateTime dt))
        {
            DateTime local = dt.Kind == DateTimeKind.Utc ? dt.ToLocalTime() : dt;
            return local.ToString("MMMM d, yyyy");
        }

        return scannedAt;
    }

    static string FormatDuration(int seconds)
    {
        if (seconds < 60)
            return seconds + " sec";

        int m = seconds / 60;
        int s = seconds % 60;
        return s == 0 ? m + "m" : m + "m " + s + "s";
    }
}
