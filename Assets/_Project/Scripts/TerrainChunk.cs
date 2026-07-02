using UnityEngine;

public enum WaterType { None, Ocean, River, Lake }

public readonly struct ClimateData
{
    public readonly float     Elevation;
    public readonly float     NormalizedElev;
    public readonly float     Temperature;
    public readonly float     Humidity;
    public readonly string    DominantBiome;
    public readonly WaterType Water;

    internal ClimateData(float el, float ne, float t, float h, string b,
                         WaterType w = WaterType.None)
    { Elevation = el; NormalizedElev = ne; Temperature = t; Humidity = h;
      DominantBiome = b; Water = w; }
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

        // Canal visual: riverWidth interior desde el pico de la función carpa.
        float riverThr     = 1f - p.riverWidth;
        // Zona de banco: 3× riverWidth total. La talla arranca aquí con S-curve → sin pinches.
        float bankEdge     = riverThr - p.riverWidth * 2f;
        float bankSpan     = p.riverWidth * 3f;
        float coastBandInv = p.coastalBand > 0f ? 1f / p.coastalBand : 1e6f;
        float riverRadInv  = p.riverHumidityRadius > 0f ? 1f / p.riverHumidityRadius : 1e6f;

        for (int zi = 0; zi <= steps; zi++)
        for (int xi = 0; xi <= steps; xi++)
        {
            int xLocal = xi * step;
            int zLocal = zi * step;
            int worldX = coord.x * size + xLocal;
            int worldZ = coord.y * size + zLocal;

            // ── 1. Elevación base (FBM + exponente + cresta) ───────────────
            float elev     = SampleHeight(worldX, worldZ, p);
            float normElev = Mathf.Clamp01(elev / p.maxHeight);

            // ── 2. Mar ─────────────────────────────────────────────────────
            bool isOcean = normElev < p.seaLevel;

            // ── 3. Ríos: bancos suaves (fix artefacto de pinches) ──────────
            float riverRidge = isOcean ? 0f : RiverRidge(worldX, worldZ, p);

            // carveM: S-curve sobre zona 3× el canal; pendiente 0 en ambos extremos
            float rawCarveM = Mathf.Clamp01((riverRidge - bankEdge) / bankSpan);
            float carveM    = Mathf.SmoothStep(0f, 1f, rawCarveM);

            // riverM: máscara del canal central únicamente, también suavizada
            float rawVisualM = Mathf.Clamp01((riverRidge - riverThr) / p.riverWidth);
            float riverM     = Mathf.SmoothStep(0f, 1f, rawVisualM);
            bool  isRiver    = riverM > 1e-3f;

            if (carveM > 1e-5f)
            {
                // Lerp hacia seaLevel ponderado por riverStrength; Max evita bajar del mar
                normElev = Mathf.Max(p.seaLevel,
                    Mathf.Lerp(normElev, p.seaLevel, carveM * p.riverStrength));
                elev = normElev * p.maxHeight;
            }

            // ── 4. Lagos: cuencas aplanadas al nivel del mar ───────────────
            float cachedLakeM = 0f;
            bool  isLake      = false;
            if (!isOcean && !isRiver)
            {
                float lm = LakeMask(worldX, worldZ, normElev, p);
                if (lm > 0f)
                {
                    cachedLakeM = lm;
                    isLake      = true;
                    normElev    = Mathf.Lerp(normElev, p.seaLevel, lm);
                    elev        = normElev * p.maxHeight;
                }
            }

            // ── 5. Temperatura con elevación modificada ────────────────────
            float temp = ComputeTemperature(worldX, worldZ, normElev, p);

            // ── 6. Bonus de humedad por proximidad al agua ─────────────────
            float waterBonus;
            if (isOcean || isRiver || isLake)
            {
                waterBonus = p.waterHumidityBonus;
            }
            else
            {
                float coastB  = p.waterHumidityBonus *
                    Mathf.Clamp01(1f - (normElev - p.seaLevel) * coastBandInv);
                float auraMin = riverThr - p.riverHumidityRadius;
                float riverB  = riverRidge > auraMin
                    ? p.waterHumidityBonus * 0.70f *
                      Mathf.Clamp01((riverRidge - auraMin) * riverRadInv)
                    : 0f;
                waterBonus = Mathf.Max(coastB, riverB);
            }

            // ── 7. Humedad final ────────────────────────────────────────────
            float hum = Mathf.Clamp01(ComputeHumidity(worldX, worldZ, p) + waterBonus);

            // ── 8. Color: bioma + agua de altura (fix ríos/lagos secos) ────
            int idx = zi * vert1d + xi;
            Color col;
            if (isOcean)
            {
                col = p.oceanFloorColor;
            }
            else
            {
                col = SampleBiomeColor(normElev, temp, hum, p, invBlend);
                bool aboveSea = normElev > p.seaLevel + 0.005f;

                // Ladera del banco (carveM > 0 pero aún fuera del canal): tinte de roca
                if (carveM > 0f && riverM < 1e-3f)
                    col = Color.Lerp(col, p.riverBedColor, carveM * 0.30f);

                // Canal central: lecho seco al nivel del mar ó agua si está en altura
                if (riverM > 0f)
                {
                    Color waterTarget = aboveSea ? p.riverWaterColor : p.riverBedColor;
                    col = Color.Lerp(col, waterTarget, riverM * 0.88f);
                }

                // Lagos de altura: vertex color de agua (el plano global no los alcanza)
                if (isLake && cachedLakeM > 0f)
                {
                    Color lakeTarget = aboveSea ? p.riverWaterColor : p.oceanFloorColor;
                    col = Color.Lerp(col, lakeTarget, cachedLakeM * 0.85f);
                }
            }

            vertices[idx] = new Vector3(xLocal, elev, zLocal);
            colors[idx]   = col;
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

    // ── RÍOS: Domain-warped ridge noise ──────────────────────────────────────
    // Devuelve [0..1]: 0 = lejos del canal, 1 = centro del canal.
    // Domain warping: se desplazan las coordenadas con otro Perlin antes de muestrear,
    // lo que produce canales sinuosos orgánicos sin líneas rectas de artefacto.
    // 3 muestras Perlin por vértice (2 warp + 1 ridge).
    private static float RiverRidge(int wx, int wz, TerrainPreset p)
    {
        float bx = wx * p.riverNoiseScale + p.riverNoiseOffset.x;
        float bz = wz * p.riverNoiseScale + p.riverNoiseOffset.y;
        float dx = (Mathf.PerlinNoise(bx + 3.71f, bz + 8.31f) - 0.5f) * 3f;
        float dz = (Mathf.PerlinNoise(bx + 8.31f, bz + 3.71f) - 0.5f) * 3f;
        float n  = Mathf.PerlinNoise(bx + dx, bz + dz);
        return 1f - Mathf.Abs(n * 2f - 1f);   // función carpa: pico en n = 0.5
    }

    // ── LAGOS: cuencas de baja frecuencia ────────────────────────────────────
    // Devuelve [0..1]: intensidad del lago (0 = no hay lago).
    // Escala ~35 % de riverNoiseScale → cuencas más grandes que el ancho de río.
    private static float LakeMask(int wx, int wz, float normElev, TerrainPreset p)
    {
        if (p.lakeThreshold <= 0f) return 0f;

        float lakeMax = p.seaLevel + p.lakeMaxHeight;
        if (normElev >= lakeMax) return 0f;

        float scale = p.riverNoiseScale * 0.35f;
        float n = Mathf.PerlinNoise(
            wx * scale + p.riverNoiseOffset.x + 500f,
            wz * scale + p.riverNoiseOffset.y + 500f);

        if (n >= p.lakeThreshold) return 0f;

        float baseMask  = 1f - n / p.lakeThreshold;
        float elevFade  = Mathf.Clamp01((lakeMax - normElev) / Mathf.Max(0.01f, p.lakeSmoothing));
        return baseMask * elevFade;
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

        float elev = shaped * p.maxHeight;

        // ── 3. FALLOFF DE BORDE DEL MUNDO ─────────────────────────────────────
        // Escala suavemente la elevación a 0 entre falloffStartRadius y worldRadius.
        // elev → 0 en el borde crea un océano profundo que rodea el continente.
        // También afecta a VegetationSpawner (no brotan plantas en la franja oceánica).
        if (p.worldRadius > 0f && p.falloffStartRadius < p.worldRadius)
        {
            float dist  = Mathf.Sqrt((float)(worldX * worldX + worldZ * worldZ));
            float fallT = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01((dist - p.falloffStartRadius) /
                              (p.worldRadius - p.falloffStartRadius)));
            elev *= 1f - fallT;  // Lerp(elev, 0) sin instanciar floats extra
        }

        return elev;
    }

    // Devuelve todos los datos climáticos de un punto del mundo.
    // Replica exactamente el pipeline de BuildMesh para que DebugHUD sea coherente.
    public static ClimateData SampleClimate(int worldX, int worldZ, TerrainPreset p)
    {
        float elev     = SampleHeight(worldX, worldZ, p);
        float normElev = Mathf.Clamp01(elev / p.maxHeight);

        bool  isOcean        = normElev < p.seaLevel;
        float riverThreshold = 1f - p.riverWidth;
        float riverRidge     = isOcean ? 0f : RiverRidge(worldX, worldZ, p);
        float riverM         = Mathf.Max(0f, riverRidge - riverThreshold) / p.riverWidth;
        bool  isRiver        = riverM > 0f;

        if (isRiver)
        {
            float gradient = (normElev - p.seaLevel) / Mathf.Max(0.01f, 1f - p.seaLevel);
            normElev = Mathf.Max(p.seaLevel, normElev - riverM * p.riverStrength * gradient);
            elev     = normElev * p.maxHeight;
        }

        bool isLake = false;
        if (!isOcean && !isRiver)
        {
            float lm = LakeMask(worldX, worldZ, normElev, p);
            if (lm > 0f) { isLake = true; normElev = Mathf.Lerp(normElev, p.seaLevel, lm); elev = normElev * p.maxHeight; }
        }

        float temp = ComputeTemperature(worldX, worldZ, normElev, p);

        float waterBonus;
        if (isOcean || isRiver || isLake)
        {
            waterBonus = p.waterHumidityBonus;
        }
        else
        {
            float coastBandInv = p.coastalBand > 0f ? 1f / p.coastalBand : 1e6f;
            float coastB = p.waterHumidityBonus *
                Mathf.Clamp01(1f - (normElev - p.seaLevel) * coastBandInv);
            float auraMin  = riverThreshold - p.riverHumidityRadius;
            float riverRadInv = p.riverHumidityRadius > 0f ? 1f / p.riverHumidityRadius : 1e6f;
            float riverB = riverRidge > auraMin
                ? p.waterHumidityBonus * 0.70f * Mathf.Clamp01((riverRidge - auraMin) * riverRadInv)
                : 0f;
            waterBonus = Mathf.Max(coastB, riverB);
        }

        float hum = Mathf.Clamp01(ComputeHumidity(worldX, worldZ, p) + waterBonus);

        string biome = "—";
        float  bestD = float.MaxValue;
        if (p.biomes != null)
            for (int i = 0; i < p.biomes.Count; i++)
            {
                if (p.biomes[i] == null) continue;
                float d = p.biomes[i].DistanceToRegion(normElev, temp, hum);
                if (d < bestD) { bestD = d; biome = p.biomes[i].biomeName; }
            }

        WaterType wt = isOcean ? WaterType.Ocean
                     : isRiver ? WaterType.River
                     : isLake  ? WaterType.Lake
                     : WaterType.None;

        return new ClimateData(elev, normElev, temp, hum, biome, wt);
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
