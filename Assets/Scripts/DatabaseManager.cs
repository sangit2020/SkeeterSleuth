using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SQLite;
using UnityEngine;

public class DatabaseManager : MonoBehaviour
{
    public static DatabaseManager Instance { get; private set; }

    [Header("Testing")]
    [Tooltip("Turn this on only when testing. Turn it off after confirming mock data saves.")]
    [SerializeField] private bool runMockScanOnStart = false;

    private SQLiteConnection db;

    private string DatabasePath
    {
        get
        {
            return Path.Combine(Application.persistentDataPath, "skeeter_sleuth.db");
        }
    }

    private void Awake()
    {
        // Keep one database manager available across scenes.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        InitializeDatabase();

        if (runMockScanOnStart)
        {
            SaveMockScan();
        }
    }

    private void OnDestroy()
    {
        CloseDatabase();
    }

    private void OnApplicationQuit()
    {
        CloseDatabase();
    }

    private void CloseDatabase()
    {
        if (db != null)
        {
            db.Close();
            db.Dispose();
            db = null;
        }
    }

    private void EnsureDatabaseInitialized()
    {
        if (db == null)
        {
            InitializeDatabase();
        }
    }

    public void InitializeDatabase()
    {
        if (db != null)
        {
            return;
        }

        string directory = Path.GetDirectoryName(DatabasePath);

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        db = new SQLiteConnection(DatabasePath);

        db.CreateTable<ObjectType>();
        db.CreateTable<ScanReport>();
        db.CreateTable<Detection>();
        db.CreateTable<Mitigation>();

        SeedObjectTypesAndMitigations();

        Debug.Log("SQLite database ready at: " + DatabasePath);
    }

    private void SeedObjectTypesAndMitigations()
    {
        // IMPORTANT: label should match the raw YOLO output string.
        SeedObjectType(
            label: "cup",
            displayName: "Cup",
            description: "Cups can collect standing water and create a possible mosquito breeding site.",
            iconAssetPath: "Icons/cup",
            mitigationDescription: "Remove the cup, recycle it, or store it upside down."
        );

        SeedObjectType(
            label: "campfire",
            displayName: "Campfire Pit",
            description: "Campfire pits can collect rainwater when uncovered.",
            iconAssetPath: "Icons/campfire",
            mitigationDescription: "Cover the fire pit when not in use and empty any standing water after rainfall."
        );

        SeedObjectType(
            label: "bucket",
            displayName: "Bucket",
            description: "Buckets can hold standing water and become mosquito breeding sites.",
            iconAssetPath: "Icons/bucket",
            mitigationDescription: "Empty the bucket, cover it, or store it upside down."
        );

        SeedObjectType(
            label: "tire",
            displayName: "Tire",
            description: "Tires can trap rainwater and are common mosquito breeding sites.",
            iconAssetPath: "Icons/tire",
            mitigationDescription: "Drain the tire, cover it, or dispose of it properly."
        );

        SeedObjectType(
            label: "bottle",
            displayName: "Bottle",
            description: "Bottles can hold small amounts of standing water where mosquitoes may breed.",
            iconAssetPath: "Icons/bottle",
            mitigationDescription: "Remove the bottle, recycle it, or store it upside down."
        );
    }

    private void SeedObjectType(
        string label,
        string displayName,
        string description,
        string iconAssetPath,
        string mitigationDescription
    )
    {
        EnsureDatabaseInitialized();

        ObjectType existingObjectType = db.Table<ObjectType>()
            .Where(objectType => objectType.label == label)
            .FirstOrDefault();

        int objectTypeId;

        if (existingObjectType == null)
        {
            ObjectType newObjectType = new ObjectType
            {
                label = label,
                display_name = displayName,
                description = description,
                icon_asset_path = iconAssetPath
            };

            db.Insert(newObjectType);
            objectTypeId = newObjectType.id;
        }
        else
        {
            objectTypeId = existingObjectType.id;

            // Keep seed data updated if we improve names/descriptions later.
            existingObjectType.display_name = displayName;
            existingObjectType.description = description;
            existingObjectType.icon_asset_path = iconAssetPath;
            db.Update(existingObjectType);
        }

        Mitigation existingMitigation = db.Table<Mitigation>()
            .Where(mitigation => mitigation.object_type_id == objectTypeId)
            .FirstOrDefault();

        if (existingMitigation == null)
        {
            Mitigation newMitigation = new Mitigation
            {
                object_type_id = objectTypeId,
                description = mitigationDescription
            };

            db.Insert(newMitigation);
        }
        else
        {
            existingMitigation.description = mitigationDescription;
            db.Update(existingMitigation);
        }
    }

    public int SaveReport(int durationSeconds, int totalObjectsDetected, string notes = "")
    {
        EnsureDatabaseInitialized();

        ScanReport report = new ScanReport
        {
            scanned_at = DateTime.UtcNow.ToString("o"),
            duration_seconds = durationSeconds,
            total_objects_detected = totalObjectsDetected,
            notes = notes ?? ""
        };

        db.Insert(report);

        Debug.Log("Saved ScanReport ID: " + report.id);

        return report.id;
    }

