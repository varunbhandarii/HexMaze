using System;
using System.Text;
using UnityEngine;

public class CommandDispatcher : MonoBehaviour
{
    public MazeGridManager gridManager;
    public NavMeshRebaker navMeshRebaker;
    public GameManager gameManager;
    public WristHUD wristHUD;

    public bool requestNavMeshRebakeAfterDoorChanges = true;
    public bool updateHudStatus = true;

    [SerializeField] string lastDemoSummary;
    [SerializeField] string lastReasoning;

    GuardAI[] guards = new GuardAI[0];
    readonly StringBuilder sb = new StringBuilder(256);

    public string LastDemoSummary => lastDemoSummary;
    public string LastReasoning => lastReasoning;

    void Awake()
    {
        ResolveRefs();
        RefreshGuardCache();
    }

    public bool Execute(LLMResponse response)
    {
        ResolveRefs();
        if (response == null) return false;
        if (gridManager == null) { Debug.LogWarning("MazeGridManager missing.", this); return false; }

        RefreshGuardCache();

        int locked = RunDoorCommands(response.lock_doors, true);
        int unlocked = RunDoorCommands(response.unlock_doors, false);
        int guardsMoved = RunGuardCommands(response.guard_targets);
        bool exitMoved = RunExitCommand(response.exit_room);

        bool mazeChanged = locked > 0 || unlocked > 0 || exitMoved;
        if (mazeChanged && requestNavMeshRebakeAfterDoorChanges)
        {
            if (navMeshRebaker != null) navMeshRebaker.RequestRebake();
            else NavMeshRebaker.RequestSceneRebake();
        }

        lastReasoning = string.IsNullOrWhiteSpace(response.reasoning) ? "No reasoning provided." : response.reasoning;
        lastDemoSummary = BuildSummary(response, locked, unlocked, guardsMoved, exitMoved);

        if (updateHudStatus && wristHUD != null && (mazeChanged || guardsMoved > 0))
            wristHUD.SetStatus("Maze shifted", 1.5f);

        return true;
    }

    public void RefreshGuardCache()
    {
        guards = FindObjectsByType<GuardAI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Array.Sort(guards, (a, b) => string.CompareOrdinal(a != null ? a.name : "", b != null ? b.name : ""));
    }

    string BuildSummary(LLMResponse r, int locked, int unlocked, int guardsMoved, bool exitMoved)
    {
        sb.Length = 0;
        if (locked == 0 && unlocked == 0 && guardsMoved == 0 && !exitMoved)
            return "AI watched this turn. No maze change was needed.";

        AppendDoorList("Locked", r.lock_doors, locked);
        AppendDoorList("Unlocked", r.unlock_doors, unlocked);

        if (guardsMoved > 0)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append("Redirected guards: ");
            int shown = 0;
            for (int i = 0; i < r.guard_targets.Length; i++)
            {
                var c = r.guard_targets[i];
                if (c == null || c.target_room == null || c.target_room.Length < 2) continue;
                if (shown > 0) sb.Append(", ");
                sb.Append("G").Append(c.guard_id).Append(" -> ")
                  .Append($"[{c.target_room[0]},{c.target_room[1]}]");
                shown++;
            }
        }

        if (exitMoved && r.exit_room != null && r.exit_room.Length >= 2)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append("Moved exit to ").Append($"[{r.exit_room[0]},{r.exit_room[1]}]");
        }

        return sb.ToString();
    }

    void AppendDoorList(string label, DoorCommand[] cmds, int applied)
    {
        if (applied <= 0 || cmds == null) return;
        if (sb.Length > 0) sb.AppendLine();
        sb.Append(label).Append(" doors: ");

        int shown = 0;
        for (int i = 0; i < cmds.Length; i++)
        {
            var c = cmds[i];
            if (c == null || c.room == null || c.room.Length < 2) continue;
            if (shown > 0) sb.Append(", ");
            sb.Append($"[{c.room[0]},{c.room[1]}] D").Append(c.door);
            shown++;
        }
    }

    int RunDoorCommands(DoorCommand[] cmds, bool lockDoor)
    {
        if (cmds == null) return 0;
        int applied = 0;
        foreach (var c in cmds)
        {
            if (c == null || c.room == null || c.room.Length < 2) continue;
            var coord = new Vector2Int(c.room[0], c.room[1]);
            if (lockDoor) gridManager.LockDoor(coord, c.door);
            else gridManager.UnlockDoor(coord, c.door);
            applied++;
        }
        return applied;
    }

    int RunGuardCommands(GuardCommand[] cmds)
    {
        if (cmds == null) return 0;
        int applied = 0;
        foreach (var c in cmds)
        {
            if (c == null || c.target_room == null || c.target_room.Length < 2) continue;
            if (c.guard_id < 0 || c.guard_id >= guards.Length || guards[c.guard_id] == null) continue;

            guards[c.guard_id].SetPatrolTarget(new Vector2Int(c.target_room[0], c.target_room[1]));
            applied++;
        }
        return applied;
    }

    bool RunExitCommand(int[] exitRoom)
    {
        if (exitRoom == null || exitRoom.Length < 2) return false;
        var newExit = new Vector2Int(exitRoom[0], exitRoom[1]);
        if (!gridManager.SetExitRoom(newExit)) return false;
        if (gameManager != null) gameManager.exitRoom = newExit;
        return true;
    }

    void ResolveRefs()
    {
        if (gridManager == null) gridManager = FindFirstObjectByType<MazeGridManager>(FindObjectsInactive.Include);
        if (navMeshRebaker == null) navMeshRebaker = FindFirstObjectByType<NavMeshRebaker>(FindObjectsInactive.Include);
        if (gameManager == null) gameManager = GameManager.Current ?? FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
        if (wristHUD == null) wristHUD = FindFirstObjectByType<WristHUD>(FindObjectsInactive.Include);
    }
}
