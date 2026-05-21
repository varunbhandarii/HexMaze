using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

public enum GameLoopState { WaitingToStart, Playing, Won, Caught, TimeUp }

public class GameManager : MonoBehaviour
{
    public static GameManager Current { get; private set; }

    public float timeLimit = 180f;
    public Vector2Int entranceRoom;
    public Vector2Int exitRoom;
    public bool useMazeManagerExit = true;

    public WristHUD wristHUD;
    public MazeGridManager gridManager;
    public GuardSpawner guardSpawner;
    public HexMazeDifficultyTuning difficultyTuning;
    public PlayerRoomTracker roomTracker;
    public CrouchDetector crouchDetector;
    public Transform player;
    public Camera playerCamera;

    public bool startWithPrimaryButton = true;
    public bool restartWithPrimaryButton = true;
    public XRNode primaryInputHand = XRNode.RightHand;
    public float restartPromptDelay = 3f;

    public Canvas gameLoopCanvas;
    public Image backgroundPanel;
    public Text messageText;
    public Text promptText;
    public float screenDistance = 1.65f;
    public float screenVerticalOffset = -0.04f;
    public Vector2 screenSize = new Vector2(560f, 280f);
    public float screenScale = 0.0022f;

    public GameLoopState state = GameLoopState.WaitingToStart;
    public bool gameActive;
    public float remainingTime;
    public bool restartPromptVisible;

    bool primaryPrev;
    bool restartAllowed;
    Font runtimeFont;
    CrouchDetector subscribedCrouch;
    WristHUD[] huds;
    bool restarting;
    Coroutine timerCo;

    void Awake()
    {
        gameObject.name = "GameManager";
        if (Current != null && Current != this && Current.gameObject != null && Current.isActiveAndEnabled)
        {
            enabled = false;
            Debug.LogWarning("Disabled duplicate GameManager.", this);
            return;
        }
        Current = this;

        Time.timeScale = 1f;
        ResolveRefs();
        if (difficultyTuning != null) difficultyTuning.ApplyToScene();
        if (guardSpawner != null) guardSpawner.spawnOnStart = false;
    }

    void Start()
    {
        if (Current != this) return;

        ResolveRefs();
        ApplyMazeRooms();
        EnsureScreen();
        SubscribeRoomTracker();
        RefreshHuds();

        if (difficultyTuning != null) difficultyTuning.ApplyToScene();

        remainingTime = timeLimit;
        UpdateHudTimer(remainingTime);
        ShowStartScreen();
    }

    void OnEnable()
    {
        if (Current == null) Current = this;
        SubscribeRoomTracker();
        SubscribeCrouch();
    }

    void OnDisable()
    {
        if (roomTracker != null) roomTracker.onRoomChanged.RemoveListener(OnRoomChanged);
        if (subscribedCrouch != null)
        {
            subscribedCrouch.onCalibrated.RemoveListener(OnCrouchCalibrated);
            subscribedCrouch = null;
        }
        if (Current == this) Current = null;
    }

    void Update()
    {
        if (Current != this) return;

        ResolveRefs();

        if (state != GameLoopState.Playing) PositionScreen();

        bool primary = IsPrimaryDown();
        bool primaryEdge = primary && !primaryPrev;
        primaryPrev = primary;

        if (state == GameLoopState.WaitingToStart && startWithPrimaryButton && primaryEdge)
        {
            StartGame();
            return;
        }

        if (restartAllowed && restartWithPrimaryButton && primary)
        {
            RestartGame();
            return;
        }

        if (state == GameLoopState.Playing && gameActive && timerCo == null)
            timerCo = StartCoroutine(RunTimer());
    }

