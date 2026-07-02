# Re-Chronos — Dev Reference

## Scripts

| Script | Rol |
|---|---|
| `TerrainPreset.cs` | ScriptableObject: todos los parámetros de noise, clima y lista de BiomeData |
| `MapManager.cs` | Mapa global togglable con M: cámara ortográfica cenital, RenderTexture, Canvas circular, marcador del jugador |
| `WorldMapPreview.cs` *(Editor)* | EditorWindow: genera textura 2D del mapa sin Play mode, con modos BiomeColor / Elevation / Temperature / Humidity / WaterType |
| `BiomeData.cs` | ScriptableObject: nombre, color y rangos de temperatura/humedad/elevación de un bioma |
| `ProceduralTerrain.cs` | World manager: ciclo de vida de chunks, LOD, tracking del viewer |
| `TerrainChunk.cs` | Un chunk: malla + simulación climática + vertex colors por bioma + MeshCollider + LOD |
| `VegetationSpawner.cs` | Genera árboles y pasto en cada chunk con seeds determinísticos |
| `PlayerController.cs` | CharacterController + Input System, movimiento, salto, gravedad |
| `PlayerVisual.cs` | Modelo placeholder (primitivas) + animación procedural |
| `CameraController.cs` | Cámara de tercera persona, órbita con mouse |
| `DayNightCycle.cs` | Ciclo día/noche: rota luz solar, gradientes de color/intensidad/ambiente/niebla |
| `AmbientAudioManager.cs` | 3 capas de audio (día, noche, viento), crossfade por curvas AnimationCurve según timeOfDay |
| `WaterManager.cs` | Crea plano de agua subdivido a waterLevel Y, sigue al viewer en XZ con snap de 1u |
| `TerrainVertexColor.shader` | Shader URP custom: lee Mesh.colors, Lambert difuso |
| `Water.shader` | Shader URP transparente: suma de senos (vertex + fragment), Fresnel, SampleSH ambient |
| `DebugHUD.cs` | Panel IMGUI togglable con F3: posición, chunk, elevación, temperatura, humedad, bioma, FPS |

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

## Agua (Mar, Ríos y Lagos)

El sistema de agua es una capa que se ejecuta **antes** del cálculo climático y modifica la elevación y la humedad de cada vértice. Todo es función pura de coordenadas mundiales → sin estado cross-chunk → sin costuras en los bordes.

### Pipeline de vértice (BuildMesh)

```
1. SampleHeight(worldX, worldZ)            → elev, normElev
2. isOcean   = normElev < seaLevel
3. RiverRidge(worldX, worldZ)              → ridgeVal [0..1]  (domain warping × 3 Perlin)
   bankEdge  = riverThr − riverWidth × 2   ← zona de banco, 3× el canal
   bankSpan  = riverWidth × 3
   carveM    = SmoothStep(0,1, (ridgeVal−bankEdge)/bankSpan)    ← S-curve, sin pinches
   riverM    = SmoothStep(0,1, (ridgeVal−riverThr)/riverWidth)  ← canal visual
   isRiver   = riverM > 0
   if carveM > 0: normElev = Max(seaLevel, Lerp(normElev, seaLevel, carveM×riverStrength))
4. LakeMask(worldX, worldZ, normElev)      → lakeM [0..1]  (1 Perlin de baja freq)
   if lakeM > 0: normElev = Lerp(normElev, seaLevel, lakeM)
5. temperature = f(worldZ, normElev, altitudeCooling)
6. waterBonus:
   si isOcean/isRiver/isLake → bonus = waterHumidityBonus
   si zona costera            → bonus = waterHumidityBonus × (1 − coastDist/coastalBand)
   si aura fluvial            → bonus = waterHumidityBonus × 0.70 × auraNorm
7. humidity = Clamp01(ComputeHumidity() + waterBonus)
8. biomeColor = SampleBiomeColor(normElev, temp, hum)
   si isOcean  → oceanFloorColor
   si banco (carveM>0, riverM=0) → Lerp(biomeColor, riverBedColor, carveM×0.30)
   si canal (riverM>0):
       aboveSea? → Lerp(biomeColor, riverWaterColor, riverM×0.88)   ← agua de altura
       al nivel  → Lerp(biomeColor, riverBedColor, riverM×0.88)      ← lecho seco
   si lago (lakeM>0):
       aboveSea? → Lerp(biomeColor, riverWaterColor, lakeM×0.85)
       al nivel  → Lerp(biomeColor, oceanFloorColor, lakeM×0.85)
```

