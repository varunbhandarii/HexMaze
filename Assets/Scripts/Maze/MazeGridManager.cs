using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class MazeGridManager : MonoBehaviour
{
    public GameObject hexRoomPrefab;
    public GameObject doorPrefab;
    public int gridCols = 4;
    public int gridRows = 4;
    public float outerRadius = HexGridData.DefaultOuterRadius;
    public bool generateOnStart = true;
    public bool clearExistingRooms = true;
    public bool showInteriorDoors = true;
    public bool showExteriorDoorPanels = false;
    public bool hideDuplicateSharedWalls = true;
    public bool createBoundaryBlockers = true;
    public bool useRuntimeSharedDoorPanels = true;
    public bool useCleanLockedDoorPanels = false;
    public float lockedDoorPanelWidth = 1.24f;
    public float lockedDoorPanelHeight = 2.28f;
    public float lockedDoorPanelThickness = 0.16f;
    public float lockedDoorPanelCenterY = 1.12f;
    public bool enhancedLockedDoorFeedback = true;
    public AudioClip lockedDoorSound;
    public float lockedDoorHapticAmplitude = 0.8f;
    public float lockedDoorHapticDuration = 0.2f;
    public float lockedDoorShakeDuration = 0.15f;
    public float lockedDoorShakeIntensity = 0.012f;
    public bool useBuiltInLockedDoorSound = true;
    public bool showOpenDoorwayCues = false;
    public Material openDoorwayCueMaterial;
    public float openDoorwayCueWidth = 0.95f;
    public float openDoorwayCueDepth = 0.08f;
    public float openDoorwayCueHeight = 0.025f;
    public float openDoorwayCueCenterY = 0.035f;
    public bool showExitMarker = true;
    public Material exitDoorMaterial;
    public bool createRoomLights = true;
    public Color roomLightColor = new Color(0.62f, 0.78f, 0.86f, 1f);
    public float roomLightIntensity = 0.55f;
    public float roomLightRange = 3.4f;
    public float roomLightHeight = 2.35f;
    public bool applyDefaultMazeConfig = true;
    [Range(0f, 1f)] public float defaultLockedDoorChance = 0.4f;
    public bool useDefaultMazeSeed = true;
    public int defaultMazeSeed = 566;
    public int defaultMazeMaxAttempts = 30;
    public Vector2Int entranceCoord = Vector2Int.zero;
    public Vector2Int exitCoord = new Vector2Int(3, 3);
    public GameObject barricadePrefab;
    public bool placeBarricades = false;
    [Range(0f, 1f)] public float barricadeRoomChance = 0.4f;
    public bool useBarricadeSeed = true;
    public int barricadeSeed = 914;
    public int barricadesPerSelectedRoomMin = 1;
    public int barricadesPerSelectedRoomMax = 2;
    public bool skipEntranceExitForBarricades = true;
    public float barricadeMinDistanceFromCenter = 0.55f;
    public float barricadeMaxDistanceFromCenter = 1.15f;
    public float barricadeSpawnHeight = 0.02f;
    public bool requestNavMeshRebuilds = true;

    readonly Dictionary<Vector2Int, HexRoom> rooms = new Dictionary<Vector2Int, HexRoom>();
    Material runtimeDoorMat;
    Material runtimeDoorDetailMat;

    struct Edge
    {
        public Vector2Int coord;
        public int door;
        public Edge(Vector2Int c, int d) { coord = c; door = d; }
    }

    struct Step
    {
        public Vector2Int prev;
        public int door;
        public Step(Vector2Int p, int d) { prev = p; door = d; }
    }

    void Start()
    {
        if (generateOnStart) GenerateGrid();
    }

    [ContextMenu("Generate Grid")]
    public void GenerateGrid()
    {
        if (hexRoomPrefab == null)
        {
            Debug.LogWarning("MazeGridManager needs a HexRoom prefab.", this);
            return;
        }

        rooms.Clear();
        clearExistingRooms = true;
        hideDuplicateSharedWalls = true;
        showExteriorDoorPanels = false;

        Transform parent = GetRoomParent();
        ClearOldRoomObjects(parent);
        ClearChildren(parent);

        for (int c = 0; c < gridCols; c++)
        {
            for (int r = 0; r < gridRows; r++)
            {
                var coord = new Vector2Int(c, r);
                var pos = transform.position + HexGridData.GridToWorld(coord, outerRadius);

                var go = Instantiate(hexRoomPrefab, pos, Quaternion.identity, parent);
                go.name = $"Room_{c}_{r}";

                var room = go.GetComponent<HexRoom>();
                if (room == null) room = go.AddComponent<HexRoom>();

                room.Initialize(coord);
                MakeRoomLight(go.transform);
                rooms[coord] = room;
            }
        }

        entranceCoord = Vector2Int.zero;
        exitCoord = new Vector2Int(Mathf.Max(0, gridCols - 1), Mathf.Max(0, gridRows - 1));

        ConnectDoors();

        if (applyDefaultMazeConfig) ApplyDefaultMazeConfig();
        RefreshExitMarker();
        if (placeBarricades) PlaceBarricades();
        RequestRebake();
    }

    [ContextMenu("Apply Default Maze Config")]
    public void ApplyDefaultMazeConfig()
    {
        if (rooms.Count == 0)
        {
            Debug.LogWarning("Generate the grid first.", this);
            return;
        }

        var edges = GetInteriorEdges();
        if (edges.Count == 0) return;

        int seed = useDefaultMazeSeed ? defaultMazeSeed : Environment.TickCount;
        var rng = new System.Random(seed);
        int attempts = Mathf.Max(1, defaultMazeMaxAttempts);

        for (int a = 0; a < attempts; a++)
        {
            ResetDoorStates();
            foreach (var e in edges)
                if (rng.NextDouble() < defaultLockedDoorChance)
                    LockDoor(e.coord, e.door);

            if (PathExists(entranceCoord, exitCoord))
            {
                OpenEntranceToExitPath();
                OpenEntranceDoors();
                RefreshLockedPanels();
                RefreshDoorwayCues();
                RefreshExitMarker();
                return;
            }
        }

        OpenEntranceToExitPath();
        OpenEntranceDoors();
        RefreshLockedPanels();
        RefreshDoorwayCues();
        RefreshExitMarker();
        Debug.LogWarning($"Maze config fell back to a forced entrance-exit path. Seed {seed}.", this);
    }

    public void ConnectDoors()
    {
        if (useRuntimeSharedDoorPanels) useCleanLockedDoorPanels = false;

        ClearContainer("Locked Door Panels");
        ClearContainer("Exit Marker");
        ClearContainer("Open Doorway Cues");
        ClearContainer("Runtime Shared Doors");

        if (useRuntimeSharedDoorPanels)
        {
            foreach (var room in rooms.Values)
            {
                if (room == null) continue;
                RemoveOldDoorObjects(room);
                room.Initialize(room.gridCoord);
            }
        }

        foreach (var kv in rooms)
        {
            var neighbors = new bool[HexGridData.DirectionCount];
            for (int d = 0; d < HexGridData.DirectionCount; d++)
                neighbors[d] = rooms.ContainsKey(GetNeighbor(kv.Key, d));
            kv.Value.SealExteriorWalls(neighbors);
        }

        UpdateInteriorDoorVisibility();
        if (hideDuplicateSharedWalls) UpdateSharedWallVisibility();
        if (createBoundaryBlockers && !showExteriorDoorPanels) BuildBoundaryBlockers();
        if (useRuntimeSharedDoorPanels) BuildRuntimeSharedDoors();

        RefreshLockedPanels();
        RefreshDoorwayCues();
    }

    public HexRoom GetRoom(Vector2Int coord)
    {
        rooms.TryGetValue(coord, out var room);
        return room;
    }

    public Dictionary<Vector2Int, HexRoom> GetAllRooms() => rooms;

    public Vector2Int GetNearestRoom(Vector3 world)
    {
        if (rooms.Count == 0) return entranceCoord;

        Vector3 flat = world;
        flat.y = 0f;
        float best = float.MaxValue;
        Vector2Int bestCoord = entranceCoord;

        foreach (var kv in rooms)
        {
            if (kv.Value == null) continue;
            var p = kv.Value.transform.position;
            p.y = 0f;
            float d = Vector3.SqrMagnitude(flat - p);
            if (d < best) { best = d; bestCoord = kv.Key; }
        }
        return bestCoord;
    }

    public Vector2Int GetNeighbor(Vector2Int coord, int door) => HexGridData.GetNeighbor(coord, door);
    public int OppositeDoor(int door) => HexGridData.OppositeDoor(door);

    public void LockDoor(Vector2Int coord, int door) => SetDoorPair(coord, door, DoorState.Locked);
    public void UnlockDoor(Vector2Int coord, int door) => SetDoorPair(coord, door, DoorState.Unlocked);

    public bool SetExitRoom(Vector2Int newExit)
    {
        if (!rooms.ContainsKey(newExit))
        {
            Debug.LogWarning($"Exit {newExit} not in grid.", this);
            return false;
        }

        exitCoord = newExit;
        RefreshExitMarker();
        RequestRebake();
        return true;
    }

    public void ResetAllDoors()
    {
        ResetDoorStates();
        ConnectDoors();
        RequestRebake();
    }

    void ResetDoorStates()
    {
        foreach (var room in rooms.Values)
        {
            if (room == null) continue;
            foreach (var door in room.GetComponentsInChildren<DoorController>(true))
            {
                door.gameObject.SetActive(true);
                door.ResetDoor();
            }
        }
    }

    public bool PathExists(Vector2Int start, Vector2Int goal)
    {
        if (!rooms.ContainsKey(start) || !rooms.ContainsKey(goal)) return false;

        var frontier = new Queue<Vector2Int>();
        var seen = new HashSet<Vector2Int>();
        frontier.Enqueue(start);
        seen.Add(start);

        while (frontier.Count > 0)
        {
            var cur = frontier.Dequeue();
            if (cur == goal) return true;

            for (int d = 0; d < HexGridData.DirectionCount; d++)
            {
                var n = GetNeighbor(cur, d);
                if (!rooms.ContainsKey(n) || seen.Contains(n)) continue;
                if (IsDoorLocked(cur, d)) continue;
                seen.Add(n);
                frontier.Enqueue(n);
            }
        }
        return false;
    }

    void SetDoorPair(Vector2Int coord, int doorIdx, DoorState state)
    {
        if (!HexGridData.IsValidDoorIndex(doorIdx)) return;

        var room = GetRoom(coord);
        var door = room?.GetDoor(doorIdx);
        var nCoord = GetNeighbor(coord, doorIdx);
        var neighbor = GetRoom(nCoord);
        var opp = neighbor != null ? neighbor.GetDoor(OppositeDoor(doorIdx)) : null;

        if (neighbor == null)
        {
            if (door != null)
            {
                ConfigDoor(door);
                door.SetState(state);
                door.gameObject.SetActive(showExteriorDoorPanels);
            }
            RequestRebake();
            return;
        }

        bool ownsThis = OwnsSharedBoundary(coord, nCoord);
        var visible = ownsThis ? door : opp;
        var hidden = ownsThis ? opp : door;

        if (visible != null)
        {
            ConfigDoor(visible);
            visible.SetState(state);
            visible.gameObject.SetActive(showInteriorDoors);
        }
        if (hidden != null)
        {
            hidden.SetState(state);
            hidden.gameObject.SetActive(false);
        }

        RequestRebake();
    }

    void ConfigDoor(DoorController door)
    {
        door.lockedHapticAmplitude = lockedDoorHapticAmplitude;
        door.lockedHapticDuration = lockedDoorHapticDuration;
        door.lockedShakeDuration = lockedDoorShakeDuration;
        door.lockedShakeIntensity = lockedDoorShakeIntensity;
        if (lockedDoorSound != null) door.lockedSound = lockedDoorSound;
        door.maxOpenAngle = 105f;
        door.closedSnapAngle = 4f;
        door.requireNearHandle = true;
        door.handleGrabRadius = 0.55f;
        door.handleLocalPoint = new Vector3(0.34f, 0f, -0.55f);
        door.hingeLocalX = -0.5f;
        door.autoClose = true;
        door.autoCloseDelay = 2.75f;
        door.autoCloseSpeed = 120f;
        door.revealLockedMaterial = false;
        door.showLockedStatusOnWristHud = true;
    }

    void RemoveOldDoorObjects(HexRoom room)
    {
        var legacyDoorFolder = room.transform.Find("Doors");
        if (legacyDoorFolder != null)
        {
            DestroyContainer(legacyDoorFolder);
        }

        var doors = room.GetComponentsInChildren<DoorController>(true);
        foreach (var door in doors)
        {
            if (door == null) continue;
            if (IsInsideNamedParent(door.transform, "Runtime Shared Doors")) continue;
            DestroyNow(door.gameObject);
        }
    }

    static bool IsInsideNamedParent(Transform t, string parentName)
    {
        while (t != null)
        {
            if (t.name == parentName) return true;
            t = t.parent;
        }
        return false;
    }

    void BuildRuntimeSharedDoors()
    {
        if (!showInteriorDoors) return;

        foreach (var e in GetInteriorEdges())
        {
            var room = GetRoom(e.coord);
            if (room == null) continue;
            var parent = GetOrCreate(room.transform, "Runtime Shared Doors");
            BuildSharedDoor(parent, e.door);
            room.Initialize(e.coord);
        }
    }

    void BuildSharedDoor(Transform parent, int doorIdx)
    {
        var template = doorPrefab != null ? doorPrefab.GetComponent<DoorController>() : null;
        var templateRend = doorPrefab != null ? doorPrefab.GetComponent<Renderer>() : null;

        float inner = HexGridData.InnerRadius(outerRadius);
        float rad = 60f * doorIdx * Mathf.Deg2Rad;
        Vector3 outward = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = $"SharedDoor_{doorIdx}_{HexGridData.DirectionName(doorIdx)}";
        go.transform.SetParent(parent, false);
        float dist = inner - Mathf.Min(0.08f, lockedDoorPanelThickness);
        go.transform.localPosition = new Vector3(outward.x * dist, lockedDoorPanelCenterY, outward.z * dist);
        go.transform.localRotation = Quaternion.LookRotation(outward, Vector3.up);
        go.transform.localScale = new Vector3(lockedDoorPanelWidth, lockedDoorPanelHeight, lockedDoorPanelThickness);

        Material unlocked = (template != null && template.unlockedMaterial != null)
            ? template.unlockedMaterial
            : (templateRend != null ? templateRend.sharedMaterial : null);
        if (unlocked == null) unlocked = GetRuntimeDoorMat();
        Material locked = template != null ? template.lockedMaterial : null;

        go.GetComponent<Renderer>().sharedMaterial = unlocked;

        var body = go.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.constraints = RigidbodyConstraints.FreezeAll;

        var simple = go.AddComponent<XRSimpleInteractable>();
        var mgr = FindFirstObjectByType<XRInteractionManager>();
        if (mgr != null) simple.interactionManager = mgr;

        var audioSrc = go.AddComponent<AudioSource>();
        audioSrc.playOnAwake = false;
        audioSrc.spatialBlend = 1f;
        audioSrc.minDistance = 0.5f;
        audioSrc.maxDistance = 8f;
        audioSrc.rolloffMode = AudioRolloffMode.Logarithmic;

        var ctrl = go.AddComponent<DoorController>();
        ctrl.wallIndex = doorIdx;
        ctrl.state = DoorState.Unlocked;
        ctrl.maxOpenAngle = template != null ? template.maxOpenAngle : 105f;
        ctrl.closedSnapAngle = template != null ? template.closedSnapAngle : 4f;
        ctrl.requireNearHandle = true;
        ctrl.handleGrabRadius = template != null ? template.handleGrabRadius : 0.55f;
        ctrl.handleLocalPoint = template != null ? template.handleLocalPoint : new Vector3(0.34f, 0f, -0.55f);
        ctrl.hingeLocalX = template != null ? template.hingeLocalX : -0.5f;
        ctrl.unlockedMaterial = unlocked;
        ctrl.lockedMaterial = locked;
        ctrl.openSound = template != null ? template.openSound : null;
        ctrl.lockedSound = lockedDoorSound != null ? lockedDoorSound : (template != null ? template.lockedSound : null);

        ConfigDoor(ctrl);
        ctrl.CaptureClosedPosition();
        ctrl.SetState(DoorState.Unlocked);

        AddDoorDetail(go.transform, "Handle",
            new Vector3(0.34f / lockedDoorPanelWidth, 0f, -0.62f),
            new Vector3(0.08f / lockedDoorPanelWidth, 0.22f / lockedDoorPanelHeight, 0.035f / lockedDoorPanelThickness));
        AddDoorDetail(go.transform, "Brace",
            new Vector3(0f, 0.34f / lockedDoorPanelHeight, -0.62f),
            new Vector3(0.82f / lockedDoorPanelWidth, 0.05f / lockedDoorPanelHeight, 0.03f / lockedDoorPanelThickness));

        DoorAutoOpen.CreateOrUpdateTrigger(ctrl);
    }

    void AddDoorDetail(Transform doorT, string name, Vector3 pos, Vector3 scale)
    {
        var d = GameObject.CreatePrimitive(PrimitiveType.Cube);
        d.name = name;
        d.transform.SetParent(doorT, false);
        d.transform.localPosition = pos;
        d.transform.localScale = scale;

        var c = d.GetComponent<Collider>();
        if (c != null) DestroyNow(c);

        var mat = GetRuntimeDoorDetailMat();
        if (mat != null) d.GetComponent<Renderer>().sharedMaterial = mat;
    }

    Material GetRuntimeDoorMat()
    {
        if (runtimeDoorMat == null) runtimeDoorMat = MakeMat(new Color(0.58f, 0.46f, 0.32f, 1f));
        return runtimeDoorMat;
    }

    Material GetRuntimeDoorDetailMat()
    {
        if (runtimeDoorDetailMat == null) runtimeDoorDetailMat = MakeMat(new Color(0.82f, 0.68f, 0.42f, 1f));
        return runtimeDoorDetailMat;
    }

    static Material MakeMat(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
        var m = new Material(shader);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        return m;
    }

    void RefreshLockedPanels()
    {
        ClearContainer("Locked Door Panels");
        if (!useCleanLockedDoorPanels || useRuntimeSharedDoorPanels) return;

        Material lockedMat = null;
        foreach (var room in rooms.Values)
        {
            foreach (var door in room.GetComponentsInChildren<DoorController>(true))
            {
                if (door.lockedMaterial != null) { lockedMat = door.lockedMaterial; break; }
            }
            if (lockedMat != null) break;
        }

        foreach (var e in GetInteriorEdges())
        {
            if (!IsDoorLocked(e.coord, e.door)) continue;
            var room = GetRoom(e.coord);
            if (room == null) continue;
            var parent = GetOrCreate(room.transform, "Locked Door Panels");
            BuildLockedPanel(parent, e.door, lockedMat);
        }
    }

    void BuildLockedPanel(Transform parent, int doorIdx, Material mat)
    {
        float inner = HexGridData.InnerRadius(outerRadius);
        float rad = 60f * doorIdx * Mathf.Deg2Rad;
        Vector3 outward = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));

        var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = $"LockedDoorPanel_{doorIdx}_{HexGridData.DirectionName(doorIdx)}";
        panel.transform.SetParent(parent, false);
        panel.transform.localPosition = new Vector3(outward.x * inner, lockedDoorPanelCenterY, outward.z * inner);
        panel.transform.localRotation = Quaternion.LookRotation(outward, Vector3.up);
        panel.transform.localScale = new Vector3(lockedDoorPanelWidth, lockedDoorPanelHeight, lockedDoorPanelThickness);
        panel.isStatic = !enhancedLockedDoorFeedback;

        if (mat != null) panel.GetComponent<Renderer>().sharedMaterial = mat;

        if (enhancedLockedDoorFeedback)
        {
            var fb = panel.AddComponent<LockedDoorFeedback>();
            fb.hapticAmplitude = lockedDoorHapticAmplitude;
            fb.hapticDuration = lockedDoorHapticDuration;
            fb.shakeDuration = lockedDoorShakeDuration;
            fb.shakeIntensity = lockedDoorShakeIntensity;
            fb.lockedSound = lockedDoorSound;
            fb.useBuiltInLockedSound = useBuiltInLockedDoorSound;
        }
    }

    void RefreshExitMarker()
    {
        ClearContainer("Exit Marker");
        if (!showExitMarker) return;

        var room = GetRoom(exitCoord);
        if (room == null) return;

        int exitDoor = -1;
        for (int d = 0; d < HexGridData.DirectionCount; d++)
        {
            if (!rooms.ContainsKey(GetNeighbor(exitCoord, d))) { exitDoor = d; break; }
        }
        if (exitDoor < 0) return;

        var holder = new GameObject("Exit Marker");
        holder.transform.SetParent(room.transform, false);

        float inner = HexGridData.InnerRadius(outerRadius);
        float rad = 60f * exitDoor * Mathf.Deg2Rad;
        Vector3 outward = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));

        var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = $"ExitDoorPanel_{exitDoor}_{HexGridData.DirectionName(exitDoor)}";
        panel.transform.SetParent(holder.transform, false);
        panel.transform.localPosition = new Vector3(outward.x * inner, lockedDoorPanelCenterY, outward.z * inner);
        panel.transform.localRotation = Quaternion.LookRotation(outward, Vector3.up);
        panel.transform.localScale = new Vector3(lockedDoorPanelWidth, lockedDoorPanelHeight, lockedDoorPanelThickness);
        panel.isStatic = true;
        if (exitDoorMaterial != null) panel.GetComponent<Renderer>().sharedMaterial = exitDoorMaterial;
    }

    void MakeRoomLight(Transform roomT)
    {
        if (!createRoomLights) return;
        var go = new GameObject("Room Light");
        go.transform.SetParent(roomT, false);
        go.transform.localPosition = new Vector3(0f, roomLightHeight, 0f);
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = roomLightColor;
        l.intensity = roomLightIntensity;
        l.range = roomLightRange;
        l.shadows = LightShadows.None;
    }

    List<Edge> GetInteriorEdges()
    {
        var edges = new List<Edge>();
        foreach (var kv in rooms)
        {
            for (int d = 0; d < HexGridData.DirectionCount; d++)
            {
                var n = GetNeighbor(kv.Key, d);
                if (!rooms.ContainsKey(n)) continue;
                if (OwnsSharedBoundary(kv.Key, n)) edges.Add(new Edge(kv.Key, d));
            }
        }
        return edges;
    }

    public bool IsDoorLocked(Vector2Int coord, int doorIdx)
    {
        var room = GetRoom(coord);
        var door = room?.GetDoor(doorIdx);
        if (door != null && door.state == DoorState.Locked) return true;

        var neighbor = GetRoom(GetNeighbor(coord, doorIdx));
        var opp = neighbor?.GetDoor(OppositeDoor(doorIdx));
        return opp != null && opp.state == DoorState.Locked;
    }

    void RefreshDoorwayCues()
    {
        ClearContainer("Open Doorway Cues");
        if (!showOpenDoorwayCues) return;

        foreach (var e in GetInteriorEdges())
        {
            if (IsDoorLocked(e.coord, e.door)) continue;
            var room = GetRoom(e.coord);
            if (room == null) continue;

            var parent = GetOrCreate(room.transform, "Open Doorway Cues");
            float inner = HexGridData.InnerRadius(outerRadius);
            float rad = 60f * e.door * Mathf.Deg2Rad;
            Vector3 outward = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));

            var cue = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cue.name = $"OpenDoorwayCue_{e.door}_{HexGridData.DirectionName(e.door)}";
            cue.transform.SetParent(parent, false);
            cue.transform.localPosition = new Vector3(outward.x * inner, openDoorwayCueCenterY, outward.z * inner);
            cue.transform.localRotation = Quaternion.LookRotation(outward, Vector3.up);
            cue.transform.localScale = new Vector3(openDoorwayCueWidth, openDoorwayCueHeight, openDoorwayCueDepth);
            cue.isStatic = true;

            if (openDoorwayCueMaterial != null) cue.GetComponent<Renderer>().sharedMaterial = openDoorwayCueMaterial;
            var c = cue.GetComponent<Collider>();
            if (c != null) DestroyNow(c);
        }
    }

    [ContextMenu("Place Barricades")]
    public void PlaceBarricades()
    {
        ClearBarricades();
        if (!placeBarricades || barricadePrefab == null || rooms.Count == 0) return;

        int seed = useBarricadeSeed ? barricadeSeed : Environment.TickCount;
        var rng = new System.Random(seed);

        foreach (var kv in rooms)
        {
            if (kv.Value == null) continue;
            if (skipEntranceExitForBarricades && (kv.Key == entranceCoord || kv.Key == exitCoord)) continue;
            if (rng.NextDouble() > barricadeRoomChance) continue;

            int min = Mathf.Max(1, barricadesPerSelectedRoomMin);
            int max = Mathf.Max(min, barricadesPerSelectedRoomMax);
            int count = rng.Next(min, max + 1);

            var parent = GetOrCreate(kv.Value.transform, "Barricades");
            for (int i = 0; i < count; i++) SpawnBarricade(kv.Key, parent, rng, i);
        }

        RequestRebake();
    }

    [ContextMenu("Clear Barricades")]
    public void ClearBarricades()
    {
        foreach (var room in rooms.Values)
        {
            if (room == null) continue;
            var p = room.transform.Find("Barricades");
            if (p != null) DestroyContainer(p);
        }
        RequestRebake();
    }

    void SpawnBarricade(Vector2Int coord, Transform parent, System.Random rng, int index)
    {
        float angle = (float)(rng.NextDouble() * Mathf.PI * 2f);
        float dist = Mathf.Lerp(barricadeMinDistanceFromCenter, barricadeMaxDistanceFromCenter, (float)rng.NextDouble());
        var pos = new Vector3(Mathf.Cos(angle) * dist, barricadeSpawnHeight, Mathf.Sin(angle) * dist);
        float yaw = (float)(rng.NextDouble() * 360f);

        var go = Instantiate(barricadePrefab, parent);
        go.name = $"Barricade_{coord.x}_{coord.y}_{index + 1}";
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = Vector3.one;

        var ctrl = go.GetComponent<BarricadeController>();
        if (ctrl != null) ctrl.Initialize(this, FindFirstObjectByType<PlayerRoomTracker>(), coord);
    }

    void OpenEntranceToExitPath()
    {
        if (!rooms.ContainsKey(entranceCoord) || !rooms.ContainsKey(exitCoord)) return;

        var frontier = new Queue<Vector2Int>();
        var came = new Dictionary<Vector2Int, Step>();
        var seen = new HashSet<Vector2Int>();
        frontier.Enqueue(entranceCoord);
        seen.Add(entranceCoord);

        while (frontier.Count > 0 && !seen.Contains(exitCoord))
        {
            var cur = frontier.Dequeue();
            for (int d = 0; d < HexGridData.DirectionCount; d++)
            {
                var n = GetNeighbor(cur, d);
                if (!rooms.ContainsKey(n) || seen.Contains(n)) continue;
                came[n] = new Step(cur, d);
                seen.Add(n);
                frontier.Enqueue(n);
            }
        }

        if (!came.ContainsKey(exitCoord)) return;

        var cursor = exitCoord;
        while (cursor != entranceCoord)
        {
            var s = came[cursor];
            UnlockDoor(s.prev, s.door);
            cursor = s.prev;
        }
    }

    void OpenEntranceDoors()
    {
        if (!rooms.ContainsKey(entranceCoord)) return;
        for (int d = 0; d < HexGridData.DirectionCount; d++)
            if (rooms.ContainsKey(GetNeighbor(entranceCoord, d))) UnlockDoor(entranceCoord, d);
    }

    void UpdateInteriorDoorVisibility()
    {
        foreach (var kv in rooms)
        {
            foreach (var door in kv.Value.GetComponentsInChildren<DoorController>(true))
                door.gameObject.SetActive(false);

            for (int d = 0; d < HexGridData.DirectionCount; d++)
            {
                var n = GetNeighbor(kv.Key, d);
                var door = kv.Value.GetDoor(d);
                if (door == null) continue;

                if (!rooms.ContainsKey(n))
                {
                    door.gameObject.SetActive(showExteriorDoorPanels);
                    if (showExteriorDoorPanels) door.SetState(DoorState.Locked);
                    continue;
                }

                bool owns = OwnsSharedBoundary(kv.Key, n);
                door.gameObject.SetActive(showInteriorDoors && owns);
                if (owns)
                {
                    ConfigDoor(door);
                    door.SetState(DoorState.Unlocked);
                }
            }
        }
    }

    void UpdateSharedWallVisibility()
    {
        foreach (var kv in rooms)
        {
            for (int d = 0; d < HexGridData.DirectionCount; d++)
            {
                var wall = kv.Value.transform.Find($"WallSide_{d}_{HexGridData.DirectionName(d)}");
                if (wall == null) continue;
                var n = GetNeighbor(kv.Key, d);
                bool has = rooms.ContainsKey(n);
                wall.gameObject.SetActive(!has || OwnsSharedBoundary(kv.Key, n));
            }
        }
    }

    static bool OwnsSharedBoundary(Vector2Int a, Vector2Int b)
        => a.x != b.x ? a.x < b.x : a.y < b.y;

    void BuildBoundaryBlockers()
    {
        foreach (var kv in rooms)
        {
            var old = kv.Value.transform.Find("Boundary Blockers");
            if (old != null) DestroyContainer(old);

            var parent = new GameObject("Boundary Blockers");
            parent.transform.SetParent(kv.Value.transform, false);

            int exitDoor = -1;
            if (showExitMarker && kv.Key == exitCoord)
            {
                for (int d = 0; d < HexGridData.DirectionCount; d++)
                    if (!rooms.ContainsKey(GetNeighbor(kv.Key, d))) { exitDoor = d; break; }
            }

            for (int d = 0; d < HexGridData.DirectionCount; d++)
            {
                if (rooms.ContainsKey(GetNeighbor(kv.Key, d))) continue;
                if (d == exitDoor) continue;
                BuildBlocker(kv.Value.transform, parent.transform, d);
            }
        }
    }

    void BuildBlocker(Transform roomT, Transform parent, int doorIdx)
    {
        Material mat = null;
        var wall = roomT.Find($"WallSide_{doorIdx}_{HexGridData.DirectionName(doorIdx)}");
        if (wall != null)
        {
            var r = wall.GetComponentInChildren<Renderer>();
            if (r != null) mat = r.sharedMaterial;
        }

        float inner = HexGridData.InnerRadius(outerRadius);
        float rad = 60f * doorIdx * Mathf.Deg2Rad;
        Vector3 outward = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));

        var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
        b.name = $"BoundaryBlocker_{doorIdx}_{HexGridData.DirectionName(doorIdx)}";
        b.transform.SetParent(parent, false);
        b.transform.localPosition = new Vector3(outward.x * inner, 1.1f, outward.z * inner);
        b.transform.localRotation = Quaternion.LookRotation(outward, Vector3.up);
        b.transform.localScale = new Vector3(1.2f, 2.2f, 0.15f);
        b.isStatic = true;
        if (mat != null) b.GetComponent<Renderer>().sharedMaterial = mat;
    }

    void RequestRebake()
    {
        if (requestNavMeshRebuilds) NavMeshRebaker.RequestSceneRebake();
    }

    void ClearContainer(string name)
    {
        foreach (var room in rooms.Values)
        {
            if (room == null) continue;

            for (int i = room.transform.childCount - 1; i >= 0; i--)
            {
                var child = room.transform.GetChild(i);
                if (IsContainerName(child.name, name))
                {
                    DestroyContainer(child);
                }
            }
        }
    }

    Transform GetRoomParent()
    {
        Transform fallback = null;

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child.name == "Rooms")
            {
                return child;
            }

            if (fallback == null && IsRoomParentName(child.name))
            {
                fallback = child;
            }
        }

        if (fallback != null)
        {
            fallback.name = "Rooms";
            return fallback;
        }

        var go = new GameObject("Rooms");
        go.transform.SetParent(transform, false);
        return go.transform;
    }

    void ClearOldRoomObjects(Transform roomParent)
    {
        var sceneRooms = FindObjectsByType<HexRoom>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var room in sceneRooms)
        {
            if (room == null) continue;
            if (room.transform.IsChildOf(roomParent)) continue;
            DestroyNow(room.gameObject);
        }

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child == roomParent) continue;

            if (IsRoomParentName(child.name) || child.GetComponent<HexRoom>() != null || child.GetComponentInChildren<HexRoom>(true) != null)
            {
                DestroyContainer(child);
            }
        }
    }

    static bool IsRoomParentName(string name)
    {
        return name == "Rooms"
            || name.StartsWith("Rooms (", StringComparison.Ordinal)
            || name.EndsWith(" Rooms", StringComparison.Ordinal)
            || name.StartsWith("Room_", StringComparison.Ordinal);
    }

    static Transform GetOrCreate(Transform parent, string name)
    {
        Transform fallback = null;

        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name == name) return child;
            if (fallback == null && IsContainerName(child.name, name)) fallback = child;
        }

        if (fallback != null)
        {
            fallback.name = name;
            return fallback;
        }

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static bool IsContainerName(string currentName, string wantedName)
    {
        return currentName == wantedName
            || currentName.StartsWith(wantedName + " (", StringComparison.Ordinal);
    }

    void DestroyContainer(Transform t)
    {
        if (Application.isPlaying)
        {
            t.name = $"__Deleting_{t.name}";
            t.SetParent(null, true);
            Destroy(t.gameObject);
            return;
        }
        ClearChildren(t);
        DestroyImmediate(t.gameObject);
    }

    void DestroyNow(UnityEngine.Object o)
    {
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }

    void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i).gameObject;
            if (Application.isPlaying)
            {
                child.name = $"__Deleting_{child.name}";
                child.transform.SetParent(null, true);
                Destroy(child);
            }
            else DestroyImmediate(child);
        }
    }
}
