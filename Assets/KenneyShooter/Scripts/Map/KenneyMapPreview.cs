using System.IO;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace KenneyShooter
{
    /// <summary>
    /// Play-mode helper: load exported .map.json, frame the camera, pan/zoom to inspect.
    /// </summary>
    public class KenneyMapPreview : MonoBehaviour
    {
        /// <summary>EditorPrefs key set by Play Exported Map before entering Play.</summary>
        public const string PendingMapFilePrefKey = "KenneyShooter.PreviewMapFile";

        [SerializeField] private KenneyMapLoader loader;
        [SerializeField] private Camera previewCamera;
        [SerializeField] private float panSpeed = 24f;
        [SerializeField] private float zoomMin = 4f;
        [SerializeField] private float zoomMax = 80f;

        private string[] _maps = System.Array.Empty<string>();
        private Vector2 _scroll;
        private string _status = "";

        private void Start()
        {
            if (previewCamera == null)
                previewCamera = Camera.main;

            string pending = null;
#if UNITY_EDITOR
            pending = UnityEditor.EditorPrefs.GetString(PendingMapFilePrefKey, "");
            if (!string.IsNullOrEmpty(pending))
                UnityEditor.EditorPrefs.DeleteKey(PendingMapFilePrefKey);
#endif

            RefreshMapList();
            if (!string.IsNullOrEmpty(pending))
            {
                LoadMap(pending);
                return;
            }

            FrameLoadedMap();
            var data = loader != null ? loader.LastLoaded : null;
            _status = data != null
                ? string.Format("已加载 {0}  {1}×{2}", data.name, data.width, data.height)
                : "未加载到 JSON，请确认 StreamingAssets/Maps/";
        }

        private void Update()
        {
            if (previewCamera == null) return;
            float dt = Time.unscaledDeltaTime;
            float x = Input.GetAxisRaw("Horizontal");
            float y = Input.GetAxisRaw("Vertical");
            if (x != 0f || y != 0f)
            {
                float scale = previewCamera.orthographicSize * 0.15f;
                previewCamera.transform.position += new Vector3(x, y, 0f) * panSpeed * scale * dt;
            }

            float scroll = Input.mouseScrollDelta.y;
            if (scroll != 0f)
            {
                previewCamera.orthographicSize = Mathf.Clamp(
                    previewCamera.orthographicSize - scroll * 2.5f, zoomMin, zoomMax);
            }

            if (Input.GetKeyDown(KeyCode.F))
                FrameLoadedMap();
        }

        private void OnGUI()
        {
            const float width = 280f;
            GUILayout.BeginArea(new Rect(12, 12, width, Screen.height - 24), GUI.skin.box);
            GUILayout.Label("地图预览（本次：Export 所选 / StreamingAssets）");
            GUILayout.Label(_status);
            GUILayout.Label("WASD 平移  滚轮缩放  F 框选全图");

            if (GUILayout.Button("刷新文件列表"))
                RefreshMapList();

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(220));
            for (int i = 0; i < _maps.Length; i++)
            {
                string name = _maps[i];
                bool current = loader != null && name == loader.MapFileName;
                if (GUILayout.Button((current ? "▶ " : "   ") + name))
                    LoadMap(name);
            }
            GUILayout.EndScrollView();

            if (_maps.Length == 0)
                GUILayout.Label("没有 .json。先导出到 Export/ 再 Play Exported Map。");

            GUILayout.EndArea();
        }

        /// <summary>
        /// Reloads a JSON from StreamingAssets/Maps and frames the camera.
        /// </summary>
        public bool LoadMap(string fileName)
        {
            if (loader == null)
            {
                _status = "未挂 KenneyMapLoader";
                return false;
            }
            loader.MapFileName = fileName;
            bool ok = loader.LoadFromStreamingAssets(fileName);
            FrameLoadedMap();
            var data = loader.LastLoaded;
            int notes = data != null && data.cellNotes != null ? data.cellNotes.Count : 0;
            _status = ok && data != null
                ? string.Format("已加载 {0}  {1}×{2}  备注 {3}", data.name, data.width, data.height, notes)
                : "加载失败: " + fileName;
            return ok;
        }

        private void RefreshMapList()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, KenneyMapFormat.StreamingMapsFolder);
            if (!Directory.Exists(dir))
            {
                _maps = System.Array.Empty<string>();
                return;
            }
            var files = Directory.GetFiles(dir, "*.json");
            _maps = new string[files.Length];
            for (int i = 0; i < files.Length; i++)
                _maps[i] = Path.GetFileName(files[i]);
            System.Array.Sort(_maps);
        }

        private void FrameLoadedMap()
        {
            if (previewCamera == null) return;
            var root = GameObject.Find(KenneyMapFormat.RootObjectName);
            if (root == null) return;
            var maps = root.GetComponentsInChildren<Tilemap>();
            bool any = false;
            Vector3 min = Vector3.zero;
            Vector3 max = Vector3.zero;
            for (int i = 0; i < maps.Length; i++)
            {
                var map = maps[i];
                map.CompressBounds();
                if (map.cellBounds.size.x <= 0 || map.cellBounds.size.y <= 0)
                    continue;
                Vector3 wmin = map.CellToWorld(map.cellBounds.min);
                Vector3 wmax = map.CellToWorld(map.cellBounds.max);
                if (!any)
                {
                    min = wmin;
                    max = wmax;
                    any = true;
                }
                else
                {
                    min = Vector3.Min(min, wmin);
                    max = Vector3.Max(max, wmax);
                }
            }
            if (!any) return;
            Vector3 center = (min + max) * 0.5f;
            previewCamera.transform.position = new Vector3(center.x, center.y, -10f);
            float halfH = Mathf.Max(2f, (max.y - min.y) * 0.5f + 2f);
            float halfW = (max.x - min.x) * 0.5f + 2f;
            float aspect = Mathf.Max(0.01f, previewCamera.aspect);
            previewCamera.orthographicSize = Mathf.Clamp(Mathf.Max(halfH, halfW / aspect), zoomMin, zoomMax);
        }
    }
}
