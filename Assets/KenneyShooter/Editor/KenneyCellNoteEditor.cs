using KenneyShooter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Tilemaps;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Lets designers annotate the Grid Selection cell: footprint, type (prefab key), description.
/// Anchor is the selection min cell (bottom-left). Menu: Tools → Kenney → Map → Cell Notes.
/// </summary>
[InitializeOnLoad]
public static class KenneyCellNoteInspector
{
    static string _layer = "";
    static int _anchorX;
    static int _anchorY;
    static int _cols = 1;
    static int _rows = 1;
    static int _rotation;
    static int _opening;
    static string _type = "";
    static string _description = "";
    static readonly int[] OpeningValues = { 0, 90, 180, 270 };
    static readonly string[] OpeningLabels =
    {
        "上",
        "左",
        "下",
        "右"
    };
    const string CatalogPath = KenneyMapFormat.PlaceableCatalogAssetPath;
    const string RotationHelp =
        "地编只填地图占地和开口方向（看摆上去的图）。\n" +
        "Prefab 带原画列×行和原画开口（沙发：3×1，开口朝上）。\n" +
        "旋转自动算：世界开口 − 原画开口。改开口若转了 90°，列行会对换。\n" +
        "填类型并保存后会登记原画；也可在 Prefab 上挂 KenneyPlaceable。";
    static bool _hasSelection;
    static bool _hasSaved;

    static KenneyCellNoteInspector()
    {
        Editor.finishedDefaultHeaderGUI += OnInspectorHeader;
        SceneView.duringSceneGui += OnSceneGUI;
        GridSelection.gridSelectionChanged += OnGridSelectionChanged;
        GridPaintingState.scenePaintTargetChanged += OnPaintTargetChanged;
        OnGridSelectionChanged();
    }

    [MenuItem("Tools/Kenney/Map/Cell Notes")]
    public static void OpenWindow()
    {
        KenneyCellNoteWindow.Open();
    }

    static void OnPaintTargetChanged(GameObject _)
    {
        OnGridSelectionChanged();
        KenneyCellNoteWindow.RepaintIfOpen();
    }

    static void OnGridSelectionChanged()
    {
        bool wasSelected = _hasSelection;
        int prevX = _anchorX;
        int prevY = _anchorY;
        string prevLayer = _layer;

        _hasSelection = GridSelection.active && GridSelection.grid != null;
        if (!_hasSelection)
        {
            KenneyCellNoteWindow.RepaintIfOpen();
            SceneView.RepaintAll();
            return;
        }

        var pos = GridSelection.position;
        string newLayer = ResolveLayerName();
        int newX = pos.xMin;
        int newY = pos.yMin;
        bool sameAnchor = wasSelected
            && newX == prevX && newY == prevY
            && string.Equals(newLayer, prevLayer, System.StringComparison.Ordinal);

        _anchorX = newX;
        _anchorY = newY;
        _layer = newLayer;

        var store = FindOrCreateStore(false);
        KenneyCellNote saved = null;
        bool found = store != null && store.TryGet(_layer, _anchorX, _anchorY, out saved) && saved != null;
        _hasSaved = found;

        if (!sameAnchor)
        {
            if (found)
            {
                _cols = Mathf.Max(1, saved.width);
                _rows = Mathf.Max(1, saved.height);
                _rotation = KenneyCellNote.NormalizeRotation(saved.rotation);
                _type = saved.type ?? "";
                _opening = KenneyCellNote.NormalizeRotation(saved.opening);
                if (_opening == 0 && _rotation != 0 && ResolveNativeOpening() == 0)
                    _opening = _rotation;
                else
                    _rotation = KenneyCellNote.RotationFromOpening(ResolveNativeOpening(), _opening);
                _description = saved.description ?? "";
            }
            else
            {
                _cols = Mathf.Max(1, pos.size.x);
                _rows = Mathf.Max(1, pos.size.y);
                _rotation = 0;
                _opening = 0;
                _type = "";
                _description = "";
            }
        }

        KenneyCellNoteWindow.RepaintIfOpen();
        SceneView.RepaintAll();
    }

    static void OnInspectorHeader(Editor editor)
    {
        if (!IsGridSelectionEditor(editor)) return;
        DrawNotePanel();
    }

    static bool IsGridSelectionEditor(Editor editor)
    {
        if (!GridSelection.active || editor == null || editor.target == null)
            return false;
        return editor.target is GridSelection
            || editor.GetType().Name == "GridSelectionEditor";
    }

    internal static void DrawNotePanel()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("单元格备注", EditorStyles.boldLabel);