### Parámetros de TerrainPreset

| Sección | Param | Default | Efecto |
|---|---|---|---|
| Mar | `seaLevel` | `0.15` | Cutoff normalizado. Con exp=2.5 da ~40% océano |
| Mar | `oceanFloorColor` | azul oscuro | Color del fondo marino |
| Mar | `coastalBand` | `0.08` | Banda de humedad costera sobre seaLevel |
| Ríos | `riverNoiseScale` | `0.003` | Frecuencia. Bajo = ríos más largos |
| Ríos | `riverNoiseOffset` | `(1000, 700)` | Seed de la red fluvial |
| Ríos | `riverWidth` | `0.07` | Fracción del ridge que define el canal |
| Ríos | `riverStrength` | `0.55` | Profundidad de talla en alta montaña |
| Ríos | `riverBedColor` | grava oscura | Color del lecho del río (al nivel del mar) |
| Ríos | `riverWaterColor` | azul-teal | Color del agua de ríos/lagos por encima del seaLevel (vertex color) |
| Lagos | `lakeThreshold` | `0.28` | Umbral del ruido de cuenca; más alto = más lagos |
| Lagos | `lakeMaxHeight` | `0.40` | Altura máxima sobre seaLevel para lagos |
| Lagos | `lakeSmoothing` | `0.08` | Fadeout del borde del lago (elev normalizada) |
| Humedad | `waterHumidityBonus` | `0.50` | Bonus máximo junto al agua |
| Humedad | `riverHumidityRadius` | `0.15` | Ancho del aura de humedad fluvial (ridge space) |

### Islas

Las islas emergen automáticamente: son zonas donde el ruido de elevación base supera `seaLevel`. No requieren código extra.

### RiverRidge — domain warping

```
bx,bz = worldXZ × riverNoiseScale + riverNoiseOffset
dx    = (Perlin(bx+3.71, bz+8.31) − 0.5) × 3     ← warp en X
dz    = (Perlin(bx+8.31, bz+3.71) − 0.5) × 3     ← warp en Z
n     = Perlin(bx+dx, bz+dz)                       ← ruido warped
ridge = 1 − |n × 2 − 1|                            ← función carpa
```
El domain warping desplaza las coordenadas antes de muestrear → canales curvos y orgánicos sin líneas rectas de artefacto. 3 muestras Perlin por vértice.

### LakeMask

```
scale = riverNoiseScale × 0.35     ← freq muy baja → cuencas grandes
n     = Perlin(wx×scale + 500, wz×scale + 500)
if n < lakeThreshold AND seaLevel < normElev < seaLevel+lakeMaxHeight:
    baseMask = 1 − n/lakeThreshold
    elevFade = clamp((lakeMax − normElev) / lakeSmoothing)
    lakeMask = baseMask × elevFade
```
Los lagos se aplanan a `seaLevel`, quedando cubiertos por el plano de agua del WaterManager.

### WaterManager sync

`ProceduralTerrain` sincroniza automáticamente `WaterManager.SetWaterLevel(seaLevel × maxHeight)` en `Start()` y `RegenerateAll()`. Arrastrá el WaterManager al campo **Agua** del componente `Procedural Terrain`.

### Notas de performance

