using System;
using UnityEngine;
using UnityEngine.Events;

[Serializable]
public class RoomChangedEvent : UnityEvent<Vector2Int> { }

public class PlayerRoomTracker : MonoBehaviour
{
    public MazeGridManager gridManager;
    public Transform trackedTransform;
    public float checkInterval = 0.25f;
    public bool invokeInitialRoomEvent = true;
    public Vector2Int currentRoom;
    public RoomChangedEvent onRoomChanged = new RoomChangedEvent();

    float timer;
    bool hasRoom;

    void Start()
    {
        if (gridManager == null) gridManager = FindFirstObjectByType<MazeGridManager>();
        if (trackedTransform == null) trackedTransform = Camera.main != null ? Camera.main.transform : transform;
        Recheck(true);
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer < checkInterval) return;
        timer = 0f;
        Recheck(false);
    }

    public Vector2Int GetCurrentRoom()
    {
        Recheck(false);
        return currentRoom;
    }

    void Recheck(bool firstTime)
    {
        if (gridManager == null || trackedTransform == null) return;

        var rooms = gridManager.GetAllRooms();
        if (rooms.Count == 0) return;

        Vector3 flat = trackedTransform.position;
        flat.y = 0f;
        float best = float.MaxValue;
        Vector2Int found = currentRoom;

        foreach (var kv in rooms)
        {
            if (kv.Value == null) continue;
            var p = kv.Value.transform.position;
            p.y = 0f;
            float d = Vector3.SqrMagnitude(flat - p);
            if (d < best) { best = d; found = kv.Key; }
        }

        if (hasRoom && found == currentRoom) return;
        currentRoom = found;
        hasRoom = true;

        if (firstTime && !invokeInitialRoomEvent) return;
        onRoomChanged?.Invoke(currentRoom);
    }
}
