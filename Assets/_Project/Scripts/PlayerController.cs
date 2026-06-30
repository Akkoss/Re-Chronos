using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float walkSpeed    = 5f;
    [SerializeField] private float sprintSpeed  = 10f;
    [SerializeField] private float jumpHeight   = 2f;
    // grados/segundo que tarda el personaje en girar hacia la dirección de movimiento
    [SerializeField] private float rotationSpeed = 720f;

    [Header("References")]
    // Arrastrá la Main Camera aquí. Si está vacío se busca automáticamente.
    [SerializeField] private Transform cameraTransform;

    private CharacterController _cc;
    private InputSystem_Actions _input;

    // Velocidad vertical acumulada (gravedad + impulso de salto).
    private float _verticalVelocity;

    // Física: aceleración de gravedad (negativa = hacia abajo).
    private const float Gravity = -20f;

    // El jugador no acepta input ni aplica física hasta que el terreno esté listo.
    private bool _ready;

    // Estado público para que PlayerVisual pueda leer la situación del jugador
    // sin acoplarse a los detalles internos del controlador.
    public bool IsGrounded  => _cc.isGrounded;
    public bool IsMoving    => _ready && _input.Player.Move.ReadValue<Vector2>() != Vector2.zero;
    public bool IsSprinting => _ready && _input.Player.Sprint.IsPressed();

    private void Awake()
    {
        _cc    = GetComponent<CharacterController>();
        _input = new InputSystem_Actions();
    }

    private void Start()
    {
        if (cameraTransform == null)
            cameraTransform = Camera.main?.transform;

        // Deshabilitamos el CharacterController para que no aplique física
        // mientras esperamos que el motor registre los MeshColliders del terreno.
        // Análogo a no montar un componente React hasta que lleguen los datos del fetch.
        _cc.enabled = false;
        StartCoroutine(SpawnOnTerrain());
    }

    // ------------------------------------------------------------------
    // SPAWN SOBRE EL TERRENO
    // ------------------------------------------------------------------
    // Los MeshColliders se agregan en Start() de ProceduralTerrain, pero el
    // broadphase de Unity los registra en el FixedUpdate siguiente — no en el
    // mismo frame. Si intentamos hacer Raycast en el frame 0, no encuentra nada.
    //
    // Secuencia:
    //   Frame 0: todos los Start() corren → chunks generados con MeshCollider
    //   Frame 1 (yield null): broadphase procesó los nuevos colliders
    //   Frame 2 (yield null): segundo yield por seguridad en builds más lentas
    //   Raycast: encuentra la superficie exacta del terreno
    //   Teleport: mueve el transform (CC deshabilitado, sin resistencia)
    //   Habilitar CC: a partir de aquí la física funciona normalmente
    // ------------------------------------------------------------------
    private IEnumerator SpawnOnTerrain()
    {
        yield return null; // esperar frame 1
        yield return null; // esperar frame 2

        // Raycast desde arriba (posición actual del jugador, que debería ser Y alto).
        // La distancia 2000f cubre cualquier maxHeight razonable.
        if (Physics.Raycast(transform.position + Vector3.up * 10f, Vector3.down, out RaycastHit hit, 2000f))
        {
            // Posicionamos el pivot del CharacterController justo sobre el suelo.
            // El pivot del CC está en el centro de la cápsula, así que subimos
            // half-height + skinWidth para que los pies queden al nivel del terreno.
            Vector3 spawnPos = hit.point + Vector3.up * (_cc.height * 0.5f + _cc.skinWidth);
            transform.position = spawnPos;
        }
        else
        {
            Debug.LogWarning("[PlayerController] No se encontró terreno bajo el jugador. " +
                             "Verificá que los chunks tengan MeshCollider y que el Player esté sobre el mapa.");
        }

        _cc.enabled = true;
        _ready      = true;
    }

    private void OnEnable()  => _input.Player.Enable();
    private void OnDisable() => _input.Player.Disable();

    private void Update()
    {
        if (!_ready) return;
        Move();
        ApplyGravityAndJump();
    }

    // ------------------------------------------------------------------
    // MOVIMIENTO RELATIVO A LA CÁMARA
    // ------------------------------------------------------------------
    // Proyectamos forward y right de la cámara sobre el plano XZ (Y=0)
    // para obtener las direcciones horizontales puras.
    //
    // Análogo en React a normalizar un vector de dirección antes de
    // aplicarlo — sin ProjectOnPlane, el jugador "volaría" si la cámara
    // apunta hacia arriba o hacia abajo.
    // ------------------------------------------------------------------
    private void Move()
    {
        Vector2 input = _input.Player.Move.ReadValue<Vector2>();
        if (input == Vector2.zero) return;

        Vector3 camForward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
        Vector3 camRight   = Vector3.ProjectOnPlane(cameraTransform.right,   Vector3.up).normalized;
        Vector3 moveDir    = (camForward * input.y + camRight * input.x).normalized;

        float speed = _input.Player.Sprint.IsPressed() ? sprintSpeed : walkSpeed;
        _cc.Move(moveDir * (speed * Time.deltaTime));

        // Rotar el personaje hacia la dirección de movimiento suavemente.
        Quaternion targetRot = Quaternion.LookRotation(moveDir);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
    }

    // ------------------------------------------------------------------
    // GRAVEDAD Y SALTO
    // ------------------------------------------------------------------
    // La fórmula v = sqrt(2 * |g| * h) viene de cinemática:
    //   h = v²/(2g)  →  v = sqrt(2gh)
    // Nos da la velocidad inicial exacta para alcanzar jumpHeight metros.
    //
    // -2f cuando está en el suelo: valor mínimo para que CharacterController
    // detecte correctamente isGrounded en pendientes (sin esto, flota).
    // ------------------------------------------------------------------
    private void ApplyGravityAndJump()
    {
        if (_cc.isGrounded)
        {
            _verticalVelocity = -2f;

            if (_input.Player.Jump.triggered)
                _verticalVelocity = Mathf.Sqrt(-2f * Gravity * jumpHeight);
        }
        else
        {
            _verticalVelocity += Gravity * Time.deltaTime;
        }

        _cc.Move(new Vector3(0f, _verticalVelocity * Time.deltaTime, 0f));
    }
}
