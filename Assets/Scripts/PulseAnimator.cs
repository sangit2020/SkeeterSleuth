using UnityEngine;
using UnityEngine.UI;

public class PulseAnimator : MonoBehaviour
{
    public float minAlpha = 0.3f;
    public float maxAlpha = 1f;
    public float speed = 1.4f;

    private Image img;

    void Awake()
    {
        img = GetComponent<Image>();
    }

    void Update()
    {
        float t = (Mathf.Sin(Time.time * speed) + 1f) / 2f;
        float alpha = Mathf.Lerp(minAlpha, maxAlpha, t);
        Color c = img.color;
        c.a = alpha;
        img.color = c;
    }
}