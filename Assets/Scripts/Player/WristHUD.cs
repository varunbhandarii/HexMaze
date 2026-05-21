using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class WristHUD : MonoBehaviour
{
    public Text timerText;
    public Text markerCountText;
    public Text roomCountText;
    public Text statusText;
    public Image detectionBar;
    public GuardDetection[] guardDetections;
    public bool createTextMeshFallback = true;
    public bool useMeshOnlyDisplay = true;
    public float textMeshFallbackDepth = -3f;
    public Vector2 meshPanelSize = new Vector2(190f, 120f);
    public bool showDetectionBar = true;
    public Vector2 detectionBarSize = new Vector2(148f, 5f);
    public Vector3 detectionBarLocalPosition = new Vector3(0f, -53f, -3f);
    public GameObject meshHudRoot;
    public GameObject meshDetectionTrack;
    public GameObject meshDetectionFill;
    public TextMesh timerMesh;
    public TextMesh markerCountMesh;
    public TextMesh roomCountMesh;
    public TextMesh statusMesh;

    public MarkerDispenser markerDispenser;
    public PlayerRoomTracker roomTracker;
    public CrouchDetector crouchDetector;
    public LocomotionModeManager locomotionModeManager;
    public MazeGridManager gridManager;
    public GameManager gameManager;

    public bool countUpTimer = true;
    public bool useExternalTimer;
    public bool readTimerFromGameManager = true;
    public float externalTimerRemaining;
    public bool showTotalRoomCount = true;
    public bool showModeInStatus = true;
    public string defaultStatus = "Exploring";

    readonly HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
    float elapsed;
    Vector2Int currentRoom;
    string statusOverride;
    float statusOverrideUntil;
    Font font;
    MeshRenderer detectionFillRend;
    float nextDetectionRefresh;
    GameLoopState lastObservedState = GameLoopState.WaitingToStart;
    float lastManagerRemaining = -1f;
    float timerMirror;
    bool timerMirrorReady;

    void OnEnable()
    {
        ResolveRefs();
        Subscribe();
    }

    void Start()
    {
        ResolveRefs();
        Subscribe();
        BuildMeshFallback();

        if (roomTracker != null) OnRoomChanged(roomTracker.GetCurrentRoom());
        PullTimer();
        Redraw();
    }

    void OnDisable()
    {
        if (roomTracker != null) roomTracker.onRoomChanged.RemoveListener(OnRoomChanged);
    }

    void Update()
    {
        ResolveRefs();
        BuildMeshFallback();

        if (countUpTimer && !useExternalTimer) elapsed += Time.deltaTime;
        PullTimer();
        Redraw();
    }

    public void SetStatus(string message, float duration = 2f)
    {
        statusOverride = message;
        statusOverrideUntil = Time.time + duration;
        Redraw();
    }

    public void UpdateTimer(float remaining)
    {
        useExternalTimer = true;
        externalTimerRemaining = Mathf.Max(0f, remaining);
        Redraw();
    }

    public HashSet<Vector2Int> GetVisitedRooms() => visited;

    void OnRoomChanged(Vector2Int newRoom)
    {
        currentRoom = newRoom;
        visited.Add(newRoom);
        Redraw();
    }

    void Redraw()
    {
        float t = useExternalTimer ? externalTimerRemaining : elapsed;
        int mins = Mathf.FloorToInt(t / 60f);
        int secs = Mathf.FloorToInt(t % 60f);
        string timer = $"{mins:00}:{secs:00}";
        SetLabel(timerText, timerMesh, timer, "Timer Mesh");

        Color timerColor = !useExternalTimer || t >= 30f
            ? new Color(1f, 0.96f, 0.82f, 1f)
            : Color.Lerp(new Color(1f, 0.16f, 0.1f, 1f), new Color(1f, 0.96f, 0.82f, 1f), Mathf.PingPong(Time.time * 3f, 1f));
        if (timerText != null) timerText.color = timerColor;
        if (timerMesh != null) timerMesh.color = timerColor;

        string marker = markerDispenser != null ? $"M {markerDispenser.GetMarkersRemaining()}" : "M --";
        SetLabel(markerCountText, markerCountMesh, marker, "Marker Mesh");

        int total = gridManager != null ? gridManager.GetAllRooms().Count : 0;
        string room = showTotalRoomCount && total > 0 ? $"R {visited.Count}/{total}" : $"R {visited.Count}";
        SetLabel(roomCountText, roomCountMesh, room, "Room Mesh");

        SetLabel(statusText, statusMesh, BuildStatusText(), "Status Mesh");
        DrawDetection();
    }

    void PullTimer()
    {
        if (!readTimerFromGameManager) return;

        if (GameManager.Current != null && gameManager != GameManager.Current) gameManager = GameManager.Current;
        if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
        if (gameManager == null) return;

        float managerRemaining = Mathf.Max(0f, gameManager.remainingTime);
        useExternalTimer = true;

        if (gameManager.state == GameLoopState.Playing)
        {
            bool stateChanged = !timerMirrorReady || lastObservedState != gameManager.state;
            bool changed = Mathf.Abs(managerRemaining - lastManagerRemaining) > 0.001f;
            if (stateChanged || changed) timerMirror = managerRemaining;
            else timerMirror = Mathf.Max(0f, timerMirror - Time.unscaledDeltaTime);
            externalTimerRemaining = timerMirror;
        }
        else
        {
            timerMirror = managerRemaining;
            externalTimerRemaining = managerRemaining;
        }

        lastObservedState = gameManager.state;
        lastManagerRemaining = managerRemaining;
        timerMirrorReady = true;
    }

    void SetLabel(Text ui, TextMesh mesh, string value, string meshName)
    {
        if (ui != null) ui.text = value;
        if (mesh != null) mesh.text = value;

        string token = meshName.Replace(" Mesh", "");
        foreach (var l in GetComponentsInChildren<Text>(true))
            if (l != null && l.name.Contains(token)) l.text = value;
        foreach (var l in GetComponentsInChildren<TextMesh>(true))
            if (l != null && l.name == meshName) l.text = value;
    }

    string BuildStatusText()
    {
        if (!string.IsNullOrEmpty(statusOverride) && Time.time <= statusOverrideUntil) return statusOverride;
        if (crouchDetector != null && crouchDetector.isCrouching) return "CROUCH";

        string mode = showModeInStatus && locomotionModeManager != null ? "MOVE" : defaultStatus;
        return $"{mode} {currentRoom.x},{currentRoom.y}";
    }

    void BuildMeshFallback()
    {
        if (!createTextMeshFallback) return;

        if (useMeshOnlyDisplay)
        {
            var canvas = GetComponent<Canvas>();
            if (canvas != null) canvas.enabled = false;

            var vis = GetComponent<WristHUDVisibility>();
            if (vis != null)
            {
                vis.useMeshHudInsteadOfCanvas = true;
                vis.meshHudRoot = meshHudRoot;
            }
        }

        if (font == null)
        {
            try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch (UnityException) { }
        }

        Transform parent = useMeshOnlyDisplay ? EnsureMeshRoot().transform : transform;

        timerMesh = timerMesh ?? FindMesh(parent, "Timer Mesh") ?? MakeMesh(parent, "Timer Mesh",
            new Vector3(-82f, 47f, textMeshFallbackDepth), TextAnchor.UpperLeft, 48, 4.8f, new Color(1f, 0.96f, 0.82f, 1f));
        markerCountMesh = markerCountMesh ?? FindMesh(parent, "Marker Mesh") ?? MakeMesh(parent, "Marker Mesh",
            new Vector3(82f, 47f, textMeshFallbackDepth), TextAnchor.UpperRight, 48, 4.8f, new Color(1f, 0.66f, 0.25f, 1f));
        roomCountMesh = roomCountMesh ?? FindMesh(parent, "Room Mesh") ?? MakeMesh(parent, "Room Mesh",
            new Vector3(0f, 10f, textMeshFallbackDepth), TextAnchor.MiddleCenter, 48, 4.6f, new Color(0.72f, 1f, 0.92f, 1f));
        statusMesh = statusMesh ?? FindMesh(parent, "Status Mesh") ?? MakeMesh(parent, "Status Mesh",
            new Vector3(0f, -38f, textMeshFallbackDepth), TextAnchor.LowerCenter, 42, 3.8f, new Color(0.9f, 0.95f, 0.86f, 1f));

        if (showDetectionBar) EnsureDetectionBar(parent);
    }

    GameObject EnsureMeshRoot()
    {
        if (meshHudRoot != null) return meshHudRoot;
        var existing = transform.Find("Mesh Wrist HUD");
        if (existing != null) { meshHudRoot = existing.gameObject; return meshHudRoot; }

        meshHudRoot = new GameObject("Mesh Wrist HUD");
        meshHudRoot.transform.SetParent(transform, false);
        BuildMeshPanel(meshHudRoot.transform);

        var vis = GetComponent<WristHUDVisibility>();
        if (vis != null) vis.meshHudRoot = meshHudRoot;
        return meshHudRoot;
    }

    void EnsureDetectionBar(Transform parent)
    {
        if (meshDetectionTrack == null)
        {
            var existing = parent.Find("Detection Track");
            meshDetectionTrack = existing != null ? existing.gameObject
                : MakeBarPart(parent, "Detection Track", detectionBarSize.x, new Color(0.08f, 0.08f, 0.08f, 1f));
        }
        if (meshDetectionFill == null)
        {
            var existing = parent.Find("Detection Fill");
            meshDetectionFill = existing != null ? existing.gameObject
                : MakeBarPart(parent, "Detection Fill", 0.1f, new Color(0.28f, 0.28f, 0.28f, 1f));
        }

        meshDetectionTrack.transform.localPosition = detectionBarLocalPosition;
        meshDetectionTrack.transform.localScale = new Vector3(detectionBarSize.x, detectionBarSize.y, 1f);
        detectionFillRend = meshDetectionFill.GetComponent<MeshRenderer>();
    }

    GameObject MakeBarPart(Transform parent, string name, float width, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = detectionBarLocalPosition;
        go.transform.localScale = new Vector3(width, detectionBarSize.y, 1f);

        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = MakeUnlitMat(color);
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;

        var c = go.GetComponent<Collider>();
        if (c != null) Destroy(c);
        return go;
    }

    void DrawDetection()
    {
        if (!showDetectionBar) return;

        if (guardDetections == null || guardDetections.Length == 0 || Time.time >= nextDetectionRefresh)
        {
            nextDetectionRefresh = Time.time + 0.75f;
            guardDetections = FindObjectsByType<GuardDetection>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        float max = 0f;
        if (guardDetections != null)
        {
            foreach (var d in guardDetections)
                if (d != null) max = Mathf.Max(max, d.GetDetectionLevel());
        }

        Color color = max >= 0.99f ? new Color(1f, 0.08f, 0.06f, 1f)
                    : max > 0.3f ? new Color(1f, 0.45f, 0.05f, 1f)
                    : max > 0f ? new Color(1f, 0.9f, 0.18f, 1f)
                    : new Color(0.28f, 0.28f, 0.28f, 1f);

        if (detectionBar != null)
        {
            detectionBar.fillAmount = max;
            detectionBar.color = color;
        }

        if (meshDetectionFill != null)
        {
            float w = Mathf.Max(0.1f, detectionBarSize.x * max);
            float left = detectionBarLocalPosition.x - detectionBarSize.x * 0.5f;
            meshDetectionFill.transform.localScale = new Vector3(w, detectionBarSize.y, 1f);
            meshDetectionFill.transform.localPosition = new Vector3(left + w * 0.5f, detectionBarLocalPosition.y, detectionBarLocalPosition.z - 0.1f);

            if (detectionFillRend == null) detectionFillRend = meshDetectionFill.GetComponent<MeshRenderer>();
            if (detectionFillRend != null)
            {
                var m = detectionFillRend.sharedMaterial;
                if (m != null)
                {
                    if (m.HasProperty("_Color")) m.SetColor("_Color", color);
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
                }
            }
        }
    }

    void BuildMeshPanel(Transform parent)
    {
        var panel = new GameObject("Mesh Panel");
        panel.transform.SetParent(parent, false);

        var mesh = new Mesh();
        float hw = meshPanelSize.x * 0.5f;
        float hh = meshPanelSize.y * 0.5f;
        mesh.vertices = new[] {
            new Vector3(-hw, -hh, 0f), new Vector3(hw, -hh, 0f),
            new Vector3(hw, hh, 0f), new Vector3(-hw, hh, 0f)
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 2, 1, 0, 3, 2, 0 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        panel.AddComponent<MeshFilter>().sharedMesh = mesh;
        panel.AddComponent<MeshRenderer>().sharedMaterial = MakeUnlitMat(new Color(0.02f, 0.045f, 0.045f, 1f));

        var accent = GameObject.CreatePrimitive(PrimitiveType.Cube);
        accent.name = "Mesh Accent";
        accent.transform.SetParent(parent, false);
        accent.transform.localPosition = new Vector3(0f, hh - 5f, textMeshFallbackDepth);
        accent.transform.localScale = new Vector3(meshPanelSize.x - 18f, 3f, 1f);
        accent.GetComponent<MeshRenderer>().sharedMaterial = MakeUnlitMat(new Color(0.08f, 0.95f, 0.85f, 1f));

        var c = accent.GetComponent<Collider>();
        if (c != null) Destroy(c);
    }

    static Material MakeUnlitMat(Color color)
    {
        var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard");
        var m = new Material(shader);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        return m;
    }

    TextMesh MakeMesh(Transform parent, string name, Vector3 pos, TextAnchor anchor, int size, float charSize, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;

        var tm = go.AddComponent<TextMesh>();
        tm.anchor = anchor;
        tm.alignment = anchor == TextAnchor.UpperRight ? TextAlignment.Right
            : (anchor == TextAnchor.MiddleCenter || anchor == TextAnchor.LowerCenter ? TextAlignment.Center : TextAlignment.Left);
        tm.fontSize = size;
        tm.characterSize = charSize;
        tm.lineSpacing = 0.75f;
        tm.color = color;
        if (font != null) tm.font = font;

        var rend = go.GetComponent<MeshRenderer>();
        if (rend != null)
        {
            var mat = MakeTextMat(color);
            if (mat != null) rend.sharedMaterial = mat;
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.sortingOrder = 1000;
        }
        return tm;
    }

    static TextMesh FindMesh(Transform parent, string name)
    {
        if (parent == null) return null;
        foreach (var tm in parent.GetComponentsInChildren<TextMesh>(true))
            if (tm != null && tm.name == name) return tm;
        return null;
    }

    Material MakeTextMat(Color color)
    {
        if (font == null || font.material == null) return null;
        var mat = new Material(font.material);
        var s = Shader.Find("GUI/Text Shader");
        if (s != null) mat.shader = s;
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", font.material.mainTexture);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        mat.color = color;
        mat.renderQueue = 5000;
        if (mat.HasProperty("_ZTest")) mat.SetInt("_ZTest", (int)CompareFunction.Always);
        return mat;
    }

    void Subscribe()
    {
        if (roomTracker == null) return;
        roomTracker.onRoomChanged.RemoveListener(OnRoomChanged);
        roomTracker.onRoomChanged.AddListener(OnRoomChanged);
    }

    void ResolveRefs()
    {
        if (markerDispenser == null) markerDispenser = FindFirstObjectByType<MarkerDispenser>(FindObjectsInactive.Include);
        if (roomTracker == null) roomTracker = FindFirstObjectByType<PlayerRoomTracker>(FindObjectsInactive.Include);
        if (crouchDetector == null) crouchDetector = FindFirstObjectByType<CrouchDetector>(FindObjectsInactive.Include);
        if (locomotionModeManager == null) locomotionModeManager = FindFirstObjectByType<LocomotionModeManager>(FindObjectsInactive.Include);
        if (gridManager == null) gridManager = FindFirstObjectByType<MazeGridManager>(FindObjectsInactive.Include);
        if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
    }
}
