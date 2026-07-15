using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_IOS || UNITY_EDITOR
using Unity.Notifications.iOS;
#endif

// Attach this to its own "NotificationManager" GameObject at the root of
// ARScreen (sibling to the ScanManager / DatabaseManager objects). Wire
// weeklyReminderToggle to Drawer v3 > ... > PageSettings > SettingReminders >
// Toggle, and wire that Toggle's OnValueChanged(bool) event to
// NotificationManager.OnReminderToggleChanged.
public class NotificationManager : MonoBehaviour
{
    public static NotificationManager Instance { get; private set; }

    [Header("Weekly Reminders Toggle")]
    [Tooltip("SettingReminders > Toggle in PageSettings. Used to bounce the switch back to Off if notification permission is denied.")]
    public Toggle weeklyReminderToggle;

    const string PrefKeyReminderEnabled = "WeeklyReminderEnabled";
    const string NotificationIdentifier = "weekly_reminder";
    const int ReminderDays = 7;
    const int ReminderHourLocal = 10;

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
        ResyncOnLaunch();
    }

    public bool IsReminderEnabled()
    {
        return PlayerPrefs.GetInt(PrefKeyReminderEnabled, 0) == 1;
    }

    // Wire the Weekly Reminders Toggle's OnValueChanged(bool) event to this method in the Inspector.
    public void OnReminderToggleChanged(bool isOn)
    {
        if (isOn)
            EnableReminder();
        else
            DisableReminder();
    }

    void EnableReminder()
    {
#if UNITY_IOS || UNITY_EDITOR
        StartCoroutine(RequestAuthorizationAndSchedule());
#else
        Debug.LogWarning("[NotificationManager] Local notifications are only supported on iOS.");
        RevertToggleToOff();
#endif
    }

    void DisableReminder()
    {
        CancelReminder();
        PlayerPrefs.SetInt(PrefKeyReminderEnabled, 0);
        PlayerPrefs.Save();
    }

    // Called from ScanManager right after a scan report is saved, so an active
    // reminder rolls forward from the scan that just finished instead of
    // staying anchored to whatever scan was most recent before.
    public void OnScanCompleted()
    {
        if (!IsReminderEnabled()) return;

#if UNITY_IOS || UNITY_EDITOR
        if (!IsAuthorized())
            return;
#endif

        ScheduleFromMostRecentScan();
    }

    void ResyncOnLaunch()
    {
        if (!IsReminderEnabled()) return;

#if UNITY_IOS || UNITY_EDITOR
        var status = iOSNotificationCenter.GetNotificationSettings().AuthorizationStatus;

        if (status == AuthorizationStatus.Denied)
        {
            // Permission was revoked (or never actually granted) since the toggle was last saved on.
            RevertToggleToOff();
            return;
        }

        if (status == AuthorizationStatus.NotDetermined)
        {
            // Fresh install / never asked yet. Leave the saved state alone; the
            // permission prompt only fires from an explicit toggle interaction.
            return;
        }

        bool alreadyScheduled = iOSNotificationCenter.GetScheduledNotifications()
            .Any(n => n.Identifier == NotificationIdentifier);

        if (!alreadyScheduled)
            ScheduleFromMostRecentScan();
#endif
    }

#if UNITY_IOS || UNITY_EDITOR
    bool IsAuthorized()
    {
        var status = iOSNotificationCenter.GetNotificationSettings().AuthorizationStatus;
        return status == AuthorizationStatus.Authorized || status == AuthorizationStatus.Provisional;
    }

    IEnumerator RequestAuthorizationAndSchedule()
    {
        using (var req = new AuthorizationRequest(AuthorizationOption.Alert | AuthorizationOption.Badge | AuthorizationOption.Sound, false))
        {
            while (!req.IsFinished)
                yield return null;

            if (req.Granted)
            {
                ScheduleFromMostRecentScan();
            }
            else
            {
                Debug.LogWarning("[NotificationManager] Notification permission denied: " + req.Error);
                RevertToggleToOff();
            }
        }
    }
