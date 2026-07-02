using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Mapa global togglable con la tecla M.
//
// SETUP RÁPIDO:
//   1. Crear GO vacío "MapManager" → agregar este componente.
//   2. Asignar Player transform y TerrainPreset.
//   3. (Opcional) Asignar un Sprite circular en circleSprite para enmascarar el mapa.
//      Crearlo en Unity: clic derecho en Project → Create → 2D → Sprites → Circle.
//   4. (Opcional) Asignar un Sprite de flecha en markerSprite para la dirección del jugador.
//
// Si los campos de cámara/canvas están vacíos se crean automáticamente en Awake.
// El radio del mundo se lee de TerrainPreset.worldRadius (default: 5000 u).
[DefaultExecutionOrder(50)]
public class MapManager : MonoBehaviour
{
    // ── Referencias ──────────────────────────────────────────────────────────
    [Header("Referencias")]
    [SerializeField] private Transform     player;
    [SerializeField] private TerrainPreset preset;

    // ── Cámara del mapa ───────────────────────────────────────────────────────
    [Header("Cámara del Mapa")]
    [Tooltip("Si null, se crea automáticamente como hijo de este GO.")]
    [SerializeField] private Camera    mapCamera;
    [SerializeField] private int       textureSize  = 512;   // resolución de la RenderTexture
    [SerializeField] private float     cameraHeight = 300f;  // altura Y de la cámara sobre el mundo
    [SerializeField] private LayerMask renderLayers = ~0;    // capas que renderiza el mapa

    // ── UI ────────────────────────────────────────────────────────────────────
    [Header("UI")]
    [Tooltip("Canvas del mapa. Si null, se crea automáticamente.")]
    [SerializeField] private Canvas    mapCanvas;
    [Tooltip("Diámetro del panel circular del mapa en puntos de pantalla.")]
    [SerializeField] private float     panelSize    = 620f;
    [Tooltip("Sprite circular (Create → 2D → Sprites → Circle) para recortar el mapa a un círculo.")]
    [SerializeField] private Sprite    circleSprite;
    [Tooltip("Sprite del marcador del jugador (flecha apuntando hacia arriba = Norte).")]
    [SerializeField] private Sprite    markerSprite;
    [SerializeField] private Color     markerColor  = new(0.98f, 0.82f, 0.18f);
    [SerializeField] private float     markerSize   = 18f;

    // ── Internos ──────────────────────────────────────────────────────────────
    private RenderTexture _rt;
    private RectTransform _markerRt;
    private bool          _open;
    private float         _halfWorld; // = worldRadius en unidades (la cámara cubre [-half, +half])

    // ──────────────────────────────────────────────────────────────────────────
    // CICLO DE VIDA
    // ──────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _halfWorld = (preset != null && preset.worldRadius > 0f)
            ? preset.worldRadius : 5000f;

        BuildRenderTexture();
        BuildCamera();
        BuildUI();
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (_rt != null) { _rt.Release(); Destroy(_rt); }
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
            SetVisible(!_open);

        if (_open && player != null && _markerRt != null)
            UpdateMarker();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // VISIBILIDAD
    // ──────────────────────────────────────────────────────────────────────────

