using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SQLite;
using UnityEngine;

public class DatabaseManager : MonoBehaviour
{
    public static DatabaseManager Instance { get; private set; }

    private SQLiteConnection db;

    private string DatabasePath
    {
        get
        {
            return Path.Combine(
                Application.persistentDataPath,
                "skeeter_sleuth.db"
            );
        }
    }

    private string DetectionImagesDirectory
    {
        get
        {
            return Path.Combine(
                Application.persistentDataPath,
                "DetectionImages"
            );
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        InitializeDatabase();
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
            InitializeDatabase();
    }

    public void InitializeDatabase()
    {
        if (db != null)
            return;

        string directory = Path.GetDirectoryName(DatabasePath);

        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

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
        SeedObjectType(
            label: "ss_birdbath",
            displayName: "Bird Bath",
            description: "Bird baths can hold standing water and become mosquito breeding sites if the water is not changed regularly.",
            iconAssetPath: "Icons/ss_birdbath",
            mitigationDescription: "Empty and scrub the bird bath regularly. Change the water at least once a week. Keep the basin clean to prevent mosquito larvae."
        );

        SeedObjectType(
            label: "ss_bromiliad",
            displayName: "Bromeliad",
            description: "Bromeliads can collect water between their leaves, which may create a small mosquito breeding site.",
            iconAssetPath: "Icons/ss_bromiliad",
            mitigationDescription: "Flush the plant regularly with fresh water. Remove excess standing water from leaf pockets when possible. Monitor the plant after rain."
        );

        SeedObjectType(
            label: "ss_bucket",
            displayName: "Bucket",
            description: "Buckets can collect rainwater and become mosquito breeding sites when left outside.",
            iconAssetPath: "Icons/ss_bucket",
            mitigationDescription: "Empty the bucket after rain. Store it upside down. Keep it covered when not in use."
        );

        SeedObjectType(
            label: "ss_grill",
            displayName: "Grill",
            description: "Outdoor grills and grill covers can trap rainwater, creating areas where mosquitoes may breed.",
            iconAssetPath: "Icons/ss_grill",
            mitigationDescription: "Check the grill and cover for pooled water. Empty any standing water. Store the grill under cover or adjust the cover so water does not collect."
        );

        SeedObjectType(
            label: "ss_inflatablepool",
            displayName: "Inflatable Pool",
            description: "Inflatable pools can hold large amounts of standing water and quickly become mosquito breeding sites when unused.",
            iconAssetPath: "Icons/ss_inflatablepool",
            mitigationDescription: "Drain the pool when not in use. Store it indoors or upside down. Refresh and treat the water if it must remain filled."
        );

        SeedObjectType(
            label: "ss_pot",
            displayName: "Plant Pot",
            description: "Plant pots and saucers can collect water after watering or rainfall, creating small mosquito breeding areas.",
            iconAssetPath: "Icons/ss_pot",
            mitigationDescription: "Empty saucers after watering. Improve drainage. Store unused pots upside down."
        );

        SeedObjectType(
            label: "ss_tire",
            displayName: "Tire",
            description: "Tires can trap rainwater and are one of the most common outdoor mosquito breeding sites.",
            iconAssetPath: "Icons/ss_tire",
            mitigationDescription: "Drain all standing water. Store tires indoors or under cover. Dispose of unused tires properly."
        );

        SeedObjectType(
            label: "ss_trashcan",
            displayName: "Trash Can",
            description: "Trash cans and lids can collect standing water, especially if they are left uncovered or upside down incorrectly.",
            iconAssetPath: "Icons/ss_trashcan",
            mitigationDescription: "Keep the lid closed. Empty any pooled water. Store bins so water cannot collect inside or on top."
        );

        SeedObjectType(
            label: "ss_treehole",
            displayName: "Tree Hole",
            description: "Tree holes can naturally collect rainwater and may become mosquito breeding sites.",
            iconAssetPath: "Icons/ss_treehole",
            mitigationDescription: "Fill small tree holes with sand or expandable foam if appropriate. Monitor after rain. Avoid damaging the tree."
        );

        SeedObjectType(
            label: "ss_waterhyacinth",
            displayName: "Water Hyacinth",
            description: "Floating aquatic plants like water hyacinth can create sheltered areas where mosquitoes may breed.",
            iconAssetPath: "Icons/ss_waterhyacinth",
            mitigationDescription: "Thin or remove excess plants. Keep water moving when possible. Monitor ponds or containers for mosquito larvae."
        );

        SeedObjectType(
            label: "ss_wateringcan",
            displayName: "Watering Can",
            description: "Watering cans can hold leftover water and become mosquito breeding sites when left outside.",
            iconAssetPath: "Icons/ss_wateringcan",
            mitigationDescription: "Empty the watering can after use. Store it upside down. Keep it under cover when not being used."
        );

        SeedObjectType(
            label: "ss_waterlettuce",
            displayName: "Water Lettuce",
            description: "Water lettuce can create sheltered standing-water areas that may support mosquito breeding.",
            iconAssetPath: "Icons/ss_waterlettuce",
            mitigationDescription: "Remove excess plant growth. Keep water circulating when possible. Check regularly for mosquito larvae."
        );

        SeedObjectType(
            label: "ss_wheelbarrow",
            displayName: "Wheelbarrow",
            description: "Wheelbarrows can collect rainwater when left outside upright.",
            iconAssetPath: "Icons/ss_wheelbarrow",
            mitigationDescription: "Empty any standing water. Store the wheelbarrow upside down or under cover. Check it after rainfall."
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

    public int SaveReport(
        int durationSeconds,
        int totalObjectsDetected,
        string notes = ""
    )
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

    public string SaveDetectionScreenshot(
        int reportId,
        string objectLabel,
        byte[] jpgBytes,
        int imageIndex = 0
    )
    {
        if (jpgBytes == null || jpgBytes.Length == 0)
        {
            Debug.LogWarning("[DatabaseManager] Detection screenshot bytes were empty.");
            return "";
        }

        try
        {
            if (!Directory.Exists(DetectionImagesDirectory))
                Directory.CreateDirectory(DetectionImagesDirectory);

            string safeLabel = SanitizeFileNamePart(objectLabel);

            string fileName =
                "report_" + reportId +
                "_" + safeLabel +
                "_" + imageIndex +
                "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff") +
                ".jpg";

            string fullPath = Path.Combine(
                DetectionImagesDirectory,
                fileName
            );

            File.WriteAllBytes(fullPath, jpgBytes);

            Debug.Log("[DatabaseManager] Saved detection screenshot: " + fullPath);

            return fullPath;
        }
        catch (Exception e)
        {
            Debug.LogError(
                "[DatabaseManager] Failed to save detection screenshot: " +
                e.Message
            );

            return "";
        }
    }

    private string SanitizeFileNamePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        char[] invalidCharacters = Path.GetInvalidFileNameChars();

        char[] sanitized = value
            .Select(character =>
                invalidCharacters.Contains(character)
                    ? '_'
                    : character)
            .ToArray();

        return new string(sanitized);
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
            Debug.LogError(
                "Could not save detection. Unknown object label: " +
                objectLabel
            );
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

        Debug.Log(
            "Saved Detection ID: " + detection.id +
            " for object: " + objectLabel
        );

        return detection.id;
    }

    public List<DetectionWithDetails> GetDetectionsForReport(
        int reportId
    )
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
            INNER JOIN ObjectType
                ON Detection.object_type_id = ObjectType.id
            LEFT JOIN Mitigation
                ON Mitigation.object_type_id = ObjectType.id
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

    public void ClearScanHistory()
    {
        EnsureDatabaseInitialized();

        List<string> screenshotPaths = db.Table<Detection>()
            .ToList()
            .Select(detection => detection.screenshot_path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct()
            .ToList();

        foreach (string screenshotPath in screenshotPaths)
        {
            try
            {
                if (File.Exists(screenshotPath))
                    File.Delete(screenshotPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "[DatabaseManager] Could not delete screenshot " +
                    screenshotPath + ": " + e.Message
                );
            }
        }

        db.DeleteAll<Detection>();
        db.DeleteAll<ScanReport>();

        try
        {
            if (Directory.Exists(DetectionImagesDirectory) &&
                Directory.GetFiles(DetectionImagesDirectory).Length == 0)
            {
                Directory.Delete(DetectionImagesDirectory);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning(
                "[DatabaseManager] Could not clean screenshot directory: " +
                e.Message
            );
        }

        Debug.Log("[DatabaseManager] Scan history cleared.");
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
