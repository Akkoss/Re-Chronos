using UnityEngine;
using UnityEngine.InputSystem;

// Cámara de tercera persona estilo Valheim:
//   · RMB mantenido → orbita la cámara con el mouse (cursor oculto/locked)
//   · Sin RMB → cursor libre, la cámara sigue al jugador sin rotar
//   · Scroll wheel → zoom suave entre minDistance y maxDistance
//   · SphereCast pivot → cámara → acorta la distancia ante el primer obstáculo
//
// Ejecuta en LateUpdate para garantizar que el jugador ya actualizó
// su posición en este frame antes de que la cámara recalcule la suya.
public class CameraController : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;
    // Cuánto sube el punto de mira respecto a los pies del jugador.
    [SerializeField] private float heightOffset = 1.8f;

    [Header("Orbit")]
    [SerializeField] private float mouseSensitivity = 0.20f;
    [SerializeField] private float minPitch = -20f;
    [SerializeField] private float maxPitch =  75f;

    [Header("Zoom")]
    [SerializeField] private float distance    = 6f;
    [SerializeField] private float minDistance = 1.5f;
    [SerializeField] private float maxDistance = 14f;
    // Unidades Unity que se acercan/alejan por notch de scroll.
    [SerializeField] private float zoomStep    = 1.5f;
    // Velocidad del suavizado exponencial hacia el zoom objetivo.
    [SerializeField] private float zoomSmooth  = 8f;

    [Header("Collision")]
    // Radio de la esfera usada para detectar obstáculos entre pivot y cámara.
    [SerializeField] private float     collisionRadius = 0.25f;
    // Excluir la capa del jugador (Player) para que el SphereCast no lo detecte.
    [SerializeField] private LayerMask collisionMask   = ~0;

    private float _yaw;
    private float _pitch       = 20f;
    private float _targetDist;
    private float _currentDist;

    private void Awake()
    {
        _targetDist  = distance;
        _currentDist = distance;
    }

    private void OnEnable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    private void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    private void LateUpdate()
    {
        if (target == null) return;
        HandleOrbit();
        HandleZoom();
        PositionCamera();
    }

    // ──────────────────────────────────────────────────────────────────────
    // ÓRBITA
    // ──────────────────────────────────────────────────────────────────────
    private void HandleOrbit()
    {
        if (Mouse.current == null) return;

        bool rmb      = Mouse.current.rightButton.isPressed;
        bool justDown = Mouse.current.rightButton.wasPressedThisFrame;

        // El cursor se oculta y bloquea solo mientras se orbita.
        Cursor.lockState = rmb ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible   = !rmb;

        // Primer frame de RMB: descartamos el delta acumulado antes del lockeo
        // para evitar el salto brusco de cámara al apretar el botón.
        if (!rmb || justDown) return;

        Vector2 delta = Mouse.current.delta.ReadValue();
        _yaw   += delta.x * mouseSensitivity;
        _pitch -= delta.y * mouseSensitivity;
        _pitch  = Mathf.Clamp(_pitch, minPitch, maxPitch);
    }

    // ──────────────────────────────────────────────────────────────────────
    // ZOOM
    // ──────────────────────────────────────────────────────────────────────
    // Normalizar por notch (independiente de la velocidad del scroll)
    // da una respuesta predecible en cualquier mouse.
    private void HandleZoom()
    {
        if (Mouse.current == null) return;

        float scroll = Mouse.current.scroll.ReadValue().y;
        if      (scroll >  0.01f) _targetDist -= zoomStep;
        else if (scroll < -0.01f) _targetDist += zoomStep;

        _targetDist  = Mathf.Clamp(_targetDist, minDistance, maxDistance);
        _currentDist = Mathf.Lerp(_currentDist, _targetDist, Time.deltaTime * zoomSmooth);
    }

    // ──────────────────────────────────────────────────────────────────────
    // POSICIÓN Y ORIENTACIÓN DE LA CÁMARA
    // ──────────────────────────────────────────────────────────────────────
    // SphereCast desde el pivot hacia la dirección de la cámara.
    // Si hay un obstáculo, acortamos la distancia para que la cámara
    // no clippe con paredes ni terreno.
    private void PositionCamera()
    {
        Vector3    pivot = target.position + Vector3.up * heightOffset;
        Quaternion rot   = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3    dir   = rot * Vector3.back;   // dirección pivot → cámara

        float dist = _currentDist;
        if (Physics.SphereCast(pivot, collisionRadius, dir, out RaycastHit hit,
                               dist, collisionMask, QueryTriggerInteraction.Ignore))
            dist = Mathf.Max(hit.distance - collisionRadius, 0.1f);

        transform.position = pivot + dir * dist;
        transform.LookAt(pivot);
    }
}
