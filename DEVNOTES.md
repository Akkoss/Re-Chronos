# Re-Chronos — Dev Reference

## Scripts

| Script | Rol |
|---|---|
| `ProceduralTerrain.cs` | World manager: ciclo de vida de chunks, tracking del viewer |
| `TerrainChunk.cs` | Un chunk: malla + ruido + vertex colors + MeshCollider |
| `PlayerController.cs` | CharacterController + Input System, movimiento, salto, gravedad |
| `PlayerVisual.cs` | Modelo placeholder (primitivas) + animación procedural |
| `CameraController.cs` | Cámara de tercera persona, órbita con mouse |
| `TerrainVertexColor.shader` | Shader URP custom: lee Mesh.colors, Lambert difuso |

## Terrain

| Param | Default | Efecto |
|---|---|---|
| `noiseScale` | `0.03` | Frecuencia del ruido (bajo = colinas grandes) |
| `maxHeight` | `30` | Altura máxima en unidades Unity |
| `octaves` | `4` | Capas de ruido (más = más detalle) |
| `persistence` | `0.5` | Peso de cada octava (0.5 = mitad cada vez) |
| `lacunarity` | `2.0` | Multiplicador de frecuencia por octava |
| `chunkSize` | `50` | Vértices por lado de chunk |
| `viewDistance` | `3` | Radio de chunks visibles (7×7 = 49 chunks) |

## Player

- Walk: 5 u/s · Sprint: 10 u/s · Jump height: 2 m · Gravity: -20
- Spawn: espera 2 frames → Raycast → snappea al terreno
- Animaciones procedurales: bob (caminar/correr), squash (aterrizaje), respiración (idle)

## Camera

- followDistance: 6 · heightOffset: 1.8 · mouseSensitivity: 0.15
- Pitch: [-30°, 70°] · LateUpdate (siempre después del jugador)

## Input

| Tecla | Acción |
|---|---|
| WASD / Flechas | Mover |
| Shift | Correr |
| Space | Saltar |
| Mouse | Órbita de cámara |
| Escape | Desbloquear cursor |
| Clic izquierdo | Atacar / rebloquear cursor |
| E (hold) | Interactuar |
| C | Agacharse |

## Setup checklist

- [ ] `InputSystem_Actions.inputactions` → Inspector → **Generate C# Class** → Apply
- [ ] Terrain GO → `Procedural Terrain` → **Terrain Material** → arrastrar `TerrainMaterial`
- [ ] Terrain GO → **Viewer** → arrastrar Transform del Player
- [ ] Player GO → agregar `Character Controller`, `Player Controller`, `Player Visual`
- [ ] Main Camera → agregar `Camera Controller` → **Target** → arrastrar Player
- [ ] Player posición inicial: X/Z sobre el terreno, Y > maxHeight (ej. Y = 50)

## Fixes aplicados

### Pink terrain (shader rosa)
`UsePass "Universal Render Pipeline/Lit/ShadowCaster"` falla en URP 17 e invalida el SubShader completo.
**Fix:** ShadowCaster pass inline en `TerrainVertexColor.shader`.

### Jugador cae al vacío
Los `MeshCollider` agregados en `Start()` no se registran en el broadphase de física hasta el siguiente `FixedUpdate`.
**Fix:** `PlayerController` deshabilita el `CharacterController`, espera 2 frames, hace Raycast, snappea.

### No aparece el campo Viewer / campos faltantes
Error de compilación porque `InputSystem_Actions` (clase C# del Input System) no existe todavía.
**Fix:** Seleccionar `.inputactions` → Inspector → activar **Generate C# Class** → Apply.

### Regenerate All Chunks no hace nada en Edit Mode
`Object.Destroy()` en Edit Mode es diferido indefinidamente — los GameObjects nunca se eliminan.
Además, al salir de Play Mode el `Dictionary<Vector2Int, TerrainChunk>` (no serializado) se vacía pero los GameObjects hijos del transform permanecen en la escena como huérfanos.
**Fix:** `TerrainChunk.Destroy()` usa `Object.DestroyImmediate` cuando `!Application.isPlaying`. `RegenerateAll()` también itera los hijos del transform y destruye cualquier huérfano antes de regenerar.

## Próximos pasos

- [ ] LOD (Level of Detail) en chunks lejanos
- [ ] Pool + descarga de chunks fuera de rango
- [ ] Modelo 3D real + Unity Animator Controller
- [ ] Skybox / atmósfera
- [ ] Vegetación procedural (árboles, pasto)
- [ ] Agua (plano con shader)
- [ ] Ciclo día/noche
- [ ] Sistema de guardado
- [ ] Audio (pasos, ambiente)
- [ ] Inventario / items
