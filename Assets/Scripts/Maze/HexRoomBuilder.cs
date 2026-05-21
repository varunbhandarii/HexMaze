using UnityEngine;

public class HexRoomBuilder : MonoBehaviour
{
    public float outerRadius = 2.5f;
    public float wallHeight = 3f;
    public float wallThickness = 0.15f;
    public float doorWidth = 1.2f;
    public float doorHeight = 2.2f;
    public float floorOffset = 0.01f;
    public Material wallMaterial;
    public Material floorMaterial;
    public Material ceilingMaterial;

    static readonly string[] dirNames = { "East", "NE", "NW", "West", "SW", "SE" };

    void Start()
    {
        if (transform.childCount == 0) RebuildRoom();
    }

    [ContextMenu("Rebuild Hex Room")]
    public void RebuildRoom()
    {
        ClearChildren();

        BuildHexSurface("Floor", floorOffset, true, floorMaterial);
        BuildHexSurface("Ceiling", wallHeight, false, ceilingMaterial != null ? ceilingMaterial : floorMaterial);

        float inner = outerRadius * Mathf.Sqrt(3f) * 0.5f;

        for (int i = 0; i < 6; i++)
        {
            float rad = (60f * i) * Mathf.Deg2Rad;
            Vector3 outward = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
            Vector3 center = outward * inner;
            center.y = wallHeight * 0.5f;

            var side = new GameObject($"WallSide_{i}_{dirNames[i]}");
            side.transform.SetParent(transform, false);
            side.transform.localPosition = center;
            side.transform.localRotation = Quaternion.LookRotation(outward, Vector3.up);
            side.isStatic = true;

            BuildWallWithDoorGap(side.transform);
        }
    }

    void BuildWallWithDoorGap(Transform parent)
    {
        float sideW = (outerRadius - doorWidth) * 0.5f;
        float sideOffset = doorWidth * 0.5f + sideW * 0.5f;
        float headerH = wallHeight - doorHeight;
        float headerY = (wallHeight - headerH) * 0.5f;

        MakePiece(parent, "Wall_Left", new Vector3(-sideOffset, 0f, 0f), new Vector3(sideW, wallHeight, wallThickness));
        MakePiece(parent, "Wall_Right", new Vector3(sideOffset, 0f, 0f), new Vector3(sideW, wallHeight, wallThickness));
        MakePiece(parent, "Wall_Header", new Vector3(0f, headerY, 0f), new Vector3(doorWidth, headerH, wallThickness));
    }

    void MakePiece(Transform parent, string name, Vector3 pos, Vector3 scale)
    {
        var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
        piece.name = name;
        piece.transform.SetParent(parent, false);
        piece.transform.localPosition = pos;
        piece.transform.localScale = scale;
        piece.isStatic = true;

        if (wallMaterial != null) piece.GetComponent<MeshRenderer>().sharedMaterial = wallMaterial;
    }

    void BuildHexSurface(string name, float y, bool faceUp, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, y, 0f);
        go.isStatic = true;

        var mesh = new Mesh { name = $"{name}_HexMesh" };
        var verts = new Vector3[7];
        verts[0] = Vector3.zero;
        for (int i = 0; i < 6; i++)
        {
            float rad = (30f + 60f * i) * Mathf.Deg2Rad;
            verts[i + 1] = new Vector3(Mathf.Cos(rad) * outerRadius, 0f, Mathf.Sin(rad) * outerRadius);
        }

        var tris = new int[18];
        for (int i = 0; i < 6; i++)
        {
            int curr = i + 1;
            int next = i == 5 ? 1 : i + 2;
            int idx = i * 3;
            tris[idx] = 0;
            tris[idx + 1] = faceUp ? next : curr;
            tris[idx + 2] = faceUp ? curr : next;
        }

        var uvs = new Vector2[verts.Length];
        for (int i = 0; i < verts.Length; i++)
            uvs[i] = new Vector2((verts[i].x / outerRadius + 1f) * 0.5f, (verts[i].z / outerRadius + 1f) * 0.5f);

        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var rend = go.AddComponent<MeshRenderer>();
        if (material != null) rend.sharedMaterial = material;
    }

    void ClearChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
    }
}
