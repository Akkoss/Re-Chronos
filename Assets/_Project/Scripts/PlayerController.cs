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
    [SerializeField] private float rotationSpeed = 720f;

    [Header("Fly Mode")]
    [Tooltip("Velocidad de vuelo base. Sprint la duplica.")]
    [SerializeField] private float flySpeed = 20f;
    [Tooltip("Ventana de tiempo (s) para detectar el doble toque de Jump que activa el vuelo.")]
    [SerializeField] private float flyDoubleTapWindow = 0.30f;

    [Header("References")]
    [SerializeField] private Transform cameraTransform;

    private CharacterController _cc;
    private InputSystem_Actions _input;

    private float _verticalVelocity;
    private const float Gravity = -20f;

    private bool  _ready;
    private bool  _flying;
    private float _lastJumpTime = -1f;

    public bool IsGrounded  => _cc.isGrounded;
    public bool IsMoving    => _ready && _input.Player.Move.ReadValue<Vector2>() != Vector2.zero;
    public bool IsSprinting => _ready && _input.Player.Sprint.IsPressed();
    public bool IsFlying    => _flying;

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

        // Doble toque de Jump (dentro de flyDoubleTapWindow) → toggle vuelo.
        if (_input.Player.Jump.triggered)
        {
            if (Time.time - _lastJumpTime <= flyDoubleTapWindow)
            {
                _flying = !_flying;
                _verticalVelocity = 0f; // sin inercia al cambiar de modo
            }
            _lastJumpTime = Time.time;
        }

        if (_flying)
            MoveFlying();
        else
        {
            Move();
            ApplyGravityAndJump();
        }
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
    // MODO VUELO
    // ------------------------------------------------------------------
    // WASD vuela en la dirección a la que apunta la cámara (incluyendo pitch),
    // Jump sube verticalmente, Crouch baja. Sprint duplica la velocidad.
    // El CharacterController sigue activo para detectar colisiones.
    // ------------------------------------------------------------------
    private void MoveFlying()
    {
        Vector2 moveInput = _input.Player.Move.ReadValue<Vector2>();
        float   speed     = _input.Player.Sprint.IsPressed() ? flySpeed * 2f : flySpeed;

        // Dirección 3D completa siguiendo la cámara (no proyectada en el plano XZ).
        Vector3 dir = cameraTransform.forward * moveInput.y
                    + cameraTransform.right   * moveInput.x;

        // Ascenso / descenso absolutos con Jump y Crouch.
        if (_input.Player.Jump.IsPressed())   dir += Vector3.up;
        if (_input.Player.Crouch.IsPressed()) dir -= Vector3.up;

        if (dir != Vector3.zero)
            _cc.Move(dir.normalized * (speed * Time.deltaTime));

        // Rotar el cuerpo hacia la componente horizontal del movimiento.
        Vector3 flat = Vector3.ProjectOnPlane(dir, Vector3.up);
        if (flat.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(flat);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }
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
