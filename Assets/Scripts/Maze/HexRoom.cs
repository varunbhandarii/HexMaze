using UnityEngine;

public class HexRoom : MonoBehaviour
{
    public DoorController[] doors = new DoorController[HexGridData.DirectionCount];
    public Vector2Int gridCoord;

    public void Initialize(Vector2Int coord)
    {
        gridCoord = coord;
        RebuildDoorArray();
        for (int i = 0; i < doors.Length; i++)
        {
            if (doors[i] != null) doors[i].wallIndex = i;
        }
    }

    public DoorController GetDoor(int wallIndex)
    {
        RebuildDoorArray();
        if (wallIndex < 0 || wallIndex >= doors.Length) return null;
        return doors[wallIndex];
    }

    public void SealExteriorWalls(bool[] hasNeighbor)
    {
        RebuildDoorArray();
        for (int i = 0; i < doors.Length && i < hasNeighbor.Length; i++)
        {
            if (doors[i] == null) continue;
            doors[i].gameObject.SetActive(true);
            doors[i].SetState(hasNeighbor[i] ? DoorState.Unlocked : DoorState.Locked);
        }
    }

    void RebuildDoorArray()
    {
        if (doors == null || doors.Length != HexGridData.DirectionCount)
            doors = new DoorController[HexGridData.DirectionCount];

        for (int i = 0; i < doors.Length; i++) doors[i] = null;
        bool[] taken = new bool[HexGridData.DirectionCount];

        foreach (var door in GetComponentsInChildren<DoorController>(true))
        {
            int idx = PickDoorSlot(door, taken);
            if (idx < 0) continue;
            door.wallIndex = idx;
            doors[idx] = door;
            taken[idx] = true;
        }
    }

    int PickDoorSlot(DoorController door, bool[] taken)
    {
        if (door.wallIndex >= 0 && door.wallIndex < HexGridData.DirectionCount && !taken[door.wallIndex])
            return door.wallIndex;

        // door names look like "Door_3_NW", grab the number if it's there
        var parts = door.name.Split('_');
        if (parts.Length >= 2 && int.TryParse(parts[1], out int parsed)
            && parsed >= 0 && parsed < HexGridData.DirectionCount && !taken[parsed])
        {
            return parsed;
        }

        for (int i = 0; i < HexGridData.DirectionCount; i++)
            if (!taken[i]) return i;

        return -1;
    }
}
