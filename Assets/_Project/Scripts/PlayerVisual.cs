using UnityEngine;

// Crea el modelo visual del jugador y lo anima proceduralmente según su estado.
// "Procedural" significa que las animaciones se calculan con funciones matemáticas
// en tiempo de ejecución — sin clips de animación ni Animator Controller.
//
// Análogo en React a derivar estilos de estado en lugar de hardcodear keyframes CSS:
//   style={{ transform: `translateY(${isMoving ? bobOffset : breathOffset}px)` }}
public class PlayerVisual : MonoBehaviour
{
    [Header("Modelo real (opcional)")]
    [SerializeField] private GameObject modelPrefab;

    [Header("Materiales del placeholder")]
    [SerializeField] private Material bodyMaterial;
    [SerializeField] private Material headMaterial;

    // Referencias a las partes animadas, guardadas al construir el placeholder.
    private Transform _body;
    private Transform _head;

    // Posición y escala base — las animaciones se aplican como offsets SOBRE estos valores,
    // así el modelo siempre tiene un punto de reposo claro al que volver.
    private Vector3 _bodyBasePos;
    private Vector3 _bodyBaseScale;
    private Vector3 _headBasePos;

    // Referencia al controlador para leer estado (IsGrounded, IsMoving, IsSprinting).
    private PlayerController _controller;

    // ── Ciclos de animación ──────────────────────────────────────────────
    // _bobCycle avanza solo cuando el jugador se mueve → el rebote está sincronizado
    // con los pasos, no con el reloj. Si el jugador para, el rebote también para.
    private float _bobCycle;

    // _idleCycle avanza siempre → simula respiración continua en reposo.
    private float _idleCycle;

    // Para detectar el momento exacto en que el jugador toca el suelo.
    private bool  _wasGrounded;
    private float _landTime = -99f;

    private void Start()
    {
        _controller = GetComponent<PlayerController>();

        if (modelPrefab != null)
        {
            Instantiate(modelPrefab, transform.position, transform.rotation, transform);
            return;
        }

        BuildPlaceholder();

        // Guardar estado base DESPUÉS de construir para capturar las posiciones correctas.
        if (_body != null) { _bodyBasePos = _body.localPosition; _bodyBaseScale = _body.localScale; }
        if (_head != null)   _headBasePos = _head.localPosition;
    }

    private void Update()
    {
        // Sin modelo procedural o sin controlador no hay nada que animar.
        if (_body == null || _controller == null) return;
        Animate();
    }

