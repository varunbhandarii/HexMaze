using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public class MarkerDispenser : MonoBehaviour
{
    public enum ControllerButton { PrimaryButton, SecondaryButton, GripButton, TriggerButton }

    public GameObject markerPrefab;
    public int maxMarkers = 20;
    public PlayerRoomTracker playerRoomTracker;
    public MazeGridManager gridManager;
    public Transform headTransform;
    public bool allowButtonDrop = true;
    public XRNode inputHand = XRNode.LeftHand;
    public ControllerButton dropButton = ControllerButton.PrimaryButton;
    public float markerForwardOffset = 0.45f;
    public float spawnHeightAboveFloor = 0.14f;
    public float floorProbeHeight = 1.5f;
    public float floorProbeDistance = 3f;
    public LayerMask floorMask = ~0;

    readonly List<BreadcrumbMarker> markers = new List<BreadcrumbMarker>();
    bool prevButton;

    void Start()
    {
        if (gridManager == null) gridManager = FindFirstObjectByType<MazeGridManager>();
        if (playerRoomTracker == null) playerRoomTracker = FindFirstObjectByType<PlayerRoomTracker>();
        if (headTransform == null && Camera.main != null) headTransform = Camera.main.transform;
    }

    void Update()
    {
        if (!allowButtonDrop) return;
        bool down = ReadButton();
        if (down && !prevButton) DropMarkerAtFeet();
        prevButton = down;
    }

    public void DropMarkerAtFeet() => DropMarkerAtFeet(headTransform != null ? headTransform : transform);

    public void DropMarkerAtFeet(Transform reference)
    {
        Prune();
        if (markerPrefab == null)
        {
            Debug.LogWarning("MarkerDispenser needs a marker prefab.", this);
            return;
        }
        if (markers.Count >= maxMarkers) return;

        if (reference == null) reference = transform;
        var pos = DropPosition(reference);
        var rot = Quaternion.Euler(0f, reference.eulerAngles.y, 0f);
        var go = Instantiate(markerPrefab, pos, rot);

        var marker = go.GetComponent<BreadcrumbMarker>();
        if (marker == null) marker = go.AddComponent<BreadcrumbMarker>();

        var room = playerRoomTracker != null ? playerRoomTracker.GetCurrentRoom() : Vector2Int.zero;
        marker.Initialize(gridManager, playerRoomTracker, room);
        markers.Add(marker);
    }

    public int GetMarkersRemaining()
    {
        Prune();
        return Mathf.Max(0, maxMarkers - markers.Count);
    }

    public void ClearMarkers()
    {
        for (int i = markers.Count - 1; i >= 0; i--)
            if (markers[i] != null) Destroy(markers[i].gameObject);
        markers.Clear();
    }

    Vector3 DropPosition(Transform reference)
    {
        Vector3 fwd = reference.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) { fwd = transform.forward; fwd.y = 0f; }
        fwd.Normalize();

        Vector3 target = reference.position + fwd * markerForwardOffset;
        Vector3 rayStart = target + Vector3.up * floorProbeHeight;

        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit,
            floorProbeHeight + floorProbeDistance, floorMask, QueryTriggerInteraction.Ignore))
        {
            target = hit.point;
        }
        else
        {
            target.y = 0f;
        }

        target.y += spawnHeightAboveFloor;
        return target;
    }

    bool ReadButton()
    {
        var dev = InputDevices.GetDeviceAtXRNode(inputHand);
        if (!dev.isValid) return false;

        InputFeatureUsage<bool> usage = CommonUsages.primaryButton;
        switch (dropButton)
        {
            case ControllerButton.SecondaryButton: usage = CommonUsages.secondaryButton; break;
            case ControllerButton.GripButton: usage = CommonUsages.gripButton; break;
            case ControllerButton.TriggerButton: usage = CommonUsages.triggerButton; break;
        }
        return dev.TryGetFeatureValue(usage, out bool pressed) && pressed;
    }

    void Prune()
    {
        for (int i = markers.Count - 1; i >= 0; i--)
            if (markers[i] == null) markers.RemoveAt(i);
    }
}
