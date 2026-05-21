using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class MazeState
{
    public PlayerState player;
    public GuardInfo[] guards;
    public MazeInfo maze;
}

[Serializable]
public class PlayerState
{
    public int[] position;
    public int[] prev_position;
    public string[] visited_rooms;
    public int markers_remaining;
    public float time_remaining;
    public int doors_tried_locked;
    public bool is_crouching;
}

[Serializable]
public class GuardInfo
{
    public int id;
    public int[] position;
    public string state;
    public float detection_level;
}

[Serializable]
public class MazeInfo
{
    public int cols;
    public int rows;
    public int[] entrance;
    public int[] current_exit;
    public DoorInfo[] locked_doors;
}

[Serializable]
public class DoorInfo
{
    public int[] room;
    public int door;
}

public class MazeStateSerializer : MonoBehaviour
{
    public MazeGridManager gridManager;
    public PlayerRoomTracker roomTracker;
    public WristHUD wristHUD;
    public CrouchDetector crouchDetector;
    public GameManager gameManager;
    public MarkerDispenser markerDispenser;
    public bool prettyPrintInEditor;
    public bool includeOnlyInteriorLockedDoors = true;

    Vector2Int prevRoom;
    bool hasPrev;
    int lockedAttempts;

    void Start()
    {
        ResolveRefs();
        if (roomTracker != null)
        {
            prevRoom = roomTracker.GetCurrentRoom();
            hasPrev = true;
        }
    }

    public void IncrementLockedDoorAttempts() => lockedAttempts++;
    public void ResetLockedDoorAttempts() => lockedAttempts = 0;

    public string Serialize()
    {
        return JsonUtility.ToJson(BuildState(), prettyPrintInEditor && Application.isEditor);
    }

    public MazeState BuildState()
    {
        ResolveRefs();

        Vector2Int cur = roomTracker != null ? roomTracker.GetCurrentRoom()
            : (gridManager != null ? gridManager.entranceCoord : Vector2Int.zero);
        Vector2Int last = hasPrev ? prevRoom : cur;

        var s = new MazeState
        {
            player = new PlayerState
            {
                position = new[] { cur.x, cur.y },
                prev_position = new[] { last.x, last.y },
                visited_rooms = VisitedRoomList(cur),
                markers_remaining = markerDispenser != null ? markerDispenser.GetMarkersRemaining() : 0,
                time_remaining = gameManager != null ? gameManager.GetRemainingTime() : 0f,
                doors_tried_locked = lockedAttempts,
                is_crouching = crouchDetector != null && crouchDetector.isCrouching
            },
            guards = GuardSnapshot(),
            maze = new MazeInfo
            {
                cols = gridManager != null ? gridManager.gridCols : 0,
                rows = gridManager != null ? gridManager.gridRows : 0,
                entrance = gridManager != null ? new[] { gridManager.entranceCoord.x, gridManager.entranceCoord.y } : new[] { 0, 0 },
                current_exit = gridManager != null ? new[] { gridManager.exitCoord.x, gridManager.exitCoord.y } : new[] { 0, 0 },
                locked_doors = LockedDoorList()
            }
        };

        prevRoom = cur;
        hasPrev = true;
        return s;
    }

    GuardInfo[] GuardSnapshot()
    {
        var guards = FindObjectsByType<GuardAI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Array.Sort(guards, (a, b) => string.CompareOrdinal(a != null ? a.name : "", b != null ? b.name : ""));

        var list = new List<GuardInfo>();
        for (int i = 0; i < guards.Length; i++)
        {
            var g = guards[i];
            if (g == null) continue;

            var room = gridManager != null ? gridManager.GetNearestRoom(g.transform.position) : Vector2Int.zero;
            var det = g.GetComponent<GuardDetection>();
            list.Add(new GuardInfo
            {
                id = list.Count,
                position = new[] { room.x, room.y },
                state = g.currentState.ToString().ToLowerInvariant(),
                detection_level = det != null ? det.GetDetectionLevel() : 0f
            });
        }
        return list.ToArray();
    }

    string[] VisitedRoomList(Vector2Int cur)
    {
        var rooms = new List<Vector2Int>();
        if (wristHUD != null)
        {
            var visited = wristHUD.GetVisitedRooms();
            if (visited != null) rooms.AddRange(visited);
        }
        if (!rooms.Contains(cur)) rooms.Add(cur);

        rooms.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

        var arr = new string[rooms.Count];
        for (int i = 0; i < rooms.Count; i++) arr[i] = $"{rooms[i].x},{rooms[i].y}";
        return arr;
    }

    DoorInfo[] LockedDoorList()
    {
        var list = new List<DoorInfo>();
        if (gridManager == null) return list.ToArray();

        foreach (var kv in gridManager.GetAllRooms())
        {
            if (kv.Value == null) continue;
            for (int d = 0; d < HexGridData.DirectionCount; d++)
            {
                var n = gridManager.GetNeighbor(kv.Key, d);
                bool hasNeighbor = gridManager.GetRoom(n) != null;

                if (includeOnlyInteriorLockedDoors && !hasNeighbor) continue;
                if (hasNeighbor && !OwnsSharedDoor(kv.Key, n)) continue;
                if (!gridManager.IsDoorLocked(kv.Key, d)) continue;

                list.Add(new DoorInfo { room = new[] { kv.Key.x, kv.Key.y }, door = d });
            }
        }
        return list.ToArray();
    }

    static bool OwnsSharedDoor(Vector2Int a, Vector2Int b) => a.x != b.x ? a.x < b.x : a.y < b.y;

    void ResolveRefs()
    {
        if (gridManager == null) gridManager = FindFirstObjectByType<MazeGridManager>(FindObjectsInactive.Include);
        if (roomTracker == null) roomTracker = FindFirstObjectByType<PlayerRoomTracker>(FindObjectsInactive.Include);
        if (wristHUD == null) wristHUD = FindFirstObjectByType<WristHUD>(FindObjectsInactive.Include);
        if (crouchDetector == null) crouchDetector = FindFirstObjectByType<CrouchDetector>(FindObjectsInactive.Include);
        if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
        if (markerDispenser == null) markerDispenser = FindFirstObjectByType<MarkerDispenser>(FindObjectsInactive.Include);
    }
}