    public void StartGame()
    {
        if (Current != this || state == GameLoopState.Playing) return;

        Time.timeScale = 1f;
        ResolveRefs();
        ApplyMazeRooms();
        if (difficultyTuning != null) difficultyTuning.ApplyToScene();

        state = GameLoopState.Playing;
        gameActive = true;
        restartAllowed = false;
        restartPromptVisible = false;
        remainingTime = timeLimit;

        HideScreen();
        StopTimer();

        if (crouchDetector != null) crouchDetector.CalibrateStandingHeight();

        if (guardSpawner != null)
        {
            guardSpawner.spawnOnStart = false;
            guardSpawner.rebuildNavMeshBeforeSpawning = false;
            guardSpawner.ClearSpawnedGuards();
            guardSpawner.SpawnGuards();
        }
        if (difficultyTuning != null) difficultyTuning.ApplyExistingGuardSettings();

        SetGuardsEnabled(true);
        RefreshHuds();
        UpdateHudTimer(remainingTime);
        timerCo = StartCoroutine(RunTimer());
        SetHudStatus("Started", 1.5f);

        if (roomTracker != null && roomTracker.GetCurrentRoom() == exitRoom) OnPlayerReachedExit();
    }

    public void OnPlayerReachedExit()
    {
        if (!gameActive) return;
        gameActive = false;
        state = GameLoopState.Won;
        restartAllowed = true;
        StopTimer();

        float elapsed = Mathf.Max(0f, timeLimit - remainingTime);
        int mins = Mathf.FloorToInt(elapsed / 60f);
        int secs = Mathf.FloorToInt(elapsed % 60f);
        int visited = wristHUD != null ? wristHUD.GetVisitedRooms().Count : 0;
        int total = gridManager != null ? gridManager.GetAllRooms().Count : 0;

        ShowEndScreen($"ESCAPED\nTime {mins:00}:{secs:00}\nRooms {visited}/{total}",
            "Press A to restart", new Color(0.35f, 0.95f, 0.45f, 1f));
    }

    public void OnPlayerCaught()
    {
        if (!gameActive && state != GameLoopState.Playing) return;
        gameActive = false;
        state = GameLoopState.Caught;
        restartAllowed = true;
        StopTimer();
        ShowEndScreen("CAUGHT\nThe guard found you", "Press A to restart", new Color(1f, 0.3f, 0.24f, 1f));
    }

    public void RestartGame()
    {
        if (restarting) return;
        restarting = true;
        StopTimer();
        Time.timeScale = 1f;

        var scene = SceneManager.GetActiveScene();
        if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
        else SceneManager.LoadScene(scene.name);
    }

    public float GetRemainingTime() => Mathf.Max(0f, remainingTime);

    void OnTimeUp()
    {
        if (!gameActive) return;
        gameActive = false;
        state = GameLoopState.TimeUp;
        restartAllowed = true;
        StopTimer();
        ShowEndScreen("TIME UP\nYou ran out of time", "Press A to restart", new Color(1f, 0.64f, 0.18f, 1f));
    }

    void ShowStartScreen()
    {
        state = GameLoopState.WaitingToStart;
        gameActive = false;
        restartAllowed = false;
        restartPromptVisible = false;
        StopTimer();

        if (guardSpawner != null)
        {
            guardSpawner.spawnOnStart = false;
            guardSpawner.ClearSpawnedGuards();
        }
        SetGuardsEnabled(false);
        ShowScreen("HEXMAZE\nReach the green exit\nAvoid the guards", "Press A to begin", new Color(0.76f, 1f, 0.92f, 1f));
    }

    void ShowEndScreen(string message, string prompt, Color color)
    {
        SetGuardsEnabled(false);
        restartAllowed = true;
        restartPromptVisible = true;
        ShowScreen(message, prompt, color);
    }

    IEnumerator RunTimer()
    {
        while (state == GameLoopState.Playing && gameActive)
        {
            remainingTime = Mathf.Max(0f, remainingTime - Time.unscaledDeltaTime);
            UpdateHudTimer(remainingTime);

            if (remainingTime <= 0f)
            {
                timerCo = null;
                OnTimeUp();
                yield break;
            }
            yield return null;
        }
        timerCo = null;
    }

