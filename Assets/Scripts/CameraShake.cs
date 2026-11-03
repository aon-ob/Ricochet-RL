using UnityEngine;

public class CameraShake : MonoBehaviour
{
    public static CameraShake Instance { get; private set; }

    [SerializeField] private float defaultDuration = 0.25f;
    [SerializeField] private float defaultIntensity = 0.35f;

    private Vector3 initialLocalPos;
    private float shakeTimer = 0f;
    private float currentIntensity = 0f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        initialLocalPos = transform.localPosition;
    }

    private void Update()
    {
        if (shakeTimer > 0f)
        {
            shakeTimer -= Time.deltaTime;

            // Random unit offset scaled by intensity and decaying linearly
            Vector3 randomOffset = Random.insideUnitSphere * currentIntensity * (shakeTimer / defaultDuration);
            transform.localPosition = initialLocalPos + randomOffset;

            if (shakeTimer <= 0f)
            {
                transform.localPosition = initialLocalPos;
            }
        }
    }

    public void Shake(float intensity = -1f, float duration = -1f)
    {
        currentIntensity = intensity > 0f ? intensity : defaultIntensity;
        defaultDuration = duration > 0f ? duration : defaultDuration;
        shakeTimer = defaultDuration;
    }
}