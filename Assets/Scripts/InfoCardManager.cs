using UnityEngine;
using UnityEngine.InputSystem;

public class InfoCardManager : MonoBehaviour
{
    [Header("UI Panels")]
    public GameObject infoCardUI;

    void Start()
    {
        if (infoCardUI != null)
        {
            infoCardUI.SetActive(false);
        }
    }
    
    void Update()
    {
        // Tap
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            DetectPinTap(Touchscreen.current.primaryTouch.position.ReadValue());
        }
        
        // Click
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            DetectPinTap(Mouse.current.position.ReadValue());
        }
    }

    private void DetectPinTap(Vector2 screenPosition)
    {
        // Shoot a 3D physics laser from the camera to the screen tap position
        Ray ray = Camera.main.ScreenPointToRay(screenPosition);
        
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            // If the laser hits an object tagged as pin
            if (hit.collider.CompareTag("HazardPin"))
            {
                Debug.Log("[SkeeterSleuth] Pin Tapped! Opening Info Card.");
                
                // Open the mitigation strategy card
                if (infoCardUI != null)
                {
                    infoCardUI.SetActive(true);
                }
            }
        }
    }

    // Closing button 
    public void CloseInfoCard()
    {
        if (infoCardUI != null)
        {
            infoCardUI.SetActive(false);
        }
    }
}