        if (!_hasSelection)
        {
            EditorGUILayout.HelpBox(
                "用 Tile Palette 的 Select 工具点中某一层的格子，再在这里填写备注。",
                MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField("层", string.IsNullOrEmpty(_layer) ? "(未选中 Tilemap 层)" : _layer);
        EditorGUILayout.LabelField("锚点（左下）", string.Format("({0}, {1})", _anchorX, _anchorY));

        EditorGUI.BeginChangeCheck();
        int prevCols = _cols;
        int prevRows = _rows;
        int prevOpening = _opening;
        string prevType = _type ?? "";
        _cols = Mathf.Max(1, EditorGUILayout.IntField("列（宽）", _cols));
        _rows = Mathf.Max(1, EditorGUILayout.IntField("行（高）", _rows));
        _opening = EditorGUILayout.IntPopup("开口（看摆好的图）", _opening, OpeningLabels, OpeningValues);
        EditorGUILayout.LabelField("旋转（自动）", _rotation + "°");
        _type = EditorGUILayout.TextField("类型（Prefab 名）", _type ?? "");
        DrawNativeSizeHint();
        if (EditorGUI.EndChangeCheck())
        {
            bool typeChanged = !string.Equals(_type ?? "", prevType, System.StringComparison.Ordinal);
            if (_opening != prevOpening)
                ApplyOpeningToRotation();
            else if (_cols != prevCols || _rows != prevRows)
                TryAutoRotationFromSize(prevCols, prevRows, _rotation);
            else if (typeChanged)
                ApplyOpeningToRotation();
            SceneView.RepaintAll();
        }

        EditorGUILayout.HelpBox(RotationHelp, MessageType.Info);

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.LabelField("描述");
        _description = EditorGUILayout.TextArea(_description ?? "", GUILayout.MinHeight(48));
        if (EditorGUI.EndChangeCheck())
            SceneView.RepaintAll();

        if (GridSelection.position.size.x > 1 || GridSelection.position.size.y > 1)
        {
            if (GUILayout.Button("用当前选区作为占地"))
            {
                int prevC = _cols;
                int prevR = _rows;
                int prevRotSel = _rotation;
                _cols = Mathf.Max(1, GridSelection.position.size.x);
                _rows = Mathf.Max(1, GridSelection.position.size.y);
                TryAutoRotationFromSize(prevC, prevR, prevRotSel);
                SceneView.RepaintAll();
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = !string.IsNullOrEmpty(_layer);
            if (GUILayout.Button(_hasSaved ? "更新备注" : "保存备注"))
                SaveCurrent();
            GUI.enabled = _hasSaved;
            if (GUILayout.Button("删除备注"))
                DeleteCurrent();
            GUI.enabled = true;
        }

        if (_hasSaved)
            EditorGUILayout.HelpBox("此锚点已有备注。", MessageType.None);
    }

    static void SaveCurrent()
    {
        if (string.IsNullOrEmpty(_layer))
        {
            EditorUtility.DisplayDialog("单元格备注", "请先在 Hierarchy 里选中 Tilemap 层（Ground / Floor / Walls / Decor / Props）。", "OK");
            return;
        }

        var store = FindOrCreateStore(true);
        if (store == null) return;

        _rotation = KenneyCellNote.RotationFromOpening(ResolveNativeOpening(), _opening);

        var note = new KenneyCellNote
        {
            layer = _layer,
            x = _anchorX,
            y = _anchorY,
            width = Mathf.Max(1, _cols),
            height = Mathf.Max(1, _rows),
            rotation = KenneyCellNote.NormalizeRotation(_rotation),
            opening = KenneyCellNote.NormalizeRotation(_opening),
            type = _type ?? "",
            description = _description ?? ""
        };

        Undo.RecordObject(store, "Save Cell Note");
        store.Upsert(note);
        RememberNativeSize(note);
        _hasSaved = true;
        MarkDirty(store);
    }

    static void DrawNativeSizeHint()
    {
        var catalog = FindOrCreateCatalog(false);
        int nw, nh, nOpen;
        if (catalog != null && catalog.TryGetNative(_type, out nw, out nh, out nOpen))
            EditorGUILayout.LabelField("Prefab 原画",
                string.Format("{0} 列 × {1} 行，开口朝{2}", nw, nh, OpeningName(nOpen)));
        else
            EditorGUILayout.LabelField("Prefab 原画", "未登记（填类型并保存后自动记住，默认开口朝上）");
    }

    public static string OpeningName(int opening)
    {
        switch (KenneyCellNote.NormalizeRotation(opening))
        {
            case 90: return "左";
            case 180: return "下";
            case 270: return "右";
            default: return "上";
        }
    }

    static void ApplyOpeningToRotation()
    {
        int prevRot = _rotation;
        _rotation = KenneyCellNote.RotationFromOpening(ResolveNativeOpening(), _opening);
        KenneyCellNote.SwapSizeIfRotationTurnsAxes(ref _cols, ref _rows, prevRot, _rotation);
    }

    static int ResolveNativeOpening()
    {
        var catalog = FindOrCreateCatalog(false);
        int nw, nh, nOpen;
        if (catalog != null && catalog.TryGetNative(_type, out nw, out nh, out nOpen))
            return nOpen;
        return 0;
    }

    static void TryAutoRotationFromSize(int prevCols, int prevRows, int prevRot)
    {
        int nw, nh;
        if (!TryResolveNative(prevCols, prevRows, prevRot, out nw, out nh))
            return;
        int rot;
        if (KenneyCellNote.TryRotationFromFootprint(nw, nh, _cols, _rows, prevRot, out rot))
        {
            _rotation = rot;
            _opening = KenneyCellNote.OpeningFromRotation(ResolveNativeOpening(), _rotation);
        }
    }

    static bool TryResolveNative(int worldW, int worldH, int worldRot, out int nativeW, out int nativeH)
    {
        var catalog = FindOrCreateCatalog(false);
        if (catalog != null && catalog.TryGetNative(_type, out nativeW, out nativeH))
            return true;
        KenneyCellNote.NativeFromWorld(worldW, worldH, worldRot, out nativeW, out nativeH);
        return true;
    }

    static void RememberNativeSize(KenneyCellNote note)
    {
        if (note == null || string.IsNullOrEmpty(note.type)) return;
        int nw, nh;
        KenneyCellNote.NativeFromWorld(note.width, note.height, note.rotation, out nw, out nh);
        int nativeOpening = KenneyCellNote.NormalizeRotation(note.opening - note.rotation);
        var catalog = FindOrCreateCatalog(true);
        if (catalog == null) return;
        Undo.RecordObject(catalog, "Register Placeable Native Size");
        catalog.UpsertNative(note.type, nw, nh, nativeOpening);
        EditorUtility.SetDirty(catalog);
    }

    static KenneyPlaceableCatalog FindOrCreateCatalog(bool create)
    {
        var catalog = AssetDatabase.LoadAssetAtPath<KenneyPlaceableCatalog>(CatalogPath);
        if (catalog != null || !create) return catalog;
        EnsureFolder("Assets/KenneyShooter/Config");
        catalog = ScriptableObject.CreateInstance<KenneyPlaceableCatalog>();
        AssetDatabase.CreateAsset(catalog, CatalogPath);
        AssetDatabase.SaveAssets();
        return catalog;
    }

    static void EnsureFolder(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath)) return;
        var parts = assetPath.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }

