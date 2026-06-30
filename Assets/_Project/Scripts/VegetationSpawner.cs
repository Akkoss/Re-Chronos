using UnityEngine;

[System.Serializable]
public class VegetationSettings
{
    [Range(0, 80)] public int treesPerChunk = 15;
    [Range(0f, 1f)] public float minHeightFraction = 0.10f;
    [Range(0f, 1f)] public float maxHeightFraction = 0.75f;
    [Range(0f, 1f)] public float minSlopeNormal = 0.80f;
    public Material trunkMaterial;
    public Material foliageMaterial;
}

[System.Serializable]
public class GrassSettings
{
    [Range(0, 500)] public int grassPerChunk = 200;
    [Range(0f, 1f)] public float minHeightFraction = 0.05f;
    [Range(0f, 1f)] public float maxHeightFraction = 0.65f;
    // Stricter than trees: pasto solo en zonas casi planas.
    [Range(0f, 1f)] public float minSlopeNormal = 0.85f;
    public float bladeWidth  = 0.15f;
    public float bladeHeight = 0.45f;
    public Material material;
}

// Devuelve un GO raíz "Vegetation" con sub-raíces "Trees" y "Grass".
// TerrainChunk lo guarda para mostrarlo/ocultarlo con SetActive.
public static class VegetationSpawner
{
    public static GameObject Spawn(Vector2Int coord, int chunkSize,
                                   Transform chunkTransform,
                                   TerrainSettings terrain,
                                   VegetationSettings veg,
                                   GrassSettings grass = null)
    {
        bool hasVeg   = veg   != null && veg.treesPerChunk  > 0;
        bool hasGrass = grass != null && grass.grassPerChunk > 0;
        if (!hasVeg && !hasGrass) return null;

        var root = new GameObject("Vegetation");
        root.transform.SetParent(chunkTransform, false);

        if (hasVeg)   SpawnTrees(coord, chunkSize, terrain, veg,   root.transform);
        if (hasGrass) SpawnGrass(coord, chunkSize, terrain, grass, root.transform);

        return root;
    }

    // ──────────────────────────────────────────────────────────────────────
    // ÁRBOLES
    // ──────────────────────────────────────────────────────────────────────