    // ────────────────────────────────────────────────────────────────────
    // ANIMACIÓN PROCEDURAL
    // ────────────────────────────────────────────────────────────────────
    // Cada frame calculamos tres efectos y los sumamos como offsets:
    //
    //   posición Y final = basePos + bob + landingOffset
    //   escala final     = baseScale * squash
    //
    // Este patrón es análogo a combinar múltiples animaciones CSS con
    // "animation-composition: add" — cada efecto es independiente.
    // ────────────────────────────────────────────────────────────────────
    private void Animate()
    {
        bool grounded  = _controller.IsGrounded;
        bool moving    = _controller.IsMoving;
        bool sprinting = _controller.IsSprinting;

        // ── Detectar aterrizaje ──────────────────────────────────────────
        if (grounded && !_wasGrounded)
            _landTime = Time.time;
        _wasGrounded = grounded;

        // ── Ciclos ──────────────────────────────────────────────────────
        // Bob ligado al movimiento: acumula más rápido al correr.
        // Cuando el jugador para, _bobCycle deja de crecer → el seno se "congela".
        if (moving && grounded)
            _bobCycle += Time.deltaTime * (sprinting ? 14f : 9f);

        // Respiración: ciclo constante, período ~3 segundos.
        _idleCycle += Time.deltaTime * (Mathf.PI * 2f / 3f);

        // ── Bob vertical ────────────────────────────────────────────────
        // Mientras camina o corre: rebote rítmico ligado a los pasos.
        // En reposo sobre el suelo: respiración lenta y sutil.
        // En el aire: sin bob (el cuerpo está en caída libre).
        float bob = 0f;
        if (grounded)
        {
            bob = moving
                ? Mathf.Sin(_bobCycle) * (sprinting ? 0.07f : 0.045f)
                : Mathf.Sin(_idleCycle) * 0.012f;
        }

        // ── Squash de aterrizaje ─────────────────────────────────────────
        // Al tocar el suelo: comprimir brevemente en Y y expandir en X/Z,
        // como si el cuerpo absorbiera el impacto.
        // La función sin(t * PI) forma una "campana" que sube y baja en [0, duration].
        float squashY = 0f, squashXZ = 0f;
        float sinceLand = Time.time - _landTime;
        if (sinceLand < 0.35f)
        {
            float bell = Mathf.Sin(sinceLand / 0.35f * Mathf.PI);
            squashY  = -0.18f * bell;   // aplana verticalmente
            squashXZ =  0.09f * bell;   // ensancha horizontalmente (conservación de volumen)
        }

        // ── Aplicar al cuerpo ────────────────────────────────────────────
        _body.localPosition = _bodyBasePos + new Vector3(0f, bob, 0f);
        _body.localScale = new Vector3(
            _bodyBaseScale.x * (1f + squashXZ),
            _bodyBaseScale.y * (1f + squashY),
            _bodyBaseScale.z * (1f + squashXZ)
        );

        // ── Aplicar a la cabeza ──────────────────────────────────────────
        // Sigue el bob a la mitad de amplitud y con un leve desfase de fase (+0.4 rad)
        // para que parezca que oscila independiente del cuerpo — más orgánico.
        float headBob = moving && grounded
            ? Mathf.Sin(_bobCycle + 0.4f) * (sprinting ? 0.035f : 0.022f)
            : Mathf.Sin(_idleCycle + 0.8f) * 0.007f;

        _head.localPosition = _headBasePos + new Vector3(0f, headBob + squashY * 0.25f, 0f);
    }

    // ────────────────────────────────────────────────────────────────────
    // CONSTRUCCIÓN DEL MODELO PLACEHOLDER
    // ────────────────────────────────────────────────────────────────────
    private void BuildPlaceholder()
    {
        _body = MakePrimitive(PrimitiveType.Capsule, "Body",
            pos:   new Vector3(0f, -0.2f, 0f),
            scale: new Vector3(0.6f, 0.7f, 0.6f),
            mat:   bodyMaterial,
            color: new Color32(70, 130, 200, 255)).transform;

        _head = MakePrimitive(PrimitiveType.Sphere, "Head",
            pos:   new Vector3(0f, 0.75f, 0f),
            scale: Vector3.one * 0.45f,
            mat:   headMaterial,
            color: new Color32(230, 190, 150, 255)).transform;

        // El indicador de dirección no se anima por separado —
        // es hijo de la raíz del jugador, así rota junto con él.
        MakePrimitive(PrimitiveType.Cube, "FaceIndicator",
            pos:   new Vector3(0f, 0.75f, 0.27f),
            scale: Vector3.one * 0.12f,
            mat:   null,
            color: new Color32(20, 20, 20, 255));
    }

    // Crea un primitivo sin Collider y con material URP aplicado.
    // CRÍTICO — Destroy(Collider): CreatePrimitive() agrega un Collider automáticamente.
    // Si lo dejamos, el CharacterController choca con su propio cuerpo y tiembla.
    private GameObject MakePrimitive(PrimitiveType type, string goName,
                                     Vector3 pos, Vector3 scale,
                                     Material mat, Color32 color)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = goName;
        go.transform.SetParent(transform, worldPositionStays: false);
        go.transform.localPosition = pos;
        go.transform.localScale    = scale;

        Destroy(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().material = ResolveMaterial(mat, goName, color);
        return go;
    }

    private static Material ResolveMaterial(Material assigned, string partName, Color32 color)
    {
        if (assigned != null) return assigned;

        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard");

        Material m = new Material(urpLit) { name = $"PlayerMat_{partName}" };
        m.SetColor("_BaseColor", color);
        return m;
    }
}
