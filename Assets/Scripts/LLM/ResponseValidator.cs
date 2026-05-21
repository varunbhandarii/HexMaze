using System;
using System.Collections.Generic;
using UnityEngine;

public class ResponseValidator : MonoBehaviour
{
    public MazeGridManager gridManager;
    public PlayerRoomTracker playerRoomTracker;

    public int maxLockCommands = 3;
    public int maxUnlockCommands = 2;
    public int minResponsesBetweenExitMoves = 3;
    public int minExitMoveDistance = 2;
    public bool requireInteriorDoorTargets = true;

    [SerializeField] int responsesSinceExitMove = 3;
    [SerializeField] string lastRejectionReason;

    public string LastRejectionReason => lastRejectionReason;

    void Awake()
    {
        if (gridManager == null) gridManager = FindFirstObjectByType<MazeGridManager>(FindObjectsInactive.Include);
        if (playerRoomTracker == null) playerRoomTracker = FindFirstObjectByType<PlayerRoomTracker>(FindObjectsInactive.Include);
        responsesSinceExitMove = Mathf.Max(responsesSinceExitMove, minResponsesBetweenExitMoves);
    }

    public LLMResponse ValidateOrFallback(string rawJson)
    {
        if (gridManager == null) gridManager = FindFirstObjectByType<MazeGridManager>(FindObjectsInactive.Include);

        var resp = TryValidate(rawJson, out string reason);
        if (resp == null)
        {
            lastRejectionReason = string.IsNullOrWhiteSpace(reason) ? "Unknown validation error." : reason;
            return new LLMResponse
            {
                lock_doors = new DoorCommand[0],
                unlock_doors = new DoorCommand[0],
                guard_targets = new GuardCommand[0],
                exit_room = null,
                reasoning = string.IsNullOrWhiteSpace(reason) ? "Fallback: keeping current maze state." : "Fallback: " + reason
            };
        }

        lastRejectionReason = "";

        if (resp.exit_room != null && resp.exit_room.Length == 2 && new Vector2Int(resp.exit_room[0], resp.exit_room[1]) != gridManager.exitCoord)
            responsesSinceExitMove = 0;
        else
            responsesSinceExitMove = Mathf.Min(responsesSinceExitMove + 1, minResponsesBetweenExitMoves);

        return resp;
    }

    LLMResponse TryValidate(string rawJson, out string reason)
    {
        reason = "";

        if (gridManager == null) { reason = "MazeGridManager is missing."; return null; }
        if (gridManager.GetAllRooms().Count == 0) { reason = "Maze grid has no rooms."; return null; }
        if (string.IsNullOrWhiteSpace(rawJson)) { reason = "LLM returned an empty response."; return null; }

        string json = ExtractJson(StripFences(rawJson));
        if (string.IsNullOrWhiteSpace(json)) { reason = "LLM response did not contain a JSON object."; return null; }

        LLMResponse r;
        try { r = JsonUtility.FromJson<LLMResponse>(json); }
        catch (Exception ex) { reason = "JSON parse failed: " + ex.Message; return null; }
        if (r == null) { reason = "JSON parsed to null."; return null; }

        r.lock_doors ??= new DoorCommand[0];
        r.unlock_doors ??= new DoorCommand[0];
        r.guard_targets ??= new GuardCommand[0];
        if (r.exit_room != null && r.exit_room.Length == 0) r.exit_room = null;
        if (string.IsNullOrWhiteSpace(r.reasoning)) r.reasoning = "No reasoning provided.";

        if (!ValidateDoors(r.lock_doors, "lock", maxLockCommands, out reason)) return null;
        if (!ValidateDoors(r.unlock_doors, "unlock", maxUnlockCommands, out reason)) return null;

        var locks = new HashSet<string>();
        foreach (var d in r.lock_doors) locks.Add(CanonicalKey(new Vector2Int(d.room[0], d.room[1]), d.door));
        foreach (var d in r.unlock_doors)
        {
            var key = CanonicalKey(new Vector2Int(d.room[0], d.room[1]), d.door);
            if (locks.Contains(key)) { reason = "Response both locks and unlocks " + key + "."; return null; }
        }

        if (!ValidateGuards(r.guard_targets, out reason)) return null;
        if (!ValidateExit(r.exit_room, out reason)) return null;
        if (!PathStillReachable(r, out reason)) return null;

        return r;
    }