- `RiverRidge`: 3 Perlin/vértice (2 warp + 1 ridge)
- `LakeMask`: 1 Perlin/vértice
- Total nuevo: 4 Perlin adicionales. En LOD0 (51×51): ~10 400 calls extra/chunk — insignificante.
- `riverThr`, `bankEdge`, `bankSpan`, `coastBandInv`, `riverRadInv` se pre-computan fuera del loop de vértices.

## Biomas y clima

El sistema reemplaza el gradiente de altura fijo por una **simulación climática geográfica** por vértice. Temperatura y humedad determinan el bioma; la altitud actúa como modificador.

### Flujo de cálculo por vértice

```
normalizedElevation = height / maxHeight                           [0..1]
temperature         = baseTemp + worldZ × latitudeScale            ← gradiente N-S
                    - normalizedElev × altitudeCooling             ← enfriamiento por altura
                    + PerlinNoise(worldX, worldZ, tempOffset) × strength
humidity            = baseHumidity
                    + PerlinNoise(worldX, worldZ, humOffset) × strength
color               = mezcla ponderada de biomas por distancia en espacio (elev, temp, hum)
```

### Parámetros clave del TerrainPreset

| Param | Default | Efecto |
|---|---|---|
| `noiseScale` | `0.03` | Frecuencia del ruido de elevación |
| `maxHeight` | `30` | Altura máxima en unidades |
| `octaves` | `4` | Capas del ruido fbm |
| `persistence` | `0.5` | Peso de cada octava |
| `lacunarity` | `2.0` | Multiplicador de frecuencia |
| `baseTemperature` | `0.50` | Temperatura en Z=0 (zona templada, estilo Buenos Aires) |
| `latitudeScale` | `0.002` | Cambio de temp/unidad en Z. Con 0.002: ±500u cubre 0→1 |
| `altitudeCooling` | `0.55` | Cuánto resta la cima: temp_cima = temp_base − 0.55 |
| `baseHumidity` | `0.50` | Humedad base antes del ruido climático |
| `climateNoiseScale` | `0.008` | Escala del Perlin de clima (bajo = manchas grandes) |
| `climateNoiseStrength` | `0.20` | Amplitud del ruido climático (±0.20) |
| `blendRadius` | `0.12` | Radio de mezcla en espacio climático. Más alto = bordes más suaves |
| `elevationExponent` | `2.5` | Pow aplicado al ruido normalizado [0,1]. Aplana cuencas; preserva montañas. 1=lineal |
| `ridgeThreshold` | `0.50` | Umbral desde el cual activa la cresta. Con exp=2.5 corresponde al ~20% superior |
| `ridgeStrength` | `0.75` | Intensidad del efecto cresta. 0=sin efecto, 1=V pura. 0.7–0.8 = cordillera con laderas empinadas |
| `worldRadius` | `5000` | Radio del continente circular. El terreno cae al fondo del mar más allá de `falloffStartRadius` |
| `falloffStartRadius` | `4200` | Inicio del degradado. Franja de transición = `worldRadius − falloffStartRadius` = 800 u con defaults |

### Rangos sugeridos para los 5 biomas

> Todos los valores son normalizados (0.0–1.0). `blendRadius = 0.12` crea transiciones suaves en los bordes.

| Bioma | Temp min | Temp max | Hum min | Hum max | Elev min | Elev max | Color hex |
|---|---|---|---|---|---|---|---|
| **Alta Montaña** | 0.00 | 1.00 | 0.00 | 1.00 | 0.62 | 1.00 | `#9EA8A4` gris-nieve |
| **Sur Frío / Tundra** | 0.00 | 0.38 | 0.00 | 1.00 | 0.00 | 0.65 | `#6E8068` gris-verde |
| **Llanura Templada** | 0.30 | 0.68 | 0.25 | 0.75 | 0.00 | 0.55 | `#6B8F3E` oliva |
| **Norte Seco / Estepa** | 0.55 | 1.00 | 0.00 | 0.45 | 0.00 | 0.65 | `#C4A84A` tierra seca |
| **Norte Húmedo / Selva** | 0.58 | 1.00 | 0.40 | 1.00 | 0.00 | 0.55 | `#2A7A2A` verde oscuro |

