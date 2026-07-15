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

    [Header("Detection Filtering")]
    [Tooltip("Low-level model cutoff. Final pins/reports use a stricter threshold in ARPinManager.")]
    [Range(0f, 1f)]
    public float rawConfidenceThreshold = 0.80f;

    [Range(0f, 1f)]
    public float nmsIouThreshold = 0.45f;

    [Tooltip("When enabled, overlapping boxes of different classes compete and only the strongest survives.")]
    public bool classAgnosticNms = true;

    [Tooltip("Limits CPU inference frequency. 0.12 seconds is about 8 inferences per second.")]
    [Min(0f)]
    public float inferenceIntervalSeconds = 0.12f;

    public List<DetectionResult> currentDetections = new List<DetectionResult>();

    // Incremented once for each completed inference result. Consumers use this
    // so one YOLO result cannot be counted multiple times across Unity Update frames.
    public int DetectionFrameId { get; private set; }

    private float nextInferenceTime;

    // Raw sensor dimensions of the most recently processed camera frame (e.g.
    // 1920x1440, landscape sensor mount), before the 90-degree rotation below.
    // ARPinManager needs these to know the true aspect ratio of the image the
    // bbox coordinates are normalized against, since the AR camera background
    // crops that image to cover the screen (rather than letterboxing it).
    public int LastCpuImageWidth { get; private set; }
    public int LastCpuImageHeight { get; private set; }

    void Start()
    {
        runtimeModel = Unity.InferenceEngine.ModelLoader.Load(modelAsset);
        worker = new Unity.InferenceEngine.Worker(runtimeModel, Unity.InferenceEngine.BackendType.CPU);
#if UNITY_EDITOR
        Debug.Log("YOLO model loaded successfully!");
#endif
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

        if (Time.unscaledTime < nextInferenceTime) return;
        nextInferenceTime = Time.unscaledTime + Mathf.Max(0f, inferenceIntervalSeconds);

        RunInference();
    }

    void RunInference()
    {
        if (!cameraManager.TryAcquireLatestCpuImage(out var cpuImage))
        {
#if UNITY_EDITOR
            Debug.Log("[YOLOInference] RunInference: TryAcquireLatestCpuImage FAILED this frame - no CPU image acquired, inference did not run.");
#endif
            return;
        }

        using (cpuImage)
        {
            LastCpuImageWidth = cpuImage.width;
            LastCpuImageHeight = cpuImage.height;

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

            // cpuImage is the raw camera sensor buffer (e.g. 1920x1440, landscape),
            // squashed above to a 640x640 square with no orientation correction. The
            // sensor is mounted landscape on iOS regardless of how the phone is held,
            // so when the user holds the phone portrait (the normal case for this app),
            // the image YOLO receives is still rotated 90 degrees relative to what's on
            // screen - the model sees distorted/sideways objects, which explains zero
            // detections. Rotate 90 degrees clockwise here so the image matches the
            // portrait orientation the user sees. Rotating the already-squashed square
            // (instead of the full-resolution source) gives the same result for far
            // less work, since resizing into equal width/height commutes with rotation.
            // If detections come back rotated/mirrored the wrong way on device, swap
            // this to counter-clockwise: srcRow = destCol; srcCol = INPUT_SIZE - 1 - destRow;
            var floatData = new float[1 * 3 * INPUT_SIZE * INPUT_SIZE];
            for (int destRow = 0; destRow < INPUT_SIZE; destRow++)
            {
                for (int destCol = 0; destCol < INPUT_SIZE; destCol++)
                {
                    int srcRow = INPUT_SIZE - 1 - destCol;
                    int srcCol = destRow;
                    int srcIdx = (srcRow * INPUT_SIZE + srcCol) * 3;
                    int destIdx = destRow * INPUT_SIZE + destCol;

                    floatData[destIdx] = rawBytes[srcIdx] / 255f;
                    floatData[INPUT_SIZE * INPUT_SIZE + destIdx] = rawBytes[srcIdx + 1] / 255f;
                    floatData[2 * INPUT_SIZE * INPUT_SIZE + destIdx] = rawBytes[srcIdx + 2] / 255f;
                }
            }

            rawBytes.Dispose();

            using var inputTensor = new Unity.InferenceEngine.Tensor<float>(
                new Unity.InferenceEngine.TensorShape(1, 3, INPUT_SIZE, INPUT_SIZE), floatData);

            worker.Schedule(inputTensor);

            using var outputTensor = worker.PeekOutput("output0") as Unity.InferenceEngine.Tensor<float>;

            // Derive the output layout from the model itself instead of hardcoding
            // 8400 anchors / 13 classes. A swapped-in ONNX file (different class
            // count, different input resolution, different anchor count) changes
            // this shape, and hardcoded constants would index outputData out of
            // bounds - which is exactly what an IndexOutOfRangeException here means.
            var outputShape = outputTensor.shape;
            if (outputShape.rank != 3 || outputShape[0] != 1)
            {
                Debug.LogError($"[YOLOInference] Unexpected output tensor shape {outputShape} (rank={outputShape.rank}). " +
                                "Expected rank 3, batch 1, e.g. (1, 4+numClasses, numDetections). Skipping this frame - " +
                                "check that the ONNX model's output layout matches what ParseDetections assumes.");
                return;
            }

            int stride = outputShape[1];
            int numDetections = outputShape[2];
            int numClasses = stride - 4;

            if (numClasses != GetLabelCount())
            {
                Debug.LogWarning($"[YOLOInference] Model reports {numClasses} classes but the GetLabel() list has " +
                                  $"{GetLabelCount()} entries - labels will be wrong/\"unknown\" for out-of-range class IDs. " +
                                  "Update the labels array in GetLabel() to match this model.");
            }

            var outputData = outputTensor.DownloadToArray();

            // Unconditional - fires every frame regardless of whether anything below
            // clears CONFIDENCE_THRESHOLD. Lets us tell "the model ran and scored
            // everything low" apart from "this frame never reached inference at all",
            // which the old code (which only logged when count > 0) couldn't show.
            float maxRawConfidence = 0f;
            int maxRawClassId = -1;
            for (int i = 0; i < numDetections; i++)
            {
                for (int c = 4; c < stride; c++)
                {
                    float score = outputData[c * numDetections + i];
                    if (score > maxRawConfidence)
                    {
                        maxRawConfidence = score;
                        maxRawClassId = c - 4;
                    }
                }
            }
#if UNITY_EDITOR
            Debug.Log($"[YOLOInference] RunInference ran: cpuImage={cpuImage.width}x{cpuImage.height} " +
                      $"outputShape={outputShape} numDetections={numDetections} numClasses={numClasses} " +
                      $"maxRawConfidence={maxRawConfidence:F4} (rawThreshold={rawConfidenceThreshold:F2}) " +
                      $"bestClass={GetLabel(maxRawClassId)} t={Time.time:F2}");
#endif

            var rawDetections = ParseDetections(outputData, numDetections, stride);
            currentDetections = ApplyNMS(rawDetections);
            DetectionFrameId++;

            // IMPORTANT: raw detections are not saved here. ARPinManager confirms
            // confidence, spatial consistency, consecutive inference frames, and
            // camera stability before registering anything with ScanManager.

#if UNITY_EDITOR
            if (currentDetections.Count > 0)
                Debug.Log($"Detections this frame: {currentDetections.Count}");
#endif
        }
    }

    List<DetectionResult> ParseDetections(float[] output, int numDetections, int stride)
    {
        var results = new List<DetectionResult>();

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

            if (confidence < rawConfidenceThreshold) continue;

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
                (classAgnosticNms || d.label == best.label) &&
                IoU(best, d) > nmsIouThreshold);
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

    // IMPORTANT: This order must exactly match the class metadata embedded in
    // the ONNX model currently assigned in Unity. The current best.onnx has
    // 12 classes and does NOT include ss_pot. Keeping ss_pot here would shift
    // every later label backward by one (trash can -> tire, watering can ->
    // water hyacinth, etc.).
    static readonly string[] labels = {
        "ss_birdbath",
        "ss_bromiliad",
        "ss_bucket",
        "ss_grill",
        "ss_inflatablepool",
        "ss_tire",
        "ss_trashcan",
        "ss_treehole",
        "ss_waterhyacinth",
        "ss_wateringcan",
        "ss_waterlettuce",
        "ss_wheelbarrow"
    };

    string GetLabel(int classId)
    {
        if (classId >= 0 && classId < labels.Length)
            return labels[classId];

        return "unknown";
    }

    int GetLabelCount() => labels.Length;

    void OnDestroy()
    {
        worker?.Dispose();
    }
}