    bool ValidateDoors(DoorCommand[] cmds, string label, int max, out string reason)
    {
        reason = "";
        if (cmds == null) return true;
        if (cmds.Length > Mathf.Max(0, max)) { reason = $"Too many {label} door commands: {cmds.Length}."; return false; }

        var seen = new HashSet<string>();
        for (int i = 0; i < cmds.Length; i++)
        {
            var c = cmds[i];
            if (c == null) { reason = $"{label}_doors[{i}] was null."; return false; }
            if (c.room == null || c.room.Length != 2) { reason = $"{label}_doors[{i}] has invalid room."; return false; }

            var coord = new Vector2Int(c.room[0], c.room[1]);
            if (!InBounds(coord) || gridManager.GetRoom(coord) == null) { reason = $"{label} door room [{coord.x},{coord.y}] outside maze."; return false; }
            if (!HexGridData.IsValidDoorIndex(c.door)) { reason = $"{label} door index {c.door} not in 0-5."; return false; }

            var neighbor = gridManager.GetNeighbor(coord, c.door);
            if (requireInteriorDoorTargets && gridManager.GetRoom(neighbor) == null)
            { reason = $"{label} door [{coord.x},{coord.y}] door {c.door} has no neighbor."; return false; }

            string key = CanonicalKey(coord, c.door);
            if (!seen.Add(key)) { reason = $"Duplicate {label} command for {key}."; return false; }
        }
        return true;
    }

    bool ValidateGuards(GuardCommand[] cmds, out string reason)
    {
        reason = "";
        if (cmds == null) return true;

        int guardCount = FindObjectsByType<GuardAI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
        var seen = new HashSet<int>();

        for (int i = 0; i < cmds.Length; i++)
        {
            var c = cmds[i];
            if (c == null) { reason = $"guard_targets[{i}] was null."; return false; }
            if (c.guard_id < 0 || c.guard_id >= guardCount) { reason = $"Invalid guard_id {c.guard_id} (known: {guardCount})."; return false; }
            if (!seen.Add(c.guard_id)) { reason = $"Duplicate target for guard_id {c.guard_id}."; return false; }
            if (c.target_room == null || c.target_room.Length != 2) { reason = $"guard_targets[{i}] has invalid target_room."; return false; }

            var coord = new Vector2Int(c.target_room[0], c.target_room[1]);
            if (!InBounds(coord) || gridManager.GetRoom(coord) == null) { reason = $"Guard target [{coord.x},{coord.y}] outside maze."; return false; }
        }
        return true;
    }

    bool ValidateExit(int[] exit, out string reason)
    {
        reason = "";
        if (exit == null || exit.Length == 0) return true;
        if (exit.Length != 2) { reason = "exit_room must be null or [col,row]."; return false; }

        var proposed = new Vector2Int(exit[0], exit[1]);
        if (!InBounds(proposed) || gridManager.GetRoom(proposed) == null) { reason = $"exit_room [{proposed.x},{proposed.y}] outside maze."; return false; }
        if (proposed == gridManager.exitCoord) return true;

        if (responsesSinceExitMove < minResponsesBetweenExitMoves) { reason = "Exit moved too recently."; return false; }

        int dist = ShortestDistance(CurrentPlayerRoom(), gridManager.exitCoord);
        if (dist >= 0 && dist <= minExitMoveDistance) { reason = "Player too close to current exit for an exit move."; return false; }

        return true;
    }

