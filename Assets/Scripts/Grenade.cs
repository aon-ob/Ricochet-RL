using UnityEngine;

public class Grenade : MonoBehaviour
{
    [SerializeField] private float time = 2.5f;
    [SerializeField] private float explosionRadius = 4f;
    [SerializeField] private float maxDamage = 100f;
    [SerializeField] private float explosionForce = 12f;
    [SerializeField] private LayerMask playerLayer;
    [SerializeField] private LayerMask obstacleLayer;
    [SerializeField] private GameObject explosionVfxPrefab;

    public PlayerController throwerAgent; // Assigned when spawned

    private Rigidbody rb;
    private SphereCollider col;
    private bool isHeld = false;

    public float RemainingTime => time;

    public PlayerController Thrower => throwerAgent; // Expose cleanly
    private bool hasBonked = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<SphereCollider>();
    }

    void Update()
    {
        // Fuse timer runs constantly, whether held in hand or flying
        if (time > 0)
        {
            time -= Time.deltaTime;
        }
        else
        {
            time = 0;
            Explode();
        }
    }

    public void InitHeld(PlayerController thrower)
    {
        throwerAgent = thrower;
        isHeld = true;

        if (rb == null) rb = GetComponent<Rigidbody>();
        if (col == null) col = GetComponent<SphereCollider>();

        // Turn off physics and collision while resting in the player's hand
        rb.isKinematic = true;
        col.enabled = false;
    }

    public void Throw(Vector3 launchVelocity)
    {
        isHeld = false;
        transform.SetParent(null);

        col.enabled = true;
        rb.isKinematic = false;
        rb.linearVelocity = launchVelocity;

        // Ignore collision with the thrower so running forward doesn't snag the grenade
        if (throwerAgent != null && throwerAgent.TryGetComponent<Collider>(out Collider throwerCol))
        {
            Physics.IgnoreCollision(col, throwerCol, true);
        }
    }

    void Explode()
    {
        // If it blew up inside the player's hand, clear reference
        if (isHeld && throwerAgent != null)
        {
            throwerAgent.ClearHeldGrenade();
        }

        // Lift origin slightly so it doesn't self-clip against the floor mesh
        Vector3 blastOrigin = transform.position + Vector3.up * 0.15f;
        
        // Grab everything on the player layer inside the blast sphere
        Collider[] hits = Physics.OverlapSphere(blastOrigin, explosionRadius, playerLayer);

        foreach (Collider hit in hits)
        {
            // Use the collider's center rather than transform root (which is often at the feet)
            Vector3 targetPoint = hit.bounds.center;
        
            // Line of sight check: if a wall blocks the path, skip damage
            if (Physics.Linecast(blastOrigin, targetPoint, obstacleLayer))
            {
                continue;
            }

            // Distance & quadratic falloff math
            float distance = Vector3.Distance(blastOrigin, targetPoint);
            float t = Mathf.Clamp01(distance / explosionRadius);
            float damage = maxDamage * Mathf.Pow(1f - t, 2f);

            // Deal damage & pass credit
            PlayerController victim = hit.GetComponentInParent<PlayerController>();
            if (victim != null)
            {
                // Calculate blast direction from grenade to player center
                Vector3 pushDir = (targetPoint - blastOrigin).normalized;

                // Add a slight upward kick so they arc through the air instead of scraping the floor
                pushDir += Vector3.up * 0.4f; 
                pushDir.Normalize();

                float blastFalloff = 1f - t; // stronger push closer to center
                Vector3 impulse = pushDir * (explosionForce * blastFalloff);

                // Apply knockback through the controller so it halts input override
                victim.ApplyKnockback(impulse);
                victim.TakeDamage(damage, throwerAgent);
            }
        }

        if (explosionVfxPrefab != null)
        {
            Instantiate(explosionVfxPrefab, blastOrigin, Quaternion.identity);
        }
        if (CameraShake.Instance != null)
        {
            CameraShake.Instance.Shake();
        }

        Destroy(gameObject);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Don't bonk if already hit someone, held in hand, or moving at crawling speed
        if (hasBonked || isHeld) return;

        // Check if we hit a player
        PlayerController victim = collision.collider.GetComponentInParent<PlayerController>();
        if (victim != null && victim != throwerAgent)
        {
            float impactSpeed = collision.relativeVelocity.magnitude;
            
            // Ignore dead rolls/taps under 3 m/s
            if (impactSpeed < 3f) return;

            hasBonked = true;

            // Map speed [5f, 25f] -> [5f, 30f] damage
            float t = Mathf.InverseLerp(5f, 25f, impactSpeed);
            float bonkDamage = Mathf.Lerp(5f, 30f, t);

            victim.TakeDamage(bonkDamage, throwerAgent);
        }
    }
}