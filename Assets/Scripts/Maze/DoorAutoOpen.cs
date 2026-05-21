using UnityEngine;

public class DoorAutoOpen : MonoBehaviour
{
    public DoorController door;
    public float reopenCooldown = 0.75f;
    public bool requestNavMeshRebakeOnOpen;

    float lastOpen = -100f;

    void Awake()
    {
        if (door == null) door = GetComponentInParent<DoorController>();
    }

    void OnTriggerEnter(Collider other) => TryOpen(other);
    void OnTriggerStay(Collider other) => TryOpen(other);

    public static DoorAutoOpen CreateOrUpdateTrigger(DoorController door)
    {
        var t = door.transform.Find("Guard Auto Open Trigger");
        if (t == null)
        {
            t = new GameObject("Guard Auto Open Trigger").transform;
            t.SetParent(door.transform, false);
        }

        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;

        var box = t.GetComponent<BoxCollider>();
        if (box == null) box = t.gameObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = Vector3.zero;
        box.size = LocalTriggerSize(door.transform);

        var auto = t.GetComponent<DoorAutoOpen>();
        if (auto == null) auto = t.gameObject.AddComponent<DoorAutoOpen>();
        auto.door = door;
        auto.reopenCooldown = 0.75f;
        auto.requestNavMeshRebakeOnOpen = false;
        return auto;
    }

    void TryOpen(Collider other)
    {
        if (door == null) return;
        if (Time.time - lastOpen < reopenCooldown) return;
        if (!other.CompareTag("Guard") && other.GetComponentInParent<GuardAI>() == null) return;

        if (door.state == DoorState.Open) { door.DelayAutoClose(); return; }
        if (door.state != DoorState.Unlocked) return;

        lastOpen = Time.time;
        door.OpenDoor();
        if (requestNavMeshRebakeOnOpen) NavMeshRebaker.RequestSceneRebake();
    }

    static Vector3 LocalTriggerSize(Transform t)
    {
        Vector3 s = t.lossyScale;
        return new Vector3(
            1.65f / Mathf.Max(0.001f, Mathf.Abs(s.x)),
            2.45f / Mathf.Max(0.001f, Mathf.Abs(s.y)),
            1.25f / Mathf.Max(0.001f, Mathf.Abs(s.z)));
    }
}
