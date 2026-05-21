using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

public class NavMeshRebaker : MonoBehaviour
{
    public NavMeshSurface surface;
    public float rebakeCooldown = 1f;
    public bool buildOnStart = true;
    public bool disableUnlockedDoorCollidersDuringBake = true;
    public bool ignoreMarkerCollidersDuringBake = true;
    public bool usePhysicsColliders = true;

    bool pending;
    int requestFrame = -1;
    float lastBake = -10f;

    void Awake()
    {
        if (surface == null) surface = GetComponent<NavMeshSurface>();
    }

    void Start()
    {
        if (buildOnStart) StartCoroutine(BuildAfterFrames());
    }

    void Update()
    {
        if (!pending) return;
        if (Time.frameCount <= requestFrame) return;
        if (Time.time - lastBake < rebakeCooldown) return;
        BuildNow();
    }

    public void RequestRebake()
    {
        pending = true;
        requestFrame = Time.frameCount;
    }

    [ContextMenu("Build NavMesh Now")]
    public void BuildNow()
    {
        if (surface == null) surface = GetComponent<NavMeshSurface>();
        if (surface == null)
        {
            Debug.LogWarning("NavMeshRebaker needs a NavMeshSurface.", this);
            return;
        }

        pending = false;
        var disabled = TempDisableColliders();

        try
        {
            if (usePhysicsColliders) surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            lastBake = Time.time;
        }
        finally
        {
            foreach (var c in disabled) if (c != null) c.enabled = true;
        }
    }

    public static void RequestSceneRebake()
    {
        foreach (var r in FindObjectsByType<NavMeshRebaker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (r != null) r.RequestRebake();
    }

    public static void BuildSceneNavMeshNow()
    {
        var r = FindFirstObjectByType<NavMeshRebaker>(FindObjectsInactive.Include);
        if (r != null) r.BuildNow();
    }

    IEnumerator BuildAfterFrames()
    {
        yield return null;
        yield return null;
        BuildNow();
    }

    List<Collider> TempDisableColliders()
    {
        var disabled = new List<Collider>();

        if (disableUnlockedDoorCollidersDuringBake)
        {
            foreach (var door in FindObjectsByType<DoorController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (door.state == DoorState.Locked) continue;
                foreach (var c in door.GetComponentsInChildren<Collider>(false))
                    if (c.enabled) { c.enabled = false; disabled.Add(c); }
            }
        }

        if (ignoreMarkerCollidersDuringBake)
        {
            foreach (var m in FindObjectsByType<BreadcrumbMarker>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                foreach (var c in m.GetComponentsInChildren<Collider>(false))
                    if (c.enabled) { c.enabled = false; disabled.Add(c); }
            }
        }

        foreach (var trigger in FindObjectsByType<DoorAutoOpen>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            var c = trigger.GetComponent<Collider>();
            if (c != null && c.isTrigger && c.enabled) { c.enabled = false; disabled.Add(c); }
        }

        return disabled;
    }
}