#endif

    void RevertToggleToOff()
    {
        PlayerPrefs.SetInt(PrefKeyReminderEnabled, 0);
        PlayerPrefs.Save();

        if (weeklyReminderToggle != null)
            weeklyReminderToggle.SetIsOnWithoutNotify(false);
    }

    void ScheduleFromMostRecentScan()
    {
        DateTime baseTimeUtc = DateTime.UtcNow;
        ScanReport report = null;

        if (DatabaseManager.Instance != null)
        {
            report = DatabaseManager.Instance.GetAllReports().FirstOrDefault();
            if (report != null && DateTimeOffset.TryParse(report.scanned_at, out DateTimeOffset dto))
                baseTimeUtc = dto.UtcDateTime;
        }

        DateTime fireTimeUtc = ComputeFireTimeUtc(baseTimeUtc);
        ScheduleReminder(fireTimeUtc, report);

        PlayerPrefs.SetInt(PrefKeyReminderEnabled, 1);
        PlayerPrefs.Save();
    }

    // Keeps the DATE 7 days out from the scan (or now), but clamps the TIME
    // to a fixed local hour so the reminder doesn't fire at whatever time of
    // day the original scan happened to occur (e.g. late at night).
    static DateTime ComputeFireTimeUtc(DateTime baseTimeUtc)
    {
        DateTime targetLocalDate = baseTimeUtc.ToLocalTime().Date.AddDays(ReminderDays);
        DateTime fireLocal = targetLocalDate.AddHours(ReminderHourLocal);
        return fireLocal.ToUniversalTime();
    }

    void ScheduleReminder(DateTime fireTimeUtc, ScanReport report)
    {
        CancelReminder();

#if UNITY_IOS || UNITY_EDITOR
        TimeSpan interval = fireTimeUtc - DateTime.UtcNow;
        if (interval < TimeSpan.FromMinutes(1))
            interval = TimeSpan.FromMinutes(1);

        BuildNotificationContent(report, out string title, out string body);

        var notification = new iOSNotification
        {
            Identifier = NotificationIdentifier,
            Title = title,
            Body = body,
            ShowInForeground = true,
            ForegroundPresentationOption = PresentationOption.Alert | PresentationOption.Sound,
            Trigger = new iOSNotificationTimeIntervalTrigger
            {
                TimeInterval = interval,
                Repeats = false
            }
        };

        iOSNotificationCenter.ScheduleNotification(notification);
        Debug.Log("[NotificationManager] Scheduled weekly reminder for " + fireTimeUtc.ToLocalTime());
#endif
    }

    // No prior scan (fresh install, reminder scheduled from "now") falls back
    // to a generic message since there's no real scan data to reference yet.
    static void BuildNotificationContent(ScanReport report, out string title, out string body)
    {
        title = "Weekly Yard Check-In";

        if (report == null || DatabaseManager.Instance == null)
        {
            body = "Time to check your yard for mosquito breeding sites.";
            return;
        }

        int count = report.total_objects_detected;

        if (count <= 0)
        {
            body = "Your last scan didn't find any breeding sites " + ReminderDays + " days ago — a quick recheck never hurts.";
            return;
        }

        string risk = ComputeOverallRisk(report.id);
        string itemPhrase = count == 1 ? "item" : "items";

        body = "Your last scan found " + count + " " + risk + " Risk " + itemPhrase + " " + ReminderDays + " days ago — check your yard again.";
    }

    // Mirrors LastScanCardController.ComputeOverallRisk: reuse
    // ReportUIBuilder's per-label risk lookup and take the highest risk
    // across every detection in the report.
    static string ComputeOverallRisk(int reportId)
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

    public void CancelReminder()
    {
#if UNITY_IOS || UNITY_EDITOR
        iOSNotificationCenter.RemoveScheduledNotification(NotificationIdentifier);
#endif
    }
}
