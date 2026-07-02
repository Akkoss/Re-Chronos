// WorldMapPreview — Unity EditorWindow para previsualización 2D del mapa mundial.
// Acceder desde: Re-Chronos → World Map Preview  (o Ctrl+Shift+M)
// No requiere entrar en Play mode.

using System.Diagnostics;
using UnityEditor;
using UnityEngine;

public class WorldMapPreview : EditorWindow
{
    // ── Configuración ─────────────────────────────────────────────────────────
    private TerrainPreset _preset;
    private int    _width      = 512;
    private int    _height     = 512;
    private float  _pixelScale = 20f;   // unidades de mundo por píxel

    private enum PreviewMode
    {
        BiomeColor,   // pipeline completo — igual que BuildMesh en TerrainChunk
        Elevation,    // escala de grises: negro = mar, blanco = cima
        Temperature,  // azul (frío) → verde → rojo (cálido)
        Humidity,     // amarillo (seco) → azul (húmedo)
        WaterType,    // océano / río / lago / tierra en colores sólidos
    }
    private PreviewMode _mode = PreviewMode.BiomeColor;

    // ── Estado ────────────────────────────────────────────────────────────────
    private Texture2D _tex;
    private string    _status = "Asigná un TerrainPreset y pulsá Generate.";
    private Vector2   _scroll;

    // ──────────────────────────────────────────────────────────────────────────
    // MENÚ
    // ──────────────────────────────────────────────────────────────────────────
    [MenuItem("Re-Chronos/World Map Preview %#m")]
    public static void ShowWindow() =>
        GetWindow<WorldMapPreview>("World Map Preview");

    // ──────────────────────────────────────────────────────────────────────────
    // GUI
    // ──────────────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        EditorGUILayout.Space(6);

        // ── Preset ─────────────────────────────────────────────────────────────
        _preset = (TerrainPreset)EditorGUILayout.ObjectField(
            new GUIContent("Terrain Preset"), _preset, typeof(TerrainPreset), false);

        // ── Resolución ─────────────────────────────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        _width  = Mathf.Max(16, EditorGUILayout.IntField("Ancho (px)", _width));
        _height = Mathf.Max(16, EditorGUILayout.IntField("Alto (px)",  _height));
        EditorGUILayout.EndHorizontal();

