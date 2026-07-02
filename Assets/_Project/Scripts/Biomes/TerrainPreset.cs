using System.Collections.Generic;
using UnityEngine;

// Creá un asset vía: clic derecho en Project → Create → Re-Chronos → Terrain Preset
[CreateAssetMenu(fileName = "TerrainPreset", menuName = "Re-Chronos/Terrain Preset")]
public class TerrainPreset : ScriptableObject
{
    // ── NOISE DE ELEVACIÓN ────────────────────────────────────────────────────
    [Header("Ruido de elevación")]
    public float   noiseScale   = 0.03f;
    public float   maxHeight    = 30f;
    [Range(1, 8)]       public int   octaves     = 4;
    [Range(0.1f, 0.9f)] public float persistence = 0.5f;
    [Range(1f, 4f)]     public float lacunarity  = 2f;
    public Vector2 offset = Vector2.zero;

    // ── FORMA DEL TERRENO ────────────────────────────────────────────────────
    [Header("Forma del terreno")]
    [Tooltip("Exponente aplicado al ruido normalizado [0,1] antes de escalar a maxHeight.\n" +
             "1.0 = lineal. 2–3 = aplana cuencas y estepas; preserva montañas.")]
    [Range(0.5f, 5f)] public float elevationExponent = 2.5f;

    [Tooltip("Umbral de ruido shaped [0,1] a partir del cual activa la cresta.\n" +
             "Con elevationExponent=2.5, valor 0.5 equivale aproximadamente al 20% superior del mapa.")]
    [Range(0f, 0.95f)] public float ridgeThreshold = 0.50f;

    [Tooltip("Intensidad de la máscara de cresta (ridge). 0 = sin efecto. 1 = V pura.\n" +
             "0.7–0.8 produce cordilleras con laderas empinadas sin colapsar las cimas.")]
    [Range(0f, 1f)] public float ridgeStrength = 0.75f;

    // ── LÍMITES DEL MUNDO ─────────────────────────────────────────────────────
    [Header("Límites del Mundo")]
    [Tooltip("Radio del continente en unidades. El terreno se hunde al océano pasado falloffStartRadius.\n" +
             "0 = desactiva el efecto (mundo plano e infinito).")]
    public float worldRadius = 5000f;

    [Tooltip("Radio desde el centro (0,0) donde comienza el degradado suave hacia el borde.\n" +
             "Debe ser menor que worldRadius. La diferencia (worldRadius − falloffStartRadius) define la anchura de la franja de transición.")]
    public float falloffStartRadius = 4200f;

    // ── MAR E ISLAS ───────────────────────────────────────────────────────────
    [Header("Mar e Islas")]
    [Tooltip("Elevación normalizada [0,1] que separa océano de tierra.\n" +
             "El WaterManager debe tener waterLevel = seaLevel × maxHeight.\n" +
             "Con elevationExponent=2.5: 0.15 ≈ 40% océano · 0.08 ≈ 25%.")]
    [Range(0f, 0.4f)] public float seaLevel = 0.15f;

    [Tooltip("Color del fondo marino (lo cubre el plano de agua, pero es visible en costas someras).")]
    public Color oceanFloorColor = new(0.16f, 0.20f, 0.25f);

    [Tooltip("Banda de elevación normalizada sobre seaLevel que se considera zona costera.\n" +
             "Los vértices en esta franja reciben bonus de humedad decreciente.")]
    [Range(0f, 0.25f)] public float coastalBand = 0.08f;

    // ── RÍOS ──────────────────────────────────────────────────────────────────
    [Header("Ríos")]
    [Tooltip("Frecuencia del mapa de ríos. Bajo = canales largos y sinuosos (0.002–0.006).")]
    public float riverNoiseScale = 0.003f;

    [Tooltip("Seed de desplazamiento para el patrón de ríos. Cambiar para otra red fluvial.")]
    public Vector2 riverNoiseOffset = new(1000f, 700f);

    [Tooltip("Fracción del rango de la función carpa que define el canal del río.\n" +
             "0.04 = ríos muy estrechos · 0.15 = cauces anchos.")]
    [Range(0.02f, 0.20f)] public float riverWidth = 0.07f;

    [Tooltip("Profundidad máxima de la talla fluvial normalizada.\n" +
             "Los ríos tallan más en montaña (carve ∝ altura) y se suavizan al llegar al mar.")]
    [Range(0f, 0.8f)] public float riverStrength = 0.55f;

    [Tooltip("Color del lecho del río (grava húmeda / roca oscura).")]
    public Color riverBedColor = new(0.27f, 0.25f, 0.22f);

