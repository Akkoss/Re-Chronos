using UnityEngine;

public readonly struct ClimateData
{
    public readonly float  Elevation;
    public readonly float  NormalizedElev;
    public readonly float  Temperature;
    public readonly float  Humidity;
    public readonly string DominantBiome;

    internal ClimateData(float el, float ne, float t, float h, string b)
    { Elevation = el; NormalizedElev = ne; Temperature = t; Humidity = h; DominantBiome = b; }
}

// TerrainChunk: clase C# pura (no MonoBehaviour) que gestiona un chunk de terreno.
// Almacena el TerrainPreset para poder reconstruir la malla en cualquier LOD step.
public class TerrainChunk
{
    private readonly Vector2Int  _coord;
    private readonly int         _chunkSize;
    private readonly TerrainPreset _preset;
    private readonly GameObject  _gameObject;
    private readonly MeshFilter  _meshFilter;
    private readonly MeshRenderer _renderer;
    private readonly MeshCollider _collider;
    private readonly GameObject  _vegetation; // raíz "Vegetation" (árboles + pasto)

    private int  _currentStep = -1;
    private bool _vegEnabled  = true;

    public TerrainChunk(Vector2Int coord, int chunkSize, Transform parent,
                        Material material, TerrainPreset preset,
                        VegetationSettings vegetation = null,
                        GrassSettings grass = null,
                        int lodStep = 1, bool showVegetation = true)
    {
        _coord     = coord;
        _chunkSize = chunkSize;
        _preset    = preset;

        Vector3 worldPos = new(coord.x * chunkSize, 0f, coord.y * chunkSize);
        _gameObject = new GameObject($"Chunk {coord.x},{coord.y}");
        _gameObject.transform.SetParent(parent, worldPositionStays: false);
        _gameObject.transform.SetPositionAndRotation(worldPos, Quaternion.identity);

        _meshFilter = _gameObject.AddComponent<MeshFilter>();
        _renderer   = _gameObject.AddComponent<MeshRenderer>();
        _collider   = _gameObject.AddComponent<MeshCollider>();
        _renderer.sharedMaterial = material;

        _vegetation = VegetationSpawner.Spawn(coord, chunkSize, _gameObject.transform,
                                               preset, vegetation, grass);
        UpdateLOD(lodStep, showVegetation);
    }

    public void UpdateLOD(int step, bool vegVisible)
    {
        _vegEnabled = vegVisible;

        if (step != _currentStep)
        {
            _currentStep = step;
            Mesh old  = _meshFilter.sharedMesh;
            Mesh mesh = BuildMesh(_coord, _chunkSize, _preset, step);
            _meshFilter.sharedMesh = mesh;
            _collider.sharedMesh   = mesh;

            if (old != null)
            {
                if (Application.isPlaying) Object.Destroy(old);
                else Object.DestroyImmediate(old);
            }
        }

        if (_vegetation != null)
            _vegetation.SetActive(_renderer.enabled && _vegEnabled);
    }

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

    // ──────────────────────────────────────────────────────────────────────────
    // CONSTRUCCIÓN DE MALLA + SIMULACIÓN CLIMÁTICA
    // ──────────────────────────────────────────────────────────────────────────