    private static void SpawnTrees(Vector2Int coord, int chunkSize,
                                   TerrainSettings terrain, VegetationSettings veg,
                                   Transform parent)
    {
        int seed = coord.x * 73856093 ^ coord.y * 19349663;
        var rng  = new System.Random(seed);
        float minH = veg.minHeightFraction * terrain.maxHeight;
        float maxH = veg.maxHeightFraction * terrain.maxHeight;

        var treeRoot = new GameObject("Trees");
        treeRoot.transform.SetParent(parent, false);

        for (int i = 0; i < veg.treesPerChunk; i++)
        {
            float lx = (float)(rng.NextDouble() * (chunkSize - 2) + 1);
            float lz = (float)(rng.NextDouble() * (chunkSize - 2) + 1);
            int wx = Mathf.RoundToInt(coord.x * chunkSize + lx);
            int wz = Mathf.RoundToInt(coord.y * chunkSize + lz);

            float h = TerrainChunk.SampleHeight(wx, wz, terrain);
            if (h < minH || h > maxH) continue;

            float dhdx = TerrainChunk.SampleHeight(wx + 1, wz,     terrain)
                       - TerrainChunk.SampleHeight(wx - 1, wz,     terrain);
            float dhdz = TerrainChunk.SampleHeight(wx,     wz + 1, terrain)
                       - TerrainChunk.SampleHeight(wx,     wz - 1, terrain);
            if (new Vector3(-dhdx, 2f, -dhdz).normalized.y < veg.minSlopeNormal) continue;

            float scale = (float)(rng.NextDouble() * 0.5 + 0.75);
            float yaw   = (float)(rng.NextDouble() * 360f);
            var pos = new Vector3(coord.x * chunkSize + lx, h, coord.y * chunkSize + lz);
            BuildTree(pos, scale, yaw, treeRoot.transform, veg);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // PASTO
    // ──────────────────────────────────────────────────────────────────────
    // Cada mata: dos quads cruzados en X (crossed-quad billboard estático).
    // Un Mesh compartido por chunk evita duplicar geometría en memoria.
    // ──────────────────────────────────────────────────────────────────────

    private static void SpawnGrass(Vector2Int coord, int chunkSize,
                                   TerrainSettings terrain, GrassSettings grass,
                                   Transform parent)
    {
        // Seed distinto al de árboles para que las posiciones no se correlacionen.
        int seed = coord.x * 19349663 ^ coord.y * 83492791;
        var rng  = new System.Random(seed);
        float minH = grass.minHeightFraction * terrain.maxHeight;
        float maxH = grass.maxHeightFraction * terrain.maxHeight;

        Mesh bladeMesh = BuildBladeMesh(grass.bladeWidth, grass.bladeHeight);
        Material mat   = grass.material;
        if (mat == null)
        {
            Shader urp = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(urp) { name = "GrassMat" };
            mat.SetColor("_BaseColor", new Color32(72, 120, 48, 255));
        }

        var grassRoot = new GameObject("Grass");
        grassRoot.transform.SetParent(parent, false);

        for (int i = 0; i < grass.grassPerChunk; i++)
        {
            float lx = (float)(rng.NextDouble() * (chunkSize - 2) + 1);
            float lz = (float)(rng.NextDouble() * (chunkSize - 2) + 1);
            int wx = Mathf.RoundToInt(coord.x * chunkSize + lx);
            int wz = Mathf.RoundToInt(coord.y * chunkSize + lz);

            float h = TerrainChunk.SampleHeight(wx, wz, terrain);
            if (h < minH || h > maxH) continue;

            float dhdx = TerrainChunk.SampleHeight(wx + 1, wz,     terrain)
                       - TerrainChunk.SampleHeight(wx - 1, wz,     terrain);
            float dhdz = TerrainChunk.SampleHeight(wx,     wz + 1, terrain)
                       - TerrainChunk.SampleHeight(wx,     wz - 1, terrain);
            if (new Vector3(-dhdx, 2f, -dhdz).normalized.y < grass.minSlopeNormal) continue;

            float scaleY = (float)(rng.NextDouble() * 0.5 + 0.75); // 0.75–1.25
            float yaw    = (float)(rng.NextDouble() * 360f);
            var pos = new Vector3(coord.x * chunkSize + lx, h, coord.y * chunkSize + lz);

            var go = new GameObject("Blade");
            go.transform.SetParent(grassRoot.transform, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = new Vector3(1f, scaleY, 1f);

            go.AddComponent<MeshFilter>().sharedMesh     = bladeMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }

    // Dos quads cruzados, doble cara.
    // hw = half-width. Los quads se intersectan en el eje Y.
    private static Mesh BuildBladeMesh(float width, float height)
    {
        float hw = width * 0.5f;

        var verts = new Vector3[]
        {
            // Quad 1: paralelo al eje X (la hoja mira ±Z)
            new(-hw, 0f,    0f), new(hw, 0f,    0f),
            new( hw, height, 0f), new(-hw, height, 0f),
            // Quad 2: paralelo al eje Z (la hoja mira ±X)
            new(0f, 0f,    -hw), new(0f, 0f,     hw),
            new(0f, height,  hw), new(0f, height, -hw),
        };

        var uvs = new Vector2[]
        {
            new(0,0), new(1,0), new(1,1), new(0,1),
            new(0,0), new(1,0), new(1,1), new(0,1),
        };

        // Frente + espalda de cada quad para que sea visible desde los dos lados.
        var tris = new int[]
        {
            0,2,1, 0,3,2,  // Q1 frente
            0,1,2, 0,2,3,  // Q1 espalda
            4,6,5, 4,7,6,  // Q2 frente
            4,5,6, 4,6,7,  // Q2 espalda
        };

        var mesh = new Mesh { name = "GrassBlade" };
        mesh.vertices  = verts;
        mesh.uv        = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ──────────────────────────────────────────────────────────────────────
    // ÁRBOL PLACEHOLDER: cilindro (tronco) + esfera (copa)
    // ──────────────────────────────────────────────────────────────────────

    private static void BuildTree(Vector3 pos, float scale, float yaw,
                                   Transform parent, VegetationSettings veg)
    {
        var tree = new GameObject("Tree");
        tree.transform.SetParent(parent, false);
        tree.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        tree.transform.localScale = Vector3.one * scale;

        BuildPart("Trunk", PrimitiveType.Cylinder, tree.transform,
            localPos:   new Vector3(0f, 0.5f, 0f),
            localScale: new Vector3(0.18f, 0.5f, 0.18f),
            mat:        veg.trunkMaterial,
            fallback:   new Color32(101, 67, 33, 255));

        BuildPart("Foliage", PrimitiveType.Sphere, tree.transform,
            localPos:   new Vector3(0f, 1.55f, 0f),
            localScale: Vector3.one * 0.9f,
            mat:        veg.foliageMaterial,
            fallback:   new Color32(34, 90, 30, 255));
    }

    private static void BuildPart(string partName, PrimitiveType type,
                                   Transform parent, Vector3 localPos, Vector3 localScale,
                                   Material mat, Color32 fallback)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = partName;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale    = localScale;

        var mr = go.GetComponent<MeshRenderer>();
        if (mat != null) { mr.sharedMaterial = mat; return; }

        Shader urp = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(urp) { name = $"VegMat_{partName}" };
        m.SetColor("_BaseColor", fallback);
        mr.sharedMaterial = m;
    }
}