    static void DeleteCurrent()
    {
        var store = FindOrCreateStore(false);
        if (store == null) return;
        Undo.RecordObject(store, "Delete Cell Note");
        if (!store.Remove(_layer, _anchorX, _anchorY)) return;
        _hasSaved = false;
        _rotation = 0;
        _opening = 0;
        _type = "";
        _description = "";
        MarkDirty(store);
    }

    static void MarkDirty(KenneyCellNoteStore store)
    {
        EditorUtility.SetDirty(store);
        if (store.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(store.gameObject.scene);
        OnGridSelectionChanged();
    }

    internal static KenneyCellNoteStore FindOrCreateStore(bool create)
    {
        var root = GameObject.Find(KenneyMapFormat.RootObjectName);
        if (root == null) return null;
        var store = root.GetComponent<KenneyCellNoteStore>();
        if (store == null && create)
        {
            Undo.AddComponent<KenneyCellNoteStore>(root);
            store = root.GetComponent<KenneyCellNoteStore>();
            EditorSceneManager.MarkSceneDirty(root.scene);
        }
        return store;
    }

    static string ResolveLayerName()
    {
        var selTarget = GridSelection.target;
        if (selTarget != null)
        {
            var map = selTarget.GetComponent<Tilemap>();
            if (map != null)
                return map.gameObject.name;
        }

        var paint = GridPaintingState.scenePaintTarget;
        if (paint != null && paint.GetComponent<Tilemap>() != null)
            return paint.name;

        if (Selection.activeGameObject != null)
        {
            var map = Selection.activeGameObject.GetComponent<Tilemap>();
            if (map != null) return map.gameObject.name;
        }

        return "";
    }

    static void OnSceneGUI(SceneView _)
    {
        var store = FindOrCreateStore(false);
        GridLayout grid = GridSelection.grid;
        if (grid == null && store != null)
            grid = store.GetComponent<Grid>();
        if (grid == null) return;

        if (store != null && store.notes != null)
        {
            for (int i = 0; i < store.notes.Count; i++)
            {
                var n = store.notes[i];
                if (n == null) continue;
                bool isCurrent = _hasSelection && n.MatchesAnchor(_layer, _anchorX, _anchorY);
                if (isCurrent) continue;
                DrawFootprint(grid, n.x, n.y, n.width, n.height, n.rotation,
                    new Color(1f, 0.85f, 0.2f, 0.12f), new Color(1f, 0.75f, 0.1f, 0.9f), n.type);
            }
        }

        if (_hasSelection)
        {
            DrawFootprint(grid, _anchorX, _anchorY, _cols, _rows, _rotation,
                new Color(0.2f, 0.85f, 1f, 0.18f), new Color(0.1f, 0.7f, 1f, 1f),
                string.IsNullOrEmpty(_type) ? "anchor" : _type);
        }
    }

    static void DrawFootprint(
        GridLayout grid, int x, int y, int w, int h, int rotation, Color fill, Color outline, string label)
    {
        w = Mathf.Max(1, w);
        h = Mathf.Max(1, h);
        rotation = KenneyCellNote.NormalizeRotation(rotation);
        Vector3 origin = grid.CellToWorld(new Vector3Int(x, y, 0));
        Vector3 cell = grid.cellSize;
        Vector3 size = new Vector3(cell.x * w, cell.y * h, 0f);
        var verts = new[]
        {
            origin,
            origin + new Vector3(size.x, 0f, 0f),
            origin + new Vector3(size.x, size.y, 0f),
            origin + new Vector3(0f, size.y, 0f)
        };
        Handles.DrawSolidRectangleWithOutline(verts, fill, outline);
        Vector3 center = origin + size * 0.5f;
        Handles.color = outline;
        Handles.DotHandleCap(0, origin + new Vector3(cell.x * 0.5f, cell.y * 0.5f, 0f),
            Quaternion.identity, 0.12f, Event.current.type);
        Vector3 dir = Quaternion.Euler(0f, 0f, rotation) * Vector3.up;
        Handles.DrawLine(center, center + dir * 0.55f);
        if (!string.IsNullOrEmpty(label))
            Handles.Label(center, rotation == 0 ? label : label + "  " + rotation + "°");
    }
}

public class KenneyCellNoteWindow : EditorWindow
{
    public static void Open()
    {
        var w = GetWindow<KenneyCellNoteWindow>(false, "Cell Notes", true);
        w.minSize = new Vector2(280, 220);
        w.Show();
    }

