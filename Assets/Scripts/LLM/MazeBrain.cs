using UnityEngine;

public class MazeBrain : MonoBehaviour
{
    public MazeStateSerializer stateSerializer;
    public LLMClient llmClient;
    public CommandDispatcher dispatcher;
    public PlayerRoomTracker roomTracker;
    public GameManager gameManager;
    public WristHUD wristHUD;

    public bool enableLLMBrain = true;
    public bool requestOnGameStart = true;
    public bool applyCachedResponseOnRoomEntry = true;
    public bool discardLateResponses = true;
    public float requestRetryDelay = 2f;
    public float startupRequestDelay = 0.75f;

    [SerializeField] bool requestInFlight;
    [SerializeField] bool hasCached;
    [SerializeField] int callCount;

    LLMResponse cached;
    float nextRetry;
    float gameStartedAt;
    bool wasActive;
    bool requestedForGame;

    public int GetCallCount() => callCount;
    public bool IsRequestPending() => requestInFlight;

    void Awake() => ResolveRefs();

    void OnEnable()
    {
        ResolveRefs();
        Subscribe();
    }

    void Start()
    {
        ResolveRefs();
        Subscribe();
    }

    void OnDisable()
    {
        if (roomTracker != null) roomTracker.onRoomChanged.RemoveListener(OnPlayerEnteredRoom);
    }

    void Update()
    {
        bool active = gameManager != null && gameManager.gameActive;

        if (active && !wasActive)
        {
            gameStartedAt = Time.time;
            requestedForGame = false;
            cached = null;
            hasCached = false;
        }
        if (!active && wasActive)
        {
            requestInFlight = false;
            cached = null;
            hasCached = false;
            requestedForGame = false;
        }
        wasActive = active;

        if (!enableLLMBrain || !active || !requestOnGameStart || requestedForGame) return;
        if (Time.time - gameStartedAt >= startupRequestDelay)
            requestedForGame = RequestUpdate(CurrentRoom());
    }

    public void OnPlayerEnteredRoom(Vector2Int newRoom)
    {
        if (!Allowed()) return;

        if (applyCachedResponseOnRoomEntry && hasCached && cached != null)
        {
            if (dispatcher != null && dispatcher.Execute(cached))
            {
                if (wristHUD != null) wristHUD.SetStatus("Maze shifted", 1.2f);
            }
            cached = null;
            hasCached = false;
        }

        if (!requestInFlight) RequestUpdate(newRoom);
    }

    public void RequestLLMUpdateNow() => RequestUpdate(CurrentRoom());

    bool RequestUpdate(Vector2Int room)
    {
        ResolveRefs();
        if (!Allowed() || requestInFlight) return false;

        if (llmClient == null || !llmClient.IsInitialized)
        {
            if (Time.time >= nextRetry) nextRetry = Time.time + Mathf.Max(0.25f, requestRetryDelay);
            return false;
        }

        if (stateSerializer == null) return false;
        string json = stateSerializer.Serialize();
        if (string.IsNullOrWhiteSpace(json)) return false;

        requestInFlight = true;
        callCount++;

        llmClient.SendRequestForResponse(json,
            r =>
            {
                requestInFlight = false;
                if (!Allowed() || r == null) return;
                if (discardLateResponses && CurrentRoom() != room) return;

                cached = r;
                hasCached = true;
                if (wristHUD != null) wristHUD.SetStatus("Maze thinking", 1.0f);
            },
            err =>
            {
                requestInFlight = false;
                cached = null;
                hasCached = false;
            });

        return true;
    }

    bool Allowed()
    {
        if (!enableLLMBrain) return false;
        if (gameManager != null && !gameManager.gameActive) return false;
        return true;
    }

    Vector2Int CurrentRoom() => roomTracker != null ? roomTracker.GetCurrentRoom() : Vector2Int.zero;

    void Subscribe()
    {
        if (roomTracker == null) return;
        roomTracker.onRoomChanged.RemoveListener(OnPlayerEnteredRoom);
        roomTracker.onRoomChanged.AddListener(OnPlayerEnteredRoom);
    }

    void ResolveRefs()
    {
        if (stateSerializer == null) stateSerializer = FindFirstObjectByType<MazeStateSerializer>(FindObjectsInactive.Include);
        if (llmClient == null) llmClient = FindFirstObjectByType<LLMClient>(FindObjectsInactive.Include);
        if (dispatcher == null) dispatcher = FindFirstObjectByType<CommandDispatcher>(FindObjectsInactive.Include);
        if (roomTracker == null) roomTracker = FindFirstObjectByType<PlayerRoomTracker>(FindObjectsInactive.Include);
        if (gameManager == null) gameManager = GameManager.Current ?? FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
        if (wristHUD == null) wristHUD = FindFirstObjectByType<WristHUD>(FindObjectsInactive.Include);
    }
}