**Lógica de las franjas verticales:**
- **Z positivo** → Norte → temperatura alta → Norte Seco o Norte Húmedo según humedad del Perlin
- **Z = 0** → Zona templada → Llanura Templada en elevaciones bajas, Alta Montaña en cimas
- **Z negativo** → Sur → temperatura baja → Sur Frío / Tundra
- **Cualquier latitud a gran altura** → altitudeCooling reduce la temp → Alta Montaña (elev ≥ 0.62)

### Creación de assets en Unity

1. Clic derecho en Project → **Create → Re-Chronos → Terrain Preset** → configurar parámetros y asignar biomas
2. Para cada bioma: **Create → Re-Chronos → Biome Data** → poner nombre, color y rangos
3. Arrastrar los BiomeData al array `Biomes` del TerrainPreset (orden no importa)
4. Arrastrar el TerrainPreset al campo **Preset** del componente `Procedural Terrain`

### Notas técnicas

- `SampleHeight`: FBM → normalizar [0,1] → `Mathf.Pow(n, elevationExponent)` → si > ridgeThreshold, tent function `1 − |t·2 − 1|` mezclada con `ridgeStrength` → escalar a `maxHeight` → **world falloff** (`elev *= 1 − SmoothStep(falloffStartRadius, worldRadius, dist)`). El falloff se aplica dentro de `SampleHeight` → afecta automáticamente a `BuildMesh`, `SampleClimate` y `VegetationSpawner`.
- `TerrainSettings` (struct) eliminado — `TerrainPreset` (ScriptableObject) lo reemplaza en toda la cadena
- `VegetationSpawner.Spawn()` ahora recibe `TerrainPreset` directamente
- `TerrainChunk.SampleHeight()` sigue siendo `public static` para que el spawner acceda
- `blendRadius` precomputado como `invBlend = 1 / blendRadius` antes del loop de vértices (evita división por vértice)
- La fallback de bioma (cuando ningún bioma cubre el punto) usa el bioma de menor distancia centroide

## Terrain

Los parámetros de noise y clima están en el **TerrainPreset** ScriptableObject. Los parámetros de chunk siguen en `ProceduralTerrain`:

| Param | Default | Efecto |
|---|---|---|
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

### Modo Vuelo

**Activación:** doble toque de Jump (`Space`) en menos de `flyDoubleTapWindow` (default 0.30 s). Mismo gesto lo desactiva.

| Tecla | Acción en vuelo |
|---|---|
| WASD | Vuela en la dirección completa de la cámara (incluye pitch) |
| Space (hold) | Sube verticalmente |
| C (hold) | Baja verticalmente |
| Shift | Duplica la velocidad de vuelo |

| Param | Default | Efecto |
|---|---|---|
| `flySpeed` | `20` | Velocidad base de vuelo (u/s). Sprint la duplica a 40 u/s. |
| `flyDoubleTapWindow` | `0.30` | Ventana de tiempo (s) para detectar el doble toque |

El `CharacterController` permanece activo en vuelo → colisiones con paredes y techo funcionan igual. `IsFlying` es un getter público para que `PlayerVisual` pueda leer el estado si lo necesita.

## Camera

Estilo **Valheim**: cursor libre en reposo; RMB orbita la cámara; scroll hace zoom; SphereCast evita clipping.

