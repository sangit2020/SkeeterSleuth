using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;

public class YOLOInference : MonoBehaviour
{
    [Header("References")]
    public Unity.InferenceEngine.ModelAsset modelAsset;
    public ARCameraManager cameraManager;
    public ScanManager scanManager;

    private Unity.InferenceEngine.Model runtimeModel;
    private Unity.InferenceEngine.Worker worker;

    const int INPUT_SIZE = 640;
    const float CONFIDENCE_THRESHOLD = 0.70f;
    const float NMS_IOU_THRESHOLD = 0.45f;

    public List<DetectionResult> currentDetections = new List<DetectionResult>();

    void Start()
    {
        runtimeModel = Unity.InferenceEngine.ModelLoader.Load(modelAsset);
        worker = new Unity.InferenceEngine.Worker(runtimeModel, Unity.InferenceEngine.BackendType.CPU);
        Debug.Log("YOLO model loaded successfully!");
    }

    void OnEnable()
    {
        if (cameraManager != null)
            cameraManager.frameReceived += OnCameraFrameReceived;
    }

    void OnDisable()
    {
        if (cameraManager != null)
            cameraManager.frameReceived -= OnCameraFrameReceived;
    }

    void OnCameraFrameReceived(ARCameraFrameEventArgs args)
    {
        if (scanManager != null && !scanManager.IsScanning()) return;
        RunInference();
    }

    void RunInference()
    {
        if (!cameraManager.TryAcquireLatestCpuImage(out var cpuImage)) return;

        using (cpuImage)
        {
            var conversionParams = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),
                outputDimensions = new Vector2Int(INPUT_SIZE, INPUT_SIZE),
                outputFormat = TextureFormat.RGB24
            };

            var rawBytes = new Unity.Collections.NativeArray<byte>(
                INPUT_SIZE * INPUT_SIZE * 3,
                Unity.Collections.Allocator.Temp
            );

            cpuImage.Convert(conversionParams, rawBytes);

            var floatData = new float[1 * 3 * INPUT_SIZE * INPUT_SIZE];
            for (int i = 0; i < INPUT_SIZE * INPUT_SIZE; i++)
            {
                floatData[i] = rawBytes[i * 3] / 255f;
                floatData[INPUT_SIZE * INPUT_SIZE + i] = rawBytes[i * 3 + 1] / 255f;
                floatData[2 * INPUT_SIZE * INPUT_SIZE + i] = rawBytes[i * 3 + 2] / 255f;
            }

            rawBytes.Dispose();

            using var inputTensor = new Unity.InferenceEngine.Tensor<float>(
                new Unity.InferenceEngine.TensorShape(1, 3, INPUT_SIZE, INPUT_SIZE), floatData);
            worker.Schedule(inputTensor);

            using var outputTensor = worker.PeekOutput("output0") as Unity.InferenceEngine.Tensor<float>;
            var outputData = outputTensor.DownloadToArray();

            var rawDetections = ParseDetections(outputData);
            currentDetections = ApplyNMS(rawDetections);

            if (currentDetections.Count > 0)
                Debug.Log($"Detections this frame: {currentDetections.Count}");
        }
    }

    List<DetectionResult> ParseDetections(float[] output)
    {
        var results = new List<DetectionResult>();
        int numDetections = 8400;
        int numClasses = 10;
        int stride = 4 + numClasses;

        for (int i = 0; i < numDetections; i++)
        {
            float confidence = 0f;
            int classId = -1;

            for (int c = 4; c < stride; c++)
            {
                float score = output[c * numDetections + i];
                if (score > confidence)
                {
                    confidence = score;
                    classId = c - 4;
                }
            }

            if (confidence < CONFIDENCE_THRESHOLD) continue;

            float cx = output[0 * numDetections + i];
            float cy = output[1 * numDetections + i];
            float w  = output[2 * numDetections + i];
            float h  = output[3 * numDetections + i];

            results.Add(new DetectionResult
            {
                label = GetLabel(classId),
                bbox_x = (cx - w / 2f) / INPUT_SIZE,
                bbox_y = (cy - h / 2f) / INPUT_SIZE,
                bbox_w = w / INPUT_SIZE,
                bbox_h = h / INPUT_SIZE,
                confidence = confidence
            });
        }

        return results;
    }

    List<DetectionResult> ApplyNMS(List<DetectionResult> detections)
    {
        detections.Sort((a, b) => b.confidence.CompareTo(a.confidence));
        var kept = new List<DetectionResult>();

        while (detections.Count > 0)
        {
            var best = detections[0];
            kept.Add(best);
            detections.RemoveAt(0);

            detections.RemoveAll(d =>
                d.label == best.label && IoU(best, d) > NMS_IOU_THRESHOLD);
        }

        return kept;
    }

    float IoU(DetectionResult a, DetectionResult b)
    {
        float ax2 = a.bbox_x + a.bbox_w;
        float ay2 = a.bbox_y + a.bbox_h;
        float bx2 = b.bbox_x + b.bbox_w;
        float by2 = b.bbox_y + b.bbox_h;

        float ix1 = Mathf.Max(a.bbox_x, b.bbox_x);
        float iy1 = Mathf.Max(a.bbox_y, b.bbox_y);
        float ix2 = Mathf.Min(ax2, bx2);
        float iy2 = Mathf.Min(ay2, by2);

        float interW = Mathf.Max(0, ix2 - ix1);
        float interH = Mathf.Max(0, iy2 - iy1);
        float intersection = interW * interH;

        float aArea = a.bbox_w * a.bbox_h;
        float bArea = b.bbox_w * b.bbox_h;
        float union = aArea + bArea - intersection;

        return union <= 0 ? 0 : intersection / union;
    }

    string GetLabel(int classId)
    {
        string[] labels = {
            "ss_birdbath", "ss_bromiliad", "ss_bucket", "ss_pot",
            "ss_puddle", "ss_tire", "ss_trashcan", "ss_treehole",
            "ss_wateringcan", "ss_wheelbarrow"
        };
        if (classId >= 0 && classId < labels.Length)
            return labels[classId];
        return "unknown";
    }

    void OnDestroy()
    {
        worker?.Dispose();
    }
}