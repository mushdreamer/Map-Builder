using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace KenneyShooter
{
    /// <summary>
    /// Loads a designer-exported .map.json from StreamingAssets and rebuilds Tilemaps locally.
    /// </summary>
    public class KenneyMapLoader : MonoBehaviour
    {
        [SerializeField] private KenneyTileCatalog catalog;
        [SerializeField] private KenneyPlaceableCatalog placeableCatalog;
        [SerializeField] private string mapFileName = KenneyMapFormat.DefaultMapFile;
        [SerializeField] private string mapRootName = KenneyMapFormat.RootObjectName;
        [SerializeField] private bool loadOnAwake = true;
        [SerializeField] private bool clearExistingTiles = true;
        [Tooltip("If true, missing JSON keeps the scene-baked Tilemap instead of erroring.")]
        [SerializeField] private bool keepBakedMapIfMissing = true;

        public KenneyMapFile LastLoaded { get; private set; }

        public string MapFileName
        {
            get { return mapFileName; }
            set { mapFileName = value; }
        }

        private void Awake()
        {
            if (loadOnAwake)
                LoadConfiguredMap();
        }

        public bool LoadConfiguredMap()
        {
            return LoadFromStreamingAssets(mapFileName);
        }

        public bool LoadFromStreamingAssets(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                Debug.LogError("[KenneyMap] mapFileName is empty.");
                return false;
            }

            string path = Path.Combine(Application.streamingAssetsPath, KenneyMapFormat.StreamingMapsFolder, fileName);
            if (!File.Exists(path))
            {
                if (keepBakedMapIfMissing)
                {
                    Debug.LogWarning("[KenneyMap] Map file not found, keeping baked Tilemap: " + path);
                    return false;
                }
                Debug.LogError("[KenneyMap] Map file not found: " + path);
                return false;
            }

            string json = File.ReadAllText(path);
            return LoadFromJson(json, fileName);
        }

        public bool LoadFromJson(string json, string debugName = null)
        {
            if (catalog == null)
            {
                Debug.LogError("[KenneyMap] KenneyTileCatalog is not assigned.");
                return false;
            }

            KenneyMapFile data;
            try
            {
                data = JsonUtility.FromJson<KenneyMapFile>(json);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[KenneyMap] JSON parse failed: " + e.Message);
                return false;
            }

            if (data == null)
            {
                Debug.LogError("[KenneyMap] JSON parsed to null.");
                return false;
            }

            if (data.formatVersion != KenneyMapFormat.CurrentVersion)
            {
                Debug.LogWarning(string.Format(
                    "[KenneyMap] formatVersion={0}, expected={1}. Loading anyway.",
                    data.formatVersion, KenneyMapFormat.CurrentVersion));
            }

            ApplyToScene(data);
            LastLoaded = data;
            Debug.Log(string.Format(
                "[KenneyMap] Loaded '{0}' ({1}x{2}) from {3}",
                data.name, data.width, data.height, debugName ?? "json"));
            return true;
        }

        public void ApplyToScene(KenneyMapFile data)
        {
            catalog.RebuildLookup();

            var root = EnsureMapRoot(data.cellSize > 0f ? data.cellSize : 1f);
            var layerMaps = EnsureLayers(root);

            if (clearExistingTiles)
            {
                foreach (var kv in layerMaps)
                    kv.Value.ClearAllTiles();
            }

            int missing = 0;
            int placed = 0;
            if (data.layers != null)
            {
                for (int i = 0; i < data.layers.Count; i++)
                {
                    var layer = data.layers[i];
                    if (layer == null || string.IsNullOrEmpty(layer.name)) continue;
                    Tilemap map;
                    if (!layerMaps.TryGetValue(layer.name, out map))
                    {
                        map = CreateLayer(root.transform, layer.name, layer.sortingOrder, layer.hasCollider);
                        layerMaps[layer.name] = map;
                    }

                    if (layer.tiles == null) continue;
                    for (int t = 0; t < layer.tiles.Count; t++)
                    {
                        var cell = layer.tiles[t];
                        Tile tile;
                        if (!catalog.TryGet(cell.id, out tile) || tile == null)
                        {
                            missing++;
                            continue;
                        }
                        map.SetTile(new Vector3Int(cell.x, cell.y, 0), tile);
                        placed++;
                    }
                }
            }

            if (missing > 0)
                Debug.LogWarning("[KenneyMap] Missing tile assets for " + missing + " cells (check KenneyTileCatalog).");

            ApplyCellNotes(root, data);
            SpawnPlaceables(root, data, layerMaps);
            SpawnArtPlacements(root, data);

            Debug.Log("[KenneyMap] Placed tiles: " + placed);
        }

        private void SpawnArtPlacements(GameObject root, KenneyMapFile data)
        {
            Transform parent = EnsureChild(root.transform, "ArtPlacements");
            for (int i = parent.childCount - 1; i >= 0; i--)
                DestroyImmediate(parent.GetChild(i).gameObject);
            var store = root.GetComponent<KenneyArtPlacementStore>();
            if (store == null) store = root.AddComponent<KenneyArtPlacementStore>();
            store.ReplaceAll(data.placements);
            if (data.placements == null || placeableCatalog == null) return;
            for (int i = 0; i < data.placements.Count; i++)
            {
                var placement = data.placements[i];
                GameObject prefab;
                if (placement == null || !placeableCatalog.TryGetPrefab(placement.assetKey, out prefab))
                    continue;
                var go = Instantiate(prefab, placement.position,
                    Quaternion.Euler(0, 0, placement.rotationDeg), parent);
                go.name = placement.assetKey;
                go.transform.localScale = new Vector3(placement.scale.x, placement.scale.y, 1);
                var link = go.GetComponent<KenneyArtPlacementInstance>();
                if (link == null) link = go.AddComponent<KenneyArtPlacementInstance>();
                link.instanceId = placement.instanceId;
                link.assetKey = placement.assetKey;
                link.layerPath = placement.layerPath;
                link.confidence = placement.confidence;
                link.reviewState = placement.reviewState;
                link.noCollision = placement.noCollision;
                var renderers = go.GetComponentsInChildren<SpriteRenderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                    renderers[r].sortingOrder += placement.sortingOrder;
            }
        }

        /// <summary>
        /// Writes designer cell notes onto the map root.
        /// </summary>
        private static void ApplyCellNotes(GameObject root, KenneyMapFile data)
        {
            var store = root.GetComponent<KenneyCellNoteStore>();
            if (store == null)
                store = root.AddComponent<KenneyCellNoteStore>();
            store.ReplaceAll(data.cellNotes);
            int n = store.notes != null ? store.notes.Count : 0;
            if (n > 0)
                Debug.Log("[KenneyMap] Restored cell notes: " + n);
        }

        /// <summary>
        /// Replaces annotated footprints with prefabs from KenneyShooter/Prefabs (via placeable catalog).
        /// </summary>
        private void SpawnPlaceables(
            GameObject root, KenneyMapFile data, Dictionary<string, Tilemap> layerMaps)
        {
            Transform parent = EnsureChild(root.transform, "Placeables");
            for (int i = parent.childCount - 1; i >= 0; i--)
                DestroyImmediate(parent.GetChild(i).gameObject);

            if (data.cellNotes == null || data.cellNotes.Count == 0)
                return;
            if (placeableCatalog == null)
            {
                Debug.LogWarning("[KenneyMap] KenneyPlaceableCatalog is not assigned; skip prefab swap.");
                return;
            }

            var grid = root.GetComponent<Grid>();
            int spawned = 0;
            int missingPrefab = 0;
            for (int i = 0; i < data.cellNotes.Count; i++)
            {
                var note = data.cellNotes[i];
                if (note == null || string.IsNullOrEmpty(note.type))
                    continue;
                GameObject prefab;
                if (!placeableCatalog.TryGetPrefab(note.type, out prefab) || prefab == null)
                {
                    missingPrefab++;
                    continue;
                }

                ClearNoteFootprint(layerMaps, note);
                var go = Instantiate(prefab, FootprintCenter(grid, note), note.PrefabRotation(), parent);
                go.name = note.type;
                ApplyLayerSorting(go, note, layerMaps);
                spawned++;
            }

            if (spawned > 0)
                Debug.Log("[KenneyMap] Spawned placeables: " + spawned);
            if (missingPrefab > 0)
                Debug.LogWarning("[KenneyMap] Missing prefabs for " + missingPrefab
                    + " notes (put them in " + KenneyMapFormat.PlaceablePrefabFolder
                    + " and run Tools → Kenney → Map → Rebuild Placeable Catalog).");
        }

        private static void ClearNoteFootprint(Dictionary<string, Tilemap> layerMaps, KenneyCellNote note)
        {
            if (layerMaps == null || note == null || string.IsNullOrEmpty(note.layer))
                return;
            Tilemap map;
            if (!layerMaps.TryGetValue(note.layer, out map) || map == null)
                return;
            int w = Mathf.Max(1, note.width);
            int h = Mathf.Max(1, note.height);
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                    map.SetTile(new Vector3Int(note.x + x, note.y + y, 0), null);
            }
        }

        private static Vector3 FootprintCenter(Grid grid, KenneyCellNote note)
        {
            int w = Mathf.Max(1, note.width);
            int h = Mathf.Max(1, note.height);
            if (grid == null)
                return new Vector3(note.x + w * 0.5f, note.y + h * 0.5f, 0f);
            Vector3 origin = grid.CellToWorld(new Vector3Int(note.x, note.y, 0));
            Vector3 cell = grid.cellSize;
            return origin + new Vector3(cell.x * w * 0.5f, cell.y * h * 0.5f, 0f);
        }

        private static void ApplyLayerSorting(
            GameObject go, KenneyCellNote note, Dictionary<string, Tilemap> layerMaps)
        {
            if (go == null || layerMaps == null) return;
            int order = 0;
            Tilemap map;
            if (!string.IsNullOrEmpty(note.layer) && layerMaps.TryGetValue(note.layer, out map) && map != null)
            {
                var r = map.GetComponent<TilemapRenderer>();
                if (r != null) order = r.sortingOrder;
            }
            var renderers = go.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].sortingOrder = order;
        }

        private static Transform EnsureChild(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t != null) return t;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private GameObject EnsureMapRoot(float cellSize)
        {
            var root = GameObject.Find(mapRootName);
            if (root == null)
            {
                root = new GameObject(mapRootName);
                var grid = root.AddComponent<Grid>();
                grid.cellSize = new Vector3(cellSize, cellSize, 0f);
            }
            else
            {
                var grid = root.GetComponent<Grid>();
                if (grid == null) grid = root.AddComponent<Grid>();
                grid.cellSize = new Vector3(cellSize, cellSize, 0f);
            }
            return root;
        }

        private Dictionary<string, Tilemap> EnsureLayers(GameObject root)
        {
            var result = new Dictionary<string, Tilemap>();
            for (int i = 0; i < KenneyMapFormat.LayerOrder.Length; i++)
            {
                string name = KenneyMapFormat.LayerOrder[i];
                var existing = root.transform.Find(name);
                Tilemap map;
                if (existing != null)
                    map = existing.GetComponent<Tilemap>();
                else
                    map = null;

                if (map == null)
                {
                    map = CreateLayer(
                        root.transform,
                        name,
                        KenneyMapFormat.LayerSorting[i],
                        KenneyMapFormat.LayerShouldCollide(name));
                }
                else
                {
                    var r = map.GetComponent<TilemapRenderer>();
                    if (r != null) r.sortingOrder = KenneyMapFormat.LayerSorting[i];
                    if (KenneyMapFormat.LayerShouldCollide(name) && map.GetComponent<TilemapCollider2D>() == null)
                        map.gameObject.AddComponent<TilemapCollider2D>();
                }

                result[name] = map;
            }
            return result;
        }

        private static Tilemap CreateLayer(Transform parent, string name, int sortingOrder, bool collider)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var map = go.AddComponent<Tilemap>();
            var renderer = go.AddComponent<TilemapRenderer>();
            renderer.sortingOrder = sortingOrder;
            if (collider)
                go.AddComponent<TilemapCollider2D>();
            return map;
        }
    }
}