| Param | Default | Efecto |
|---|---|---|
| `heightOffset` | `1.8` | Altura del pivot de cámara respecto a los pies del jugador |
| `mouseSensitivity` | `0.20` | Grados por píxel de delta de mouse |
| `minPitch / maxPitch` | `-20° / 75°` | Límites verticales de órbita |
| `distance` | `6` | Distancia inicial de la cámara al pivot |
| `minDistance / maxDistance` | `1.5 / 14` | Rango de zoom |
| `zoomStep` | `1.5` | Unidades de zoom por notch de scroll |
| `zoomSmooth` | `8` | Velocidad del suavizado exponencial al zoom objetivo |
| `collisionRadius` | `0.25` | Radio del SphereCast de colisión cámara |
| `collisionMask` | `~0` | Capas detectadas. Excluir la capa del jugador (Player) |

**Flujo:**
- RMB mantenido → cursor oculto/locked, mouse delta acumula yaw/pitch
- El primer frame de RMB descarta el delta (evita salto brusco al bloquear el cursor)
- Sin RMB → cursor libre, cámara sigue al jugador sin rotar
- `SphereCast(pivot → dir)` → acorta distancia si hay obstáculo entre pivot y cámara

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

**Biomas (hacer primero)**
- [ ] Para cada bioma: clic derecho en Project → **Create → Re-Chronos → Biome Data** → configurar nombre, color y rangos de Temperatura/Humedad/Elevación
- [ ] Clic derecho → **Create → Re-Chronos → Terrain Preset** → configurar noise, clima y arrastrar los BiomeData al array **Biomes**

**Terreno**
- [ ] Terrain GO → Transform en `(0, 0, 0)` con Scale `(1, 1, 1)` y Rotation `(0, 0, 0)`
- [ ] Terrain GO → `Procedural Terrain` → **Terrain Material** → arrastrar `TerrainMaterial`
- [ ] Terrain GO → **Preset** → arrastrar el `TerrainPreset` creado
- [ ] Terrain GO → **Viewer** → arrastrar Transform del Player
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

### Artefacto "Muro de Espinas" (pinches en bordes de ríos)
`riverM = (ridgeVal − riverThreshold) / riverWidth` es lineal → pendiente C0 en el borde del canal → vértices adyacentes a uno y otro lado del umbral quedan a elevaciones muy distintas → sierra visible.
**Fix:** Separar la zona de talla (3× el ancho visual, `bankEdge`) de la zona de canal (`riverThr`). Aplicar `Mathf.SmoothStep(0,1,t)` a ambas máscaras → pendiente = 0 en los extremos → transición C2 sin discontinuidades. Carving cambiado de resta con gradiente a `Lerp(normElev, seaLevel, carveM × riverStrength)` — bounded naturalmente.

### Ríos y lagos de altura secos (sin agua visible)
El WaterManager genera un único plano global en `seaLevel`. Los cauces y cuencas talladas en zonas altas quedaban visibles como valles/cañones secos.
**Fix:** En el paso de color de `BuildMesh`, cuando `normElev > seaLevel + 0.005`, el canal y el lago reciben un vertex color interpolado hacia `TerrainPreset.riverWaterColor` (azul-teal configurable). Al nivel del mar reciben `riverBedColor` / `oceanFloorColor` como antes (el plano de agua los cubre).

### noiseScale demasiado alto crea tiling
Si `noiseScale × chunkSize` da un entero (ej. `0.3 × 50 = 15`), el Perlin noise repite exactamente y todos los chunks se ven iguales.
**Fix:** Usar `noiseScale` que no produzca múltiplo entero con `chunkSize`. Default recomendado: `0.03`.

## Debug HUD

Panel IMGUI togglable con **F3**. Agregar el componente `DebugHUD` a cualquier GO de la escena (ej. un vacío "DebugManager"). No requiere Canvas.

| Param | Default | Efecto |
|---|---|---|
| `player` | — | Transform del jugador (fuente de posición y coord. de clima) |
| `preset` | — | TerrainPreset activo (para llamar a `SampleClimate`) |
| `chunkSize` | `50` | Tamaño de chunk para calcular las coordenadas de chunk |
| `refreshEveryFrames` | `8` | Intervalo de refresco de datos climáticos (~7/seg a 60fps) |

