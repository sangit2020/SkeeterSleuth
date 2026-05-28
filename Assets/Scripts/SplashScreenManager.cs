using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SplashScreenManager : MonoBehaviour
{
    [Header("Timing")]
    public float splashDuration = 3f;

    void Start()
    {
        StartCoroutine(HandleSplash());
    }

    IEnumerator HandleSplash()
    {
        // Wait for the splash duration
        yield return new WaitForSeconds(splashDuration);

        // Check if user has completed onboarding before
        bool onboardingDone = PlayerPrefs.GetInt("OnboardingComplete", 0) == 1;

        if (onboardingDone)
        {
            // Skip onboarding, go straight to AR
            SceneManager.LoadScene("ARScreen");
        }
        else
        {
            // First time — show onboarding
            SceneManager.LoadScene("OnboardingScreen");
        }
    }
}