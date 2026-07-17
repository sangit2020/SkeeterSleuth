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

    [Header("Saved Detection Crops")]
    [Tooltip("The model input uses top-left image coordinates, while Unity textures use a bottom-left origin. Leave this enabled unless saved photos appear upside down.")]
    public bool flipSavedCropsVertically = true;

    [Tooltip("Enable only if saved photos appear mirrored on the target device.")]
    public bool flipSavedCropsHorizontally = false;

    public List<DetectionResult> currentDetections = new List<DetectionResult>();

    // Incremented once for each completed inference result. Consumers use this
    // so one YOLO result cannot be counted multiple times across Unity Update frames.
    public int DetectionFrameId { get; private set; }

    private float nextInferenceTime;

    // Stores the exact 640x640 RGB frame that was passed to YOLO after orientation
    // correction. Cropping from this frame guarantees that the normalized YOLO
    // bounding box lines up with the saved image.
    private byte[] latestOrientedRgb24;
    private bool hasLatestInferenceFrame;

    // Raw sensor dimensions of the most recently processed camera frame (e.g.
    // 1920x1440, landscape sensor mount), before the 90-degree rotation below.
    // ARPinManager needs these to know the true aspect ratio of the image the
    // bbox coordinates are normalized against, since the AR camera background
    // crops that image to cover the screen (rather than letterboxing it).
    public int LastCpuImageWidth { get; private set; }
    public int LastCpuImageHeight { get; private set; }

    void Start()
    {
        if (scanManager == null)
            scanManager = UnityEngine.Object.FindFirstObjectByType<ScanManager>();

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

            if (latestOrientedRgb24 == null ||
                latestOrientedRgb24.Length != INPUT_SIZE * INPUT_SIZE * 3)
            {
                latestOrientedRgb24 = new byte[INPUT_SIZE * INPUT_SIZE * 3];
            }

            // cpuImage is the raw camera sensor buffer (e.g. 1920x1440, landscape),
            // squashed above to a 640x640 square with no orientation correction. The
            // sensor is mounted landscape on iOS regardless of how the phone is held,
            // so when the user holds the phone portrait (the normal case for this app),
            // the image YOLO receives is still rotated 90 degrees relative to what's on
            // screen. Rotate 90 degrees clockwise so the model input matches portrait.
            //
            // The same oriented RGB bytes are kept for photo cropping. Because both
            // detection and cropping use this exact frame, bbox coordinates stay aligned.
            var floatData = new float[1 * 3 * INPUT_SIZE * INPUT_SIZE];

            for (int destRow = 0; destRow < INPUT_SIZE; destRow++)
            {
                for (int destCol = 0; destCol < INPUT_SIZE; destCol++)
                {
                    int srcRow = INPUT_SIZE - 1 - destCol;
                    int srcCol = destRow;
                    int srcIdx = (srcRow * INPUT_SIZE + srcCol) * 3;
                    int destPixelIdx = destRow * INPUT_SIZE + destCol;
                    int destRgbIdx = destPixelIdx * 3;

                    byte r = rawBytes[srcIdx];
                    byte g = rawBytes[srcIdx + 1];
                    byte b = rawBytes[srcIdx + 2];

                    latestOrientedRgb24[destRgbIdx] = r;
                    latestOrientedRgb24[destRgbIdx + 1] = g;
                    latestOrientedRgb24[destRgbIdx + 2] = b;

                    floatData[destPixelIdx] = r / 255f;
                    floatData[INPUT_SIZE * INPUT_SIZE + destPixelIdx] = g / 255f;
                    floatData[2 * INPUT_SIZE * INPUT_SIZE + destPixelIdx] = b / 255f;
                }
            }

            hasLatestInferenceFrame = true;
            rawBytes.Dispose();

            using var inputTensor = new Unity.InferenceEngine.Tensor<float>(
                new Unity.InferenceEngine.TensorShape(1, 3, INPUT_SIZE, INPUT_SIZE), floatData);

            worker.Schedule(inputTensor);

            using var outputTensor = worker.PeekOutput("output0") as Unity.InferenceEngine.Tensor<float>;

            if (outputTensor == null)
            {
                Debug.LogError("[YOLOInference] Could not read model output named output0.");
                return;
            }

            // Derive the output layout from the model itself instead of hardcoding
            // 8400 anchors / a fixed class count.
            var outputShape = outputTensor.shape;
            if (outputShape.rank != 3 || outputShape[0] != 1)
            {
                Debug.LogError($"[YOLOInference] Unexpected output tensor shape {outputShape} (rank={outputShape.rank}). " +
                               "Expected rank 3, batch 1, e.g. (1, 4+numClasses, numDetections). Skipping this frame.");
                return;
            }

            int stride = outputShape[1];
            int numDetections = outputShape[2];
            int numClasses = stride - 4;

            if (numClasses != GetLabelCount())
            {
                Debug.LogWarning($"[YOLOInference] Model reports {numClasses} classes but the labels list has " +
                                 $"{GetLabelCount()} entries. Update the labels array to match the ONNX model.");
            }

            var outputData = outputTensor.DownloadToArray();

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

            // ScanManager only uses this callback for photo candidate selection.
            // It does not register raw detections in the report. ARPinManager still
            // decides which detections are actually confirmed.
            if (scanManager == null)
                scanManager = ScanManager.Instance;

            if (scanManager != null)
            {
                scanManager.OnInferenceFrameCompletedForPhotoSelection(
                    DetectionFrameId,
                    currentDetections
                );
            }

#if UNITY_EDITOR
            if (currentDetections.Count > 0)
                Debug.Log($"Detections this frame: {currentDetections.Count}");
#endif
        }
    }

    /// <summary>
    /// Crops the latest oriented 640x640 YOLO input around a normalized detection
    /// bounding box, adds padding, and returns a JPG. Because this uses the exact
    /// frame that YOLO saw, no separate camera-to-model coordinate conversion is needed.
    /// </summary>
    public bool TryCreatePaddedCropJpg(
        DetectionResult detection,
        float paddingPercent,
        int jpegQuality,
        out byte[] jpgBytes
    )
    {
        jpgBytes = null;

        if (!hasLatestInferenceFrame ||
            latestOrientedRgb24 == null ||
            latestOrientedRgb24.Length != INPUT_SIZE * INPUT_SIZE * 3)
        {
            Debug.LogWarning("[YOLOInference] Cannot create crop because no inference frame is available yet.");
            return false;
        }

        if (detection == null || detection.bbox_w <= 0f || detection.bbox_h <= 0f)
        {
            Debug.LogWarning("[YOLOInference] Cannot create crop because the detection bbox is invalid.");
            return false;
        }

        float padding = Mathf.Clamp(paddingPercent, 0f, 0.5f);

        float xMinNormalized = Mathf.Clamp01(detection.bbox_x - detection.bbox_w * padding);
        float yMinNormalized = Mathf.Clamp01(detection.bbox_y - detection.bbox_h * padding);
        float xMaxNormalized = Mathf.Clamp01(detection.bbox_x + detection.bbox_w * (1f + padding));
        float yMaxNormalized = Mathf.Clamp01(detection.bbox_y + detection.bbox_h * (1f + padding));

        int xMin = Mathf.Clamp(
            Mathf.FloorToInt(xMinNormalized * INPUT_SIZE),
            0,
            INPUT_SIZE - 1
        );

        int yMin = Mathf.Clamp(
            Mathf.FloorToInt(yMinNormalized * INPUT_SIZE),
            0,
            INPUT_SIZE - 1
        );

        int xMax = Mathf.Clamp(
            Mathf.CeilToInt(xMaxNormalized * INPUT_SIZE),
            xMin + 1,
            INPUT_SIZE
        );

        int yMax = Mathf.Clamp(
            Mathf.CeilToInt(yMaxNormalized * INPUT_SIZE),
            yMin + 1,
            INPUT_SIZE
        );

        int cropWidth = xMax - xMin;
        int cropHeight = yMax - yMin;

        if (cropWidth < 2 || cropHeight < 2)
        {
            Debug.LogWarning("[YOLOInference] Calculated crop was too small.");
            return false;
        }

        byte[] croppedRgb24 = new byte[cropWidth * cropHeight * 3];

        for (int sourceRowOffset = 0; sourceRowOffset < cropHeight; sourceRowOffset++)
        {
            int sourceRow = yMin + sourceRowOffset;
            int destinationRow = flipSavedCropsVertically
                ? cropHeight - 1 - sourceRowOffset
                : sourceRowOffset;

            if (!flipSavedCropsHorizontally)
            {
                int sourceByteIndex = (sourceRow * INPUT_SIZE + xMin) * 3;
                int destinationByteIndex = destinationRow * cropWidth * 3;

                System.Buffer.BlockCopy(
                    latestOrientedRgb24,
                    sourceByteIndex,
                    croppedRgb24,
                    destinationByteIndex,
                    cropWidth * 3
                );
            }
            else
            {
                for (int sourceColumnOffset = 0; sourceColumnOffset < cropWidth; sourceColumnOffset++)
                {
                    int sourceColumn = xMin + sourceColumnOffset;
                    int destinationColumn = cropWidth - 1 - sourceColumnOffset;

                    int sourceByteIndex = (sourceRow * INPUT_SIZE + sourceColumn) * 3;
                    int destinationByteIndex =
                        (destinationRow * cropWidth + destinationColumn) * 3;

                    croppedRgb24[destinationByteIndex] = latestOrientedRgb24[sourceByteIndex];
                    croppedRgb24[destinationByteIndex + 1] = latestOrientedRgb24[sourceByteIndex + 1];
                    croppedRgb24[destinationByteIndex + 2] = latestOrientedRgb24[sourceByteIndex + 2];
                }
            }
        }

        Texture2D cropTexture = null;

        try
        {
            cropTexture = new Texture2D(
                cropWidth,
                cropHeight,
                TextureFormat.RGB24,
                false
            );

            cropTexture.LoadRawTextureData(croppedRgb24);
            cropTexture.Apply(false, false);

            jpgBytes = cropTexture.EncodeToJPG(Mathf.Clamp(jpegQuality, 1, 100));

            return jpgBytes != null && jpgBytes.Length > 0;
        }
        catch (System.Exception e)
        {
            Debug.LogError("[YOLOInference] Failed to encode detection crop: " + e.Message);
            jpgBytes = null;
            return false;
        }
        finally
        {
            if (cropTexture != null)
                Destroy(cropTexture);
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
            float w = output[2 * numDetections + i];
            float h = output[3 * numDetections + i];

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

        float interW = Mathf.Max(0f, ix2 - ix1);
        float interH = Mathf.Max(0f, iy2 - iy1);
        float intersection = interW * interH;

        float aArea = a.bbox_w * a.bbox_h;
        float bArea = b.bbox_w * b.bbox_h;
        float union = aArea + bArea - intersection;

        return union <= 0f ? 0f : intersection / union;
    }

    // IMPORTANT: This order must exactly match the class metadata embedded in
    // the ONNX model currently assigned in Unity. The current best.onnx has
    // 12 classes and does NOT include ss_pot.
    static readonly string[] labels =
    {
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

    int GetLabelCount()
    {
        return labels.Length;
    }

    void OnDestroy()
    {
        worker?.Dispose();
    }
}