**Contenido del panel:**

| Fila | Descripción |
|---|---|
| Header + FPS | Título y FPS (verde ≥55 · amarillo ≥28 · rojo <28) |
| X / Y / Z | Posición mundial en unidades Unity |
| CHUNK | Coordenadas de chunk `(cx, cz)` |
| ELEV | Altura muestreada en el punto y porcentaje del maxHeight |
| TEMP | Barra de color (azul→verde→naranja) + valor 0..1 |
| HUM | Barra de color (tierra→azul) + valor 0..1 |
| BIOMA | Nombre del bioma dominante en esa posición |

**Notas técnicas:**
- Los datos climáticos usan `TerrainChunk.SampleClimate(roundX, roundZ, preset)` — mismo algoritmo que la generación del terreno
- `SampleClimate` llama internamente a `SampleHeight` → `ComputeTemperature` → `ComputeHumidity` → bioma más cercano por distancia en espacio climático
- El struct `ClimateData` (definido en `TerrainChunk.cs`) expone: `Elevation`, `NormalizedElev`, `Temperature`, `Humidity`, `DominantBiome`
- Los estilos IMGUI se inicializan lazy la primera vez que `_visible` es true (evita allocs en OnGUI cada frame)
- `Derived(style, color)` crea un `GUIStyle` inline para colorear FPS y bioma sin definir un estilo extra

## Sistema de Mapa (MapManager.cs)

Panel de mapa global togglable con **M**. Se crea enteramente en código — no necesita prefabs ni Canvases previos.

| Param | Default | Efecto |
|---|---|---|
| `player` | — | Transform del jugador (fuente de posición y orientación) |
| `preset` | — | TerrainPreset (para leer `worldRadius`) |
| `mapCamera` | auto | Si null, se crea `MapCamera` hijo del GO |
| `textureSize` | `512` | Resolución de la RenderTexture |
| `cameraHeight` | `300` | Altura Y de la cámara sobre el mundo |
| `renderLayers` | `~0` | Capas que renderiza el mapa |
| `panelSize` | `620` | Diámetro del mapa en puntos de pantalla |
| `circleSprite` | null | Sprite circular para la máscara (Create → 2D → Sprites → Circle) |
| `markerSprite` | null | Sprite del marcador del jugador (flecha apuntando al Norte) |
| `markerColor` | amarillo | Color del marcador |
| `markerSize` | `18` | Tamaño del marcador en puntos |

### Jerarquía UI generada en Awake

```
MapCanvas (Screen Space Overlay, sortOrder 20)
 └── MapRoot
      ├── MapBg         Image oscura semitransparente (recortada a círculo si hay circleSprite)
      │    ├── MapBorder Image dorada, aro decorativo
      │    └── MapMask   Image + Mask → recorta los hijos en círculo
      │         ├── MapTex        RawImage → RenderTexture de la cámara ortográfica
      │         └── PlayerMarker  Image, se actualiza cada frame con la posición del jugador
      └── CloseHint     Text "[ M ] Cerrar mapa"
```

### Coordenadas del marcador

```
// Cámara Euler(90,0,0): cameraRight = +X world, cameraUp = +Z world → Norte = arriba
u = player.x / (worldRadius × 2) + 0.5   → [0..1] en X
v = player.z / (worldRadius × 2) + 0.5   → [0..1] en Z
markerAnchoredPos = ((u-0.5) × panelSize, (v-0.5) × panelSize)
markerRotation.z  = -player.eulerY        → apunta en la dirección del jugador
```

### Notas de performance

- La cámara del mapa se habilita solo mientras el panel está abierto (`mapCamera.enabled = false` por default).
- La RenderTexture se mantiene en GPU; el Canvas se activa/desactiva con `SetActive` sin destruirla.
- Para una minimap always-on, habilitar `mapCamera.enabled = true` permanentemente y mostrar el Canvas sin el toggle.

