using UnityEngine;


public class YOLOInference : MonoBehaviour
{
    public Unity.InferenceEngine.ModelAsset modelAsset;
    private Unity.InferenceEngine.Model runtimeModel;
    private Unity.InferenceEngine.Worker worker;

    void Start()
    {
        runtimeModel = Unity.InferenceEngine.ModelLoader.Load(modelAsset);
        worker = new Unity.InferenceEngine.Worker(runtimeModel, Unity.InferenceEngine.BackendType.CPU);
        Debug.Log("YOLO model loaded successfully!");
    }

    void OnDestroy()
    {
        worker?.Dispose();
    }
}