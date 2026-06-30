# Re-Chronos — Dev Reference

## Scripts

| Script | Rol |
|---|---|
| `ProceduralTerrain.cs` | World manager: ciclo de vida de chunks, LOD, tracking del viewer |
| `TerrainChunk.cs` | Un chunk: malla + ruido + vertex colors + MeshCollider + soporte LOD |
| `VegetationSpawner.cs` | Genera árboles y pasto en cada chunk con seeds determinísticos |
| `PlayerController.cs` | CharacterController + Input System, movimiento, salto, gravedad |
| `PlayerVisual.cs` | Modelo placeholder (primitivas) + animación procedural |
| `CameraController.cs` | Cámara de tercera persona, órbita con mouse |
| `DayNightCycle.cs` | Ciclo día/noche: rota luz solar, gradientes de color/intensidad/ambiente/niebla |
| `AmbientAudioManager.cs` | 3 capas de audio (día, noche, viento), crossfade por curvas AnimationCurve según timeOfDay |
| `WaterManager.cs` | Crea plano de agua subdivido a waterLevel Y, sigue al viewer en XZ con snap de 1u |
| `TerrainVertexColor.shader` | Shader URP custom: lee Mesh.colors, Lambert difuso |
| `Water.shader` | Shader URP transparente: suma de senos (vertex + fragment), Fresnel, SampleSH ambient |

## Chunk Pool

| Param | Default | Efecto |
|---|---|---|
| `unloadBuffer` | `2` | Chunks extra más allá de `viewDistance` que se mantienen como caché invisible |

- **Zona visible:** `dist ≤ viewDistance` → chunks activos, renderizados
- **Zona caché:** `viewDistance < dist ≤ viewDistance + unloadBuffer` → chunk vivo en memoria, GO desactivado. Si el player vuelve: solo `SetVisible(true)` + `UpdateLOD`, sin reconstruir la malla
- **Zona de descarga:** `dist > viewDistance + unloadBuffer` → `TerrainChunk.Destroy()`, GO y Mesh liberados

`EvictDistantChunks(center)` se ejecuta al final de cada `UpdateVisibleChunks()`. Itera el dict, identifica coordenadas fuera de rango, destruye y remueve.

> Con `unloadBuffer = 0` los chunks se destruyen en cuanto salen de vista. Con valores altos actúa como una caché grande que evita rebuilds al revisitar zonas.

## LOD

El array `lodLevels` en `ProceduralTerrain` debe estar ordenado por `distanceThreshold` ascendente. La distancia usada es la **distancia Chebyshev**: `max(|dx|, |dz|)` en coordenadas de chunk.

| Param | Default | Efecto |
|---|---|---|
| `distanceThreshold` | — | Chunks a esta distancia o menos usan este nivel |
| `meshStep` | — | Paso de vértices: 1=full, 2=mitad, 5=quinta parte |
| `showVegetation` | — | Si `false`, oculta árboles y pasto en este nivel |

**Default con chunkSize=50:**

| Nivel | distanceThreshold | meshStep | showVegetation | Vértices/chunk |
|---|---|---|---|---|
| 0 | 1 | 1 | true | 51×51 = 2601 |
| 1 | 8 | 2 | false | 26×26 = 676 |

- `meshStep` debe dividir exactamente `chunkSize`. Con chunkSize=50: valores válidos = 1, 2, 5, 10, 25
- `TerrainChunk.UpdateLOD()` reconstruye la malla solo si el step cambió (evita rebuilds innecesarios)
- Los `Mesh` obsoletos se destruyen explícitamente para evitar memory leaks

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

## Vegetación

### Árboles (`VegetationSettings`)

| Param | Default | Efecto |
|---|---|---|
| `treesPerChunk` | `15` | Intentos de colocación por chunk (filtros reducen el total real) |
| `minHeightFraction` | `0.10` | Fracción de maxHeight mínima |
| `maxHeightFraction` | `0.75` | Fracción máxima (por encima = cimas sin vegetación) |
| `minSlopeNormal` | `0.80` | Normal Y mínima (~0.8 = ≤37°) |
| `trunkMaterial` | `null` | Opcional; fallback URP Lit marrón |
| `foliageMaterial` | `null` | Opcional; fallback URP Lit verde |

### Pasto (`GrassSettings`)

