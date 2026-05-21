using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum GuardState { Patrol, Investigate, Chase, Blocked }

public class GuardAI : MonoBehaviour
{
    public MazeGridManager gridManager;
    public Transform player;

    public float patrolSpeed = 0.75f;
    public float chaseSpeed = 1.0f;

    public List<Vector2Int> patrolRoute = new List<Vector2Int>();
    public int randomPatrolRoomCount = 5;
    public float waypointReachDistance = 0.5f;

    public float investigateTimeout = 5f;

    public float blockedTimeout = 3f;
    public bool requestNavMeshRebakeAfterBarricadeCleared;

    public float catchDistance = 1.2f;

    public GuardState currentState = GuardState.Patrol;

    NavMeshAgent agent;
    GuardAudio guardAudio;
    Coroutine blockedCo;
    int waypointIdx;
    Vector3 investigateTarget;
    float investigateTimer;
    bool ready;
    bool caught;
    float nextRetry;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        guardAudio = GetComponent<GuardAudio>();
    }

    IEnumerator Start()
    {
        for (int i = 0; i < 10; i++)
        {
            if (TryInit()) yield break;
            yield return null;
        }
        Debug.LogWarning("Guard couldn't find a NavMesh yet. Retrying at runtime.", this);
    }

    void Update()
    {
        if (!ready)
        {
            if (Time.time >= nextRetry)
            {
                nextRetry = Time.time + 1f;
                TryInit();
            }
            return;
        }

        if (!agent.enabled || !agent.isOnNavMesh) { ready = false; return; }

        switch (currentState)
        {
            case GuardState.Patrol: UpdatePatrol(); break;
            case GuardState.Investigate: UpdateInvestigate(); break;
            case GuardState.Chase: UpdateChase(); break;
        }

        CheckForCatch();
    }

    public void TriggerInvestigate(Vector3 point)
    {
        if (!ready || currentState == GuardState.Chase || currentState == GuardState.Blocked) return;

        bool wasInvestigating = currentState == GuardState.Investigate;
        currentState = GuardState.Investigate;
        investigateTarget = point;
        investigateTimer = 0f;
        caught = false;

        agent.isStopped = false;
        agent.speed = patrolSpeed * 1.2f;
        SetDest(investigateTarget);

        if (!wasInvestigating && guardAudio != null) guardAudio.PlayAlertSound();
    }

    public void TriggerChase()
    {
        if (!ready || currentState == GuardState.Blocked) return;

        bool wasChasing = currentState == GuardState.Chase;
        currentState = GuardState.Chase;
        if (!wasChasing) caught = false;
        agent.isStopped = false;
        agent.speed = chaseSpeed;

        if (!wasChasing && guardAudio != null) guardAudio.PlayChaseSound();
    }

    public void StopChase()
    {
        if (!ready || currentState == GuardState.Blocked) return;
        currentState = GuardState.Patrol;
        caught = false;
        GoToNextWaypoint();
    }

    public void TriggerBlocked(GameObject barricade)
    {
        if (!ready || currentState == GuardState.Blocked) return;
        if (blockedCo != null) StopCoroutine(blockedCo);

        currentState = GuardState.Blocked;
        agent.isStopped = true;
        blockedCo = StartCoroutine(BreakBarricade(barricade));
    }

    public void SetPatrolTarget(Vector2Int coord)
    {
        if (gridManager == null || gridManager.GetRoom(coord) == null) return;

        int insertAt = Mathf.Clamp(waypointIdx + 1, 0, patrolRoute.Count);
        patrolRoute.Insert(insertAt, coord);
        waypointIdx = insertAt;

        if (ready && currentState == GuardState.Patrol) GoToNextWaypoint();
    }

    public void SetPatrolRoute(List<Vector2Int> route)
    {
        patrolRoute = route != null ? new List<Vector2Int>(route) : new List<Vector2Int>();
        waypointIdx = 0;

        if (ready && patrolRoute.Count > 0)
        {
            currentState = GuardState.Patrol;
            GoToNextWaypoint();
        }
    }

    bool TryInit()
    {
        if (gridManager == null) gridManager = FindFirstObjectByType<MazeGridManager>(FindObjectsInactive.Include);
        if (player == null && Camera.main != null) player = Camera.main.transform;

        if (agent == null) return false;
        if (!agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                agent.Warp(hit.position);
            else return false;
        }

        if (gridManager == null) return false;

        if (patrolRoute == null || patrolRoute.Count == 0) GenerateRoute();
        if (patrolRoute.Count == 0) return false;

        ready = true;
        caught = false;
        currentState = GuardState.Patrol;
        GoToNextWaypoint();
        return true;
    }

    void UpdatePatrol()
    {
        if (patrolRoute.Count == 0)
        {
            GenerateRoute();
            if (patrolRoute.Count == 0) return;
        }

        if (!agent.pathPending && agent.remainingDistance <= waypointReachDistance)
        {
            waypointIdx = (waypointIdx + 1) % patrolRoute.Count;
            GoToNextWaypoint();
        }
    }

    void GoToNextWaypoint()
    {
        if (patrolRoute.Count == 0) return;

        agent.isStopped = false;
        agent.speed = patrolSpeed;

        for (int i = 0; i < patrolRoute.Count; i++)
        {
            waypointIdx = Mathf.Clamp(waypointIdx, 0, patrolRoute.Count - 1);
            var room = gridManager.GetRoom(patrolRoute[waypointIdx]);
            if (room != null && SetDest(room.transform.position)) return;
            waypointIdx = (waypointIdx + 1) % patrolRoute.Count;
        }
    }

    void GenerateRoute()
    {
        patrolRoute = new List<Vector2Int>();
        if (gridManager == null) return;

        var all = gridManager.GetAllRooms();
        if (all.Count == 0) return;

        var pool = new List<Vector2Int>(all.Keys);
        pool.Remove(gridManager.entranceCoord);
        if (pool.Count == 0) pool = new List<Vector2Int>(all.Keys);

        int count = Mathf.Clamp(randomPatrolRoomCount, 1, pool.Count);
        for (int i = 0; i < count; i++)
        {
            int pick = Random.Range(0, pool.Count);
            patrolRoute.Add(pool[pick]);
            pool.RemoveAt(pick);
        }
        waypointIdx = 0;
    }

    void UpdateInvestigate()
    {
        investigateTimer += Time.deltaTime;
        if (!agent.pathPending && agent.remainingDistance <= waypointReachDistance) FaceTarget(investigateTarget);

        if (investigateTimer >= investigateTimeout)
        {
            currentState = GuardState.Patrol;
            GoToNextWaypoint();
        }
    }

    void UpdateChase()
    {
        if (player == null) { StopChase(); return; }

        SetDest(player.position);
    }

    void CheckForCatch()
    {
        if (caught || currentState == GuardState.Blocked || player == null) return;

        Vector3 guardPos = transform.position;
        Vector3 playerPos = player.position;
        guardPos.y = 0f;
        playerPos.y = 0f;

        if (Vector3.Distance(guardPos, playerPos) > catchDistance) return;

        caught = true;
        var gameManager = GameManager.Current != null
            ? GameManager.Current
            : FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);

        if (gameManager != null) gameManager.OnPlayerCaught();
    }

    IEnumerator BreakBarricade(GameObject barricade)
    {
        yield return new WaitForSeconds(blockedTimeout);

        if (barricade != null)
        {
            Destroy(barricade);
            if (requestNavMeshRebakeAfterBarricadeCleared) NavMeshRebaker.RequestSceneRebake();
        }

        blockedCo = null;
        currentState = GuardState.Patrol;
        GoToNextWaypoint();
    }

    bool SetDest(Vector3 target)
    {
        if (!agent.enabled || !agent.isOnNavMesh) return false;
        if (NavMesh.SamplePosition(target, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
            return agent.SetDestination(hit.position);
        return agent.SetDestination(target);
    }

    void FaceTarget(Vector3 point)
    {
        Vector3 dir = point - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        var rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, rot, agent.angularSpeed * Time.deltaTime);
    }
}
