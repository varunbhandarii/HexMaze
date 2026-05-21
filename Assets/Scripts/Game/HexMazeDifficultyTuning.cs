using UnityEngine;
using UnityEngine.AI;

public class HexMazeDifficultyTuning : MonoBehaviour
{
    public float timeLimit = 180f;

    public int guardCount = 2;
    public float guardPatrolSpeed = 0.75f;
    public float guardChaseSpeed = 1.0f;
    public float barricadeBlockTime = 3f;

    public float viewRange = 8f;
    public float viewAngle = 90f;
    public float peripheralRange = 3f;
    public float detectionBuildTime = 1.5f;
    public float detectionDecayTime = 1f;
    public float chaseLossTimeout = 4f;

    [Range(0f, 1f)] public float lockedDoorChance = 0.4f;
    public int markerLimit = 20;

    public bool applyOnAwake = true;
    public bool updateWristHudTimer = true;

    void Awake()
    {
        if (applyOnAwake) ApplyToScene();
    }

    [ContextMenu("Apply Difficulty To Scene")]
    public void ApplyToScene()
    {
        ApplyGameLoop();
        ApplyMaze();
        ApplyMarkers();
        ApplyGuardSpawner();
        ApplyExistingGuardSettings();
    }

    public void ApplyExistingGuardSettings()
    {
        foreach (var ai in FindObjectsByType<GuardAI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            ai.patrolSpeed = guardPatrolSpeed;
            ai.chaseSpeed = guardChaseSpeed;
            ai.blockedTimeout = barricadeBlockTime;
            var agent = ai.GetComponent<NavMeshAgent>();
            if (agent != null) agent.speed = guardPatrolSpeed;
        }

        foreach (var det in FindObjectsByType<GuardDetection>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            det.viewRange = viewRange;
            det.viewAngle = viewAngle;
            det.peripheralRange = peripheralRange;
            det.detectionBuildRate = detectionBuildTime;
            det.detectionDecayRate = detectionDecayTime;
            det.chaseLossTimeout = chaseLossTimeout;
        }
    }

    void ApplyGameLoop()
    {
        var gm = GameManager.Current ?? FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
        if (gm == null) return;

        gm.timeLimit = timeLimit;
        if (!gm.gameActive) gm.remainingTime = timeLimit;

        foreach (var hud in FindObjectsByType<WristHUD>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            hud.gameManager = gm;
            hud.readTimerFromGameManager = true;
        }

        if (updateWristHudTimer && gm.wristHUD != null)
            gm.wristHUD.UpdateTimer(gm.remainingTime > 0f ? gm.remainingTime : timeLimit);
    }

    void ApplyMaze()
    {
        var grid = FindFirstObjectByType<MazeGridManager>(FindObjectsInactive.Include);
        if (grid != null) grid.defaultLockedDoorChance = lockedDoorChance;
    }

    void ApplyMarkers()
    {
        var md = FindFirstObjectByType<MarkerDispenser>(FindObjectsInactive.Include);
        if (md != null) md.maxMarkers = markerLimit;
    }

    void ApplyGuardSpawner()
    {
        var sp = FindFirstObjectByType<GuardSpawner>(FindObjectsInactive.Include);
        if (sp != null) sp.guardCount = guardCount;
    }
}
