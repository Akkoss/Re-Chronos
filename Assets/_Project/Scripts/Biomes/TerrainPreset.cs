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
