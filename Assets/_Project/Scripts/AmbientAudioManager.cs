using UnityEngine;

public class AmbientAudioManager : MonoBehaviour
{
    [Header("Clips")]
    [SerializeField] private AudioClip dayAmbient;    // pájaros, naturaleza diurna
    [SerializeField] private AudioClip nightAmbient;  // grillos, búhos
    [SerializeField] private AudioClip windAmbient;   // viento continuo

    [Header("Volumen")]
    [SerializeField] [Range(0f, 1f)]    private float masterVolume    = 0.80f;
    [SerializeField] [Range(0f, 1f)]    private float windBaseVolume  = 0.25f;
    [SerializeField] [Range(0f, 0.5f)]  private float windGustStrength = 0.12f;
    [SerializeField]                    private float windGustSpeed    = 0.35f; // ciclos/s
    [SerializeField]                    private float fadeSpeed        = 1.5f;  // unidades/s en MoveTowards

    [Header("Curvas día/noche")]
    [SerializeField] private AnimationCurve dayVolumeCurve;
    [SerializeField] private AnimationCurve nightVolumeCurve;

    [Header("Referencias")]
    [SerializeField] private DayNightCycle dayNight; // null → busca automáticamente en Start

    private AudioSource _daySource;
    private AudioSource _nightSource;
    private AudioSource _windSource;

    private void Awake()
    {
        _daySource   = MakeSource(dayAmbient);
        _nightSource = MakeSource(nightAmbient);
        _windSource  = MakeSource(windAmbient);
    }

    private void Start()
    {
        if (dayNight == null)
            dayNight = FindObjectOfType<DayNightCycle>();
    }

    private void Update()
    {
        float t = dayNight != null ? dayNight.TimeOfDay : 0.5f;

        SmoothedVolume(_daySource,   dayVolumeCurve.Evaluate(t)   * masterVolume);
        SmoothedVolume(_nightSource, nightVolumeCurve.Evaluate(t) * masterVolume);

        if (_windSource != null)
        {
            float gust = (Mathf.Sin(Time.time * windGustSpeed) * 0.5f + 0.5f) * windGustStrength;
            _windSource.volume = masterVolume * (windBaseVolume + gust);
        }
    }

    private void SmoothedVolume(AudioSource src, float target)
    {
        if (src == null) return;
        src.volume = Mathf.MoveTowards(src.volume, target, fadeSpeed * Time.deltaTime);
    }

    // Crea un AudioSource 2D (ambiente global), con volumen inicial 0, y lo pone en loop.
    // Devuelve null si el clip no está asignado para no crear componentes vacíos.
    private AudioSource MakeSource(AudioClip clip)
    {
        if (clip == null) return null;
        var src = gameObject.AddComponent<AudioSource>();
        src.clip         = clip;
        src.loop         = true;
        src.playOnAwake  = false;
        src.spatialBlend = 0f; // 2D — no tiene posición en el mundo
        src.volume       = 0f;
        src.Play();
        return src;
    }

    // Reset() pre-carga curvas por defecto al añadir el componente en el Editor.
    private void Reset()
    {
        // Día: silencio de noche, volumen máximo de 0.28 a 0.72 (amanecer → mediodía → atardecer)
        dayVolumeCurve = new AnimationCurve(
            new Keyframe(0.00f, 0f, 0f, 0f),
            new Keyframe(0.20f, 0f),
            new Keyframe(0.28f, 1f),
            new Keyframe(0.72f, 1f),
            new Keyframe(0.80f, 0f),
            new Keyframe(1.00f, 0f, 0f, 0f));

        // Noche: volumen máximo de 0.85 a 0.20, silencio durante el día
        nightVolumeCurve = new AnimationCurve(
            new Keyframe(0.00f, 1f),
            new Keyframe(0.22f, 0f),
            new Keyframe(0.78f, 0f),
            new Keyframe(0.85f, 1f),
            new Keyframe(1.00f, 1f));
    }
}
