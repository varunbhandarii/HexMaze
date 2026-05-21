using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class BreadcrumbMarker : MonoBehaviour
{
    public MazeGridManager gridManager;
    public PlayerRoomTracker playerRoomTracker;
    public Vector2Int roomCoord;
    public bool parentToRoomOnRelease = true;
    public bool keepUprightOnRelease = true;

    XRGrabInteractable grab;

    void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
    }

    void OnEnable()
    {
        if (grab != null) grab.selectExited.AddListener(OnReleased);
    }

    void OnDisable()
    {
        if (grab != null) grab.selectExited.RemoveListener(OnReleased);
    }

    public void Initialize(MazeGridManager manager, PlayerRoomTracker tracker, Vector2Int initialRoom)
    {
        gridManager = manager;
        playerRoomTracker = tracker;
        roomCoord = initialRoom;
        Reparent(roomCoord);
    }

    void OnReleased(SelectExitEventArgs args)
    {
        if (playerRoomTracker != null) roomCoord = playerRoomTracker.GetCurrentRoom();
        if (keepUprightOnRelease)
        {
            var e = transform.rotation.eulerAngles;
            transform.rotation = Quaternion.Euler(0f, e.y, 0f);
        }
        Reparent(roomCoord);
    }

    void Reparent(Vector2Int coord)
    {
        if (!parentToRoomOnRelease || gridManager == null) return;
        var room = gridManager.GetRoom(coord);
        if (room == null) return;

        transform.SetParent(room.transform, true);
        gameObject.name = $"Marker_{coord.x}_{coord.y}";
    }
}