    [Tooltip("Color del agua de ríos y lagos sobre el nivel del mar (vertex color).\n" +
             "El plano del WaterManager solo cubre el seaLevel; este color pinta el agua de altura.")]
    public Color riverWaterColor = new(0.18f, 0.38f, 0.58f);

    // ── LAGOS ─────────────────────────────────────────────────────────────────
    [Header("Lagos")]
    [Tooltip("Umbral del ruido de cuenca [0,1]: valores de ruido por debajo generan lago.\n" +
             "0 = sin lagos · 0.40 = muchos lagos.")]
    [Range(0f, 0.5f)] public float lakeThreshold = 0.28f;

    [Tooltip("Elevación máxima sobre seaLevel hasta la que pueden formarse lagos (normalizada).\n" +
             "seaLevel + lakeMaxHeight define el límite superior de las cuencas lacustres.")]
    [Range(0.05f, 0.70f)] public float lakeMaxHeight = 0.40f;

    [Tooltip("Rango de fadeout del borde del lago (en elevación normalizada).\n" +
             "Evita el borde duro entre cuenca y terreno circundante.")]
    [Range(0.01f, 0.20f)] public float lakeSmoothing = 0.08f;

    // ── AGUA → HUMEDAD ────────────────────────────────────────────────────────
    [Header("Agua → Humedad")]
    [Tooltip("Bonus de humedad máximo para vértices sobre agua o en la costa.\n" +
             "Crea valles fértiles, bosques costeros y elimina desiertos junto al agua.")]
    [Range(0f, 1f)] public float waterHumidityBonus = 0.50f;

    [Tooltip("Radio del aura de humedad fluvial, en espacio de ridge [0..1].\n" +
             "0.10 = franja fina junto al cauce · 0.25 = valles fértiles anchos.")]
    [Range(0f, 0.35f)] public float riverHumidityRadius = 0.15f;

    // ── GRADIENTE LATITUDINAL (EJE Z) ────────────────────────────────────────
    [Header("Gradiente latitudinal  (eje Z)")]
    [Tooltip("Temperatura base en Z=0. 0.5 = zona templada (estilo Buenos Aires).")]
    [Range(0f, 1f)] public float baseTemperature = 0.50f;

    [Tooltip("Cambio de temperatura por unidad mundial de Z. " +
             "Positivo: Norte (Z+) = cálido. Negativo invertería el gradiente.\n" +
             "Con 0.002: ±500 unidades de Z cubren el rango completo 0→1.")]
    public float latitudeScale = 0.002f;

    // ── IMPACTO DE ALTITUD ────────────────────────────────────────────────────
    [Header("Impacto de altitud")]
    [Tooltip("Cuánta temperatura resta la altitud máxima (normalizedHeight=1).\n" +
             "0.55 → una cumbre en zona templada (base=0.50) llega a 0.50−0.55=−0.05 → clamp(0) = polar.")]
    [Range(0f, 1f)] public float altitudeCooling = 0.55f;

    // ── HUMEDAD ───────────────────────────────────────────────────────────────
    [Header("Humedad")]
    [Tooltip("Humedad base en todo el mapa antes del ruido orgánico.")]
    [Range(0f, 1f)] public float baseHumidity = 0.50f;

    // ── RUIDO CLIMÁTICO (Perlin secundario) ───────────────────────────────────
    [Header("Ruido climático  (Perlin secundario)")]
    [Tooltip("Escala del mapa de ruido de clima. Bajo = manchas grandes.")]
    public float climateNoiseScale = 0.008f;

    [Tooltip("Amplitud del ruido aplicado a temperatura y humedad (± este valor).")]
    [Range(0f, 0.5f)] public float climateNoiseStrength = 0.20f;

    [Tooltip("Offsets independientes para temperatura y humedad — evitan correlación entre ambos mapas.")]
    public Vector2 tempNoiseOffset     = new(0f,    0f);
    public Vector2 humidityNoiseOffset = new(500f, 300f);

    // ── MEZCLA DE BIOMAS ──────────────────────────────────────────────────────
    [Header("Mezcla de biomas")]
    [Tooltip("Radio en espacio climático (0..1) en el que los biomas se difuminan entre sí.\n" +
             "0.10 = transiciones rápidas. 0.25 = bordes muy suaves.")]
    [Range(0.01f, 0.4f)] public float blendRadius = 0.12f;

    // ── BIOMAS ────────────────────────────────────────────────────────────────
    [Header("Biomas")]
    [Tooltip("Lista de BiomeData. Orden no importa; el sistema evalúa distancia para todos en paralelo.")]
    public List<BiomeData> biomes = new();
}