## World Map Preview (WorldMapPreview.cs)

EditorWindow para previsualizar el mapa mundial completo en 2D **sin entrar en Play mode**.  
Menú: **Re-Chronos → World Map Preview** · Shortcut: `Ctrl+Shift+M`  
Archivo: `Assets/_Project/Scripts/Editor/WorldMapPreview.cs`

### Configuración

| Param | Default | Efecto |
|---|---|---|
| `Terrain Preset` | — | ScriptableObject con todos los parámetros del mundo |
| `Ancho / Alto (px)` | `512 × 512` | Resolución de la textura generada |
| `Escala (u/px)` | `20` | Unidades de mundo por píxel. Con 512×512: cubre ±5 120 u |
| Botón **Auto** | — | Calcula `pixelScale = worldRadius × 2 / min(w, h)` para que el continente llene el mapa |
| `Modo` | `BiomeColor` | Ver tabla de modos abajo |

### Modos de visualización

| Modo | Descripción |
|---|---|
| `BiomeColor` | Pipeline completo: bioma, ríos, lagos, bancos — idéntico a `BuildMesh` en runtime |
| `Elevation` | Escala de grises. Fondo oceánico oscuro; tierra blanca en cimas |
| `Temperature` | Rampa azul (frío) → verde → rojo (cálido) |
| `Humidity` | Rampa amarillo (seco) → azul (húmedo) |
| `WaterType` | Bloques sólidos: océano / río / lago / tierra |

### Pipeline de generación

El loop de píxeles replica exactamente `BuildMesh` de `TerrainChunk`:

```
1. TerrainChunk.SampleHeight(wx, wz, p)   ← public static → FBM + ridge + world falloff
2. riverRidge → carveM (SmoothStep) → normElev ajustada
3. LakeMask → normElev ajustada
4. ComputeTemperature(wx, wz, normElev, p)
5. ComputeHumidity(wx, wz, p) + waterBonus
6. SampleBiomeColor(normElev, temp, hum, p) con caída cuadrática y fallback
7. Lerps finales: riverBedColor / riverWaterColor / oceanFloorColor
```

Los métodos `RiverRidge`, `LakeMask`, `ComputeTemperature`, `ComputeHumidity` y `SampleBiomeColor` son **private** en `TerrainChunk`. `WorldMapPreview` los replica verbatim — si modificás la lógica en `TerrainChunk`, actualizá también `WorldMapPreview`.

### Performance estimada

| Resolución | Pixels | Calls Perlin ≈ | Tiempo aprox. |
|---|---|---|---|
| 512 × 512 | 262 144 | 2.6 M | < 500 ms |
| 1024 × 1024 | 1 048 576 | 10.5 M | < 2 s |

- Barra de progreso actualizada cada 16 filas (overhead mínimo)
- Botón **Save PNG** exporta la textura generada mediante `EncodeToPNG()`

## Próximos pasos

- [x] LOD (Level of Detail) en chunks lejanos — meshStep + showVegetation por distancia Chebyshev
- [x] Pool + descarga de chunks fuera de rango — unloadBuffer de caché invisible
- [ ] Modelo 3D real + Unity Animator Controller
- [x] Skybox / atmósfera — skybox procedural + ciclo día/noche
- [x] Vegetación procedural (árboles placeholder)
- [x] Pasto / plantas rasantes (crossed-quad placeholder)
- [x] Agua (plano con shader)
- [x] Límites circulares del mundo (world falloff)
- [x] Mapa global (M key, cámara ortográfica + RenderTexture + UI circular)
- [x] World Map Preview (EditorWindow 2D, sin Play mode, 5 modos de visualización)
- [ ] Ciclo día/noche
- [ ] Sistema de guardado
- [x] Audio ambiente (día/noche/viento con crossfade)
- [ ] Audio (pasos)
- [ ] Inventario / items