        // ── Escala + botón Auto ────────────────────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        _pixelScale = Mathf.Max(0.1f, EditorGUILayout.FloatField(
            new GUIContent("Escala (u/px)",
                "Unidades de mundo por píxel.\n" +
                "Con 512×512 y escala 20 → cubre ±5 120 u.\n" +
                "'Auto' ajusta la escala para que worldRadius llene el mapa."),
            _pixelScale));
        if (GUILayout.Button("Auto", GUILayout.Width(44)) && _preset != null)
        {
            float r     = _preset.worldRadius > 0f ? _preset.worldRadius : 5000f;
            _pixelScale = Mathf.Max(1f, r * 2f / Mathf.Min(_width, _height));
        }
        EditorGUILayout.EndHorizontal();

        // ── Modo de visualización ──────────────────────────────────────────────
        _mode = (PreviewMode)EditorGUILayout.EnumPopup(
            new GUIContent("Modo",
                "BiomeColor — colores del bioma, igual que el terreno en pantalla.\n" +
                "Elevation  — escala de grises por altura normalizada.\n" +
                "Temperature — frío=azul, cálido=rojo.\n" +
                "Humidity   — seco=amarillo, húmedo=azul.\n" +
                "WaterType  — océano / río / lago / tierra en bloques sólidos."),
            _mode);

        EditorGUILayout.Space(6);

        // ── Botones ────────────────────────────────────────────────────────────
        bool canGenerate = _preset != null
                        && _preset.biomes != null
                        && _preset.biomes.Count > 0;

        EditorGUILayout.BeginHorizontal();
        GUI.enabled = canGenerate;
        if (GUILayout.Button("Generate Preview", GUILayout.Height(30)))
            GeneratePreview();
        GUI.enabled = _tex != null;
        if (GUILayout.Button("Save PNG", GUILayout.Width(82), GUILayout.Height(30)))
            SavePNG();
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        // ── Status ─────────────────────────────────────────────────────────────
        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox(_status,
            _tex != null ? MessageType.Info : MessageType.None);

        // ── Textura ────────────────────────────────────────────────────────────
        if (_tex == null) return;

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        float maxW   = position.width - 22f;
        float aspect = (float)_tex.height / _tex.width;
        float drawW  = Mathf.Min(_tex.width, maxW);
        float drawH  = drawW * aspect;
        Rect texRect = GUILayoutUtility.GetRect(drawW, drawH);
        EditorGUI.DrawPreviewTexture(texRect, _tex);
        EditorGUILayout.EndScrollView();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GENERACIÓN
    // ──────────────────────────────────────────────────────────────────────────

    private void GeneratePreview()
    {
        if (_tex != null) DestroyImmediate(_tex);

        int w = Mathf.Max(16, _width);
        int h = Mathf.Max(16, _height);
        TerrainPreset p = _preset;

        _tex = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false)
        {
            name       = "WorldMapPreview",
            filterMode = FilterMode.Point,
            wrapMode   = TextureWrapMode.Clamp,
        };

        // Constantes idénticas al preamble de BuildMesh en TerrainChunk.cs
        float invBlend     = p.blendRadius > 0f ? 1f / p.blendRadius : 1f / 0.001f;
        float riverThr     = 1f - p.riverWidth;
        float bankEdge     = riverThr - p.riverWidth * 2f;
        float bankSpan     = p.riverWidth * 3f;
        float coastBandInv = p.coastalBand > 0f ? 1f / p.coastalBand : 1e6f;
        float riverRadInv  = p.riverHumidityRadius > 0f ? 1f / p.riverHumidityRadius : 1e6f;
        float halfWu       = w * 0.5f * _pixelScale;  // mitad del mapa en unidades mundo
        float halfHu       = h * 0.5f * _pixelScale;

        Color[] pixels = new Color[w * h];
        var sw = new Stopwatch(); sw.Start();

        try
        {
            for (int py = 0; py < h; py++)
            {
                if (py % 16 == 0)
                    EditorUtility.DisplayProgressBar(
                        "World Map Preview",
                        $"Procesando fila {py} / {h}…",
                        (float)py / h);

                for (int px = 0; px < w; px++)
                {
                    int wx = Mathf.RoundToInt(px * _pixelScale - halfWu);
                    int wz = Mathf.RoundToInt(py * _pixelScale - halfHu);

                    pixels[py * w + px] = SamplePixel(
                        wx, wz, p,
                        invBlend,
                        riverThr, bankEdge, bankSpan,
                        coastBandInv, riverRadInv);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        sw.Stop();
        _tex.SetPixels(pixels);
        _tex.Apply(updateMipmaps: false);

        float coverHalfU = w * _pixelScale * 0.5f;
        _status = $"Generado en {sw.ElapsedMilliseconds} ms — "
                + $"{w}×{h} px · {_pixelScale} u/px · ±{coverHalfU:0} u";
        Repaint();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // PIPELINE POR PÍXEL
    // Espejo exacto del loop de BuildMesh en TerrainChunk.cs.
    // Los métodos privados de TerrainChunk no son accesibles desde scripts de Editor,
    // por lo que se replican aquí con las mismas firmas y la misma lógica.
    // ──────────────────────────────────────────────────────────────────────────

    private Color SamplePixel(
        int wx, int wz, TerrainPreset p,
        float invBlend,
        float riverThr, float bankEdge, float bankSpan,
        float coastBandInv, float riverRadInv)
    {
        // ── 1. Elevación — SampleHeight incluye FBM + ridge + world falloff ──
        float elev     = TerrainChunk.SampleHeight(wx, wz, p);
        float normElev = Mathf.Clamp01(elev / p.maxHeight);

        // Modo Elevation: escala de grises con el fondo oceánico más oscuro
        if (_mode == PreviewMode.Elevation)
        {
            float shade = normElev < p.seaLevel
                ? Mathf.Lerp(0.10f, 0.28f, normElev / Mathf.Max(0.001f, p.seaLevel))
                : normElev;
            return new Color(shade, shade, shade);
        }

        // ── 2. Mar ────────────────────────────────────────────────────────────
        bool isOcean = normElev < p.seaLevel;

        // ── 3. Ríos: bancos suaves con SmoothStep (fix muro-de-espinas) ──────
        float riverRidge = isOcean ? 0f : RiverRidge(wx, wz, p);
        float rawCarveM  = Mathf.Clamp01((riverRidge - bankEdge) / bankSpan);
        float carveM     = Mathf.SmoothStep(0f, 1f, rawCarveM);
        float rawVisualM = Mathf.Clamp01((riverRidge - riverThr) / p.riverWidth);
        float riverM     = Mathf.SmoothStep(0f, 1f, rawVisualM);
        bool  isRiver    = riverM > 1e-3f;

        if (carveM > 1e-5f)
        {
            normElev = Mathf.Max(p.seaLevel,
                Mathf.Lerp(normElev, p.seaLevel, carveM * p.riverStrength));
            elev = normElev * p.maxHeight;
        }

        // ── 4. Lagos ─────────────────────────────────────────────────────────
        float cachedLakeM = 0f;
        bool  isLake      = false;
        if (!isOcean && !isRiver)
        {
            float lm = LakeMask(wx, wz, normElev, p);
            if (lm > 0f)
            {
                cachedLakeM = lm;
                isLake      = true;
                normElev    = Mathf.Lerp(normElev, p.seaLevel, lm);
                elev        = normElev * p.maxHeight;
            }
        }

        // ── 5. Temperatura ───────────────────────────────────────────────────
        float temp = ComputeTemperature(wx, wz, normElev, p);

        if (_mode == PreviewMode.Temperature)
            return TempRamp(temp);

        // ── 6. Humedad + bonus de agua ────────────────────────────────────────
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
        float hum = Mathf.Clamp01(ComputeHumidity(wx, wz, p) + waterBonus);

        if (_mode == PreviewMode.Humidity)
            return HumRamp(hum);

        // ── 7. Modo WaterType ─────────────────────────────────────────────────
        if (_mode == PreviewMode.WaterType)
        {
            if (isOcean) return new Color(0.10f, 0.22f, 0.52f);
            if (isRiver) return new Color(0.28f, 0.65f, 0.88f);
            if (isLake)  return new Color(0.22f, 0.52f, 0.80f);
            return new Color(0.50f, 0.66f, 0.34f);
        }

        // ── 8. Modo BiomeColor — pipeline completo ────────────────────────────
        Color col;
        if (isOcean)
        {
            col = p.oceanFloorColor;
        }
        else
        {
            col = SampleBiomeColor(normElev, temp, hum, p, invBlend);
            bool aboveSea = normElev > p.seaLevel + 0.005f;

            if (carveM > 0f && riverM < 1e-3f)
                col = Color.Lerp(col, p.riverBedColor, carveM * 0.30f);
            if (riverM > 0f)
                col = Color.Lerp(col, aboveSea ? p.riverWaterColor : p.riverBedColor, riverM * 0.88f);
            if (isLake && cachedLakeM > 0f)
                col = Color.Lerp(col, aboveSea ? p.riverWaterColor : p.oceanFloorColor, cachedLakeM * 0.85f);
        }
        return col;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // RÉPLICAS DE PRIVATE STATICS DE TerrainChunk
    // ──────────────────────────────────────────────────────────────────────────

    private static float RiverRidge(int wx, int wz, TerrainPreset p)
    {
        float bx = wx * p.riverNoiseScale + p.riverNoiseOffset.x;
        float bz = wz * p.riverNoiseScale + p.riverNoiseOffset.y;
        float dx = (Mathf.PerlinNoise(bx + 3.71f, bz + 8.31f) - 0.5f) * 3f;
        float dz = (Mathf.PerlinNoise(bx + 8.31f, bz + 3.71f) - 0.5f) * 3f;
        float n  = Mathf.PerlinNoise(bx + dx, bz + dz);
        return 1f - Mathf.Abs(n * 2f - 1f);
    }

    private static float LakeMask(int wx, int wz, float normElev, TerrainPreset p)
    {
        if (p.lakeThreshold <= 0f) return 0f;
        float lakeMax = p.seaLevel + p.lakeMaxHeight;
        if (normElev >= lakeMax) return 0f;
        float scale = p.riverNoiseScale * 0.35f;
        float n     = Mathf.PerlinNoise(
            wx * scale + p.riverNoiseOffset.x + 500f,
            wz * scale + p.riverNoiseOffset.y + 500f);
        if (n >= p.lakeThreshold) return 0f;
        float baseMask = 1f - n / p.lakeThreshold;
        float elevFade = Mathf.Clamp01((lakeMax - normElev) / Mathf.Max(0.01f, p.lakeSmoothing));
        return baseMask * elevFade;
    }

    private static float ComputeTemperature(float wx, float wz, float normElev, TerrainPreset p)
    {
        float latTemp = p.baseTemperature + wz * p.latitudeScale;
        float altCool = normElev * p.altitudeCooling;
        float noise   = Mathf.PerlinNoise(
            wx * p.climateNoiseScale + p.tempNoiseOffset.x,
            wz * p.climateNoiseScale + p.tempNoiseOffset.y) - 0.5f;
        return Mathf.Clamp01(latTemp - altCool + noise * p.climateNoiseStrength);
    }

    private static float ComputeHumidity(float wx, float wz, TerrainPreset p)
    {
        float noise = Mathf.PerlinNoise(
            wx * p.climateNoiseScale + p.humidityNoiseOffset.x,
            wz * p.climateNoiseScale + p.humidityNoiseOffset.y) - 0.5f;
        return Mathf.Clamp01(p.baseHumidity + noise * p.climateNoiseStrength);
    }

    private static Color SampleBiomeColor(
        float elevation, float temperature, float humidity,
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
            weight      *= weight;  // caída cuadrática
            if (weight <= 0f) continue;
            totalWeight += weight;
            blended     += b.color * weight;
        }
        if (totalWeight > 1e-5f) return blended * (1f / totalWeight);

        // Fallback: bioma más cercano
        float bestDist = float.MaxValue;
        Color fallback = Color.gray;
        for (int i = 0; i < p.biomes.Count; i++)
        {
            if (p.biomes[i] == null) continue;
            float d = p.biomes[i].DistanceToRegion(elevation, temperature, humidity);
            if (d < bestDist) { bestDist = d; fallback = p.biomes[i].color; }
        }
        return fallback;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // RAMPAS DE COLOR PARA MODOS DE DEBUG
    // ──────────────────────────────────────────────────────────────────────────

    private static Color TempRamp(float t) =>
        t < 0.5f
            ? Color.Lerp(new Color(0.20f, 0.38f, 0.90f),
                         new Color(0.20f, 0.80f, 0.30f), t * 2f)
            : Color.Lerp(new Color(0.20f, 0.80f, 0.30f),
                         new Color(0.92f, 0.28f, 0.10f), (t - 0.5f) * 2f);

    private static Color HumRamp(float h) =>
        Color.Lerp(new Color(0.90f, 0.74f, 0.28f),
                   new Color(0.18f, 0.42f, 0.86f), h);

    // ──────────────────────────────────────────────────────────────────────────
    // EXPORTAR PNG
    // ──────────────────────────────────────────────────────────────────────────

    private void SavePNG()
    {
        if (_tex == null) return;
        string path = EditorUtility.SaveFilePanel(
            "Guardar mapa como PNG", "Assets", "WorldMap", "png");
        if (string.IsNullOrEmpty(path)) return;
        System.IO.File.WriteAllBytes(path, _tex.EncodeToPNG());
        AssetDatabase.Refresh();
        _status += $"   ·  Guardado: {System.IO.Path.GetFileName(path)}";
    }

    // ──────────────────────────────────────────────────────────────────────────

    private void OnDestroy()
    {
        if (_tex != null) DestroyImmediate(_tex);
    }
}
