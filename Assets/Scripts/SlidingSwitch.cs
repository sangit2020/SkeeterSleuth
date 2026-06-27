using UnityEngine;
using UnityEngine.UI;

public class SlidingSwitch : MonoBehaviour
{
    [Header("Positions")]
    public RectTransform handle;
    public float offXPosition = 60f;
    public float onXPosition = 250f;

    [Header("Visual Elements")]
    public Image backgroundImage; 
    public string offHexColor = "#D3D3D3"; 
    public string onHexColor = "#173404";  

    private Toggle toggle;
    private Color colorOff;
    private Color colorOn;

    void Awake()
    {
        toggle = GetComponent<Toggle>();
        
        ColorUtility.TryParseHtmlString(offHexColor, out colorOff);
        ColorUtility.TryParseHtmlString(onHexColor, out colorOn);

        toggle.onValueChanged.AddListener(AnimateSwitch);
        AnimateSwitch(toggle.isOn);
    }

    void AnimateSwitch(bool isOn)
    {
        handle.anchoredPosition = new Vector2(isOn ? onXPosition : offXPosition, handle.anchoredPosition.y);
        
        if (backgroundImage != null)
        {
            backgroundImage.color = isOn ? colorOn : colorOff;
        }
    }
}