| Param | Default | Efecto |
|---|---|---|
| `grassPerChunk` | `200` | Intentos de colocación (más denso que árboles) |
| `minHeightFraction` | `0.05` | Fracción mínima (aparece casi desde el nivel del mar) |
| `maxHeightFraction` | `0.65` | Fracción máxima (por debajo de la línea de árboles) |
| `minSlopeNormal` | `0.85` | Más estricto que árboles — solo en zonas casi planas |
| `bladeWidth` | `0.15` | Ancho de la mata |
| `bladeHeight` | `0.45` | Alto de la mata |
| `material` | `null` | Opcional; fallback URP Lit verde (#487830) |

- Seed árboles: `coord.x × 73856093 XOR coord.y × 19349663`
- Seed pasto: `coord.x × 19349663 XOR coord.y × 83492791` (distinto para no correlacionar posiciones)
- Mata de pasto: dos quads cruzados (crossed-quad), doble cara, Mesh compartido por chunk
- Árboles: cilindro (tronco) + esfera (copa), sin Collider
- `VegetationSpawner.Spawn()` retorna GO raíz `"Vegetation"` con sub-raíces `"Trees"` y `"Grass"` — `SetVisible` los oculta juntos

## Ciclo Día/Noche

| Param | Default | Efecto |
|---|---|---|
| `dayDuration` | `120` | Segundos por ciclo completo (día + noche) |
| `timeOfDay` | `0.25` | 0 = medianoche · 0.25 = amanecer · 0.5 = mediodía · 0.75 = atardecer |
| `paused` | `false` | Congela el tiempo (útil para probar estados del cielo) |
| `sun` | — | Referencia al Directional Light de la escena |
| `sunYaw` | `-30` | Inclinación N/S de la trayectoria solar en grados |
| `sunColor` | gradiente | Color de la luz solar por timeOfDay |
| `sunIntensity` | curva | Intensidad 0→1.2→0 (noche→mediodía→noche) |
| `ambientColor` | gradiente | Luz ambiente flat (RenderSettings.ambientLight) |
| `controlFog` | `true` | Si true, maneja fogColor y fogDensity |
| `fogColor` | gradiente | Color de niebla por hora |
| `fogDensity` | curva | 0.008 noche · 0.003 mediodía |

- El sol rota en X: `timeOfDay × 360° − 90°` → a t=0.25 el sol está en el horizonte Este
- `Reset()` (llamado al agregar el componente en el Editor) pre-carga todos los gradientes y curvas
- ContextMenu shortcuts: **Set Sunrise / Noon / Sunset / Midnight** para testear sin correr el juego
- `DynamicGI.UpdateEnvironment()` se llama cada 1 segundo (costoso; no cada frame)
- Requiere niebla activada: Window → Rendering → Lighting → Other Settings → **Fog** ✓
- La skybox procedural (`Skybox/Procedural`) sigue automáticamente la rotación del Directional Light

## Audio Ambiente

### AmbientAudioManager (`AmbientAudioManager.cs`)

| Param | Default | Efecto |
|---|---|---|
| `dayAmbient` | null | AudioClip de naturaleza diurna (pájaros, viento suave) |
| `nightAmbient` | null | AudioClip de ambiente nocturno (grillos, búhos) |
| `windAmbient` | null | AudioClip de viento continuo (loop; volumen modulado por ráfagas) |
| `masterVolume` | `0.80` | Volumen global multiplicador de todas las capas |
| `windBaseVolume` | `0.25` | Volumen base del viento (antes del efecto de ráfaga) |
| `windGustStrength` | `0.12` | Variación de volumen máxima por ráfaga |
| `windGustSpeed` | `0.35` | Frecuencia de las ráfagas en ciclos/s |
| `fadeSpeed` | `1.5` | Velocidad de crossfade entre capas (unidades/s) |
| `dayVolumeCurve` | curva | Volumen de día por `timeOfDay` (0→0.20: silencio, 0.28→0.72: máximo, 0.80→1: silencio) |
| `nightVolumeCurve` | curva | Volumen de noche por `timeOfDay` (inverso del día) |
| `dayNight` | auto | Referencia a `DayNightCycle`; null → busca con `FindObjectOfType` en Start |

- Cada capa es un `AudioSource` 2D (`spatialBlend=0`) creado en `Awake`, en loop continuo
- Si un clip no está asignado, no se crea el `AudioSource` para esa capa (sin componentes vacíos)
- El volumen se actualiza con `Mathf.MoveTowards` → transiciones suaves entre día y noche
- El viento se modula con `sin(Time.time * windGustSpeed)` → efecto de ráfagas sin código extra
- `DayNightCycle.TimeOfDay` (getter público, `0f..1f`) sincroniza el audio con el visual
- `Reset()` pre-carga curvas al agregar el componente en el Editor (consistente con `DayNightCycle`)

## Agua

### WaterManager (`WaterManager.cs`)

| Param | Default | Efecto |
|---|---|---|
| `waterLevel` | `4` | Altura Y del plano en unidades Unity |
| `planeSize` | `600` | Tamaño del plano en unidades (debe cubrir `viewDistance × chunkSize × 2`) |
| `divisions` | `32` | Subdivisiones de la malla (más = ondas de vértice más suaves) |
| `viewer` | auto | Si null, busca `Camera.main` en Start |
| `waterMaterial` | null | Arrastra `WaterMat.mat` (shader `Custom/Water`) |

- El plano se crea en `Start()` y sigue al viewer en XZ (snap a 1u) → el UV world-space es continuo aunque el plano se mueva
- `IndexFormat.UInt32` — soporta divisions altas sin límite de 65k vértices
- Sin `MeshCollider` — el agua es solo visual en esta fase
- Fallback: si `waterMaterial` es null, crea un `Material` con el shader `Custom/Water` en runtime

### Water.shader (`Assets/_Project/Shaders/Water.shader`)

| Prop | Default | Efecto |
|---|---|---|
| `_ShallowColor` | azul claro α0.72 | Color en zona central (Fresnel bajo) |
| `_DeepColor` | azul oscuro α0.95 | Color en bordes (Fresnel alto) |
| `_FresnelPower` | `3.0` | Curvatura del Fresnel — más alto = borde más brusco |
| `_Smoothness` | `0.92` | Tamaño del reflejo especular |
| `_WaveScale` | `0.08` | Frecuencia de las ondas en world-space |
| `_WaveSpeed` | `0.35` | Velocidad de animación de las ondas |
| `_WaveAmplitude` | `0.12` | Altura máxima del desplazamiento de vértices |

- **Vertex shader:** desplaza Y usando suma de 3 senos evaluados en `positionWS.xz` (independiente del movimiento del plano)
- **Fragment shader:** normal analítica por diferencia central de la misma función de onda → correcta iluminación por píxel
- Iluminación: Lambert difuso + Blinn-Phong especular en zonas de Fresnel alto + `SampleSH` ambient (responde al gradiente de `DayNightCycle`)
- `Blend SrcAlpha OneMinusSrcAlpha`, `ZWrite Off` — semitransparente
- `Queue = Transparent` — se renderiza después del terreno opaco

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

**Primer setup (una sola vez)**
- [ ] `InputSystem_Actions.inputactions` → Inspector → **Generate C# Class** → Apply
- [ ] `Assets/_Project/TerrainMaterial.mat` → verificar shader `Custom/TerrainVertexColor`

**Terreno**
- [ ] Terrain GO → Transform en `(0, 0, 0)` con Scale `(1, 1, 1)` y Rotation `(0, 0, 0)`
- [ ] Terrain GO → `Procedural Terrain` → **Terrain Material** → arrastrar `TerrainMaterial`
- [ ] Terrain GO → **Viewer** → arrastrar Transform del Player
- [ ] Terrain GO → **Height Gradient** → configurar stops de color
- [ ] Terrain GO → quitar `Mesh Filter` y `Mesh Renderer` si quedaron del sistema anterior

**Vegetación**
- [ ] Terrain GO → sección **Vegetation** → ajustar `Trees Per Chunk` (default 15)
- [ ] Opcional: crear material URP Lit marrón → arrastrar a **Trunk Material**
- [ ] Opcional: crear material URP Lit verde → arrastrar a **Foliage Material**
- [ ] Terrain GO → sección **Grass** → ajustar `Grass Per Chunk` (default 200)
- [ ] Opcional: crear material URP Lit verde oscuro → arrastrar a **Material** de Grass
- [ ] Para desactivar: `Trees Per Chunk = 0` o `Grass Per Chunk = 0`

**Skybox y ciclo día/noche**
- [ ] Crear Material → shader `Skybox/Procedural` → asignar en Window → Rendering → Lighting → **Skybox Material**
- [ ] En el mismo panel: **Sun Source** → arrastrar el Directional Light
- [ ] Habilitar niebla: Other Settings → **Fog** ✓ · Mode: **Exponential Squared**
- [ ] Crear GO vacío `DayNightManager` → agregar `Day Night Cycle`
- [ ] Campo **Sun** → arrastrar el Directional Light de la escena
- [ ] Ajustar `Day Duration` (default 120 s) y `Time Of Day` inicial
- [ ] Usar ContextMenu **Set Sunrise / Noon / Sunset** para previsualizar sin Play

**Audio ambiente**
- [ ] Conseguir (o crear) 3 AudioClips en loop: `day_ambient.wav`, `night_ambient.wav`, `wind_ambient.wav`
- [ ] Crear GO vacío `AudioManager` → agregar `Ambient Audio Manager`
- [ ] Arrastrar los 3 clips a sus campos correspondientes
- [ ] Campo **Day Night** → arrastrar `DayNightCycle` (o dejar null → busca automáticamente)
- [ ] Ajustar `Master Volume` y `Wind Base Volume` a gusto
- [ ] Usar `Reset()` via ContextMenu si las curvas de día/noche quedaron vacías

**Agua**
- [ ] Crear Material → shader `Custom/Water` → guardarlo como `Assets/_Project/WaterMat.mat`
- [ ] Crear GO vacío `WaterManager` → agregar `Water Manager`
- [ ] Campo **Viewer** → arrastrar Transform del Player (o dejar null → usa Main Camera)
- [ ] Campo **Water Material** → arrastrar `WaterMat.mat`
- [ ] Ajustar `Water Level` (default 4) para que quede por debajo de la mayoría del terreno
- [ ] Ajustar `Plane Size` ≥ `viewDistance × chunkSize × 2` (default 600 para vD=3, cS=50)

**Jugador y cámara**
- [ ] Player GO → agregar `Character Controller`, `Player Controller`, `Player Visual`
- [ ] Main Camera → agregar `Camera Controller` → **Target** → arrastrar Player
- [ ] Player posición inicial: X/Z sobre el terreno, Y > maxHeight (ej. Y = 50)

## Fixes aplicados

### Pink terrain (shader rosa)
`UsePass "Universal Render Pipeline/Lit/ShadowCaster"` falla en URP 17 e invalida el SubShader completo.
**Fix:** ShadowCaster pass inline en `TerrainVertexColor.shader`.

### Jugador cae al vacío al spawnear
Los `MeshCollider` agregados en `Start()` no se registran en el broadphase hasta el siguiente `FixedUpdate`.
**Fix:** `PlayerController` deshabilita el `CharacterController`, espera 2 frames, hace Raycast, snappea.

### Jugador cae al cruzar límite de chunk
`SetActive(false)` en el chunk desactivaba el `MeshCollider`; durante `UpdateVisibleChunks` el jugador quedaba sin suelo un frame.
**Fix:** `SetVisible` usa `MeshRenderer.enabled` en lugar de `SetActive` — el colisionador permanece activo siempre.

### No aparece el campo Viewer / campos faltantes
Error de compilación porque `InputSystem_Actions` no existe hasta generar la clase C#.
**Fix:** Seleccionar `.inputactions` → Inspector → activar **Generate C# Class** → Apply.

### Regenerate All Chunks no funciona en Edit Mode
`Object.Destroy()` en Edit Mode es diferido indefinidamente; además al salir de Play Mode el `Dictionary` se vacía pero los GOs quedan como huérfanos.
**Fix:** `TerrainChunk.Destroy()` usa `DestroyImmediate` cuando `!Application.isPlaying`. `RegenerateAll()` itera los hijos del transform para limpiar huérfanos.

### Chunks rotados / terrain torcido
El GO padre (`WorldGenerator`) tenía rotación no-cero; los chunks la heredaban.
**Fix:** `SetPositionAndRotation(worldPos, Quaternion.identity)` en el constructor de `TerrainChunk`.

### noiseScale demasiado alto crea tiling
Si `noiseScale × chunkSize` da un entero (ej. `0.3 × 50 = 15`), el Perlin noise repite exactamente y todos los chunks se ven iguales.
**Fix:** Usar `noiseScale` que no produzca múltiplo entero con `chunkSize`. Default recomendado: `0.03`.

## Próximos pasos

- [x] LOD (Level of Detail) en chunks lejanos — meshStep + showVegetation por distancia Chebyshev
- [x] Pool + descarga de chunks fuera de rango — unloadBuffer de caché invisible
- [ ] Modelo 3D real + Unity Animator Controller
- [x] Skybox / atmósfera — skybox procedural + ciclo día/noche
- [x] Vegetación procedural (árboles placeholder)
- [x] Pasto / plantas rasantes (crossed-quad placeholder)
- [x] Agua (plano con shader)
- [ ] Ciclo día/noche
- [ ] Sistema de guardado
- [x] Audio ambiente (día/noche/viento con crossfade)
- [ ] Audio (pasos)
- [ ] Inventario / items
