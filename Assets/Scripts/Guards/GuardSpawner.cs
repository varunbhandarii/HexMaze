using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class GuardSpawner : MonoBehaviour
{
    public MazeGridManager gridManager;
    public GameObject guardPrefab;
    public Transform playerTransform;
    public CrouchDetector crouchDetector;
    public Transform spawnedGuardsParent;

    public bool spawnOnStart = true;
    public int guardCount = 2;
    public int minHexDistanceFromEntrance = 2;
    public bool avoidEntranceRoom = true;
    public bool avoidExitRoom = false;
    public bool clearExistingSpawnedGuards = true;

    public bool rebuildNavMeshBeforeSpawning;
    public float navMeshSampleRadius = 2f;
    public float spawnHeightOffset = 0.02f;
    public int patrolRoomCount = 5;

    public bool useSpawnSeed = true;
    public int spawnSeed = 2046;

    readonly List<GameObject> spawned = new List<GameObject>();

    IEnumerator Start()
    {
        if (!spawnOnStart) yield break;

        for (int i = 0; i < 90; i++)
        {
            if (gridManager == null) gridManager = FindFirstObjectByType<MazeGridManager>(FindObjectsInactive.Include);
            if (gridManager != null && gridManager.GetAllRooms().Count > 0) break;
            yield return null;
        }

        if (rebuildNavMeshBeforeSpawning)
        {
            yield return null;
            NavMeshRebaker.BuildSceneNavMeshNow();
            yield return null;
        }

        SpawnGuards(true);
    }

    [ContextMenu("Spawn Guards Now")]
    public void SpawnGuards() => SpawnGuards(false);

    [ContextMenu("Clear Spawned Guards")]
    public void ClearSpawnedGuards()
    {
        var parent = GetParent();
        spawned.Clear();

        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i).gameObject;
            if (Application.isPlaying) { child.SetActive(false); Destroy(child); }
            else DestroyImmediate(child);
        }
    }

    void SpawnGuards(bool skipNavMeshBuild)
    {
        if (gridManager == null) gridManager = FindFirstObjectByType<MazeGridManager>(FindObjectsInactive.Include);
        if (playerTransform == null && Camera.main != null) playerTransform = Camera.main.transform;
        if (crouchDetector == null) crouchDetector = FindFirstObjectByType<CrouchDetector>(FindObjectsInactive.Include);

        if (gridManager == null) { Debug.LogWarning("GuardSpawner needs a MazeGridManager.", this); return; }
        if (guardPrefab == null) { Debug.LogWarning("GuardSpawner needs guardPrefab.", this); return; }

        var rooms = gridManager.GetAllRooms();
        if (rooms.Count == 0) { Debug.LogWarning("No rooms to spawn guards in.", this); return; }

        if (clearExistingSpawnedGuards) ClearSpawnedGuards();
        if (rebuildNavMeshBeforeSpawning && !skipNavMeshBuild) NavMeshRebaker.BuildSceneNavMeshNow();

        var candidates = PickCandidates(rooms);
        if (candidates.Count == 0) { Debug.LogWarning("No valid spawn rooms.", this); return; }

        var rng = useSpawnSeed ? new System.Random(spawnSeed) : new System.Random(Environment.TickCount);
        Shuffle(candidates, rng);

        int target = Mathf.Clamp(guardCount, 0, rooms.Count);
        int made = 0;

        for (int i = 0; i < candidates.Count && made < target; i++)
        {
            var go = SpawnAt(candidates[i], made + 1, rooms, rng);
            if (go != null) { spawned.Add(go); made++; }
        }
    }

    GameObject SpawnAt(Vector2Int coord, int index, Dictionary<Vector2Int, HexRoom> rooms, System.Random rng)
    {
        if (!rooms.TryGetValue(coord, out var room) || room == null) return null;

        Vector3 pos = room.transform.position + Vector3.up * spawnHeightOffset;
        if (NavMesh.SamplePosition(room.transform.position, out NavMeshHit hit, navMeshSampleRadius, NavMesh.AllAreas))
            pos = hit.position + Vector3.up * spawnHeightOffset;

        var go = Instantiate(guardPrefab, pos, Quaternion.identity, GetParent());
        go.name = $"Guard_{index}_Room_{coord.x}_{coord.y}";
        go.tag = "Guard";

        var ai = go.GetComponent<GuardAI>();
        if (ai != null)
        {
            ai.gridManager = gridManager;
            ai.player = playerTransform;
            ai.randomPatrolRoomCount = Mathf.Max(1, patrolRoomCount);
            ai.SetPatrolRoute(BuildPatrolRoute(coord, rooms, rng));
        }

        var det = go.GetComponent<GuardDetection>();
        if (det != null)
        {
            det.player = playerTransform;
            det.playerTransformIsHead = true;
            det.crouchDetector = crouchDetector;
        }

        var agent = go.GetComponent<NavMeshAgent>();
        if (agent != null && agent.enabled && NavMesh.SamplePosition(pos, out hit, navMeshSampleRadius, NavMesh.AllAreas))
            agent.Warp(hit.position);

        return go;
    }

    List<Vector2Int> PickCandidates(Dictionary<Vector2Int, HexRoom> rooms)
    {
        var list = new List<Vector2Int>();
        foreach (var coord in rooms.Keys)
        {
            if (avoidEntranceRoom && coord == gridManager.entranceCoord) continue;
            if (avoidExitRoom && coord == gridManager.exitCoord) continue;
            if (HexDistance(coord, gridManager.entranceCoord) < minHexDistanceFromEntrance) continue;
            list.Add(coord);
        }

        if (list.Count > 0) return list;

        foreach (var coord in rooms.Keys)
            if (!avoidEntranceRoom || coord != gridManager.entranceCoord) list.Add(coord);
        return list;
    }

    List<Vector2Int> BuildPatrolRoute(Vector2Int spawnRoom, Dictionary<Vector2Int, HexRoom> rooms, System.Random rng)
    {
        var route = new List<Vector2Int> { spawnRoom };
        var options = new List<Vector2Int>();

        foreach (var coord in rooms.Keys)
            if (coord != spawnRoom && coord != gridManager.entranceCoord) options.Add(coord);

        Shuffle(options, rng);
        int max = Mathf.Clamp(patrolRoomCount, 1, rooms.Count);
        for (int i = 0; i < options.Count && route.Count < max; i++) route.Add(options[i]);
        return route;
    }

    Transform GetParent()
    {
        if (spawnedGuardsParent != null) return spawnedGuardsParent;

        var t = transform.Find("Spawned Guards");
        if (t == null)
        {
            t = new GameObject("Spawned Guards").transform;
            t.SetParent(transform, false);
        }
        spawnedGuardsParent = t;
        return spawnedGuardsParent;
    }

    static void Shuffle<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    public static int HexDistance(Vector2Int a, Vector2Int b)
    {
        var ca = OffsetToCube(a);
        var cb = OffsetToCube(b);
        return Mathf.Max(Mathf.Abs(ca.x - cb.x), Mathf.Abs(ca.y - cb.y), Mathf.Abs(ca.z - cb.z));
    }

    static Vector3Int OffsetToCube(Vector2Int o)
    {
        int x = o.x - ((o.y - (o.y & 1)) / 2);
        int z = o.y;
        return new Vector3Int(x, -x - z, z);
    }
}
