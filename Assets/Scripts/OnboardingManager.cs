using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;

public class OnboardingManager : MonoBehaviour
{
    [Header("Panels")]
    public GameObject[] panels;

    [Header("Dots")]
    public Image[] dots;
    public Color activeDotColor = Color.white;
    public Color inactiveDotColor = new Color(1, 1, 1, 0.4f);

    [Header("Buttons")]
    public GameObject btnNext;
    public GameObject btnGetStarted;

    [Header("Camera Background")]
    public RawImage cameraBackground;

    private int currentPanel = 0;
    private WebCamTexture webCamTexture;

    void Start()
    {
        // Show only first panel
        for (int i = 0; i < panels.Length; i++)
            panels[i].SetActive(i == 0);

        // Hide Get Started initially
        btnGetStarted.SetActive(false);

        UpdateDots();
        UpdateButtons();
        StartCoroutine(RequestCameraPermission());
    }

    IEnumerator RequestCameraPermission()
    {
        yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);

        if (Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            webCamTexture = new WebCamTexture();
            cameraBackground.texture = webCamTexture;
            webCamTexture.Play();
        }
    }

    public void OnNextPressed()
    {
        if (currentPanel < panels.Length - 1)
        {
            panels[currentPanel].SetActive(false);
            currentPanel++;
            panels[currentPanel].SetActive(true);
            UpdateDots();
            UpdateButtons();
        }
    }

    public void OnGetStartedPressed()
    {
        if (webCamTexture != null)
            webCamTexture.Stop();

        PlayerPrefs.SetInt("OnboardingComplete", 1);
        PlayerPrefs.Save();
        SceneManager.LoadScene("ARScreen");
    }

    void UpdateDots()
    {
        for (int i = 0; i < dots.Length; i++)
            dots[i].color = (i == currentPanel) ? activeDotColor : inactiveDotColor;
    }

    void UpdateButtons()
    {
        bool isLastPanel = currentPanel == panels.Length - 1;
        btnNext.SetActive(!isLastPanel);
        btnGetStarted.SetActive(isLastPanel);
    }
}