using UnityEngine;

public enum HexDirection
{
    East = 0,
    NorthEast = 1,
    NorthWest = 2,
    West = 3,
    SouthWest = 4,
    SouthEast = 5
}

public static class HexGridData
{
    public const int DirectionCount = 6;
    public const float DefaultOuterRadius = 2.5f;

    // even row, odd row neighbor offsets for offset coords
    static readonly Vector2Int[][] offsets =
    {
        new[] { new Vector2Int(+1,0), new Vector2Int(0,+1), new Vector2Int(-1,+1), new Vector2Int(-1,0), new Vector2Int(-1,-1), new Vector2Int(0,-1) },
        new[] { new Vector2Int(+1,0), new Vector2Int(+1,+1), new Vector2Int(0,+1), new Vector2Int(-1,0), new Vector2Int(0,-1), new Vector2Int(+1,-1) }
    };

    static readonly string[] labels = { "East", "NE", "NW", "West", "SW", "SE" };

    public static bool IsValidDoorIndex(int d) => d >= 0 && d < DirectionCount;

    public static Vector2Int GetNeighbor(Vector2Int coord, int door)
    {
        if (!IsValidDoorIndex(door)) return coord;
        return coord + offsets[coord.y & 1][door];
    }

    public static Vector2Int GetNeighbor(Vector2Int coord, HexDirection dir) => GetNeighbor(coord, (int)dir);

    public static int OppositeDoor(int door) => IsValidDoorIndex(door) ? (door + 3) % DirectionCount : -1;

    public static HexDirection OppositeDirection(HexDirection dir) => (HexDirection)OppositeDoor((int)dir);

    public static Vector2Int[] GetAllNeighbors(Vector2Int coord)
    {
        var n = new Vector2Int[DirectionCount];
        for (int i = 0; i < DirectionCount; i++) n[i] = GetNeighbor(coord, i);
        return n;
    }

    public static float InnerRadius(float outer) => outer * Mathf.Sqrt(3f) * 0.5f;

    public static Vector3 GridToWorld(Vector2Int coord, float outer = DefaultOuterRadius)
    {
        float inner = InnerRadius(outer);
        float x = coord.x * inner * 2f;
        if ((coord.y & 1) == 1) x += inner;
        return new Vector3(x, 0f, coord.y * outer * 1.5f);
    }

    public static string DirectionName(int door) => IsValidDoorIndex(door) ? labels[door] : "Invalid";
}