    void StopTimer()
    {
        if (timerCo == null) return;
        StopCoroutine(timerCo);
        timerCo = null;
    }

    void ShowScreen(string message, string prompt, Color color)
    {
        EnsureScreen();
        PositionScreen();

        if (gameLoopCanvas != null) gameLoopCanvas.gameObject.SetActive(true);
        if (messageText != null) { messageText.text = message; messageText.color = color; }
        if (promptText != null) promptText.text = prompt;
    }

    void HideScreen()
    {
        if (gameLoopCanvas != null) gameLoopCanvas.gameObject.SetActive(false);
    }

    void EnsureScreen()
    {
        if (gameLoopCanvas != null && messageText != null && promptText != null) return;

        if (runtimeFont == null) runtimeFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var canvasGo = gameLoopCanvas != null ? gameLoopCanvas.gameObject : new GameObject("Game Loop Screen");
        canvasGo.transform.SetParent(transform, false);
        gameLoopCanvas = canvasGo.GetComponent<Canvas>();
        if (gameLoopCanvas == null) gameLoopCanvas = canvasGo.AddComponent<Canvas>();

        gameLoopCanvas.renderMode = RenderMode.WorldSpace;
        gameLoopCanvas.sortingOrder = 30;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 24f;

        canvasGo.GetComponent<RectTransform>().sizeDelta = screenSize;
        canvasGo.transform.localScale = Vector3.one * screenScale;

        if (backgroundPanel == null)
        {
            var bg = new GameObject("Background");
            bg.transform.SetParent(canvasGo.transform, false);
            backgroundPanel = bg.AddComponent<Image>();
            backgroundPanel.color = new Color(0f, 0f, 0f, 0.78f);
            var rect = bg.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        if (messageText == null) messageText = MakeText(canvasGo.transform, "Message", 34, TextAnchor.MiddleCenter, new Vector2(36f, 74f), new Vector2(-36f, -32f));
        if (promptText == null) promptText = MakeText(canvasGo.transform, "Prompt", 24, TextAnchor.MiddleCenter, new Vector2(36f, 18f), new Vector2(-36f, -218f));
    }

    Text MakeText(Transform parent, string name, int size, TextAnchor anchor, Vector2 offsetMin, Vector2 offsetMax)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var t = go.AddComponent<Text>();
        t.font = runtimeFont;
        t.fontSize = size;
        t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = Mathf.Max(14, size - 10);
        t.resizeTextMaxSize = size;
        t.color = Color.white;

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        return t;
    }

    void PositionScreen()
    {
        if (playerCamera == null) playerCamera = Camera.main;
        if (player == null && playerCamera != null) player = playerCamera.transform;
        var view = playerCamera != null ? playerCamera.transform : player;
        if (view == null || gameLoopCanvas == null) return;

        Vector3 fwd = Vector3.ProjectOnPlane(view.forward, Vector3.up);
        if (fwd.sqrMagnitude < 0.001f) fwd = view.forward;
        fwd.Normalize();

        Vector3 pos = view.position + fwd * screenDistance;
        pos.y = view.position.y + screenVerticalOffset;

        gameLoopCanvas.transform.position = pos;
        gameLoopCanvas.transform.rotation = Quaternion.LookRotation(pos - view.position, Vector3.up);
    }

    void SetGuardsEnabled(bool enabled)
    {
        foreach (var g in FindObjectsByType<GuardAI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var agent = g.GetComponent<NavMeshAgent>();
            if (agent != null && agent.enabled && agent.isOnNavMesh) agent.isStopped = !enabled;
            var det = g.GetComponent<GuardDetection>();
            if (det != null) det.enabled = enabled;
            var audio = g.GetComponent<GuardAudio>();
            if (audio != null) audio.enabled = enabled;
            g.enabled = enabled;
        }
    }

