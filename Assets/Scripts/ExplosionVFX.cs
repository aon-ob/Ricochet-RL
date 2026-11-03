using UnityEngine;

public class ExplosionVFX : MonoBehaviour
{
    [SerializeField] private float duration = 0.25f;
    [SerializeField] private float targetRadius = 4f; // Matches grenade blast radius

    private Material matInstance;
    private float timer = 0f;
    private Vector3 initialScale;

    private void Awake()
    {
        var renderer = GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            matInstance = renderer.material; // Creates a local instance to fade safely
        }
        initialScale = Vector3.one * 0.2f;
        transform.localScale = initialScale;
    }

    private void Update()
    {
        timer += Time.deltaTime;
        float progress = Mathf.Clamp01(timer / duration);

        // Fast outward pop using smooth step / ease out
        float scaleT = Mathf.Sin(progress * Mathf.PI * 0.5f);
        transform.localScale = Vector3.one * Mathf.Lerp(0.2f, targetRadius * 2f, scaleT);

        // Fade out alpha & emission
        if (matInstance != null)
        {
            Color c = matInstance.color;
            c.a = Mathf.Lerp(0.6f, 0f, progress);
            matInstance.color = c;
        }

        if (timer >= duration)
        {
            Destroy(gameObject);
        }
    }
}