    public int SaveDetection(
        int reportId,
        string objectLabel,
        float bboxX,
        float bboxY,
        float bboxW,
        float bboxH,
        string screenshotPath = ""
    )
    {
        EnsureDatabaseInitialized();

        if (string.IsNullOrWhiteSpace(objectLabel))
        {
            Debug.LogError("Could not save detection. Object label is empty.");
            return -1;
        }

        ObjectType objectType = db.Table<ObjectType>()
            .Where(type => type.label == objectLabel)
            .FirstOrDefault();

        if (objectType == null)
        {
            Debug.LogError("Could not save detection. Unknown object label: " + objectLabel);
            return -1;
        }

        Detection detection = new Detection
        {
            report_id = reportId,
            object_type_id = objectType.id,
            bbox_x = bboxX,
            bbox_y = bboxY,
            bbox_w = bboxW,
            bbox_h = bboxH,
            screenshot_path = screenshotPath ?? "",
            detected_at = DateTime.UtcNow.ToString("o")
        };

        db.Insert(detection);

        Debug.Log("Saved Detection ID: " + detection.id + " for object: " + objectLabel);

        return detection.id;
    }

    public List<DetectionWithDetails> GetDetectionsForReport(int reportId)
    {
        EnsureDatabaseInitialized();

        string query = @"
            SELECT 
                Detection.id AS detection_id,
                Detection.report_id AS report_id,
                ObjectType.display_name AS display_name,
                ObjectType.label AS label,
                ObjectType.description AS object_description,
                Mitigation.description AS mitigation_description,
                Detection.screenshot_path AS screenshot_path,
                Detection.detected_at AS detected_at
            FROM Detection
            INNER JOIN ObjectType ON Detection.object_type_id = ObjectType.id
            LEFT JOIN Mitigation ON Mitigation.object_type_id = ObjectType.id
            WHERE Detection.report_id = ?
        ";

        return db.Query<DetectionWithDetails>(query, reportId);
    }

    public List<ScanReport> GetAllReports()
    {
        EnsureDatabaseInitialized();

        return db.Table<ScanReport>()
            .OrderByDescending(report => report.id)
            .ToList();
    }

    public ScanReport GetReportById(int reportId)
    {
        EnsureDatabaseInitialized();

        return db.Table<ScanReport>()
            .Where(report => report.id == reportId)
            .FirstOrDefault();
    }

    public List<ObjectType> GetObjectTypes()
    {
        EnsureDatabaseInitialized();

        return db.Table<ObjectType>()
            .OrderBy(objectType => objectType.display_name)
            .ToList();
    }

    public void SaveMockScan()
    {
        int reportId = SaveReport(
            durationSeconds: 45,
            totalObjectsDetected: 2,
            notes: "Mock scan for database testing."
        );

        SaveDetection(
            reportId: reportId,
            objectLabel: "cup",
            bboxX: 0.25f,
            bboxY: 0.30f,
            bboxW: 0.15f,
            bboxH: 0.20f,
            screenshotPath: "Screenshots/mock_cup.png"
        );

        SaveDetection(
            reportId: reportId,
            objectLabel: "campfire",
            bboxX: 0.55f,
            bboxY: 0.40f,
            bboxW: 0.25f,
            bboxH: 0.25f,
            screenshotPath: "Screenshots/mock_campfire.png"
        );

        List<DetectionWithDetails> savedDetections = GetDetectionsForReport(reportId);

        Debug.Log("Mock scan saved with Report ID: " + reportId);
        Debug.Log("Mock scan detection count from database: " + savedDetections.Count);

        foreach (DetectionWithDetails detection in savedDetections)
        {
            Debug.Log(
                "Detection loaded from DB: " +
                detection.display_name +
                " | " +
                detection.object_description +
                " | Fix: " +
                detection.mitigation_description
            );
        }
    }
}

[Serializable]
public class ObjectType
{
    [PrimaryKey, AutoIncrement]
    public int id { get; set; }

    [Unique, NotNull]
    public string label { get; set; }

    [NotNull]
    public string display_name { get; set; }

    public string description { get; set; }
    public string icon_asset_path { get; set; }
}

[Serializable]
public class ScanReport
{
    [PrimaryKey, AutoIncrement]
    public int id { get; set; }

    [NotNull]
    public string scanned_at { get; set; }

    public int duration_seconds { get; set; }
    public int total_objects_detected { get; set; }
    public string notes { get; set; }
}

[Serializable]
public class Detection
{
    [PrimaryKey, AutoIncrement]
    public int id { get; set; }

    [Indexed]
    public int report_id { get; set; }

    [Indexed]
    public int object_type_id { get; set; }

    public float bbox_x { get; set; }
    public float bbox_y { get; set; }
    public float bbox_w { get; set; }
    public float bbox_h { get; set; }

    public string screenshot_path { get; set; }

    [NotNull]
    public string detected_at { get; set; }
}

[Serializable]
public class Mitigation
{
    [PrimaryKey, AutoIncrement]
    public int id { get; set; }

    [Indexed]
    public int object_type_id { get; set; }

    [NotNull]
    public string description { get; set; }
}

public class DetectionWithDetails
{
    public int detection_id { get; set; }
    public int report_id { get; set; }

    public string display_name { get; set; }
    public string label { get; set; }

    public string object_description { get; set; }
    public string mitigation_description { get; set; }

    public string screenshot_path { get; set; }
    public string detected_at { get; set; }
}
