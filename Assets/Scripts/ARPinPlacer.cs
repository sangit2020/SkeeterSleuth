using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.InputSystem;

public class ARPinPlacer : MonoBehaviour
{
    [Header("AR Components")]
    public ARRaycastManager raycastManager;
    
    [Header("Placement Settings")]
    public GameObject pinPrefab; 

    private List<ARRaycastHit> hitResults = new List<ARRaycastHit>();

    public void PlacePinFromDetection(Vector2 screenPosition, string hazardLabel)
    {
        if (raycastManager.Raycast(screenPosition, hitResults, TrackableType.PlaneWithinPolygon))
        {
            Pose hitPose = hitResults[0].pose;
            GameObject spawnedPin = Instantiate(pinPrefab, hitPose.position, hitPose.rotation);
            spawnedPin.name = $"AR_Plane_Pin_{hazardLabel}";
            Debug.Log($"[SkeeterSleuth] Anchored AR Pin on plane for: {hazardLabel}");
        }
        else
        {
            if (Camera.main != null)
            {
                Vector3 spawnPos = Camera.main.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, 1.5f));
                GameObject spawnedPin = Instantiate(pinPrefab, spawnPos, Quaternion.identity);
                spawnedPin.name = $"Fallback_Spatial_Pin_{hazardLabel}";
                Debug.Log($"[SkeeterSleuth] No AR plane detected yet. Spawned placeholder pin in space at: {spawnPos}");
            }
        }
    }

    // Debug function: tap to place pin
    /*
    void Update()
    {
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            Vector2 touchPos = Touchscreen.current.primaryTouch.position.ReadValue();
            PlacePinFromDetection(touchPos, "Mobile_Test");
        }
        
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            PlacePinFromDetection(mousePos, "Editor_Test");
        }
    }*/
}