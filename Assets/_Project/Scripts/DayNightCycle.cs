using UnityEngine;

public class DayNightCycle : MonoBehaviour
{
    [Header("Time")]
    [SerializeField] private float dayDuration = 120f;          // segundos por ciclo completo
    [SerializeField] [Range(0f, 1f)] private float timeOfDay = 0.25f; // 0=medianoche, 0.25=amanecer, 0.5=mediodía
    [SerializeField] private bool paused;

    [Header("Sun")]
    [SerializeField] private Light sun;
    [SerializeField] private float sunYaw = -30f;               // inclinación N/S de la trayectoria solar
    [SerializeField] private Gradient sunColor;
    [SerializeField] private AnimationCurve sunIntensity;

    [Header("Ambient")]
    [SerializeField] private Gradient ambientColor;

    [Header("Fog")]
    [SerializeField] private bool controlFog = true;
    [SerializeField] private Gradient fogColor;
    [SerializeField] private AnimationCurve fogDensity;

    public float TimeOfDay => timeOfDay;

    private float _giTimer;
    private const float GI_INTERVAL = 1f; // DynamicGI es costoso; actualizamos 1 vez/segundo

    private void Update()
    {
        if (!paused)
            timeOfDay = (timeOfDay + Time.deltaTime / dayDuration) % 1f;

        if (sun != null)
        {
            // Rotación: t=0.25 → sol en horizonte E, t=0.5 → cénit, t=0.75 → horizonte O
            sun.transform.rotation = Quaternion.Euler(timeOfDay * 360f - 90f, sunYaw, 0f);
            sun.color     = sunColor.Evaluate(timeOfDay);
            sun.intensity = Mathf.Max(0f, sunIntensity.Evaluate(timeOfDay));
        }

        RenderSettings.ambientLight = ambientColor.Evaluate(timeOfDay);

        if (controlFog)
        {
            RenderSettings.fogColor   = fogColor.Evaluate(timeOfDay);
            RenderSettings.fogDensity = Mathf.Max(0f, fogDensity.Evaluate(timeOfDay));
        }

        _giTimer += Time.deltaTime;
        if (_giTimer >= GI_INTERVAL) { _giTimer = 0f; DynamicGI.UpdateEnvironment(); }
    }

    // Atajos en el Inspector para saltar a momentos clave del día.
    [ContextMenu("Set Sunrise")] private void SetSunrise() => timeOfDay = 0.25f;
    [ContextMenu("Set Noon")]    private void SetNoon()    => timeOfDay = 0.50f;
    [ContextMenu("Set Sunset")]  private void SetSunset()  => timeOfDay = 0.75f;
    [ContextMenu("Set Midnight")]private void SetMidnight()=> timeOfDay = 0.00f;

    // Reset() es llamado por Unity al añadir el componente en el Editor.
    // Establece valores por defecto para que el ciclo funcione sin configuración manual.
    private void Reset()
    {
        sunIntensity = new AnimationCurve(
            new Keyframe(0f,    0f,    0f, 0f),
            new Keyframe(0.22f, 0f,    0f, 0f),
            new Keyframe(0.27f, 0.6f),
            new Keyframe(0.50f, 1.2f),
            new Keyframe(0.73f, 0.6f),
            new Keyframe(0.78f, 0f,    0f, 0f),
            new Keyframe(1f,    0f,    0f, 0f));

        fogDensity = new AnimationCurve(
            new Keyframe(0f,    0.008f),
            new Keyframe(0.25f, 0.005f),
            new Keyframe(0.50f, 0.003f),
            new Keyframe(0.75f, 0.005f),
            new Keyframe(1f,    0.008f));

        sunColor = MakeGradient(
            (0.20f, new Color(0.95f, 0.40f, 0.10f)),  // amanecer naranja
            (0.28f, Color.white),                       // día blanco
            (0.72f, Color.white),
            (0.80f, new Color(0.95f, 0.40f, 0.10f)),  // atardecer naranja
            (0.92f, new Color(0.10f, 0.10f, 0.30f))); // noche azul

        ambientColor = MakeGradient(
            (0.00f, new Color(0.04f, 0.04f, 0.12f)),
            (0.22f, new Color(0.30f, 0.22f, 0.38f)),
            (0.27f, new Color(0.50f, 0.52f, 0.62f)),
            (0.50f, new Color(0.62f, 0.68f, 0.76f)),
            (0.73f, new Color(0.52f, 0.42f, 0.42f)),
            (0.78f, new Color(0.30f, 0.22f, 0.38f)),
            (1.00f, new Color(0.04f, 0.04f, 0.12f)));

        fogColor = MakeGradient(
            (0.00f, new Color(0.04f, 0.04f, 0.10f)),
            (0.25f, new Color(0.70f, 0.55f, 0.40f)),
            (0.50f, new Color(0.75f, 0.82f, 0.90f)),
            (0.75f, new Color(0.70f, 0.40f, 0.30f)),
            (1.00f, new Color(0.04f, 0.04f, 0.10f)));
    }

    private static Gradient MakeGradient(params (float t, Color c)[] keys)
    {
        var colorKeys = System.Array.ConvertAll(keys, k => new GradientColorKey(k.c, k.t));
        var g = new Gradient();
        g.SetKeys(colorKeys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }
}