    bool PathStillReachable(LLMResponse r, out string reason)
    {
        reason = "";
        var player = CurrentPlayerRoom();
        var exit = r.exit_room != null && r.exit_room.Length == 2 ? new Vector2Int(r.exit_room[0], r.exit_room[1]) : gridManager.exitCoord;

        if (gridManager.GetRoom(player) == null) { reason = $"Player room [{player.x},{player.y}] not in maze."; return false; }
        if (gridManager.GetRoom(exit) == null) { reason = $"Exit room [{exit.x},{exit.y}] not in maze."; return false; }

        var locked = CurrentLockedSet();
        foreach (var d in r.lock_doors) locked.Add(CanonicalKey(new Vector2Int(d.room[0], d.room[1]), d.door));
        foreach (var d in r.unlock_doors) locked.Remove(CanonicalKey(new Vector2Int(d.room[0], d.room[1]), d.door));

        if (!ReachableWithLocks(player, exit, locked))
        {
            reason = "Response would remove every valid path from player to exit.";
            return false;
        }
        return true;
    }

    HashSet<string> CurrentLockedSet()
    {
        var set = new HashSet<string>();
        foreach (var kv in gridManager.GetAllRooms())
        {
            if (kv.Value == null) continue;
            for (int d = 0; d < HexGridData.DirectionCount; d++)
            {
                if (gridManager.GetRoom(gridManager.GetNeighbor(kv.Key, d)) == null) continue;
                if (gridManager.IsDoorLocked(kv.Key, d)) set.Add(CanonicalKey(kv.Key, d));
            }
        }
        return set;
    }

    bool ReachableWithLocks(Vector2Int start, Vector2Int goal, HashSet<string> locked)
    {
        var q = new Queue<Vector2Int>();
        var seen = new HashSet<Vector2Int>();
        q.Enqueue(start);
        seen.Add(start);

        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            if (cur == goal) return true;

            for (int d = 0; d < HexGridData.DirectionCount; d++)
            {
                var n = gridManager.GetNeighbor(cur, d);
                if (seen.Contains(n) || gridManager.GetRoom(n) == null) continue;
                if (locked.Contains(CanonicalKey(cur, d))) continue;
                seen.Add(n);
                q.Enqueue(n);
            }
        }
        return false;
    }

    int ShortestDistance(Vector2Int start, Vector2Int goal)
    {
        if (gridManager.GetRoom(start) == null || gridManager.GetRoom(goal) == null) return -1;

        var q = new Queue<Vector2Int>();
        var dist = new Dictionary<Vector2Int, int>();
        q.Enqueue(start);
        dist[start] = 0;

        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            if (cur == goal) return dist[cur];

            for (int d = 0; d < HexGridData.DirectionCount; d++)
            {
                var n = gridManager.GetNeighbor(cur, d);
                if (dist.ContainsKey(n) || gridManager.GetRoom(n) == null) continue;
                dist[n] = dist[cur] + 1;
                q.Enqueue(n);
            }
        }
        return -1;
    }

    string CanonicalKey(Vector2Int coord, int door)
    {
        var n = gridManager.GetNeighbor(coord, door);
        if (gridManager.GetRoom(n) == null) return $"{coord.x},{coord.y},{door}";

        bool firstWins = coord.x != n.x ? coord.x < n.x : coord.y <= n.y;
        return firstWins ? $"{coord.x},{coord.y},{door}" : $"{n.x},{n.y},{gridManager.OppositeDoor(door)}";
    }

    static string StripFences(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        string v = raw.Trim();
        if (!v.StartsWith("```", StringComparison.Ordinal)) return v;

        int first = v.IndexOf('\n');
        int last = v.LastIndexOf("```", StringComparison.Ordinal);
        if (first < 0 || last <= first) return v.Trim('`').Trim();
        return v.Substring(first + 1, last - first - 1).Trim();
    }

    static string ExtractJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        string t = value.Trim();
        if (t.StartsWith("{") && t.EndsWith("}")) return t;
        int s = t.IndexOf('{');
        int e = t.LastIndexOf('}');
        return s < 0 || e <= s ? "" : t.Substring(s, e - s + 1).Trim();
    }

    bool InBounds(Vector2Int c) => c.x >= 0 && c.x < gridManager.gridCols && c.y >= 0 && c.y < gridManager.gridRows;

    Vector2Int CurrentPlayerRoom()
    {
        if (playerRoomTracker == null) playerRoomTracker = FindFirstObjectByType<PlayerRoomTracker>(FindObjectsInactive.Include);
        if (playerRoomTracker != null) return playerRoomTracker.GetCurrentRoom();
        return gridManager.entranceCoord;
    }
}