    public static void RepaintIfOpen()
    {
        var windows = Resources.FindObjectsOfTypeAll<KenneyCellNoteWindow>();
        for (int i = 0; i < windows.Length; i++)
            windows[i].Repaint();
    }

    void OnGUI()
    {
        KenneyCellNoteInspector.DrawNotePanel();
    }
}

[CustomEditor(typeof(KenneyCellNoteStore))]
public class KenneyCellNoteStoreEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var store = (KenneyCellNoteStore)target;
        EditorGUILayout.HelpBox(
            "备注存在 KenneySampleMap 上，导出 JSON 时写入 cellNotes。类型 = Prefabs 目录文件名；PlayScene 加载时会替换占位砖。",
            MessageType.Info);

        if (store.notes == null || store.notes.Count == 0)
        {
            EditorGUILayout.LabelField("尚无备注。用 Tile Palette Select 选格后打开 Tools → Kenney → Map → Cell Notes。");
            return;
        }

        for (int i = 0; i < store.notes.Count; i++)
        {
            var n = store.notes[i];
            if (n == null) continue;
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(
                string.Format("{0}  ({1},{2})  {3}×{4}  开口{5}  {6}°",
                    n.layer, n.x, n.y, n.width, n.height,
                    KenneyCellNoteInspector.OpeningName(n.opening), KenneyCellNote.NormalizeRotation(n.rotation)),
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField("类型", string.IsNullOrEmpty(n.type) ? "(空)" : n.type);
            if (!string.IsNullOrEmpty(n.description))
                EditorGUILayout.LabelField("描述", n.description);
            EditorGUILayout.EndVertical();
        }
    }
}