    void OnRoomChanged(Vector2Int room)
    {
        if (gameActive && room == exitRoom) OnPlayerReachedExit();
    }

    void ApplyMazeRooms()
    {
        if (gridManager == null || !useMazeManagerExit) return;
        entranceRoom = gridManager.entranceCoord;
        exitRoom = gridManager.exitCoord;
    }

    void SubscribeRoomTracker()
    {
        if (roomTracker == null) return;
        roomTracker.onRoomChanged.RemoveListener(OnRoomChanged);
        roomTracker.onRoomChanged.AddListener(OnRoomChanged);
    }

    void SubscribeCrouch()
    {
        if (crouchDetector == subscribedCrouch) return;

        if (subscribedCrouch != null) subscribedCrouch.onCalibrated.RemoveListener(OnCrouchCalibrated);

        subscribedCrouch = crouchDetector;
        if (subscribedCrouch != null)
        {
            subscribedCrouch.onCalibrated.RemoveListener(OnCrouchCalibrated);
            subscribedCrouch.onCalibrated.AddListener(OnCrouchCalibrated);
        }
    }

    void OnCrouchCalibrated() => HandleCrouchCalibrationButton();

    public void HandleCrouchCalibrationButton()
    {
        if (state == GameLoopState.WaitingToStart) { StartGame(); return; }
        if (restartAllowed || state == GameLoopState.Won || state == GameLoopState.Caught || state == GameLoopState.TimeUp)
            RestartGame();
    }

    void RefreshHuds()
    {
        huds = FindObjectsByType<WristHUD>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (wristHUD == null && huds.Length > 0) wristHUD = huds[0];

        foreach (var hud in huds)
        {
            if (hud == null) continue;
            hud.gameManager = this;
            hud.readTimerFromGameManager = true;
            hud.useExternalTimer = true;
        }
    }

    void UpdateHudTimer(float value)
    {
        if (huds == null || huds.Length == 0) RefreshHuds();
        if (huds == null) return;
        foreach (var hud in huds) if (hud != null) hud.UpdateTimer(value);
    }

    void SetHudStatus(string message, float duration)
    {
        if (huds == null || huds.Length == 0) RefreshHuds();
        if (huds == null) return;
        foreach (var hud in huds) if (hud != null) hud.SetStatus(message, duration);
    }

    bool IsPrimaryDown()
    {
        if (Input.GetKey(KeyCode.Space)) return true;
        if (PrimaryButton(primaryInputHand)) return true;
        return PrimaryButton(primaryInputHand == XRNode.RightHand ? XRNode.LeftHand : XRNode.RightHand);
    }

    static bool PrimaryButton(XRNode hand)
    {
        var d = InputDevices.GetDeviceAtXRNode(hand);
        return d.isValid && d.TryGetFeatureValue(CommonUsages.primaryButton, out bool p) && p;
    }

    void ResolveRefs()
    {
        if (gridManager == null) gridManager = FindFirstObjectByType<MazeGridManager>(FindObjectsInactive.Include);
        if (guardSpawner == null) guardSpawner = FindFirstObjectByType<GuardSpawner>(FindObjectsInactive.Include);
        if (wristHUD == null) wristHUD = FindFirstObjectByType<WristHUD>(FindObjectsInactive.Include);
        if (difficultyTuning == null) difficultyTuning = FindFirstObjectByType<HexMazeDifficultyTuning>(FindObjectsInactive.Include);
        if (roomTracker == null) roomTracker = FindFirstObjectByType<PlayerRoomTracker>(FindObjectsInactive.Include);
        if (crouchDetector == null) crouchDetector = FindFirstObjectByType<CrouchDetector>(FindObjectsInactive.Include);

        SubscribeCrouch();
        if (playerCamera == null) playerCamera = Camera.main;
        if (player == null && playerCamera != null) player = playerCamera.transform;
    }
}
