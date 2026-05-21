using UnityEngine;

public class GuardDetection : MonoBehaviour
{
    public float viewRange = 8f;
    public float viewAngle = 90f;
    public float peripheralRange = 3f;
    public float eyeHeight = 1.5f;
    public float standingTargetHeight = 1.5f;
    public float crouchingTargetHeight = 0.6f;

    public float detectionBuildRate = 1.5f;
    public float detectionDecayRate = 1f;
    public float investigateThreshold = 0.3f;
    public float chaseLossTimeout = 4f;

    public Transform player;
    public bool playerTransformIsHead = true;
    public CrouchDetector crouchDetector;
    public LayerMask obstacleLayers = ~0;

    public bool drawSightRays;
    public bool canSeePlayer;

    GuardAI ai;
    float detection;
    float chaseLossTimer;

    void Awake()
    {
        ai = GetComponent<GuardAI>();
    }

    void Start()
    {
        if (player == null && Camera.main != null) player = Camera.main.transform;
        if (crouchDetector == null) crouchDetector = FindFirstObjectByType<CrouchDetector>(FindObjectsInactive.Include);
    }

    void Update()
    {
        if (ai == null || player == null) return;

        canSeePlayer = LookForPlayer();

        if (canSeePlayer)
        {
            chaseLossTimer = 0f;
            detection = Mathf.Clamp01(detection + Time.deltaTime / Mathf.Max(0.05f, detectionBuildRate));

            if (detection >= 1f) ai.TriggerChase();
            else if (detection > investigateThreshold) ai.TriggerInvestigate(player.position);
        }
        else
        {
            detection = Mathf.Max(0f, detection - Time.deltaTime / Mathf.Max(0.05f, detectionDecayRate));

            if (ai.currentState == GuardState.Chase)
            {
                chaseLossTimer += Time.deltaTime;
                if (chaseLossTimer >= chaseLossTimeout)
                {
                    ai.StopChase();
                    detection = 0f;
                }
            }
        }
    }

    public float GetDetectionLevel() => detection;

    bool LookForPlayer()
    {
        Vector3 eyes = transform.position + Vector3.up * eyeHeight;
        Vector3 target = PlayerPoint();
        Vector3 toP = target - eyes;
        float dist = toP.magnitude;

        if (dist <= 0.001f) return true;

        if (dist <= peripheralRange) return Clear(eyes, target);
        if (dist > viewRange) return false;
        if (Vector3.Angle(transform.forward, toP.normalized) > viewAngle * 0.5f) return false;

        return Clear(eyes, target);
    }

    Vector3 PlayerPoint()
    {
        if (playerTransformIsHead) return player.position;
        bool crouching = crouchDetector != null && crouchDetector.isCrouching;
        return player.position + Vector3.up * (crouching ? crouchingTargetHeight : standingTargetHeight);
    }

    bool Clear(Vector3 from, Vector3 to)
    {
        Vector3 dir = to - from;
        float dist = dir.magnitude;
        if (dist <= 0.001f) return true;

        var hits = Physics.RaycastAll(from, dir.normalized, dist, obstacleLayers, QueryTriggerInteraction.Ignore);
        bool blocked = false;
        foreach (var hit in hits)
        {
            if (hit.collider == null) continue;
            var ht = hit.collider.transform;
            if (ht.root == transform.root) continue;
            if (player != null && (ht == player || ht.IsChildOf(player) || player.IsChildOf(ht) || ht.root == player.root)) continue;
            blocked = true;
            break;
        }

        if (drawSightRays) Debug.DrawLine(from, to, blocked ? Color.red : Color.green, 0.05f);
        return !blocked;
    }
}
