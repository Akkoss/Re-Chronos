using System.Collections.Generic;
using UnityEngine;

// Un nivel de LOD: los chunks a distancia Chebyshev <= distanceThreshold usan meshStep.
// El array en ProceduralTerrain debe estar ordenado por distanceThreshold ascendente.
// meshStep debe dividir exactamente chunkSize (ej. chunkSize=50: steps válidos 1,2,5,10,25).
[System.Serializable]
public struct LODLevel
{
    [Tooltip("Distancia Chebyshev máxima a la que aplica este nivel")]
    public int distanceThreshold;
    [Range(1, 25)]
    [Tooltip("Paso de vértices: 1=full, 2=mitad, 5=quinta parte")]
    public int meshStep;
    [Tooltip("Mostrar árboles y pasto en este nivel")]
    public bool showVegetation;
}

public class ProceduralTerrain : MonoBehaviour
{
    [Header("Chunks")]
    [SerializeField] private int chunkSize = 50;
    [SerializeField] [Range(1, 8)] private int viewDistance = 3;
    [SerializeField] private Transform viewer;
    [SerializeField] private Material  terrainMaterial;

    [Header("Preset de terreno")]
    [SerializeField] private TerrainPreset preset;

    [Header("Vegetación")]
    [SerializeField] private VegetationSettings vegetation = new();

    [Header("Pasto")]
    [SerializeField] private GrassSettings grass = new();

    [Header("LOD")]
    // Ordenar por distanceThreshold ascendente.
    // Con chunkSize=50 los steps válidos son: 1, 2, 5, 10, 25.
    [SerializeField] private LODLevel[] lodLevels =
    {
        new() { distanceThreshold = 1, meshStep = 1, showVegetation = true  },
        new() { distanceThreshold = 8, meshStep = 2, showVegetation = false },
    };

    [Header("Chunk Pool")]
    // Chunks dentro de viewDistance: visibles.
    // Chunks dentro de viewDistance+unloadBuffer: caché invisible (malla en memoria, sin rebuild).
    // Chunks más allá de viewDistance+unloadBuffer: destruidos.
    [SerializeField] [Range(0, 6)] private int unloadBuffer = 2;

    private readonly Dictionary<Vector2Int, TerrainChunk> _chunks = new();
    private Vector2Int _lastViewerChunk;
    private bool       _initialized;

    // ──────────────────────────────────────────────────────────────────────
    // CICLO DE VIDA
    // ──────────────────────────────────────────────────────────────────────

    private void Start()
    {
        if (viewer == null) viewer = Camera.main?.transform;
        if (viewer == null)
        {
            Debug.LogWarning("[ProceduralTerrain] Viewer no asignado y no hay Main Camera.");
            return;
        }
        if (preset == null)
        {
            Debug.LogError("[ProceduralTerrain] TerrainPreset no asignado. " +
                           "Creá un asset vía Create → Re-Chronos → Terrain Preset.");
            return;
        }
        UpdateVisibleChunks();
    }

    private void Update()
    {
        if (viewer == null || preset == null) return;
        Vector2Int current = ToChunkCoord(viewer.position);
        if (_initialized && current == _lastViewerChunk) return;
        UpdateVisibleChunks();
    }

    // ──────────────────────────────────────────────────────────────────────
    // ACTUALIZACIÓN DE CHUNKS VISIBLES
    // ──────────────────────────────────────────────────────────────────────

    private void UpdateVisibleChunks()
    {
        foreach (TerrainChunk chunk in _chunks.Values)
            chunk.SetVisible(false);

        Vector2Int center = ToChunkCoord(viewer.position);
        _lastViewerChunk = center;
        _initialized     = true;

        for (int dz = -viewDistance; dz <= viewDistance; dz++)
        for (int dx = -viewDistance; dx <= viewDistance; dx++)
        {
            var coord = new Vector2Int(center.x + dx, center.y + dz);
            int dist  = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));
            LODLevel lod = GetLOD(dist);

            if (!_chunks.TryGetValue(coord, out TerrainChunk chunk))
            {
                chunk = new TerrainChunk(coord, chunkSize, transform, terrainMaterial,
                                         preset, vegetation, grass,
                                         lod.meshStep, lod.showVegetation);
                _chunks[coord] = chunk;
            }
            else
            {
                chunk.UpdateLOD(lod.meshStep, lod.showVegetation);
            }

            chunk.SetVisible(true);
        }

        EvictDistantChunks(center);
    }

    // ──────────────────────────────────────────────────────────────────────
    // REGENERAR TODO
    // ──────────────────────────────────────────────────────────────────────

    [ContextMenu("Regenerate All Chunks")]
    public void RegenerateAll()
    {
        foreach (TerrainChunk chunk in _chunks.Values)
            chunk.Destroy();
        _chunks.Clear();
        PurgeChildren();
        _initialized = false;
        if (viewer == null) viewer = Camera.main?.transform;
        if (viewer != null && preset != null) UpdateVisibleChunks();
    }

    private void PurgeChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Object.Destroy(child);
            else Object.DestroyImmediate(child);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // UTILIDADES
    // ──────────────────────────────────────────────────────────────────────

    // Destruye los chunks más allá del buffer de descarga.
    private void EvictDistantChunks(Vector2Int center)
    {
        int maxDist  = viewDistance + unloadBuffer;
        var toRemove = new List<Vector2Int>();

        foreach (var coord in _chunks.Keys)
        {
            int dist = Mathf.Max(
                Mathf.Abs(coord.x - center.x),
                Mathf.Abs(coord.y - center.y));
            if (dist > maxDist) toRemove.Add(coord);
        }

        foreach (var coord in toRemove)
        {
            _chunks[coord].Destroy();
            _chunks.Remove(coord);
        }
    }

    private LODLevel GetLOD(int dist)
    {
        for (int i = 0; i < lodLevels.Length; i++)
            if (dist <= lodLevels[i].distanceThreshold) return lodLevels[i];
        return lodLevels.Length > 0
            ? lodLevels[^1]
            : new LODLevel { meshStep = 1, showVegetation = false };
    }

    private Vector2Int ToChunkCoord(Vector3 pos) =>
        new Vector2Int(
            Mathf.FloorToInt(pos.x / chunkSize),
            Mathf.FloorToInt(pos.z / chunkSize));
}
