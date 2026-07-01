using UnityEngine;
using UnityEngine.InputSystem;

// Panel de debug en pantalla. Presionar F3 para mostrar / ocultar.
// Agregar a cualquier GO de la escena; no requiere Canvas ni prefab.
// Referencias: arrastrar Player transform y el TerrainPreset activo.
[DefaultExecutionOrder(100)]
public class DebugHUD : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private Transform     player;
    [SerializeField] private TerrainPreset preset;
    [SerializeField] private int           chunkSize = 50;

    [Header("Config")]
    [Tooltip("Recalcula datos climáticos cada N frames. 8 = ~7 veces/seg a 60fps.")]
    [SerializeField] private int refreshEveryFrames = 8;

    // ── Dimensiones del panel ──────────────────────────────────────────────────
    private const int W   = 316;   // ancho del panel
    private const int PAD = 10;    // padding interior
    private const int LH  = 20;    // altura de línea
    // Altura calculada: PAD + header + sep + xyz + chunk + sep + elev + temp + hum + sep + biome + PAD
    private const int PH  = PAD + LH + 9 + LH + LH + 9 + LH + (LH+2) + (LH+2) + 9 + LH + PAD;

    // ── Estado ────────────────────────────────────────────────────────────────
    private bool    _visible;
    private int     _tick;

    // FPS counter (actualizado cada 0.5s para que no parpadee)
    private float   _fps;
    private int     _fpsFrames;
    private float   _fpsTimer;

    // Datos climáticos cacheados entre refreshes
    private Vector3 _pos;
    private float   _elevation;
    private float   _normElev;
    private float   _temperature;
    private float   _humidity;
    private string  _biome = "—";

    // ── IMGUI styles (lazy init, solo en OnGUI) ───────────────────────────────
    private GUIStyle _bgStyle;
    private GUIStyle _headerStyle;
    private GUIStyle _keyStyle;
    private GUIStyle _valStyle;
    private Texture2D _bgTex;
    private bool     _stylesReady;

    // ──────────────────────────────────────────────────────────────────────────
    // CICLO DE VIDA
    // ──────────────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
            _visible = !_visible;

        // FPS independiente de la visibilidad
        _fpsFrames++;
        _fpsTimer += Time.unscaledDeltaTime;
        if (_fpsTimer >= 0.5f)
        {
            _fps = _fpsFrames / _fpsTimer;
            _fpsFrames = 0;
            _fpsTimer  = 0f;
        }

        if (!_visible || player == null || preset == null) return;

        // Refrescar datos climáticos cada N frames (no cada frame, es innecesario)
        if (++_tick < refreshEveryFrames) return;
        _tick = 0;

        _pos = player.position;
        ClimateData d = TerrainChunk.SampleClimate(
            Mathf.RoundToInt(_pos.x), Mathf.RoundToInt(_pos.z), preset);
        _elevation   = d.Elevation;
        _normElev    = d.NormalizedElev;
        _temperature = d.Temperature;
        _humidity    = d.Humidity;
        _biome       = d.DominantBiome;
    }

    private void OnDestroy()
    {
        if (_bgTex != null) Destroy(_bgTex);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // RENDER IMGUI
    // ──────────────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (!_visible) return;
        EnsureStyles();

        int px = 14, py = 14;
        GUI.Box(new Rect(px, py, W, PH), GUIContent.none, _bgStyle);

        int cx = px + PAD;
        int cy = py + PAD;

        // ── Header ────────────────────────────────────────────────────────────
        GUI.Label(new Rect(cx, cy, W - PAD * 2, LH), "RE-CHRONOS  DEBUG", _headerStyle);

        // FPS alineado a la derecha en la misma línea
        GUIStyle fpsS = Derived(_valStyle, FpsColor(_fps), TextAnchor.MiddleRight);
        GUI.Label(new Rect(cx, cy, W - PAD * 2, LH), $"{_fps:F0} fps", fpsS);
        cy += LH;

        Line(cx, cy, W - PAD * 2); cy += 9;

        // ── Posición mundial ──────────────────────────────────────────────────
        GUI.Label(new Rect(cx, cy, W - PAD * 2, LH),
            $"X {_pos.x,8:F1}    Y {_pos.y,6:F1}    Z {_pos.z,8:F1}", _valStyle);
        cy += LH;

        int cx_ = Mathf.FloorToInt(_pos.x / chunkSize);
        int cz_ = Mathf.FloorToInt(_pos.z / chunkSize);
        Row(cx, cy, "CHUNK", $"({cx_},  {cz_})"); cy += LH;

        Line(cx, cy, W - PAD * 2); cy += 9;

        // ── Elevación ─────────────────────────────────────────────────────────
        Row(cx, cy, "ELEV", $"{_elevation:F1} u    ({_normElev * 100f:F0}%)"); cy += LH;

        // ── Temperatura ───────────────────────────────────────────────────────
        Key(cx, cy, "TEMP");
        Bar(cx + 58, cy + 4, W - PAD * 2 - 58 - 38, LH - 8, _temperature, TempColor(_temperature));
        Rval(cx, cy, $"{_temperature:F2}");
        cy += LH + 2;

        // ── Humedad ───────────────────────────────────────────────────────────
        Key(cx, cy, "HUM");
        Bar(cx + 58, cy + 4, W - PAD * 2 - 58 - 38, LH - 8, _humidity, HumColor(_humidity));
        Rval(cx, cy, $"{_humidity:F2}");
        cy += LH + 2;

        Line(cx, cy, W - PAD * 2); cy += 9;

        // ── Bioma ─────────────────────────────────────────────────────────────
        Key(cx, cy, "BIOMA");
        GUI.Label(new Rect(cx + 58, cy, W - PAD * 2 - 58, LH), _biome,
            Derived(_valStyle, new Color(0.95f, 0.84f, 0.48f)));
    }

    // ── Helpers de layout ─────────────────────────────────────────────────────

    private void Row(int x, int y, string k, string v)
    {
        Key(x, y, k);
        GUI.Label(new Rect(x + 58, y, W - PAD * 2 - 58, LH), v, _valStyle);
    }

    private void Key(int x, int y, string k) =>
        GUI.Label(new Rect(x, y, 56, LH), k, _keyStyle);

    private void Rval(int x, int y, string v)
    {
        GUI.Label(new Rect(x, y, W - PAD * 2, LH), v,
            Derived(_valStyle, _valStyle.normal.textColor, TextAnchor.MiddleRight));
    }

    private static void Line(int x, int y, int w)
    {
        Color prev = GUI.color;
        GUI.color  = new Color(0.28f, 0.34f, 0.42f, 0.85f);
        GUI.DrawTexture(new Rect(x, y, w, 1), Texture2D.whiteTexture);
        GUI.color  = prev;
    }

    private static void Bar(int x, int y, int w, int h, float value, Color fill)
    {
        int filled = Mathf.RoundToInt(Mathf.Clamp01(value) * w);
        Color prev = GUI.color;

        GUI.color = new Color(0.14f, 0.16f, 0.20f, 0.95f);
        GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);

        if (filled > 0)
        {
            GUI.color = fill;
            GUI.DrawTexture(new Rect(x, y, filled, h), Texture2D.whiteTexture);
        }
        GUI.color = prev;
    }

    // ── Gradientes de color ────────────────────────────────────────────────────

    private static Color FpsColor(float fps) =>
        fps >= 55f ? new Color(0.33f, 0.82f, 0.40f) :
        fps >= 28f ? new Color(0.95f, 0.76f, 0.18f) :
                     new Color(0.92f, 0.32f, 0.28f);

    private static Color TempColor(float t)
    {
        // azul frío → verde templado → naranja cálido
        Color cold = new(0.42f, 0.70f, 0.96f);
        Color mid  = new(0.34f, 0.78f, 0.42f);
        Color hot  = new(0.96f, 0.50f, 0.20f);
        return t < 0.5f ? Color.Lerp(cold, mid, t * 2f) : Color.Lerp(mid, hot, (t - 0.5f) * 2f);
    }

    private static Color HumColor(float h) =>
        Color.Lerp(new Color(0.85f, 0.68f, 0.28f), new Color(0.28f, 0.54f, 0.90f), h);

    // ── Estilos IMGUI (init única dentro de OnGUI para acceder a GUI.skin) ────

    private void EnsureStyles()
    {
        if (_stylesReady) return;
        _stylesReady = true;

        _bgTex = new Texture2D(1, 1);
        _bgTex.SetPixel(0, 0, new Color(0.04f, 0.06f, 0.11f, 0.90f));
        _bgTex.Apply();

        _bgStyle = new GUIStyle(GUI.skin.box)
        {
            normal  = { background = _bgTex },
            hover   = { background = _bgTex },
            active  = { background = _bgTex },
            padding = new RectOffset(0, 0, 0, 0),
            border  = new RectOffset(0, 0, 0, 0),
        };

        _headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            fontSize  = 12,
            alignment = TextAnchor.MiddleLeft,
            normal    = { textColor = new Color(0.35f, 0.80f, 0.90f) },
        };

        _keyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 11,
            alignment = TextAnchor.MiddleLeft,
            normal    = { textColor = new Color(0.50f, 0.58f, 0.68f) },
        };

        _valStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 11,
            alignment = TextAnchor.MiddleLeft,
            normal    = { textColor = new Color(0.88f, 0.92f, 0.96f) },
        };
    }

    private static GUIStyle Derived(GUIStyle src, Color color,
                                    TextAnchor align = TextAnchor.MiddleLeft)
    {
        var s = new GUIStyle(src);
        s.normal.textColor = color;
        s.alignment        = align;
        return s;
    }
}
