using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class BarricadeController : MonoBehaviour, IXRSelectFilter
{
    public MazeGridManager gridManager;
    public PlayerRoomTracker playerRoomTracker;
    public Vector2Int roomCoord;
    public bool parentToNearestRoomOnRelease = true;
    public bool requireNearGrab = true;
    public bool requestNavMeshRebakeOnRelease;
    public float nearGrabDistance = 0.65f;
    public AudioClip dragSound;
    public AudioClip placeSound;

    XRGrabInteractable grab;
    Rigidbody body;
    Collider col;
    AudioSource audioSrc;

    public bool canProcess => isActiveAndEnabled;

    void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
        body = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
        audioSrc = GetComponent<AudioSource>();
        if (audioSrc == null) audioSrc = gameObject.AddComponent<AudioSource>();

        audioSrc.playOnAwake = false;
        audioSrc.spatialBlend = 1f;
        audioSrc.minDistance = 0.5f;
        audioSrc.maxDistance = 8f;

        ConfigureGrab();
        SetDefaultLayer();
    }

    void Start()
    {
        if (gridManager == null) gridManager = FindFirstObjectByType<MazeGridManager>();
        if (playerRoomTracker == null) playerRoomTracker = FindFirstObjectByType<PlayerRoomTracker>();
    }

    void OnEnable()
    {
        if (grab != null)
        {
            grab.selectFilters.Remove(this);
            grab.selectFilters.Add(this);
            grab.selectEntered.AddListener(OnGrabbed);
            grab.selectExited.AddListener(OnReleased);
        }
    }

    void OnDisable()
    {
        if (grab != null)
        {
            grab.selectFilters.Remove(this);
            grab.selectEntered.RemoveListener(OnGrabbed);
            grab.selectExited.RemoveListener(OnReleased);
        }
    }

    public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
    {
        if (!requireNearGrab) return true;

        Vector3 ip = interactor.transform.position;
        Vector3 closest = col != null && col.enabled ? col.ClosestPoint(ip) : transform.position;
        float r = Mathf.Max(0.05f, nearGrabDistance);
        return Vector3.SqrMagnitude(ip - closest) <= r * r;
    }

    public void Initialize(MazeGridManager manager, PlayerRoomTracker tracker, Vector2Int initialRoom)
    {
        gridManager = manager;
        playerRoomTracker = tracker;
        roomCoord = initialRoom;
        Reparent(roomCoord);
    }

    void OnGrabbed(SelectEnterEventArgs args)
    {
        if (audioSrc != null && dragSound != null) audioSrc.PlayOneShot(dragSound);
    }

    void OnReleased(SelectExitEventArgs args)
    {
        if (parentToNearestRoomOnRelease)
        {
            roomCoord = NearestRoom();
            Reparent(roomCoord);
        }

        if (audioSrc != null && placeSound != null) audioSrc.PlayOneShot(placeSound);
        if (requestNavMeshRebakeOnRelease) NavMeshRebaker.RequestSceneRebake();
    }

    Vector2Int NearestRoom()
    {
        if (gridManager != null)
        {
            var rooms = gridManager.GetAllRooms();
            if (rooms.Count > 0)
            {
                Vector3 flat = transform.position;
                flat.y = 0f;
                float best = float.MaxValue;
                var found = roomCoord;
                foreach (var kv in rooms)
                {
                    if (kv.Value == null) continue;
                    var p = kv.Value.transform.position;
                    p.y = 0f;
                    float d = Vector3.SqrMagnitude(flat - p);
                    if (d < best) { best = d; found = kv.Key; }
                }
                return found;
            }
        }

        if (playerRoomTracker != null) return playerRoomTracker.GetCurrentRoom();
        return roomCoord;
    }

    void Reparent(Vector2Int coord)
    {
        if (gridManager == null) return;
        var room = gridManager.GetRoom(coord);
        if (room == null) return;

        var parent = room.transform.Find("Barricades");
        if (parent == null)
        {
            parent = new GameObject("Barricades").transform;
            parent.SetParent(room.transform, false);
        }

        transform.SetParent(parent, true);
        if (!gameObject.name.StartsWith("Barricade_"))
            gameObject.name = $"Barricade_{coord.x}_{coord.y}";
    }

    void ConfigureGrab()
    {
        if (grab == null) return;

        grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
        grab.throwOnDetach = true;
        grab.matchAttachPosition = true;
        grab.matchAttachRotation = false;

        if (body != null)
        {
            body.useGravity = true;
            body.isKinematic = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.maxAngularVelocity = 7f;
        }
    }

    void SetDefaultLayer()
    {
        int layer = LayerMask.NameToLayer("Default");
        if (layer < 0) layer = 0;
        SetLayer(transform, layer);

        if (grab != null)
        {
            int mask = InteractionLayerMask.GetMask("Default");
            grab.interactionLayers = mask != 0 ? mask : -1;
        }
    }

    static void SetLayer(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
    }
}
