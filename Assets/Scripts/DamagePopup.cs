using UnityEngine;
using TMPro;

public class DamagePopup : MonoBehaviour
{
    [SerializeField] private float lifetime = 0.65f;
    [SerializeField] private float floatSpeed = 2.5f;

    private TextMeshPro tmp;
    private Color textColor;
    private float timer = 0f;
    private Vector3 moveDir;
    private Transform camTransform;

    private void Awake()
    {
        tmp = GetComponent<TextMeshPro>();
        textColor = tmp.color;

        if (Camera.main != null)
        {
            camTransform = Camera.main.transform;
        }

        // Slight horizontal kick so multiple hits don't overlap into a blob
        moveDir = new Vector3(Random.Range(-0.4f, 0.4f), 1f, Random.Range(-0.4f, 0.4f)).normalized;
    }

    public void Setup(float damageAmount)
    {
        tmp.text = Mathf.RoundToInt(damageAmount).ToString();
    }

    private void LateUpdate()
    {
        // Lock rotation flat to the camera plane
        if (camTransform != null)
        {
            transform.rotation = camTransform.rotation;
        }

        timer += Time.deltaTime;
        float progress = timer / lifetime;

        // Drift upward
        transform.position += moveDir * (floatSpeed * Time.deltaTime);

        // Scale pop
        float scale = progress < 0.2f 
            ? Mathf.Lerp(0.5f, 1.25f, progress / 0.2f) 
            : Mathf.Lerp(1.25f, 0.8f, (progress - 0.2f) / 0.8f);
        transform.localScale = Vector3.one * scale;

        // Fade alpha out
        textColor.a = Mathf.Lerp(1f, 0f, progress);
        tmp.color = textColor;

        if (timer >= lifetime)
        {
            Destroy(gameObject);
        }
    }
}