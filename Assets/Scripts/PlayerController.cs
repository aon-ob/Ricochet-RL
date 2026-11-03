using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine.SocialPlatforms;
using System.Collections.Generic;

public class PlayerController : Agent
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float turnSpeed = 180f;
    [SerializeField] private float jumpForce = 20f;
    [SerializeField] private float health = 100f;
    [SerializeField] private LayerMask groundLayer;

    [SerializeField] private PhysicsMaterial playerPhysicsMaterial;
    [SerializeField] private PhysicsMaterial ragdollPhysicsMaterial;

    [Header("Grenade Settings")]
    [SerializeField] private GameObject grenadePrefab;
    [SerializeField] private Transform throwPoint;
    [SerializeField] private float grenadeCooldown = 1.0f;
    [SerializeField] private float minThrowForce = 5f;
    [SerializeField] private float maxThrowForce = 25f;
    [SerializeField] private float minPitchAngle = -10f;
    [SerializeField] private float maxPitchAngle = 90f;

    [SerializeField] private PlayerController opponent;
    [SerializeField] private LayerMask grenadeLayer;

    [Header("Match Rules")]
    [SerializeField] private int maxRoundSteps = 9000; // ~30 seconds at 50Hz FixedUpdate

    [Header("Spawn Settings")]
    [SerializeField] private Transform spawnsParent;

    [Header("Team ID")]
    [SerializeField] private int playerNumber = 1; // Set to 1 on Player 1, 2 on Player 2   

    [Header("UI / VFX")]
    [SerializeField] private GameObject damagePopupPrefab;

    [Header("Death / Visual Delay")]
    [Tooltip("Set to 0 during headless/parallel training so you don't waste hours waiting on ragdolls")]
    [SerializeField] private float deathResetDelay = 1.8f;

    private bool isMatchEnding = false;

    // --- Anti-Camp Tracking ---
    private Vector3 campAnchorPos;
    private float campTimer = 0f;

    public Transform CurrentSpawn { get; private set; }

    private Transform[] spawnPoints;

    // --- Anti-Camp Tracking ---

    private bool isDead = false;
    private float knockbackTimer = 0f;
    private float cooldownTimer = 0f;
    private Grenade heldGrenade = null;
    private bool isGrounded = false;

    // Smooth manual pitch tracking for Heuristic mode
    private float heuristicPitch = 0f;

    private Rigidbody rb;
    private CapsuleCollider col;

    private Quaternion initialRotation;
    private Vector3 initialPosition;

    // --- Exploration & Anti-Camp Tracking ---
    private HashSet<Vector2Int> visitedTiles = new HashSet<Vector2Int>();
    private const float TILE_SIZE = 3.0f; // 3x3 meter grid blocks

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<CapsuleCollider>();
        initialRotation = transform.rotation;
        initialPosition = transform.position;

        if (spawnsParent != null)
        {
            spawnPoints = new Transform[spawnsParent.childCount];
            for (int i = 0; i < spawnsParent.childCount; i++)
            {
                spawnPoints[i] = spawnsParent.GetChild(i);
            }
        }
    }

    public override void OnEpisodeBegin()
    {
        isDead = false;
        isMatchEnding = false;
        StopAllCoroutines();
        knockbackTimer = 0f;
        cooldownTimer = 0f;
        heuristicPitch = 0f;
        health = 100f;

        campAnchorPos = transform.position;
        campTimer = 0f;

        visitedTiles.Clear();

        // Destroy held grenade
        if (heldGrenade != null)
        {
            Destroy(heldGrenade.gameObject);
            heldGrenade = null;
        }

        // Clean up any lingering unexploded grenades in this arena
        Collider[] lingeringGrenades = Physics.OverlapSphere(transform.position, 30f, grenadeLayer);
        foreach (var hit in lingeringGrenades)
        {
            if (hit.TryGetComponent<Grenade>(out var g))
            {
                Destroy(g.gameObject);
            }
        }

        // Clear physics momentum & force accumulators completely
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.Sleep(); // Flushes any pending torque/forces from Die()

        // Pick any random spawn except the exact one the opponent is on
        Vector3 spawnPos = initialPosition;
        Quaternion spawnRot = initialRotation;

        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            List<Transform> availableSpawns = new List<Transform>();

            for (int i = 0; i < spawnPoints.Length; i++)
            {
                Transform sp = spawnPoints[i];
                if (opponent != null && sp == opponent.CurrentSpawn)
                {
                    continue;
                }
                availableSpawns.Add(sp);
            }

            CurrentSpawn = availableSpawns.Count > 0 
                ? availableSpawns[Random.Range(0, availableSpawns.Count)] 
                : spawnPoints[Random.Range(0, spawnPoints.Length)];

            spawnPos = CurrentSpawn.position;
            spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        }

        // Teleport BOTH Transform and Rigidbody
        transform.position = spawnPos;
        transform.rotation = spawnRot;
        rb.position = spawnPos;
        spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        // Sync PhysX
        Physics.SyncTransforms();
        // Lock constraints AFTER the body is upright, then restore player material
        rb.constraints = RigidbodyConstraints.FreezeRotationX | 
                        RigidbodyConstraints.FreezeRotationY | 
                        RigidbodyConstraints.FreezeRotationZ;
        col.material = playerPhysicsMaterial;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Get local velocity (3 floats)
        Vector3 localVel = transform.InverseTransformDirection(rb.linearVelocity);
        sensor.AddObservation(localVel / moveSpeed);

        // Player stats (3 floats)
        sensor.AddObservation(health / 100f);
        sensor.AddObservation(isGrounded ? 1f : 0f);
        sensor.AddObservation(cooldownTimer / grenadeCooldown);

        // Is the grenade pin pulled in hand? (1 float)
        bool cooking = heldGrenade != null;
        sensor.AddObservation(cooking ? 1f : 0f);

        // How much time is left for grenade to explode (1 float)
        float fuseRatio = cooking ? (heldGrenade.RemainingTime / 2.5f) : 0f;
        sensor.AddObservation(fuseRatio);

        // Enemy position relative to agent (3 floats)
        if (opponent != null)
        {
            Vector3 relativeEnemyPos = transform.InverseTransformPoint(opponent.transform.position);
            sensor.AddObservation(relativeEnemyPos * 0.05f);
        }
        else
        {
            sensor.AddObservation(Vector3.zero);
        }

        // Enemy velocity and health (4 floats)
        if (opponent != null)
        {
            Vector3 enemyLocalVel = transform.InverseTransformDirection(opponent.GetComponent<Rigidbody>().linearVelocity);
            sensor.AddObservation(enemyLocalVel / moveSpeed); 
            sensor.AddObservation(opponent.health / 100f);
        }
        else
        {
            sensor.AddObservation(Vector3.zero);
            sensor.AddObservation(0f);
        }

        // GRENADE DANGER TRACKING (8 floats)
        // Finds the closest live, unheld grenade within arena bounds
        Collider[] nearbyGrenades = Physics.OverlapSphere(transform.position, 30f, grenadeLayer);

        Grenade nearestGrenade = null;
        float closestDistSqr = float.MaxValue;

        foreach (var hit in nearbyGrenades)
        {
            // Held grenades have col.enabled = false, so OverlapSphere naturally ignores them
            if (hit.TryGetComponent<Grenade>(out var g))
            {
                float distSqr = (hit.transform.position - transform.position).sqrMagnitude;
                if (distSqr < closestDistSqr)
                {
                    closestDistSqr = distSqr;
                    nearestGrenade = g;
                }
            }
        }

        if (nearestGrenade != null)
        {
            sensor.AddObservation(1f); // 1 float: live bomb detected

            // Relative position: where it is relative to the agent's face
            Vector3 relPos = transform.InverseTransformPoint(nearestGrenade.transform.position);
            sensor.AddObservation(relPos * 0.05f); // 3 floats

            // Relative velocity: tells the bot if it's sliding away or flying at their skull
            Rigidbody gRb = nearestGrenade.GetComponent<Rigidbody>();
            Vector3 relVel = gRb != null 
                ? transform.InverseTransformDirection(gRb.linearVelocity) 
                : Vector3.zero;
            sensor.AddObservation(relVel / moveSpeed); // 3 floats

            // Fuse ratio: 1.0 = fresh throw, 0.0 = exploding right now
            sensor.AddObservation(Mathf.Clamp01(nearestGrenade.RemainingTime / 2.5f)); // 1 float
        }
        else
        {
            // Constant-size fallback when no grenade is active
            sensor.AddObservation(0f);          // 1 float
            sensor.AddObservation(Vector3.zero); // 3 floats
            sensor.AddObservation(Vector3.zero); // 3 floats
            sensor.AddObservation(0f);          // 1 float:
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (isDead || isMatchEnding)
        {
            return;
        }
        // Anti-stall step penalty (forces urgency)
        AddReward(-0.0005f);
        // Timeout / Stalemate check
        if (StepCount >= maxRoundSteps)
        {
            // Punish both bots for being cowards and wasting everyone's time
            AddReward(-0.5f);
            if (opponent != null)
            {
                opponent.AddReward(-0.5f);
                opponent.EndEpisode();
            }

            EndEpisode();
            return;
        }

        // Don't execute actions if dead
        if (isDead) return;

        // Cooldown timer countdown
        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.fixedDeltaTime;
        }

        // If recovering from a blast, decrement timer and let physics momentum play out
        if (knockbackTimer > 0f)
        {
            knockbackTimer -= Time.fixedDeltaTime;
            return; 
        }

        var continuousActions = actions.ContinuousActions;
        float moveX = continuousActions[0]; // Forward/Backward (W/S)
        float moveY = continuousActions[1]; // Strafe (A/D)
        float turn = continuousActions[2]; // Turn (Q / E)
        float jump  = continuousActions[3]; // Jump: > 0.5 triggers jump
        float throwAction = continuousActions[4]; // Cook / Throw: > 0.5 cooks, <= 0.5 releases
        float pitchAction = continuousActions[5]; // Pitch: -1 (down) to 1 (high arc)
        float forceAction = continuousActions[6]; // Force: -1 (drop) to 1 (yeet)

        // Draw trajectory preview arrow while cooking
        if (heldGrenade != null && throwPoint != null)
        {
            float previewPitchT = Mathf.InverseLerp(-1f, 1f, pitchAction);
            float previewPitchAngle = Mathf.Lerp(minPitchAngle, maxPitchAngle, previewPitchT);
            float previewRad = previewPitchAngle * Mathf.Deg2Rad;
            Vector3 previewDir = (transform.forward * Mathf.Cos(previewRad) + Vector3.up * Mathf.Sin(previewRad)).normalized;

            float previewForceT = Mathf.InverseLerp(-1f, 1f, forceAction);
            float previewForce = Mathf.Lerp(minThrowForce, maxThrowForce, previewForceT);

            Vector3 arrowVector = previewDir * (previewForce * 0.25f);
            DrawDebugArrow(throwPoint.position, arrowVector, Color.cyan);
        }

        // Grenade cooking & throwing logic
        if (throwAction > 0.5f)
        {
            if (heldGrenade == null && cooldownTimer <= 0f && grenadePrefab != null && throwPoint != null)
            {
                StartCooking();
            }
        }
        else
        {
            if (heldGrenade != null)
            {
                ThrowHeldGrenade(pitchAction, forceAction);
            }
        }

        // Ground check using collider bounds to prevent scale bugs
        float rayLength = col.bounds.extents.y + 0.05f;
        isGrounded = Physics.Raycast(col.bounds.center, Vector3.down, rayLength, groundLayer);

        // Calculate move direction
        Vector3 moveDirection = (transform.forward * moveY + transform.right * moveX).normalized;

        Vector3 targetVelocity = moveDirection * moveSpeed;

        float currentYVel = rb.linearVelocity.y;

        // Jump impulse
        if (isGrounded && jump > 0.5f && Mathf.Abs(currentYVel) < 0.1f)
        {
            currentYVel = jumpForce;
        }

        rb.linearVelocity = new Vector3(targetVelocity.x, currentYVel, targetVelocity.z);

        // Turning
        transform.Rotate(Vector3.up, turn * turnSpeed * Time.fixedDeltaTime);

        // Anti-Camp Penalty: sitting inside a 2m pocket for over 5s bleeds reward
        if (Vector3.Distance(transform.position, campAnchorPos) < 3.0f)
        {
            campTimer += Time.fixedDeltaTime;
            if (campTimer >= 5f)
            {
                AddReward(-0.003f); // Slaps them every physics tick they refuse to rotate
            }
        }
        else
        {
            // Broke out of the camp zone; reset anchor
            campAnchorPos = transform.position;
            campTimer = 0f;
        }

        // Reward for facing the opponent
        if (opponent != null)
        {
            Vector3 toOpponent = (opponent.transform.position - transform.position).normalized;
            float dot = Vector3.Dot(transform.forward, toOpponent);
            AddReward(dot * 0.001f); // Encourages facing the enemy
        }

        // Reward for staying a certain distance away from the opponent (not too close, not too far)
        if (opponent != null)
        {
            float dist = Vector3.Distance(transform.position, opponent.transform.position);
            if (dist < 3f)
            {
                AddReward(-0.001f); // Too close: punish
            }
            else if (dist > 11f)
            {
                AddReward(-0.001f); // Too far: punish
            }
            else
            {
                AddReward(0.001f); // Just right: reward
            }
        }

    }

    private void StartCooking()
    {
        GameObject grenadeObj = Instantiate(grenadePrefab, throwPoint.position, throwPoint.rotation, throwPoint);
        heldGrenade = grenadeObj.GetComponent<Grenade>();
        heldGrenade.InitHeld(this);
    }

    private void ThrowHeldGrenade(float pitchAction, float forceAction)
    {
        float tPitch = Mathf.InverseLerp(-1f, 1f, pitchAction);
        float pitchAngle = Mathf.Lerp(minPitchAngle, maxPitchAngle, tPitch);
        float rad = pitchAngle * Mathf.Deg2Rad;

        // Option A: transform.right is your visual forward
        Vector3 throwDir = (transform.forward * Mathf.Cos(rad) + Vector3.up * Mathf.Sin(rad)).normalized;

        // Map [-1, 1] to [minThrowForce, maxThrowForce]
        float tForce = Mathf.InverseLerp(-1f, 1f, forceAction);
        float actualForce = Mathf.Lerp(minThrowForce, maxThrowForce, tForce);

        Vector3 launchVel = throwDir * actualForce + rb.linearVelocity * 0.4f;

        heldGrenade.Throw(launchVel);
        heldGrenade = null;
        cooldownTimer = grenadeCooldown;
    }

    public void ClearHeldGrenade()
    {
        heldGrenade = null;
        cooldownTimer = grenadeCooldown;
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActions = actionsOut.ContinuousActions;
        
        // Explicit WASD keys only - ignores arrow keys completely for movement
        float strafe = 0f;
        if (Input.GetKey(KeyCode.D)) strafe += 1f;
        if (Input.GetKey(KeyCode.A)) strafe -= 1f;
        continuousActions[0] = strafe;

        float forwardBack = 0f;
        if (Input.GetKey(KeyCode.W)) forwardBack += 1f;
        if (Input.GetKey(KeyCode.S)) forwardBack -= 1f;
        continuousActions[1] = forwardBack;

        // Turning
        float turn = 0f;
        if (Input.GetKey(KeyCode.Q)) turn -= 1f;
        if (Input.GetKey(KeyCode.E)) turn += 1f;
        continuousActions[2] = turn;

        // Jump
        continuousActions[3] = Input.GetKey(KeyCode.Space) ? 1f : 0f;

        // Hold Left Click to cook, release to throw
        continuousActions[4] = Input.GetMouseButton(0) ? 1f : 0f;

        // Smoothly adjust pitch angle with arrow keys instead of hard snapping
        float pitchSpeed = 2.0f; // full range traversal takes 1 second
        if (Input.GetKey(KeyCode.UpArrow)) heuristicPitch += pitchSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.DownArrow)) heuristicPitch -= pitchSpeed * Time.deltaTime;
        heuristicPitch = Mathf.Clamp(heuristicPitch, -1f, 1f);
        continuousActions[5] = heuristicPitch;

        // Left Shift = max force (1), Left Ctrl = soft roll (-1), default mid (0.2)
        float force = 0.2f;
        if (Input.GetKey(KeyCode.LeftShift)) force = 1f;
        if (Input.GetKey(KeyCode.LeftControl)) force = -1f;
        continuousActions[6] = force;
    }

    private void DrawDebugArrow(Vector3 origin, Vector3 vector, Color color)
    {
        if (vector == Vector3.zero) return;

        Debug.DrawRay(origin, vector, color);

        Vector3 tip = origin + vector;
        Quaternion rot = Quaternion.LookRotation(vector);
        Vector3 finRight = rot * Quaternion.Euler(0, 155, 0) * Vector3.forward * 0.4f;
        Vector3 finLeft  = rot * Quaternion.Euler(0, -155, 0) * Vector3.forward * 0.4f;

        Debug.DrawRay(tip, finRight, color);
        Debug.DrawRay(tip, finLeft, color);
    }

    // Called from the grenade when knockback is applied
    public void ApplyKnockback(Vector3 force)
    {
        if (isDead) return;
        knockbackTimer = 0.35f; // Gives 0.35s of pure physics trajectory
        rb.AddForce(force, ForceMode.Impulse);
    }

    public void TakeDamage(float damage, PlayerController attacker)
    {
        if (isDead) return;

        health -= damage;

        // Spawn damage number slightly above the head with a tiny offset
        if (damagePopupPrefab != null)
        {
            Vector3 spawnPos = transform.position + Vector3.up * 2.2f + Random.insideUnitSphere * 0.15f;
            GameObject popupObj = Instantiate(damagePopupPrefab, spawnPos, Quaternion.identity);
            if (popupObj.TryGetComponent<DamagePopup>(out var popup))
            {
                popup.Setup(damage);
            }
        }

        // Self-damage vs enemy damage
        if (attacker == this)
        {
            // Punish self-inflicted damage 1.5x as hard so it learns to respect blast radii
            AddReward(-(damage / 100f) * 1.5f);
        }
        else if (attacker != null)
        {
            attacker.AddReward(damage / 100f); // Reward attacker for splash
            AddReward(-damage / 100f);          // Punish victim for taking hits
        }

        if (health <= 0f)
        {
            isDead = true;
            Die(); // Triggers your ragdoll physics / disables animator

            // Score & rewards apply instantly so nothing desyncs
            if (attacker == this)
            {
                if (opponent != null && ScoreManager.Instance != null)
                {
                    ScoreManager.Instance.AddPoint(opponent.playerNumber);
                }
                AddReward(-1.5f);
                if (opponent != null) opponent.AddReward(0.5f);
            }
            else if (attacker != null)
            {
                if (ScoreManager.Instance != null)
                {
                    ScoreManager.Instance.AddPoint(attacker.playerNumber);
                }
                attacker.AddReward(1.0f);
                AddReward(-1.0f);
            }

            // Lock the survivor so they don't abuse the delay
            if (opponent != null)
            {
                opponent.LockPlayer();
            }

            // Delay the episode reset or snap immediately if training
            if (deathResetDelay > 0f)
            {
                StartCoroutine(DeathResetRoutine());
            }
            else
            {
                ResetRound();
            }
        }
    }

    private void Die()
    {
        isDead = true;

        if (heldGrenade != null)
        {
            // Drop it right at your corpse if you die holding it
            heldGrenade.Throw(Vector3.down * 2f);
            heldGrenade = null;
        }

        // Unlock all rotations so the capsule physically flops and rolls
        rb.constraints = RigidbodyConstraints.None;

        // Switch to ragdoll physics material for more realistic collisions
        col.material = ragdollPhysicsMaterial;

        // Add random torque to make the death tumble look violent and unhinged
        Vector3 randomTorque = new Vector3(
            Random.Range(-10f, 10f),
            Random.Range(-5f, 5f),
            Random.Range(-10f, 10f)
        );

        rb.AddTorque(randomTorque, ForceMode.Impulse);
    }

    public void LockPlayer()
    {
        isMatchEnding = true;
    }

    private System.Collections.IEnumerator DeathResetRoutine()
    {
        isMatchEnding = true;
        yield return new WaitForSeconds(deathResetDelay);
        ResetRound();
    }

    private void ResetRound()
    {
        EndEpisode();
        if (opponent != null)
        {
            opponent.EndEpisode();
        }
    }
}