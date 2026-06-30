using UnityEngine;
using UnityEngine.InputSystem;

// CameraController va en la Main Camera — no en el jugador.
// Orbita alrededor del jugador usando el input de Look (mouse delta / stick derecho).
//
// Se ejecuta en LateUpdate para garantizar que el jugador ya actualizó
// su posición en este frame antes de que la cámara recalcule la suya.
// Análogo a un useLayoutEffect en React: se ejecuta después del "render" (Update).
public class CameraController : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private float followDistance = 6f;
    // Cuánto sube el punto de mira respecto a los pies del jugador.
    [SerializeField] private float heightOffset = 1.8f;

    [Header("Look")]
    [SerializeField] private float mouseSensitivity = 0.15f;
    [SerializeField] private float gamepadSensitivity = 120f; // grados/segundo
    [SerializeField] private float minPitch = -30f;
    [SerializeField] private float maxPitch =  70f;

    private InputSystem_Actions _input;
    private float _yaw;
    private float _pitch = 20f; // ángulo inicial: ligeramente desde arriba

    private void Awake() => _input = new InputSystem_Actions();

    private void OnEnable()
    {
        _input.Player.Enable();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    private void OnDisable()
    {
        _input.Player.Disable();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    private void LateUpdate()
    {
        if (target == null) return;

        HandleEscapeKey();
        OrbitCamera();
    }

    // ------------------------------------------------------------------
    // ÓRBITA DE CÁMARA
    // ------------------------------------------------------------------
    // La posición de la cámara se calcula colocando un punto a "followDistance"
    // unidades detrás del pivot y rotando esa distancia con pitch + yaw.
    //
    // Quaternion.Euler(pitch, yaw, 0) * Vector3(0, 0, -distance):
    //   - yaw rota horizontalmente (eje Y): cuánto giró el mouse izq/der
    //   - pitch rota verticalmente (eje X): cuánto giró el mouse arriba/abajo
    //   - Z negativo porque "detrás" del jugador es -Z en espacio local de la rotación
    //
    // Es equivalente a las coordenadas esféricas:
    //   x = r * cos(pitch) * sin(yaw)
    //   y = r * sin(pitch)
    //   z = r * cos(pitch) * cos(yaw)
    // ------------------------------------------------------------------
    private void OrbitCamera()
    {
        Vector2 lookDelta = _input.Player.Look.ReadValue<Vector2>();

        // El mouse devuelve delta en píxeles — multiplicar por sensitivity da grados.
        // El stick devuelve [-1,1] continuo — multiplicar por gamepadSensitivity * dt da grados/frame.
        // Detectamos si es mouse (magnitud alta en un frame) o stick (magnitud baja, continuo).
        bool isGamepad = lookDelta.magnitude < 2f;
        float scale    = isGamepad ? gamepadSensitivity * Time.deltaTime : mouseSensitivity;

        _yaw   += lookDelta.x * scale;
        _pitch -= lookDelta.y * scale;
        _pitch  = Mathf.Clamp(_pitch, minPitch, maxPitch);

        Vector3    pivot    = target.position + Vector3.up * heightOffset;
        Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);

        transform.position = pivot + rotation * new Vector3(0f, 0f, -followDistance);
        transform.LookAt(pivot);
    }

    // Escape desbloquea el cursor para poder hacer clic en el Editor.
    // Clic izquierdo lo vuelve a bloquear.
    private void HandleEscapeKey()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }
        else if (Input.GetMouseButtonDown(0) && Cursor.lockState == CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }
    }
}
