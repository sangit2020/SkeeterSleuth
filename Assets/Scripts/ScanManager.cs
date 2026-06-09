using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class ScanManager : MonoBehaviour
{
    [Header("UI References")]
    public GameObject beginScanButton;
    public GameObject scanningIndicator;
    public GameObject scanCompletePanel;
    public TextMeshProUGUI breedingSitesText;
    public TextMeshProUGUI scanDurationText;
    public TextMeshProUGUI itemsDetectedText;
    public TextMeshProUGUI mitigationPreviewText;

    private bool isScanning = false;
    private float scanStartTime;
    private Dictionary<string, int> detectedCounts = new Dictionary<string, int>();

    private Dictionary<string, string> mitigationMap = new Dictionary<string, string>()
    {
        { "tire",         "Empty and flip discarded tires to prevent pooling." },
        { "treehole",     "Pack tree holes with sand or foam to block water." },
        { "catch_basin",  "Report clogged catch basins to local authorities." },
        { "french_drain", "Clear debris from French drain to restore drainage." },
        { "gutter",       "Clean gutters of leaves and standing water." }
    };

    public void OnBeginScanPressed()
    {
        isScanning = true;
        detectedCounts.Clear();
        scanStartTime = Time.time;

        beginScanButton.SetActive(false);
        scanningIndicator.SetActive(true);
        scanCompletePanel.SetActive(false);
    }

    public void OnStopScanPressed()
    {
        isScanning = false;
        scanningIndicator.SetActive(false);
        ShowScanComplete();
    }

    void ShowScanComplete()
    {
        scanCompletePanel.SetActive(true);

        int totalCount = 0;
        foreach (var count in detectedCounts.Values) totalCount += count;

        int duration = Mathf.RoundToInt(Time.time - scanStartTime);

        breedingSitesText.text = totalCount.ToString();
        scanDurationText.text  = duration + "s";

        if (detectedCounts.Count == 0)
        {
            itemsDetectedText.text     = "No items detected";
            mitigationPreviewText.text = "";
        }
        else
        {
            string itemList = "";
            string mitList  = "";

            foreach (var kvp in detectedCounts)
            {
                itemList += kvp.Key + " x" + kvp.Value + "\n";
                if (mitigationMap.ContainsKey(kvp.Key))
                    mitList += "• " + mitigationMap[kvp.Key] + "\n";
            }

            itemsDetectedText.text     = itemList;
            mitigationPreviewText.text = mitList;
        }
    }

    public void RegisterDetection(string label)
    {
        if (!isScanning) return;

        if (detectedCounts.ContainsKey(label))
            detectedCounts[label]++;
        else
            detectedCounts[label] = 1;
    }

    public bool IsScanning() => isScanning;
}