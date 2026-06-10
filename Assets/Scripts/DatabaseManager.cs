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
        // IMPORTANT:
        // The label field must match the raw YOLO class name from the YAML file exactly.
        // The displayName field is what the user sees in the report UI.

        SeedObjectType(
            label: "ss_birdbath",
            displayName: "Bird Bath",
            description: "Bird baths can hold standing water and become mosquito breeding sites if the water is not changed regularly.",
            iconAssetPath: "Icons/birdbath",
            mitigationDescription: "Empty and scrub the bird bath regularly; Change the water at least once a week; Keep the basin clean to prevent mosquito larvae."
        );

        SeedObjectType(
            label: "ss_bromiliad",
            displayName: "Bromeliad",
            description: "Bromeliads can collect water between their leaves, which may create a hidden mosquito breeding area.",
            iconAssetPath: "Icons/bromeliad",
            mitigationDescription: "Flush the plant cups with fresh water regularly; Remove trapped debris; Avoid letting water sit for long periods."
        );

        SeedObjectType(
            label: "ss_bucket",
            displayName: "Bucket",
            description: "Buckets can collect rainwater and are common mosquito breeding sites when left outside.",
            iconAssetPath: "Icons/bucket",
            mitigationDescription: "Empty the bucket after rain; Store it upside down; Keep it covered when not in use."
        );

        SeedObjectType(
            label: "ss_pot",
            displayName: "Planter / Empty Pot",
            description: "Empty pots and planters can collect standing water, especially in the bottom or drainage areas.",
            iconAssetPath: "Icons/pot",
            mitigationDescription: "Empty any collected water; Store unused pots upside down; Check drainage holes to make sure water can flow out."
        );

        SeedObjectType(
            label: "ss_tire",
            displayName: "Tire",
            description: "Tires can trap rainwater and are one of the most common outdoor mosquito breeding sites.",
            iconAssetPath: "Icons/tire",
            mitigationDescription: "Drain all standing water; Store tires indoors or under cover; Dispose of unused tires properly."
        );

        SeedObjectType(
            label: "ss_trashcan",
            displayName: "Trash Can",
            description: "Trash cans and lids can collect standing water if left uncovered or upside down.",
            iconAssetPath: "Icons/trashcan",
            mitigationDescription: "Keep the trash can covered; Empty water from lids or bottoms; Store bins where rainwater cannot collect."
        );

        SeedObjectType(
            label: "ss_treehole",
            displayName: "Tree Hole",
            description: "Tree holes can naturally hold water and may provide a protected area for mosquitoes to breed.",
            iconAssetPath: "Icons/treehole",
            mitigationDescription: "Flush the tree hole with water when possible; Remove organic debris; Contact a professional if filling or treating the hole is needed."
        );

        SeedObjectType(
            label: "ss_waterhyacinth",
            displayName: "Water Hyacinth",
            description: "Water hyacinths can create still, shaded water areas where mosquitoes may breed.",
            iconAssetPath: "Icons/waterhyacinth",
            mitigationDescription: "Thin or remove excessive plants; Keep water moving if possible; Check nearby standing water for larvae."
        );

        SeedObjectType(
            label: "ss_wateringcan",
            displayName: "Watering Can",
            description: "Watering cans can hold leftover water and become mosquito breeding sites if stored outside.",
            iconAssetPath: "Icons/wateringcan",
            mitigationDescription: "Empty after each use; Store upside down; Keep indoors or under cover when not in use."
        );

        SeedObjectType(
            label: "ss_waterlettuce",
            displayName: "Water Lettuce",
            description: "Water lettuce can create still water pockets and shaded areas that may support mosquito breeding.",
            iconAssetPath: "Icons/waterlettuce",
            mitigationDescription: "Thin or remove excess plants; Keep water circulating; Inspect the area regularly for mosquito larvae."
        );

        SeedObjectType(
            label: "ss_wheelbarrow",
            displayName: "Wheelbarrow",
            description: "Wheelbarrows can collect rainwater when left outside upright.",
            iconAssetPath: "Icons/wheelbarrow",
            mitigationDescription: "Tip the wheelbarrow over after use; Store it under cover; Empty any standing water after rainfall."
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
            totalObjectsDetected: 3,
            notes: "Mock scan for database testing with current YOLO labels."
        );

        SaveDetection(
            reportId: reportId,
            objectLabel: "ss_bucket",
            bboxX: 0.25f,
            bboxY: 0.30f,
            bboxW: 0.15f,
            bboxH: 0.20f,
            screenshotPath: "Screenshots/mock_bucket.png"
        );

        SaveDetection(
            reportId: reportId,
            objectLabel: "ss_tire",
            bboxX: 0.55f,
            bboxY: 0.40f,
            bboxW: 0.25f,
            bboxH: 0.25f,
            screenshotPath: "Screenshots/mock_tire.png"
        );

        SaveDetection(
            reportId: reportId,
            objectLabel: "ss_birdbath",
            bboxX: 0.40f,
            bboxY: 0.50f,
            bboxW: 0.20f,
            bboxH: 0.20f,
            screenshotPath: "Screenshots/mock_birdbath.png"
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