using UnityEngine;

public struct TerrainSettings
{
    public float    noiseScale;
    public float    maxHeight;
    public Vector2  offset;
    public int      octaves;
    public float    persistence;
    public float    lacunarity;
    public Gradient heightGradient;
}

public class TerrainChunk
{
    private readonly Vector2Int      _coord;
    private readonly int             _chunkSize;
    private readonly TerrainSettings _settings;
    private readonly GameObject      _gameObject;
    private readonly MeshFilter      _meshFilter;
    private readonly MeshRenderer    _renderer;
    private readonly MeshCollider    _collider;
    private readonly GameObject      _vegetation; // raíz "Vegetation" (árboles + pasto)

    private int  _currentStep = -1; // -1 = nunca construido
    private bool _vegEnabled  = true;

    public TerrainChunk(Vector2Int coord, int chunkSize, Transform parent,
                        Material material, TerrainSettings settings,
                        VegetationSettings vegetation = null,
                        GrassSettings grass = null,
                        int lodStep = 1, bool showVegetation = true)
    {
        _coord     = coord;
        _chunkSize = chunkSize;
        _settings  = settings;

        Vector3 worldPos = new Vector3(coord.x * chunkSize, 0f, coord.y * chunkSize);
        _gameObject = new GameObject($"Chunk {coord.x},{coord.y}");
        _gameObject.transform.SetParent(parent, worldPositionStays: false);
        _gameObject.transform.SetPositionAndRotation(worldPos, Quaternion.identity);

        _meshFilter = _gameObject.AddComponent<MeshFilter>();
        _renderer   = _gameObject.AddComponent<MeshRenderer>();
        _collider   = _gameObject.AddComponent<MeshCollider>();
        _renderer.sharedMaterial = material;

        _vegetation = VegetationSpawner.Spawn(coord, chunkSize, _gameObject.transform,
                                               settings, vegetation, grass);

        UpdateLOD(lodStep, showVegetation);
    }

    // Reconstruye la malla si el step cambió, y actualiza la visibilidad de la vegetación.
    // Llamado por ProceduralTerrain cada vez que el viewer cruza un límite de chunk.
    public void UpdateLOD(int step, bool vegVisible)
    {
        _vegEnabled = vegVisible;

        if (step != _currentStep)
        {
            _currentStep = step;

            Mesh old  = _meshFilter.sharedMesh;
            Mesh mesh = BuildMesh(_coord, _chunkSize, _settings, step);
            _meshFilter.sharedMesh = mesh;
            _collider.sharedMesh   = mesh;

            // Los Mesh creados con new Mesh() no se destruyen solos al destruir el GO.
            if (old != null)
            {
                if (Application.isPlaying) Object.Destroy(old);
                else Object.DestroyImmediate(old);
            }
        }

        if (_vegetation != null)
            _vegetation.SetActive(_renderer.enabled && _vegEnabled);
    }

    // MeshRenderer.enabled en vez de SetActive para mantener el MeshCollider vivo.
    public void SetVisible(bool visible)
    {
        _renderer.enabled = visible;
        if (_vegetation != null)
            _vegetation.SetActive(visible && _vegEnabled);
    }

    public void Destroy()
    {
        Mesh mesh = _meshFilter.sharedMesh;
        if (Application.isPlaying)
        {
            Object.Destroy(_gameObject);
            Object.Destroy(mesh);
        }
        else
        {
            Object.DestroyImmediate(_gameObject);
            if (mesh != null) Object.DestroyImmediate(mesh);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // CONSTRUCCIÓN DE MALLA
    // ──────────────────────────────────────────────────────────────────────
    // step=1 → resolución completa (chunkSize+1)² vértices
    // step=2 → mitad de resolución ((chunkSize/2)+1)² vértices
    // Requisito: chunkSize debe ser divisible por step.
    // ──────────────────────────────────────────────────────────────────────
    private static Mesh BuildMesh(Vector2Int coord, int size, TerrainSettings s, int step)
    {
        int steps     = size / step;
        int vertCount = (steps + 1) * (steps + 1);
        var vertices  = new Vector3[vertCount];
        var colors    = new Color[vertCount];

        for (int zi = 0; zi <= steps; zi++)
        {
            for (int xi = 0; xi <= steps; xi++)
            {
                int xLocal = xi * step;
                int zLocal = zi * step;
                int worldX = coord.x * size + xLocal;
                int worldZ = coord.y * size + zLocal;

                float height = SampleHeight(worldX, worldZ, s);
                int   idx    = zi * (steps + 1) + xi;

                vertices[idx] = new Vector3(xLocal, height, zLocal);
                colors[idx]   = s.heightGradient != null
                    ? s.heightGradient.Evaluate(Mathf.Clamp01(height / s.maxHeight))
                    : Color.white;
            }
        }

        var mesh = new Mesh { name = $"Chunk_{coord.x}_{coord.y}_s{step}" };
        mesh.vertices  = vertices;
        mesh.triangles = BuildTriangles(steps);
        mesh.colors    = colors;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    public static float SampleHeight(int worldX, int worldZ, TerrainSettings s)
    {
        float height = 0f, amplitude = 1f, frequency = 1f, maxAmplitude = 0f;

        for (int i = 0; i < s.octaves; i++)
        {
            float sx = (worldX + s.offset.x) * s.noiseScale * frequency;
            float sz = (worldZ + s.offset.y) * s.noiseScale * frequency;
            height       += (Mathf.PerlinNoise(sx, sz) - 0.5f) * amplitude;
            maxAmplitude += amplitude;
            amplitude    *= s.persistence;
            frequency    *= s.lacunarity;
        }

        return (height / maxAmplitude + 0.5f) * s.maxHeight;
    }

    private static int[] BuildTriangles(int steps)
    {
        var triangles = new int[steps * steps * 6];
        int t = 0;

        for (int z = 0; z < steps; z++)
        {
            for (int x = 0; x < steps; x++)
            {
                int v0 = z * (steps + 1) + x;
                int v1 = v0 + 1;
                int v2 = v0 + (steps + 1);
                int v3 = v2 + 1;

                triangles[t++] = v0; triangles[t++] = v2; triangles[t++] = v1;
                triangles[t++] = v1; triangles[t++] = v2; triangles[t++] = v3;
            }
        }

        return triangles;
    }
}