    private static Mesh BuildMesh(Vector2Int coord, int size, TerrainPreset p, int step)
    {
        int steps     = size / step;
        int vert1d    = steps + 1;
        var vertices  = new Vector3[vert1d * vert1d];
        var colors    = new Color[vert1d * vert1d];

        float invBlend = p.blendRadius > 0f ? 1f / p.blendRadius : 1f / 0.001f;

        for (int zi = 0; zi <= steps; zi++)
        for (int xi = 0; xi <= steps; xi++)
        {
            int xLocal = xi * step;
            int zLocal = zi * step;
            int worldX = coord.x * size + xLocal;
            int worldZ = coord.y * size + zLocal;

            float height   = SampleHeight(worldX, worldZ, p);
            float normElev = Mathf.Clamp01(height / p.maxHeight);

            float temp = ComputeTemperature(worldX, worldZ, normElev, p);
            float hum  = ComputeHumidity(worldX, worldZ, p);

            int idx = zi * vert1d + xi;
            vertices[idx] = new Vector3(xLocal, height, zLocal);
            colors[idx]   = SampleBiomeColor(normElev, temp, hum, p, invBlend);
        }

        var mesh = new Mesh { name = $"Chunk_{coord.x}_{coord.y}_s{step}" };
        mesh.vertices  = vertices;
        mesh.triangles = BuildTriangles(steps);
        mesh.colors    = colors;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ── SIMULACIÓN CLIMÁTICA ──────────────────────────────────────────────────

    // Temperatura: gradiente latitudinal (Z) + enfriamiento por altitud + ruido orgánico.
    // worldZ positivo = Norte (más cálido). worldZ negativo = Sur (más frío).
    private static float ComputeTemperature(float worldX, float worldZ, float normHeight, TerrainPreset p)
    {
        float latTemp  = p.baseTemperature + worldZ * p.latitudeScale;
        float altCool  = normHeight * p.altitudeCooling;
        float noise    = Mathf.PerlinNoise(
            worldX * p.climateNoiseScale + p.tempNoiseOffset.x,
            worldZ * p.climateNoiseScale + p.tempNoiseOffset.y) - 0.5f;
        return Mathf.Clamp01(latTemp - altCool + noise * p.climateNoiseStrength);
    }

    // Humedad: valor base + ruido Perlin con offset diferente al de temperatura.
    private static float ComputeHumidity(float worldX, float worldZ, TerrainPreset p)
    {
        float noise = Mathf.PerlinNoise(
            worldX * p.climateNoiseScale + p.humidityNoiseOffset.x,
            worldZ * p.climateNoiseScale + p.humidityNoiseOffset.y) - 0.5f;
        return Mathf.Clamp01(p.baseHumidity + noise * p.climateNoiseStrength);
    }

    // Mezcla ponderada de biomas por distancia a cada región en espacio climático.
    // Fallback al bioma más cercano si ninguno cubre el punto (preset incompleto).
    private static Color SampleBiomeColor(float elevation, float temperature, float humidity,
                                           TerrainPreset p, float invBlend)
    {
        float totalWeight = 0f;
        Color blended     = Color.clear;

        for (int i = 0; i < p.biomes.Count; i++)
        {
            BiomeData b = p.biomes[i];
            if (b == null) continue;
            float dist   = b.DistanceToRegion(elevation, temperature, humidity);
            float weight = Mathf.Max(0f, 1f - dist * invBlend);
            weight      *= weight; // caída cuadrática → transición más natural
            if (weight <= 0f) continue;
            totalWeight += weight;
            blended     += b.color * weight;
        }

        if (totalWeight > 1e-5f) return blended * (1f / totalWeight);

        // Fallback: bioma más cercano cuando ninguna región cubre el punto.
        float bestDist  = float.MaxValue;
        Color fallback  = Color.gray;
        for (int i = 0; i < p.biomes.Count; i++)
        {
            if (p.biomes[i] == null) continue;
            float d = p.biomes[i].DistanceToRegion(elevation, temperature, humidity);
            if (d < bestDist) { bestDist = d; fallback = p.biomes[i].color; }
        }
        return fallback;
    }

    // ── SAMPLING DE ALTURA (también usado por VegetationSpawner) ─────────────

    public static float SampleHeight(int worldX, int worldZ, TerrainPreset p)
    {
        float height = 0f, amplitude = 1f, frequency = 1f, maxAmplitude = 0f;
        for (int i = 0; i < p.octaves; i++)
        {
            float sx = (worldX + p.offset.x) * p.noiseScale * frequency;
            float sz = (worldZ + p.offset.y) * p.noiseScale * frequency;
            height       += (Mathf.PerlinNoise(sx, sz) - 0.5f) * amplitude;
            maxAmplitude += amplitude;
            amplitude    *= p.persistence;
            frequency    *= p.lacunarity;
        }

        // Normalizar FBM a [0, 1].
        float n = Mathf.Clamp01(height / maxAmplitude + 0.5f);

        // ── 1. EXPONENTE DE ELEVACIÓN ─────────────────────────────────────────
        // Pow > 1 aplana cuencas y estepas (valores bajos → casi cero).
        // Valores cercanos a 1 (montañas potenciales) apenas se ven afectados.
        float shaped = Mathf.Pow(n, p.elevationExponent);

        // ── 2. MÁSCARA DE CRESTA (ridge) ──────────────────────────────────────
        // Por encima del umbral se aplica la función carpa (tent): 1 − |t·2 − 1|.
        // Crea crestas paralelas a media altura del rango montañoso; las laderas
        // se vuelven empinadas y las cimas ligeramente más planas que un domo.
        if (p.ridgeStrength > 0f && shaped > p.ridgeThreshold)
        {
            float span    = 1f - p.ridgeThreshold;
            float t       = (shaped - p.ridgeThreshold) / span; // [0,1] dentro del rango montañoso
            float ridged  = 1f - Mathf.Abs(t * 2f - 1f);       // carpa: pico en t = 0.5
            float blended = Mathf.Lerp(t, ridged, p.ridgeStrength);
            shaped = p.ridgeThreshold + blended * span;         // remapar al rango original
        }

        return shaped * p.maxHeight;
    }

    // Devuelve todos los datos climáticos de un punto del mundo.
    // Usado por DebugHUD; reutiliza los métodos privados de simulación.
    public static ClimateData SampleClimate(int worldX, int worldZ, TerrainPreset p)
    {
        float elev    = SampleHeight(worldX, worldZ, p);
        float normElev = Mathf.Clamp01(elev / p.maxHeight);
        float temp    = ComputeTemperature(worldX, worldZ, normElev, p);
        float hum     = ComputeHumidity(worldX, worldZ, p);

        string biome = "—";
        float  bestD = float.MaxValue;
        if (p.biomes != null)
            for (int i = 0; i < p.biomes.Count; i++)
            {
                if (p.biomes[i] == null) continue;
                float d = p.biomes[i].DistanceToRegion(normElev, temp, hum);
                if (d < bestD) { bestD = d; biome = p.biomes[i].biomeName; }
            }

        return new ClimateData(elev, normElev, temp, hum, biome);
    }

    // ── TRIÁNGULOS ────────────────────────────────────────────────────────────

    private static int[] BuildTriangles(int steps)
    {
        var tris = new int[steps * steps * 6];
        int t = 0;
        for (int z = 0; z < steps; z++)
        for (int x = 0; x < steps; x++)
        {
            int v0 = z * (steps + 1) + x;
            int v1 = v0 + 1;
            int v2 = v0 + (steps + 1);
            int v3 = v2 + 1;
            tris[t++] = v0; tris[t++] = v2; tris[t++] = v1;
            tris[t++] = v1; tris[t++] = v2; tris[t++] = v3;
        }
        return tris;
    }
}