    private void SetVisible(bool on)
    {
        _open = on;
        if (mapCanvas != null) mapCanvas.gameObject.SetActive(on);
        // La cámara solo renderiza cuando el mapa está abierto → sin costo de GPU en gameplay
        if (mapCamera != null) mapCamera.enabled = on;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CONSTRUCCIÓN DE LA CÁMARA
    // ──────────────────────────────────────────────────────────────────────────

    private void BuildRenderTexture()
    {
        _rt = new RenderTexture(textureSize, textureSize, 24, RenderTextureFormat.ARGB32)
        {
            name        = "MapRT",
            filterMode  = FilterMode.Bilinear,
            antiAliasing = 1,
        };
        _rt.Create();
    }

    private void BuildCamera()
    {
        if (mapCamera == null)
        {
            var go = new GameObject("MapCamera");
            go.transform.SetParent(transform);
            mapCamera = go.AddComponent<Camera>();
        }

        // Cámara ortográfica cenital: orthographicSize = mitad del alto del área → worldRadius
        mapCamera.orthographic     = true;
        mapCamera.orthographicSize = _halfWorld;
        mapCamera.transform.SetPositionAndRotation(
            new Vector3(0f, cameraHeight, 0f),
            Quaternion.Euler(90f, 0f, 0f)); // Euler(90, 0, 0): cameraUp = +Z world → Norte arriba

        mapCamera.clearFlags      = CameraClearFlags.SolidColor;
        mapCamera.backgroundColor = new Color(0.06f, 0.11f, 0.20f); // azul oceánico profundo
        mapCamera.cullingMask     = renderLayers;
        mapCamera.nearClipPlane   = 1f;
        mapCamera.farClipPlane    = cameraHeight + 20f;
        mapCamera.targetTexture   = _rt;
        mapCamera.enabled         = false;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CONSTRUCCIÓN DE LA UI
    //
    // Jerarquía generada:
    //  MapCanvas
    //   └── MapRoot (RectTransform, pivot en centro de pantalla)
    //        ├── MapBg     (Image oscura, fondo exterior al borde)
    //        │    ├── MapBorder (Image dorada, aro decorativo)
    //        │    └── MapMask   (Image + Mask → recorta a círculo)
    //        │         ├── MapTex        (RawImage con la RenderTexture)
    //        │         └── PlayerMarker  (Image marcador, se mueve en runtime)
    //        └── CloseHint (Text "[M] Cerrar")
    // ──────────────────────────────────────────────────────────────────────────

    private void BuildUI()
    {
        // ── Canvas ───────────────────────────────────────────────────────────
        if (mapCanvas == null)
        {
            var go = new GameObject("MapCanvas");
            go.transform.SetParent(transform);
            mapCanvas = go.AddComponent<Canvas>();
            mapCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            mapCanvas.sortingOrder = 20;
            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();
        }

        // ── Raíz centrada ────────────────────────────────────────────────────
        var root = MakeRect("MapRoot", mapCanvas.transform);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);

        // ── Fondo oscuro (semitransparente, cubre el panel + borde) ──────────
        var bg    = MakeRect("MapBg", root);
        var bgImg = bg.gameObject.AddComponent<Image>();
        bgImg.color = new Color(0.04f, 0.06f, 0.10f, 0.80f);
        if (circleSprite != null) { bgImg.sprite = circleSprite; bgImg.type = Image.Type.Simple; }
        bgImg.raycastTarget = false;
        bg.sizeDelta = new Vector2(panelSize + 10f, panelSize + 10f);

        // ── Borde dorado decorativo (detrás del mask) ─────────────────────────
        var borderRt  = MakeRect("MapBorder", bg);
        var borderImg = borderRt.gameObject.AddComponent<Image>();
        borderImg.color = new Color(0.60f, 0.54f, 0.35f, 0.92f);
        if (circleSprite != null) { borderImg.sprite = circleSprite; borderImg.type = Image.Type.Simple; }
        borderImg.raycastTarget = false;
        borderRt.sizeDelta = new Vector2(panelSize + 6f, panelSize + 6f);

        // ── Máscara circular ──────────────────────────────────────────────────
        var maskRt = MakeRect("MapMask", bg);
        var maskBg = maskRt.gameObject.AddComponent<Image>();
        maskBg.color = Color.white;
        if (circleSprite != null) maskBg.sprite = circleSprite;
        var maskComp = maskRt.gameObject.AddComponent<Mask>();
        maskComp.showMaskGraphic = false; // oculta el quad blanco base del Mask
        maskRt.sizeDelta = new Vector2(panelSize, panelSize);

        // ── RawImage (render del mapa) ────────────────────────────────────────
        var imgRt  = MakeRect("MapTex", maskRt);
        var rawImg = imgRt.gameObject.AddComponent<RawImage>();
        rawImg.texture      = _rt;
        rawImg.raycastTarget = false;
        imgRt.anchorMin = Vector2.zero;
        imgRt.anchorMax = Vector2.one;
        imgRt.offsetMin = imgRt.offsetMax = Vector2.zero; // stretch para llenar la máscara

        // ── Marcador del jugador ──────────────────────────────────────────────
        _markerRt = MakeRect("PlayerMarker", maskRt);
        var markerImg = _markerRt.gameObject.AddComponent<Image>();
        if (markerSprite != null) markerImg.sprite = markerSprite;
        markerImg.color         = markerColor;
        markerImg.raycastTarget = false;
        _markerRt.sizeDelta = new Vector2(markerSize, markerSize);

        // ── Texto de ayuda "[M] Cerrar mapa" ─────────────────────────────────
        // Usa UnityEngine.UI.Text (legacy). Sustituir por TMP_Text si preferís TMP.
        var hintRt  = MakeRect("CloseHint", root);
        var hintTxt = hintRt.gameObject.AddComponent<Text>();
        hintTxt.text      = "[ M ]  Cerrar mapa";
        hintTxt.fontSize  = 14;
        hintTxt.alignment = TextAnchor.MiddleCenter;
        hintTxt.color     = new Color(0.58f, 0.53f, 0.42f, 0.85f);
        hintTxt.raycastTarget = false;
        hintRt.anchorMin = hintRt.anchorMax = new Vector2(0.5f, 0.5f);
        hintRt.anchoredPosition = new Vector2(0f, -(panelSize * 0.5f + 26f));
        hintRt.sizeDelta = new Vector2(200f, 24f);
    }

    // Crea un RectTransform vacío centrado, sin tamaño, parented al target.
    private static RectTransform MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, worldPositionStays: false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = Vector2.zero;
        return rt;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // MARCADOR DEL JUGADOR
    // ──────────────────────────────────────────────────────────────────────────

    private void UpdateMarker()
    {
        // Cámara cubre [-_halfWorld, +_halfWorld] en X y Z.
        // Euler(90,0,0): cameraRight = +X world, cameraUp = +Z world → Norte = arriba en el mapa.
        float u = player.position.x / (_halfWorld * 2f) + 0.5f; // [0..1] en X
        float v = player.position.z / (_halfWorld * 2f) + 0.5f; // [0..1] en Z (Norte = arriba)

        _markerRt.anchoredPosition = new Vector2(
            (u - 0.5f) * panelSize,
            (v - 0.5f) * panelSize);

        // Rotar el marcador según la orientación del jugador en el plano XZ.
        // Si markerSprite es una flecha que apunta hacia +Z (Norte), esta rotación es correcta.
        _markerRt.localEulerAngles = new Vector3(0f, 0f, -player.eulerAngles.y);
    }
}
