using UnityEngine;

public class WaterManager : MonoBehaviour
{
    [Header("Plane")]
    [SerializeField] private float waterLevel = 4f;
    [SerializeField] private int   planeSize  = 600;  // debe cubrir viewDistance * chunkSize * 2
    [SerializeField] private int   divisions  = 32;   // subdivisiones para ondas por vértice

    [Header("References")]
    [SerializeField] private Transform viewer;
    [SerializeField] private Material  waterMaterial; // arrastra WaterMat.mat aquí

    private GameObject _plane;

    private void Start()
    {
        if (viewer == null) viewer = Camera.main?.transform;
        BuildPlane();
    }

    // Sigue al viewer en XZ con snap de 1 unidad → los UV en world-space son continuos.
    private void LateUpdate()
    {
        if (viewer == null || _plane == null) return;
        _plane.transform.position = new Vector3(
            Mathf.Round(viewer.position.x),
            waterLevel,
            Mathf.Round(viewer.position.z));
    }

    private void BuildPlane()
    {
        _plane = new GameObject("Water");
        _plane.transform.SetParent(transform, false);
        _plane.transform.position = new Vector3(0f, waterLevel, 0f);

        var mf = _plane.AddComponent<MeshFilter>();
        var mr = _plane.AddComponent<MeshRenderer>();
        mf.sharedMesh = BuildMesh(planeSize, divisions);

        if (waterMaterial != null)
            mr.sharedMaterial = waterMaterial;
        else
        {
            Shader s = Shader.Find("Custom/Water")
                    ?? Shader.Find("Universal Render Pipeline/Lit");
            mr.sharedMaterial = new Material(s) { name = "WaterFallback" };
        }
    }

    private static Mesh BuildMesh(int size, int divs)
    {
        int   verts = divs + 1;
        float step  = (float)size / divs;
        float half  = size * 0.5f;

        var vertices = new Vector3[verts * verts];
        var uvs      = new Vector2[verts * verts];

        for (int z = 0; z < verts; z++)
        for (int x = 0; x < verts; x++)
        {
            int i       = z * verts + x;
            vertices[i] = new Vector3(x * step - half, 0f, z * step - half);
            uvs[i]      = new Vector2((float)x / divs, (float)z / divs);
        }

        var tris = new int[divs * divs * 6];
        int t    = 0;
        for (int z = 0; z < divs; z++)
        for (int x = 0; x < divs; x++)
        {
            int v0    = z * verts + x;
            tris[t++] = v0;         tris[t++] = v0 + verts;     tris[t++] = v0 + 1;
            tris[t++] = v0 + 1;     tris[t++] = v0 + verts;     tris[t++] = v0 + verts + 1;
        }

        var mesh = new Mesh
        {
            name        = "WaterMesh",
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
        };
        mesh.vertices  = vertices;
        mesh.uv        = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
