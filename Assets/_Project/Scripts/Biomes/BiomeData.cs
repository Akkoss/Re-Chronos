using UnityEngine;

// Creá un asset vía: clic derecho en Project → Create → Re-Chronos → Biome Data
[CreateAssetMenu(fileName = "BiomeData", menuName = "Re-Chronos/Biome Data")]
public class BiomeData : ScriptableObject
{
    [Header("Identidad")]
    public string biomeName = "Nuevo Bioma";
    public Color  color     = Color.green;

    [Header("Temperatura  (0 = polar · 1 = tropical)")]
    [Range(0f, 1f)] public float minTemperature = 0f;
    [Range(0f, 1f)] public float maxTemperature = 1f;

    [Header("Humedad  (0 = árido · 1 = húmedo)")]
    [Range(0f, 1f)] public float minHumidity = 0f;
    [Range(0f, 1f)] public float maxHumidity = 1f;

    [Header("Elevación normalizada  (0 = valle · 1 = cima)")]
    [Range(0f, 1f)] public float minElevation = 0f;
    [Range(0f, 1f)] public float maxElevation = 1f;

    // Distancia euclídea del punto (elevation, temperature, humidity) al borde
    // de la caja de clima de este bioma.  Cero si el punto está dentro.
    public float DistanceToRegion(float elevation, float temperature, float humidity)
    {
        float dt = Mathf.Max(0f, Mathf.Max(minTemperature - temperature, temperature - maxTemperature));
        float dh = Mathf.Max(0f, Mathf.Max(minHumidity    - humidity,    humidity    - maxHumidity));
        float de = Mathf.Max(0f, Mathf.Max(minElevation   - elevation,   elevation   - maxElevation));
        return Mathf.Sqrt(dt * dt + dh * dh + de * de);
    }
}
