#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// หน้าต่างแก้ passive tree: Tools > Project-A > Passive Tree Editor
// ซ้าย = ผัง tree / ขวา = ค่าของ node ที่เลือก
//   คลิกซ้าย = เลือก / ลาก = ย้าย (Snap ตาม grid ถ้าเปิด) / Shift+คลิก node อื่น = ต่อ/ตัดเส้นกับ node ที่เลือก
//   คลิกขวา = เมนู (เพิ่ม node / ตั้งเป็นจุด Start / ลบ) / ลากด้วยปุ่มกลางหรือ Alt+ลากซ้าย = เลื่อนผัง / ล้อเมาส์ = ซูม
//   Delete = ลบ node ที่เลือก / F = ดูทั้งผัง
// แก้ทุกอย่าง Undo ได้ (Ctrl+Z)
public class PassiveTreeEditorWindow : EditorWindow
{
    private const string DefaultAssetPath = "Assets/Resources/" + PassiveTree.DefaultResourcePath + ".asset";
    private const string InspectorWidthPref = "Project-A.PassiveTreeEditor.InspectorWidth";
    private float _inspectorWidth = 420f;
    private bool _resizing;

    private PassiveTree _tree;
    private Vector2 _pan;
    private float _zoom = 0.6f;
    private bool _snap = true;
    private int _panelTab;
    private bool _showArt = true;
    private enum ArtDrag { None, Move, Resize, Rotate }
    private ArtDrag _artDrag;
    private int _selectedBg = -1; // ภาพพื้นหลังที่เลือกอยู่ (tab Art)
    private bool _previewLit;
    private float _grid = 20f;
    private int _selected = -1;
    private bool _dragging;
    private Vector2 _inspectorScroll;
    private SerializedObject _so;

    [MenuItem("Tools/Project-A/Config/Passive Tree Editor")]
    public static void Open()
    {
        var w = GetWindow<PassiveTreeEditorWindow>("Passive Tree");
        w.minSize = new Vector2(700f, 400f);
        if (w._tree == null) w._tree = PassiveTree.LoadActive();
    }

    private void OnGUI()
    {
        DrawToolbar();
        if (_tree == null)
        {
            EditorGUILayout.HelpBox("เลือก Passive Tree หรือกด New Tree", MessageType.Info);
            return;
        }
        if (_so == null || _so.targetObject != _tree) _so = new SerializedObject(_tree);
        EnsureStyles();

        _inspectorWidth = Mathf.Clamp(_inspectorWidth, 280f, Mathf.Max(280f, position.width - 300f));
        var canvas = new Rect(0f, EditorStyles.toolbar.fixedHeight, position.width - _inspectorWidth, position.height - EditorStyles.toolbar.fixedHeight);
        var inspector = new Rect(canvas.xMax, canvas.y, _inspectorWidth, canvas.height);
        HandleSplitter(new Rect(canvas.xMax - 3f, canvas.y, 6f, canvas.height));
        DrawCanvas(canvas);
        DrawInspector(inspector);
    }

    // ลากเส้นแบ่งระหว่างผังกับ panel ขวาเพื่อขยาย/ย่อ panel (จำความกว้างไว้ใน EditorPrefs)
    private void HandleSplitter(Rect rect)
    {
        EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
        Event e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
        {
            _resizing = true;
            e.Use();
        }
        else if (_resizing && e.type == EventType.MouseDrag)
        {
            _inspectorWidth = position.width - e.mousePosition.x;
            e.Use();
            Repaint();
        }
        else if (_resizing && e.type == EventType.MouseUp)
        {
            _resizing = false;
            EditorPrefs.SetFloat(InspectorWidthPref, _inspectorWidth);
            e.Use();
        }
    }

    private void OnEnable() => _inspectorWidth = EditorPrefs.GetFloat(InspectorWidthPref, 420f);

    // ---------- Toolbar ----------

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            var t = (PassiveTree)EditorGUILayout.ObjectField(_tree, typeof(PassiveTree), false, GUILayout.Width(220f));
            if (t != _tree)
            {
                _tree = t;
                _selected = -1;
                _so = null;
            }
            if (GUILayout.Button("New Tree", EditorStyles.toolbarButton)) CreateTree();
            GUI.enabled = _tree != null;
            if (GUILayout.Button("Sample Layout", EditorStyles.toolbarButton) &&
                (_tree.nodes.Count == 0 || EditorUtility.DisplayDialog("Sample Layout", "แทนที่ node ทั้งหมดด้วยผังตัวอย่าง?", "แทนที่", "ยกเลิก")))
                BuildSample();
            if (GUILayout.Button("Frame All (F)", EditorStyles.toolbarButton)) FrameAll();
            if (GUILayout.Button("Save", EditorStyles.toolbarButton)) SaveTree();
            bool inUse = _tree != null && PassiveTree.LoadActive() == _tree;
            GUI.enabled = _tree != null && !inUse;
            if (GUILayout.Button(inUse ? "In Use ✓" : "Use Tree", EditorStyles.toolbarButton)) UseTree();
            GUI.enabled = _tree != null;
            if (GUILayout.Button("Export JSON", EditorStyles.toolbarButton)) ExportJson();
            if (GUILayout.Button("Import JSON", EditorStyles.toolbarButton)) ImportJson();
            GUI.enabled = true;
            GUILayout.Space(10f);
            _snap = GUILayout.Toggle(_snap, "Snap", EditorStyles.toolbarButton);
            _showArt = GUILayout.Toggle(_showArt, "Art", EditorStyles.toolbarButton);
            _previewLit = GUILayout.Toggle(_previewLit, "Lit", EditorStyles.toolbarButton);
            _grid = EditorGUILayout.FloatField(_grid, GUILayout.Width(40f));
            GUILayout.FlexibleSpace();
            if (_tree != null) GUILayout.Label($"{_tree.nodes.Count} nodes   zoom {_zoom:0.00}", EditorStyles.miniLabel);
        }
    }

    private void CreateTree()
    {
        string path = EditorUtility.SaveFilePanelInProject("New Passive Tree", "PassiveTree", "asset", "", System.IO.Path.GetDirectoryName(DefaultAssetPath));
        if (string.IsNullOrEmpty(path)) return;
        var tree = CreateInstance<PassiveTree>();
        AssetDatabase.CreateAsset(tree, path);
        AssetDatabase.SaveAssets();
        _tree = tree;
        _selected = -1;
        _so = null;
    }

    // ---------- Save / Export / Import ----------

    // รูปแบบไฟล์ JSON (sprite เก็บเป็น "path::ชื่อ sprite" เพราะ reference ข้ามโปรเจกต์ไม่ได้)
    [System.Serializable]
    private class NodeDto
    {
        public string id, displayName, description, icon, frame, choiceSetId;
        public PassiveNodeType type;
        public bool isStart;
        public Vector2 position;
        public List<string> links = new List<string>();
        public List<StatModifier> modifiers = new List<StatModifier>();
        public List<PassiveChoice> choices = new List<PassiveChoice>();
    }

    [System.Serializable]
    private class StyleDto
    {
        public PassiveNodeType type;
        public string frame, icon;
        public float scale = 1f;
        public bool overrideColors;
        public Color lockedColor, availableColor, allocatedColor;
        public PassiveNodeShape shape;
        public Color color;
    }

    [System.Serializable]
    private class BackgroundDto
    {
        public string name, art;
        public Vector2 position, size;
        public float rotation;
        public List<string> nodeIds = new List<string>();
        public bool requireAll;
    }

    [System.Serializable]
    private class TreeDto
    {
        public int version = 1;
        public List<NodeDto> nodes = new List<NodeDto>();
        public List<PassiveChoiceSet> choiceSets = new List<PassiveChoiceSet>();
        public List<BackgroundDto> backgrounds = new List<BackgroundDto>();
        public List<StyleDto> styles = new List<StyleDto>();
        public Color backgroundDim = new Color(0.22f, 0.22f, 0.22f, 1f), backgroundLit = Color.white;
        public float nodeSize = 48f;
    }

    private void SaveTree()
    {
        EditorUtility.SetDirty(_tree);
        AssetDatabase.SaveAssets();
        ShowNotification(new GUIContent("Saved " + _tree.name));
    }

    // ให้เกมใช้ tree นี้ (เขียนลง PassiveTreeSelection ใน Resources ผู้เล่นที่เซฟไว้: node id ที่ไม่มีใน tree ใหม่ถูกทิ้งเอง)
    private void UseTree()
    {
        SaveTree();
        string path = "Assets/Resources/" + PassiveTreeSelection.ResourcePath + ".asset";
        var selection = AssetDatabase.LoadAssetAtPath<PassiveTreeSelection>(path);
        if (selection == null)
        {
            selection = CreateInstance<PassiveTreeSelection>();
            AssetDatabase.CreateAsset(selection, path);
        }
        Undo.RecordObject(selection, "Use Passive Tree");
        selection.tree = _tree;
        EditorUtility.SetDirty(selection);
        AssetDatabase.SaveAssets();
        ShowNotification(new GUIContent($"Game now uses {_tree.name}"));
    }

    private static string SpriteToKey(Sprite s)
    {
        if (s == null) return "";
        string path = AssetDatabase.GetAssetPath(s);
        return string.IsNullOrEmpty(path) ? "" : path + "::" + s.name;
    }

    private static Sprite KeyToSprite(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        int sep = key.IndexOf("::", System.StringComparison.Ordinal);
        string path = sep >= 0 ? key.Substring(0, sep) : key;
        string name = sep >= 0 ? key.Substring(sep + 2) : null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o is Sprite sp && (name == null || sp.name == name)) return sp;
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private void ExportJson()
    {
        string dir = System.IO.Path.GetDirectoryName(Application.dataPath);
        string path = EditorUtility.SaveFilePanel("Export Passive Tree", dir, _tree.name, "json");
        if (string.IsNullOrEmpty(path)) return;

        var dto = new TreeDto { choiceSets = new List<PassiveChoiceSet>(_tree.choiceSets) };
        dto.backgroundDim = _tree.backgroundDim;
        dto.backgroundLit = _tree.backgroundLit;
        dto.nodeSize = _tree.nodeSize;
        foreach (var s in _tree.styles)
            dto.styles.Add(new StyleDto { type = s.type, frame = SpriteToKey(s.frame), icon = SpriteToKey(s.icon), scale = s.scale, overrideColors = s.overrideColors, lockedColor = s.lockedColor, availableColor = s.availableColor, allocatedColor = s.allocatedColor, shape = s.shape, color = s.color });
        foreach (var b in _tree.backgrounds)
            dto.backgrounds.Add(new BackgroundDto { name = b.name, art = SpriteToKey(b.art), position = b.position, size = b.size, rotation = b.rotation, nodeIds = new List<string>(b.nodeIds), requireAll = b.requireAll });
        foreach (var n in _tree.nodes)
        {
            dto.nodes.Add(new NodeDto
            {
                id = n.id, displayName = n.displayName, description = n.description, type = n.type,
                isStart = n.isStart, position = n.position,
                links = new List<string>(n.links), modifiers = new List<StatModifier>(n.modifiers),
                choices = n.choices.ConvertAll(c => new PassiveChoice { name = c.name, modifiers = new List<StatModifier>(c.modifiers) }),
                choiceSetId = n.choiceSetId,
                icon = SpriteToKey(n.icon), frame = SpriteToKey(n.frame),
            });
        }
        System.IO.File.WriteAllText(path, JsonUtility.ToJson(dto, true));
        ShowNotification(new GUIContent($"Exported {dto.nodes.Count} nodes"));
    }

    private void ImportJson()
    {
        string dir = System.IO.Path.GetDirectoryName(Application.dataPath);
        string path = EditorUtility.OpenFilePanel("Import Passive Tree", dir, "json");
        if (string.IsNullOrEmpty(path)) return;

        TreeDto dto;
        try { dto = JsonUtility.FromJson<TreeDto>(System.IO.File.ReadAllText(path)); }
        catch (System.Exception ex)
        {
            EditorUtility.DisplayDialog("Import JSON", "อ่านไฟล์ไม่ได้: " + ex.Message, "OK");
            return;
        }
        if (dto == null || dto.nodes == null)
        {
            EditorUtility.DisplayDialog("Import JSON", "ไฟล์ไม่ใช่ passive tree", "OK");
            return;
        }

        // ตรวจ id ซ้ำ / เส้นที่ชี้ไป node ที่ไม่มี
        var ids = new HashSet<string>();
        int dup = 0, broken = 0;
        foreach (var n in dto.nodes) if (string.IsNullOrEmpty(n.id) || !ids.Add(n.id)) dup++;
        foreach (var n in dto.nodes) foreach (var l in n.links) if (!ids.Contains(l)) broken++;
        string warn = dup + broken > 0 ? $"\n\nคำเตือน: id ซ้ำ/ว่าง {dup}, เส้นชี้ไป node ที่ไม่มี {broken}" : "";
        if (!EditorUtility.DisplayDialog("Import JSON",
                $"แทนที่ node ทั้งหมดของ {_tree.name} ({_tree.nodes.Count}) ด้วย {dto.nodes.Count} node จากไฟล์?\n(Undo ได้){warn}", "แทนที่", "ยกเลิก")) return;

        Edit("Import Passive Tree", () =>
        {
            _tree.nodes.Clear();
            if (dto.styles != null && dto.styles.Count > 0)
            {
                _tree.styles.Clear();
                foreach (var s in dto.styles)
                    _tree.styles.Add(new PassiveNodeStyle { type = s.type, frame = KeyToSprite(s.frame), icon = KeyToSprite(s.icon), scale = s.scale, overrideColors = s.overrideColors, lockedColor = s.lockedColor, availableColor = s.availableColor, allocatedColor = s.allocatedColor, shape = s.shape, color = s.color, configured = true });
                _tree.backgroundDim = dto.backgroundDim;
                _tree.backgroundLit = dto.backgroundLit;
                _tree.nodeSize = dto.nodeSize;
            }
            if (dto.backgrounds != null)
            {
                _tree.backgrounds.Clear();
                foreach (var b in dto.backgrounds)
                    _tree.backgrounds.Add(new PassiveBackground { name = b.name, art = KeyToSprite(b.art), position = b.position, size = b.size, rotation = b.rotation, nodeIds = b.nodeIds ?? new List<string>(), requireAll = b.requireAll });
            }
            if (dto.choiceSets != null && dto.choiceSets.Count > 0) // ไฟล์เก่าไม่มีชุด = คงชุดเดิมไว้
            {
                _tree.choiceSets.Clear();
                _tree.choiceSets.AddRange(dto.choiceSets);
            }
            foreach (var d in dto.nodes)
            {
                _tree.nodes.Add(new PassiveNode
                {
                    id = d.id, displayName = d.displayName, description = d.description, type = d.type,
                    isStart = d.isStart, position = d.position,
                    links = d.links ?? new List<string>(), modifiers = d.modifiers ?? new List<StatModifier>(),
                    choices = d.choices ?? new List<PassiveChoice>(),
                    choiceSetId = d.choiceSetId,
                    icon = KeyToSprite(d.icon), frame = KeyToSprite(d.frame),
                });
            }
            _selected = -1;
        });
        FrameAll();
    }

    // ---------- Canvas ----------

    private Vector2 ToScreen(Rect canvas, Vector2 p) => canvas.center + _pan + new Vector2(p.x, -p.y) * _zoom;
    private Vector2 ToTree(Rect canvas, Vector2 s) { var v = (s - canvas.center - _pan) / _zoom; return new Vector2(v.x, -v.y); }

    // ขนาดมาตรฐานเท่ากันทุกชนิด (nodeSize) คูณ scale ของชนิด / nodeSize = 0 (ใช้ template) = 48
    private float NodeRadius(PassiveNode n) => (_tree.nodeSize > 0f ? _tree.nodeSize : 48f) * 0.5f * _tree.ScaleOf(n.type);

    private Color Fill(PassiveNode n) => _tree.StyleOf(n.type)?.color ?? new Color(0.55f, 0.55f, 0.55f);

    // จำนวนด้านของรูปทรง (0 = วงกลม)
    private int Sides(PassiveNode n) => (int)(_tree.StyleOf(n.type)?.shape ?? PassiveNodeShape.Circle);

    private void DrawCanvas(Rect canvas)
    {
        EditorGUI.DrawRect(canvas, new Color(0.12f, 0.12f, 0.13f));
        GUI.BeginClip(canvas);
        var local = new Rect(0f, 0f, canvas.width, canvas.height);
        HandleInput(local, canvas);
        DrawGrid(local);
        if (_showArt || _panelTab == 2) DrawBackgroundArt(local);

        Handles.BeginGUI();
        // เส้นเชื่อม
        Handles.color = new Color(0.8f, 0.8f, 0.8f, 0.6f);
        foreach (var n in _tree.nodes)
        {
            foreach (var otherId in n.links)
            {
                var other = _tree.nodes.Find(x => x.id == otherId);
                if (other == null) continue;
                Handles.DrawAAPolyLine(3f, ToScreen(local, n.position), ToScreen(local, other.position));
            }
        }
        // node
        var label = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter, normal = { textColor = Color.white } };
        for (int i = 0; i < _tree.nodes.Count; i++)
        {
            var n = _tree.nodes[i];
            Vector2 c = ToScreen(local, n.position);
            float r = NodeRadius(n) * Mathf.Max(0.4f, _zoom);
            if (!local.Overlaps(new Rect(c.x - r * 3f, c.y - r, r * 6f, r * 3f))) continue;
            int sides = Sides(n);
            if (n.isStart) DrawShape(c, r + 5f, sides, new Color(0.3f, 1f, 0.4f), false);
            if (i == _selected) DrawShape(c, r + 3f, sides, Color.yellow, false);
            DrawShape(c, r, sides, Fill(n), false);
            DrawShape(c, r, sides, new Color(0f, 0f, 0f, 0.6f), true);
            // art ของ node (frame แล้ว icon ทับ)
            DrawSprite(_tree.FrameOf(n), c, r);
            DrawSprite(_tree.IconOf(n), c, r * 0.75f);

            if (_zoom >= 0.35f || n.type != PassiveNodeType.Small)
            {
                string text = string.IsNullOrEmpty(n.displayName) ? n.id : n.displayName;
                GUI.Label(new Rect(c.x - 80f, c.y + r + 2f, 160f, 16f), text, label);
            }
        }
        if (_panelTab == 2) DrawBackgroundSelection(local);
        Handles.EndGUI();
        GUI.EndClip();
    }

    // sides < 3 = วงกลม / ไม่งั้นรูปหลายเหลี่ยมปกติ (ยอดชี้ขึ้น 4 ด้านวางตรง) เหมือนในเกม (PolygonGraphic)
    private static void DrawShape(Vector2 c, float r, int sides, Color color, bool outlineOnly)
    {
        Handles.color = color;
        if (sides < 3)
        {
            if (outlineOnly) Handles.DrawWireDisc(c, Vector3.forward, r);
            else Handles.DrawSolidDisc(c, Vector3.forward, r);
            return;
        }
        var pts = new Vector3[sides + 1];
        float start = PolygonGraphic.StartAngle(sides) * Mathf.Deg2Rad;
        for (int i = 0; i < sides; i++)
        {
            float a = start + i * Mathf.PI * 2f / sides;
            pts[i] = new Vector3(c.x + Mathf.Cos(a) * r, c.y - Mathf.Sin(a) * r); // +y ของจอ = ลง
        }
        pts[sides] = pts[0];
        if (outlineOnly) Handles.DrawAAPolyLine(2f, pts);
        else Handles.DrawAAConvexPolygon(pts[..sides]);
    }

    private static void DrawSprite(Sprite sprite, Vector2 center, float radius)
    {
        if (sprite == null || sprite.texture == null) return;
        Texture2D tex = sprite.texture;
        Rect tr = sprite.textureRect;
        var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
        GUI.DrawTextureWithTexCoords(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), tex, uv, true);
    }

    private void DrawGrid(Rect local)
    {
        float step = _grid * _zoom;
        if (step < 6f) return;
        Handles.BeginGUI();
        Handles.color = new Color(1f, 1f, 1f, 0.04f);
        Vector2 origin = local.center + _pan;
        for (float x = origin.x % step; x < local.width; x += step) Handles.DrawLine(new Vector3(x, 0f), new Vector3(x, local.height));
        for (float y = origin.y % step; y < local.height; y += step) Handles.DrawLine(new Vector3(0f, y), new Vector3(local.width, y));
        Handles.color = new Color(1f, 1f, 1f, 0.12f);
        Handles.DrawLine(new Vector3(origin.x, 0f), new Vector3(origin.x, local.height));
        Handles.DrawLine(new Vector3(0f, origin.y), new Vector3(local.width, origin.y));
        Handles.EndGUI();
    }

    private int HitTest(Rect local, Vector2 mouse)
    {
        for (int i = _tree.nodes.Count - 1; i >= 0; i--)
        {
            var n = _tree.nodes[i];
            float r = NodeRadius(n) * Mathf.Max(0.4f, _zoom) + 3f;
            if ((ToScreen(local, n.position) - mouse).sqrMagnitude <= r * r) return i;
        }
        return -1;
    }

    private void HandleInput(Rect local, Rect canvas)
    {
        Event e = Event.current;
        if (!local.Contains(e.mousePosition) && e.type != EventType.MouseUp && e.type != EventType.MouseDrag) return;

        switch (e.type)
        {
            case EventType.ScrollWheel:
            {
                Vector2 before = ToTree(local, e.mousePosition);
                _zoom = Mathf.Clamp(_zoom * (e.delta.y > 0f ? 0.9f : 1.1f), 0.1f, 3f);
                _pan += e.mousePosition - ToScreen(local, before); // ซูมรอบเคอร์เซอร์
                e.Use();
                break;
            }
            case EventType.MouseDown when e.button == 0 && !e.alt && _panelTab == 2:
                ArtMouseDown(local, e);
                e.Use();
                break;
            case EventType.MouseDrag when e.button == 0 && !e.alt && _artDrag != ArtDrag.None:
                ArtMouseDrag(local, e);
                e.Use();
                break;
            case EventType.MouseDown when e.button == 0 && !e.alt:
            {
                int hit = HitTest(local, e.mousePosition);
                if (hit >= 0 && e.shift && _selected >= 0 && hit != _selected) ToggleLink(_selected, hit);
                else
                {
                    _selected = hit;
                    _dragging = hit >= 0;
                    if (_dragging) Undo.RecordObject(_tree, "Move Passive Node");
                }
                GUI.FocusControl(null);
                e.Use();
                break;
            }
            case EventType.MouseDrag when e.button == 2 || (e.button == 0 && e.alt):
                _pan += e.delta;
                e.Use();
                break;
            case EventType.MouseDrag when e.button == 0 && _dragging && _selected >= 0:
            {
                Vector2 p = ToTree(local, e.mousePosition);
                if (_snap && _grid > 0f) p = new Vector2(Mathf.Round(p.x / _grid) * _grid, Mathf.Round(p.y / _grid) * _grid);
                _tree.nodes[_selected].position = p;
                EditorUtility.SetDirty(_tree);
                e.Use();
                break;
            }
            case EventType.MouseUp:
                _dragging = false;
                _artDrag = ArtDrag.None;
                break;
            case EventType.ContextClick:
            {
                int hit = HitTest(local, e.mousePosition);
                Vector2 at = ToTree(local, e.mousePosition);
                if (_snap && _grid > 0f) at = new Vector2(Mathf.Round(at.x / _grid) * _grid, Mathf.Round(at.y / _grid) * _grid);
                ShowContextMenu(hit, at);
                e.Use();
                break;
            }
            case EventType.KeyDown when e.keyCode == KeyCode.Delete && _selected >= 0:
                DeleteNode(_selected);
                e.Use();
                break;
            case EventType.KeyDown when e.keyCode == KeyCode.F:
                FrameAll();
                e.Use();
                break;
        }
        if (e.type == EventType.Used) Repaint();
    }

    private void ShowContextMenu(int hit, Vector2 at)
    {
        var menu = new GenericMenu();
        if (hit >= 0)
        {
            int index = hit;
            var n = _tree.nodes[index];
            if (_selected >= 0 && _selected != index)
                menu.AddItem(new GUIContent(_tree.nodes[_selected].links.Contains(n.id) || n.links.Contains(_tree.nodes[_selected].id) ? "Unlink from selected" : "Link to selected"), false, () => ToggleLink(_selected, index));
            menu.AddItem(new GUIContent("Start Node"), n.isStart, () => Edit("Toggle Start", () => n.isStart = !n.isStart));
            menu.AddItem(new GUIContent("Type/Small"), n.type == PassiveNodeType.Small, () => Edit("Node Type", () => n.type = PassiveNodeType.Small));
            menu.AddItem(new GUIContent("Type/Notable"), n.type == PassiveNodeType.Notable, () => Edit("Node Type", () => n.type = PassiveNodeType.Notable));
            menu.AddItem(new GUIContent("Type/Keystone"), n.type == PassiveNodeType.Keystone, () => Edit("Node Type", () => n.type = PassiveNodeType.Keystone));
            menu.AddItem(new GUIContent("Type/Choice"), n.type == PassiveNodeType.Choice, () => Edit("Node Type", () => { n.type = PassiveNodeType.Choice; SeedChoices(n); }));
            menu.AddItem(new GUIContent("Type/Socket"), n.type == PassiveNodeType.Socket, () => Edit("Node Type", () => n.type = PassiveNodeType.Socket));
            menu.AddItem(new GUIContent("Type/Fixed Choice"), n.type == PassiveNodeType.FixedChoice, () => Edit("Node Type", () => { n.type = PassiveNodeType.FixedChoice; AssignChoiceSet(n); }));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Delete"), false, () => DeleteNode(index));
        }
        else
        {
            menu.AddItem(new GUIContent("Add Small"), false, () => AddNode(PassiveNodeType.Small, at, false));
            menu.AddItem(new GUIContent("Add Notable"), false, () => AddNode(PassiveNodeType.Notable, at, false));
            menu.AddItem(new GUIContent("Add Keystone"), false, () => AddNode(PassiveNodeType.Keystone, at, false));
            menu.AddItem(new GUIContent("Add Choice"), false, () => AddNode(PassiveNodeType.Choice, at, false));
            menu.AddItem(new GUIContent("Add Socket"), false, () => AddNode(PassiveNodeType.Socket, at, false));
            menu.AddItem(new GUIContent("Add Fixed Choice"), false, () => AddNode(PassiveNodeType.FixedChoice, at, false));
            menu.AddItem(new GUIContent("Add Start"), false, () => AddNode(PassiveNodeType.Small, at, true));
        }
        menu.ShowAsContext();
    }

    // ---------- แก้ข้อมูล ----------

    private void Edit(string undoName, System.Action change)
    {
        Undo.RecordObject(_tree, undoName);
        change();
        _tree.InvalidateCache();
        EditorUtility.SetDirty(_tree);
        _so?.Update();
        Repaint();
    }

    private string NewId()
    {
        int i = _tree.nodes.Count + 1;
        while (_tree.nodes.Exists(n => n.id == "n" + i)) i++;
        return "n" + i;
    }

    private void AddNode(PassiveNodeType type, Vector2 at, bool start)
    {
        Edit("Add Passive Node", () =>
        {
            var node = new PassiveNode { id = NewId(), type = type, position = at, isStart = start, displayName = start ? "Start" : type.ToString() };
            // เพิ่มต่อจาก node ที่เลือกอยู่ให้เลย (วางเป็นแนวได้เร็ว)
            if (_selected >= 0 && _selected < _tree.nodes.Count) node.links.Add(_tree.nodes[_selected].id);
            SeedChoices(node);
            AssignChoiceSet(node);
            _tree.nodes.Add(node);
            _selected = _tree.nodes.Count - 1;
        });
    }

    // node Choice ที่ยังไม่มีตัวเลือก -> ใส่ตัวอย่าง +10 Str / Dex / Int ให้แก้ต่อ
    private static void SeedChoices(PassiveNode node)
    {
        if (node.type != PassiveNodeType.Choice || node.choices.Count > 0) return;
        foreach (var (name, stat) in new[] { ("Strength", StatType.Strength), ("Dexterity", StatType.Dexterity), ("Intelligence", StatType.Intelligence) })
            node.choices.Add(new PassiveChoice { name = $"+10 {name}", modifiers = { new StatModifier(stat, ModifierType.Flat, 10f) } });
    }

    private void DeleteNode(int index)
    {
        Edit("Delete Passive Node", () =>
        {
            string id = _tree.nodes[index].id;
            _tree.nodes.RemoveAt(index);
            foreach (var n in _tree.nodes) n.links.Remove(id);
            _selected = -1;
        });
    }

    private void ToggleLink(int a, int b)
    {
        Edit("Toggle Passive Link", () =>
        {
            var na = _tree.nodes[a];
            var nb = _tree.nodes[b];
            if (na.links.Contains(nb.id) || nb.links.Contains(na.id))
            {
                na.links.Remove(nb.id);
                nb.links.Remove(na.id);
            }
            else na.links.Add(nb.id);
        });
    }

    private void FrameAll()
    {
        if (_tree == null || _tree.nodes.Count == 0) return;
        Vector2 min = _tree.nodes[0].position, max = min;
        foreach (var n in _tree.nodes)
        {
            min = Vector2.Min(min, n.position);
            max = Vector2.Max(max, n.position);
        }
        float w = position.width - _inspectorWidth - 80f, h = position.height - 100f;
        Vector2 size = Vector2.Max(max - min, Vector2.one * 100f);
        _zoom = Mathf.Clamp(Mathf.Min(w / size.x, h / size.y), 0.1f, 3f);
        Vector2 c = (min + max) * 0.5f;
        _pan = -new Vector2(c.x, -c.y) * _zoom;
        Repaint();
    }

    // ---------- Tab Config: ค่าเริ่มต้นของ node / พื้นหลัง / ชุดตัวเลือก / ตรวจความถูกต้อง ----------

    private bool _showStyles = true;
    private bool _showChecks = true;

    // ให้มี style ครบทุกชนิด node (ครั้งเดียวต่อ tree)
    private void EnsureStyles()
    {
        foreach (PassiveNodeType t in System.Enum.GetValues(typeof(PassiveNodeType)))
        {
            if (_tree.StyleOf(t) != null) continue;
            var created = new PassiveNodeStyle { type = t };
            PassiveTree.ApplyDefaultLook(created);
            _tree.styles.Add(created);
            EditorUtility.SetDirty(_tree);
            _so = null; // สร้าง SerializedObject ใหม่ให้เห็นจำนวนใหม่
        }
        foreach (var s in _tree.styles) // style จากเวอร์ชันก่อนที่ยังไม่มีสี/รูปทรง -> ใส่ค่าเริ่มต้นของชนิด
        {
            if (s.configured) continue;
            PassiveTree.ApplyDefaultLook(s);
            EditorUtility.SetDirty(_tree);
            _so = null;
        }
        if (_so == null) _so = new SerializedObject(_tree);
    }

    private void DrawConfigTab()
    {
        _so.Update();
        _showStyles = EditorGUILayout.Foldout(_showStyles, "Node Defaults  ·  รูป / ขนาด / สี เริ่มต้นของ node แต่ละชนิด", true, EditorStyles.foldoutHeader);
        if (_showStyles)
        {
            EditorGUILayout.PropertyField(_so.FindProperty("nodeSize"), new GUIContent("Node Size (ทุกชนิดเท่ากัน)"));
            EditorGUILayout.HelpBox("Node Size = ขนาดมาตรฐานของ node ทุกชนิด (0 = ใช้ขนาด template ใน prefab) / Scale = ปรับเฉพาะชนิด (1 = เท่ากัน)\nรูปทรงใช้เมื่อ node ไม่มี Frame sprite / Icon, Frame ว่าง = ใช้ template ใน GameUI prefab", MessageType.None);
            var styles = _so.FindProperty("styles");
            for (int i = 0; i < styles.arraySize; i++)
            {
                var s = styles.GetArrayElementAtIndex(i);
                var type = (PassiveNodeType)s.FindPropertyRelative("type").intValue;
                EditorGUILayout.LabelField(type.ToString(), EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(s.FindPropertyRelative("frame"));
                EditorGUILayout.PropertyField(s.FindPropertyRelative("icon"));
                EditorGUILayout.PropertyField(s.FindPropertyRelative("shape"));
                EditorGUILayout.PropertyField(s.FindPropertyRelative("color"), new GUIContent("Type Color"));
                EditorGUILayout.PropertyField(s.FindPropertyRelative("scale"));
                var over = s.FindPropertyRelative("overrideColors");
                EditorGUILayout.PropertyField(over);
                if (over.boolValue)
                {
                    EditorGUILayout.PropertyField(s.FindPropertyRelative("lockedColor"), new GUIContent("Locked"));
                    EditorGUILayout.PropertyField(s.FindPropertyRelative("availableColor"), new GUIContent("Available"));
                    EditorGUILayout.PropertyField(s.FindPropertyRelative("allocatedColor"), new GUIContent("Allocated"));
                }
                else if (GUILayout.Button("Use Type Color for states"))
                {
                    var c = s.FindPropertyRelative("color").colorValue;
                    over.boolValue = true;
                    s.FindPropertyRelative("allocatedColor").colorValue = c;
                    s.FindPropertyRelative("availableColor").colorValue = new Color(c.r * 0.65f, c.g * 0.65f, c.b * 0.65f, 1f);
                    s.FindPropertyRelative("lockedColor").colorValue = new Color(c.r * 0.35f, c.g * 0.35f, c.b * 0.35f, 1f);
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Background Art Colors", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_so.FindProperty("backgroundDim"), new GUIContent("Dim (not lit)"));
            EditorGUILayout.PropertyField(_so.FindProperty("backgroundLit"), new GUIContent("Lit"));
        }
        if (_so.ApplyModifiedProperties()) Repaint();

        DrawChoiceSets();
        DrawChecks();
    }

    // ตรวจ tree: id ซ้ำ/ว่าง, เส้นชี้ node ที่ไม่มี, ไม่มี Start, Fixed Choice ชี้ชุดที่ไม่มี ฯลฯ
    private void DrawChecks()
    {
        EditorGUILayout.Space();
        _showChecks = EditorGUILayout.Foldout(_showChecks, "Check Tree", true, EditorStyles.foldoutHeader);
        if (!_showChecks) return;

        var ids = new HashSet<string>();
        var problems = new List<string>();
        int starts = 0;
        foreach (var n in _tree.nodes)
        {
            if (string.IsNullOrEmpty(n.id)) problems.Add("มี node ที่ id ว่าง");
            else if (!ids.Add(n.id)) problems.Add($"id ซ้ำ: {n.id}");
            if (n.isStart) starts++;
        }
        foreach (var n in _tree.nodes)
        {
            foreach (var l in n.links) if (!ids.Contains(l)) problems.Add($"{n.id}: เส้นชี้ไป '{l}' ที่ไม่มี");
            if (n.type == PassiveNodeType.Choice && n.choices.Count == 0) problems.Add($"{n.id}: Choice ไม่มีตัวเลือก");
            if (n.type == PassiveNodeType.FixedChoice && _tree.ChoicesOf(n).Count == 0) problems.Add($"{n.id}: Fixed Choice ไม่มีชุด/ชุดว่าง");
        }
        if (starts == 0) problems.Add("ไม่มีจุด Start");
        foreach (var b in _tree.backgrounds)
            foreach (var id in b.nodeIds) if (!ids.Contains(id)) problems.Add($"Background '{b.name}': ไม่มี node '{id}'");

        if (problems.Count == 0) EditorGUILayout.HelpBox($"ไม่พบปัญหา ({_tree.nodes.Count} nodes)", MessageType.Info);
        else EditorGUILayout.HelpBox(string.Join("\n", problems.GetRange(0, Mathf.Min(problems.Count, 20))) + (problems.Count > 20 ? $"\n... อีก {problems.Count - 20}" : ""), MessageType.Warning);
    }

    // ---------- ภาพพื้นหลัง ----------


    // วาดภาพหลัง node: ทึบ(มืด) = ยังไม่สว่าง / ปุ่ม Lit = ดูตอนสว่าง
    private void DrawBackgroundArt(Rect local)
    {
        var saved = GUI.matrix;
        foreach (var bg in _tree.backgrounds)
        {
            if (bg.art == null || bg.art.texture == null) continue;
            Vector2 size = bg.SizeOrNative * _zoom;
            Vector2 center = ToScreen(local, bg.position);
            Rect tr = bg.art.textureRect;
            var tex = bg.art.texture;
            var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
            GUI.matrix = saved;
            GUIUtility.RotateAroundPivot(-bg.rotation, center);
            var prev = GUI.color;
            GUI.color = _previewLit ? Color.white : new Color(0.3f, 0.3f, 0.3f, 1f);
            GUI.DrawTextureWithTexCoords(new Rect(center - size * 0.5f, size), tex, uv, true);
            GUI.color = prev;
        }
        GUI.matrix = saved;
    }

    // tab Art: เปิด/ปิดภาพ + พรีวิวสว่าง + รายการภาพ (ลากบนผังได้)
    private void DrawArtTab()
    {
        EditorGUILayout.HelpBox("คลิกภาพบนผังเพื่อเลือก / ลากตัวภาพ = ย้าย / ลากสี่เหลี่ยมมุมขวาล่าง = ขยาย / ลากวงกลมด้านบน = หมุน (Shift = ปัด 15°) / ปุ่มกลางหรือ Alt+ลาก = เลื่อนผัง / ล้อเมาส์ = ซูม", MessageType.Info);
        using (new EditorGUILayout.HorizontalScope())
        {
            _showArt = GUILayout.Toggle(_showArt, "Show Art", EditorStyles.miniButtonLeft);
            _previewLit = GUILayout.Toggle(_previewLit, "Preview Lit", EditorStyles.miniButtonRight);
        }
        _so.Update();
        EditorGUILayout.PropertyField(_so.FindProperty("backgroundDim"), new GUIContent("Dim (not lit)"));
        EditorGUILayout.PropertyField(_so.FindProperty("backgroundLit"), new GUIContent("Lit"));
        _so.ApplyModifiedProperties();
        DrawBackgroundList();
    }

    private void DrawBackgroundList()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"Backgrounds ({_tree.backgrounds.Count})  ·  ภาพหลัง tree สว่างเมื่อลง node ที่กำหนด", EditorStyles.boldLabel);

        _so.Update();
        var list = _so.FindProperty("backgrounds");
        int removeAt = -1;
        for (int i = 0; i < list.arraySize; i++)
        {
            var element = list.GetArrayElementAtIndex(i);
            bool selected = i == _selectedBg;
            var box = selected ? new GUIStyle(EditorStyles.helpBox) : EditorStyles.helpBox;
            using (new EditorGUILayout.VerticalScope(box))
            {
                EditorGUILayout.PropertyField(element, new GUIContent($"{(selected ? "▶ " : "")}{i}: {_tree.backgrounds[i].name}"), true);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Select", GUILayout.Width(60f))) _selectedBg = i;
                    GUI.enabled = _selected >= 0 && _selected < _tree.nodes.Count;
                    if (GUILayout.Button("Add selected node")) AddSelectedToBackground(element);
                    GUI.enabled = true;
                    if (GUILayout.Button("Remove", GUILayout.Width(70f))) removeAt = i;
                }
            }
        }
        if (removeAt >= 0)
        {
            list.DeleteArrayElementAtIndex(removeAt);
            if (_selectedBg == removeAt) _selectedBg = -1;
            else if (_selectedBg > removeAt) _selectedBg--;
        }
        _so.ApplyModifiedProperties();
        if (GUILayout.Button("Add Background"))
        {
            Edit("Add Background", () => _tree.backgrounds.Add(new PassiveBackground { name = "Background" }));
            _selectedBg = _tree.backgrounds.Count - 1;
        }
    }

    // ---------- ลากภาพด้วยเมาส์ ----------

    private static Vector2 Rot(Vector2 v, float degrees)
    {
        float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    // จุดเมาส์ -> พิกัดในแกนของภาพ (หน่วยจอ, +y ขึ้น) / กลับกัน
    private Vector2 ToBgLocal(Rect local, PassiveBackground bg, Vector2 mouse)
    {
        Vector2 d = mouse - ToScreen(local, bg.position);
        return Rot(new Vector2(d.x, -d.y), -bg.rotation);
    }

    private Vector2 FromBgLocal(Rect local, PassiveBackground bg, Vector2 p)
    {
        Vector2 v = Rot(p, bg.rotation);
        return ToScreen(local, bg.position) + new Vector2(v.x, -v.y);
    }

    private const float RotateHandleGap = 28f;
    private const float HandleHit = 9f;

    private bool BgValid(int i) => i >= 0 && i < _tree.backgrounds.Count && _tree.backgrounds[i].art != null;

    private void ArtMouseDown(Rect local, Event e)
    {
        _artDrag = ArtDrag.None;
        if (BgValid(_selectedBg))
        {
            var bg = _tree.backgrounds[_selectedBg];
            Vector2 half = bg.SizeOrNative * _zoom * 0.5f;
            if (Vector2.Distance(e.mousePosition, FromBgLocal(local, bg, new Vector2(half.x, -half.y))) <= HandleHit)
            {
                Undo.RecordObject(_tree, "Resize Background");
                _artDrag = ArtDrag.Resize;
                return;
            }
            if (Vector2.Distance(e.mousePosition, FromBgLocal(local, bg, new Vector2(0f, half.y + RotateHandleGap))) <= HandleHit)
            {
                Undo.RecordObject(_tree, "Rotate Background");
                _artDrag = ArtDrag.Rotate;
                return;
            }
        }

        int hit = -1;
        for (int i = _tree.backgrounds.Count - 1; i >= 0; i--)
        {
            if (!BgValid(i)) continue;
            var bg = _tree.backgrounds[i];
            Vector2 half = bg.SizeOrNative * _zoom * 0.5f;
            Vector2 p = ToBgLocal(local, bg, e.mousePosition);
            if (Mathf.Abs(p.x) <= half.x && Mathf.Abs(p.y) <= half.y)
            {
                hit = i;
                break;
            }
        }
        _selectedBg = hit;
        if (hit >= 0)
        {
            Undo.RecordObject(_tree, "Move Background");
            _artDrag = ArtDrag.Move;
        }
        GUI.FocusControl(null);
        Repaint();
    }

    private void ArtMouseDrag(Rect local, Event e)
    {
        if (!BgValid(_selectedBg)) return;
        var bg = _tree.backgrounds[_selectedBg];
        switch (_artDrag)
        {
            case ArtDrag.Move:
                bg.position += new Vector2(e.delta.x, -e.delta.y) / _zoom;
                break;
            case ArtDrag.Resize:
            {
                Vector2 current = bg.SizeOrNative;
                float aspect = current.y > 0.01f ? current.x / current.y : 1f;
                float w = Mathf.Max(16f, 2f * ToBgLocal(local, bg, e.mousePosition).x / _zoom);
                bg.size = new Vector2(Mathf.Round(w), Mathf.Round(w / aspect));
                break;
            }
            case ArtDrag.Rotate:
            {
                Vector2 d = e.mousePosition - ToScreen(local, bg.position);
                float angle = Mathf.Atan2(-d.y, d.x) * Mathf.Rad2Deg - 90f;
                if (e.shift) angle = Mathf.Round(angle / 15f) * 15f;
                bg.rotation = Mathf.Repeat(angle + 180f, 360f) - 180f;
                break;
            }
        }
        EditorUtility.SetDirty(_tree);
        _so?.Update();
        Repaint();
    }

    // กรอบของทุกภาพ (จาง) + ภาพที่เลือก (เหลือง) พร้อมจุดจับ ขยาย (สี่เหลี่ยม) / หมุน (วงกลม)
    private void DrawBackgroundSelection(Rect local)
    {
        for (int i = 0; i < _tree.backgrounds.Count; i++)
        {
            if (!BgValid(i)) continue;
            var bg = _tree.backgrounds[i];
            Vector2 half = bg.SizeOrNative * _zoom * 0.5f;
            var pts = new Vector3[]
            {
                FromBgLocal(local, bg, new Vector2(-half.x, half.y)), FromBgLocal(local, bg, new Vector2(half.x, half.y)),
                FromBgLocal(local, bg, new Vector2(half.x, -half.y)), FromBgLocal(local, bg, new Vector2(-half.x, -half.y)),
                FromBgLocal(local, bg, new Vector2(-half.x, half.y)),
            };
            bool selected = i == _selectedBg;
            Handles.color = selected ? Color.yellow : new Color(1f, 1f, 1f, 0.25f);
            Handles.DrawAAPolyLine(selected ? 2.5f : 1.5f, pts);
            if (!selected) continue;

            Vector2 resize = FromBgLocal(local, bg, new Vector2(half.x, -half.y));
            Vector2 top = FromBgLocal(local, bg, new Vector2(0f, half.y));
            Vector2 rotate = FromBgLocal(local, bg, new Vector2(0f, half.y + RotateHandleGap));
            Handles.DrawLine(top, rotate);
            Handles.DrawSolidRectangleWithOutline(new Rect(resize.x - 6f, resize.y - 6f, 12f, 12f), Color.yellow, Color.black);
            Handles.DrawSolidDisc(rotate, Vector3.forward, 6f);
        }
    }

    private void AddSelectedToBackground(SerializedProperty element)
    {
        string id = _tree.nodes[_selected].id;
        var ids = element.FindPropertyRelative("nodeIds");
        for (int i = 0; i < ids.arraySize; i++)
            if (ids.GetArrayElementAtIndex(i).stringValue == id) return;
        ids.arraySize++;
        ids.GetArrayElementAtIndex(ids.arraySize - 1).stringValue = id;
    }

    // ---------- ชุดตัวเลือกกลาง (Fixed Choice) ----------

    private bool _showSets = true;

    private void DrawChoiceSetPopup(SerializedProperty idProp)
    {
        var sets = _tree.choiceSets;
        if (sets.Count == 0)
        {
            EditorGUILayout.HelpBox("ยังไม่มี Choice Set — สร้างที่ส่วน Choice Sets ด้านล่าง", MessageType.Warning);
            return;
        }
        var names = new string[sets.Count];
        int current = 0;
        for (int i = 0; i < sets.Count; i++)
        {
            names[i] = string.IsNullOrEmpty(sets[i].name) ? sets[i].id : sets[i].name;
            if (sets[i].id == idProp.stringValue) current = i;
        }
        int picked = EditorGUILayout.Popup("Choice Set", current, names);
        if (picked != current || idProp.stringValue != sets[picked].id) idProp.stringValue = sets[picked].id;

        // สรุปตัวเลือกของชุดที่เลือก (แก้ที่ส่วน Choice Sets ด้านล่าง)
        foreach (var c in sets[picked].choices)
        {
            string line = c.name;
            foreach (var m in c.modifiers) line += "  " + StatMath.Describe(m);
            EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
        }
    }

    private void DrawChoiceSets()
    {
        EditorGUILayout.Space();
        _showSets = EditorGUILayout.Foldout(_showSets, $"Choice Sets ({_tree.choiceSets.Count})  ·  แก้ชุด = มีผลทุก Fixed Choice ที่ใช้ชุดนั้น", true, EditorStyles.foldoutHeader);
        if (!_showSets) return;

        _so.Update();
        EditorGUILayout.PropertyField(_so.FindProperty("choiceSets"), true);
        if (_so.ApplyModifiedProperties())
        {
            FixSetIds();
            _tree.InvalidateCache();
        }
        if (GUILayout.Button("Add Choice Set")) Edit("Add Choice Set", () => _tree.choiceSets.Add(NewSet("New Set")));
    }

    private PassiveChoiceSet NewSet(string name)
    {
        var set = new PassiveChoiceSet { id = NewSetId(), name = name };
        foreach (var (label, stat) in new[] { ("Strength", StatType.Strength), ("Dexterity", StatType.Dexterity), ("Intelligence", StatType.Intelligence) })
            set.choices.Add(new PassiveChoice { name = $"+10 {label}", modifiers = { new StatModifier(stat, ModifierType.Flat, 10f) } });
        return set;
    }

    private string NewSetId()
    {
        int i = _tree.choiceSets.Count + 1;
        while (_tree.choiceSets.Exists(s => s.id == "set" + i)) i++;
        return "set" + i;
    }

    // id ว่าง/ซ้ำ (เช่นกด + ใน list แล้ว Unity ก๊อปตัวสุดท้าย) -> ออก id ใหม่ให้
    private void FixSetIds()
    {
        var seen = new HashSet<string>();
        foreach (var s in _tree.choiceSets)
        {
            if (string.IsNullOrEmpty(s.id) || !seen.Add(s.id))
            {
                s.id = NewSetId();
                seen.Add(s.id);
            }
        }
        EditorUtility.SetDirty(_tree);
    }

    // node Fixed Choice ที่ยังไม่ได้เลือกชุด (หรือชุดหาย) -> ใช้ชุดแรก (ไม่มีชุดเลย = สร้างชุดเริ่มต้นให้)
    private void AssignChoiceSet(PassiveNode node)
    {
        if (node.type != PassiveNodeType.FixedChoice || _tree.choiceSets.Exists(s => s.id == node.choiceSetId)) return;
        if (_tree.choiceSets.Count == 0) _tree.choiceSets.Add(NewSet("Attributes"));
        node.choiceSetId = _tree.choiceSets[0].id;
    }

    // ---------- Inspector ----------

    private static readonly (string label, string name, StatModifier mod)[] Presets =
    {
        ("+10 Str", "Strength", new StatModifier(StatType.Strength, ModifierType.Flat, 10f)),
        ("+10 Dex", "Dexterity", new StatModifier(StatType.Dexterity, ModifierType.Flat, 10f)),
        ("+10 Int", "Intelligence", new StatModifier(StatType.Intelligence, ModifierType.Flat, 10f)),
        ("+10 Life", "Life", new StatModifier(StatType.MaxHealth, ModifierType.Flat, 10f)),
        ("+10 Mana", "Mana", new StatModifier(StatType.MaxMana, ModifierType.Flat, 10f)),
        ("Adds 2-5 Dmg", "Added Damage", StatModifier.FlatRange(StatType.Damage, 2f, 5f)),
        ("+8% Damage", "Damage", new StatModifier(StatType.Damage, ModifierType.Increased, 8f)),
        ("+4% Atk Speed", "Attack Speed", new StatModifier(StatType.AttackSpeed, ModifierType.Increased, 4f)),
        ("+3% Move Speed", "Movement Speed", new StatModifier(StatType.MovementSpeed, ModifierType.Increased, 3f)),
        ("+6% Area", "Area of Effect", new StatModifier(StatType.AreaOfEffect, ModifierType.Increased, 6f)),
        ("+0.5 Mana Regen", "Mana Regeneration", new StatModifier(StatType.ManaRegen, ModifierType.Flat, 0.5f)),
    };

    private void DrawInspector(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f));
        GUILayout.BeginArea(new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f));
        _inspectorScroll = EditorGUILayout.BeginScrollView(_inspectorScroll);
        _panelTab = GUILayout.Toolbar(_panelTab, new[] { "Node", "Config", "Art" });
        EditorGUILayout.Space();
        if (_panelTab >= 1)
        {
            if (_panelTab == 1) DrawConfigTab();
            else DrawArtTab();
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
            return;
        }


        if (_selected < 0 || _selected >= _tree.nodes.Count)
        {
            EditorGUILayout.HelpBox(
                "คลิกซ้าย = เลือก / ลาก = ย้าย\nShift+คลิก node อื่น = ต่อ/ตัดเส้น\nคลิกขวา = เพิ่ม node / ตั้ง Start / ลบ\nปุ่มกลาง หรือ Alt+ลาก = เลื่อน / ล้อเมาส์ = ซูม\nDelete = ลบ / F = ดูทั้งผัง\n\nnode ใหม่ที่เพิ่มตอนเลือก node อยู่จะต่อเส้นให้เอง",
                MessageType.Info);
            int starts = 0;
            foreach (var n in _tree.nodes) if (n.isStart) starts++;
            if (starts == 0) EditorGUILayout.HelpBox("ยังไม่มีจุด Start (คลิกขวา node > Start Node)", MessageType.Warning);
        }
        else
        {
            _so.Update();
            var prop = _so.FindProperty("nodes").GetArrayElementAtIndex(_selected);
            EditorGUILayout.LabelField("Node", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(prop.FindPropertyRelative("id"));
            EditorGUILayout.PropertyField(prop.FindPropertyRelative("displayName"));
            EditorGUILayout.PropertyField(prop.FindPropertyRelative("type"));
            EditorGUILayout.PropertyField(prop.FindPropertyRelative("isStart"));
            EditorGUILayout.PropertyField(prop.FindPropertyRelative("position"));
            EditorGUILayout.PropertyField(prop.FindPropertyRelative("icon"));
            EditorGUILayout.PropertyField(prop.FindPropertyRelative("frame"));
            EditorGUILayout.PropertyField(prop.FindPropertyRelative("description"));
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(prop.FindPropertyRelative("modifiers"), true);
            if (_tree.nodes[_selected].type == PassiveNodeType.Choice) EditorGUILayout.PropertyField(prop.FindPropertyRelative("choices"), true);
            if (_tree.nodes[_selected].type == PassiveNodeType.FixedChoice) DrawChoiceSetPopup(prop.FindPropertyRelative("choiceSetId"));
            EditorGUILayout.PropertyField(prop.FindPropertyRelative("links"), true);
            if (_so.ApplyModifiedProperties()) _tree.InvalidateCache();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Quick Stat (แทนที่ stat ของ node)", EditorStyles.boldLabel);
            var node = _tree.nodes[_selected];
            for (int i = 0; i < Presets.Length; i += 2)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int k = i; k < i + 2 && k < Presets.Length; k++)
                    {
                        var p = Presets[k];
                        if (GUILayout.Button(p.label)) Edit("Passive Preset", () =>
                        {
                            node.modifiers.Clear();
                            node.modifiers.Add(p.mod);
                            if (node.type == PassiveNodeType.Small) node.displayName = p.name;
                        });
                    }
                }
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
            foreach (var m in node.modifiers) EditorGUILayout.LabelField(StatMath.Describe(m), EditorStyles.wordWrappedLabel);
        }

        EditorGUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    // ---------- ผังตัวอย่าง: จุด start กลาง + 3 สาย Str / Dex / Int (node เล็ก -> Notable -> Keystone) + วงเชื่อมระหว่างสาย ----------

    private void BuildSample()
    {
        Edit("Sample Passive Tree", () =>
        {
            FillSample(_tree);
            _selected = -1;
        });
        FrameAll();
    }

    // แทนที่ node ทั้งหมดของ tree ด้วยผังตัวอย่าง (เรียกจากสคริปต์อื่นได้)
    public static void FillSample(PassiveTree tree)
    {
        {
            tree.nodes.Clear();
            var start = new PassiveNode { id = "start", displayName = "Start", isStart = true, position = Vector2.zero };
            tree.nodes.Add(start);

            var branches = new[]
            {
                (key: "str", angle: 90f, stat: StatType.Strength, name: "Strength",
                 extra: new StatModifier(StatType.MaxHealth, ModifierType.Flat, 10f), extraName: "Life",
                 notable: "Brute Force", notableMods: new[] { new StatModifier(StatType.Strength, ModifierType.Flat, 20f), new StatModifier(StatType.Damage, ModifierType.Increased, 15f), new StatModifier(StatType.MaxHealth, ModifierType.Flat, 20f) },
                 keystone: "Iron Will", keystoneMods: new[] { new StatModifier(StatType.Damage, ModifierType.More, 30f), new StatModifier(StatType.AttackSpeed, ModifierType.More, -15f) }),
                (key: "dex", angle: 210f, stat: StatType.Dexterity, name: "Dexterity",
                 extra: new StatModifier(StatType.AttackSpeed, ModifierType.Increased, 4f), extraName: "Attack Speed",
                 notable: "Quick Hands", notableMods: new[] { new StatModifier(StatType.Dexterity, ModifierType.Flat, 20f), new StatModifier(StatType.AttackSpeed, ModifierType.Increased, 8f), new StatModifier(StatType.MovementSpeed, ModifierType.Increased, 5f) },
                 keystone: "Wind Dancer", keystoneMods: new[] { new StatModifier(StatType.AttackSpeed, ModifierType.Increased, 15f), new StatModifier(StatType.MovementSpeed, ModifierType.Increased, 15f), new StatModifier(StatType.MaxHealth, ModifierType.More, -20f) }),
                (key: "int", angle: 330f, stat: StatType.Intelligence, name: "Intelligence",
                 extra: new StatModifier(StatType.MaxMana, ModifierType.Flat, 10f), extraName: "Mana",
                 notable: "Deep Wisdom", notableMods: new[] { new StatModifier(StatType.Intelligence, ModifierType.Flat, 20f), new StatModifier(StatType.MaxMana, ModifierType.Flat, 25f), new StatModifier(StatType.ManaRegen, ModifierType.Flat, 1f) },
                 keystone: "Arcane Mind", keystoneMods: new[] { new StatModifier(StatType.MaxMana, ModifierType.More, 50f), new StatModifier(StatType.ManaRegen, ModifierType.Increased, 50f), new StatModifier(StatType.MaxHealth, ModifierType.More, -15f) }),
            };

            var ringNodes = new List<PassiveNode>();
            foreach (var b in branches)
            {
                Vector2 dir = new Vector2(Mathf.Cos(b.angle * Mathf.Deg2Rad), Mathf.Sin(b.angle * Mathf.Deg2Rad));
                PassiveNode prev = start;
                for (int i = 1; i <= 5; i++)
                {
                    var n = Add($"{b.key}{i}", b.name, PassiveNodeType.Small, dir * (80f * i), prev, new StatModifier(b.stat, ModifierType.Flat, 10f));
                    if (i == 3) ringNodes.Add(n);
                    prev = n;
                }
                prev = Add($"{b.key}_notable", b.notable, PassiveNodeType.Notable, dir * 500f, prev, b.notableMods);
                prev = Add($"{b.key}6", b.extraName, PassiveNodeType.Small, dir * 590f, prev, b.extra);
                prev = Add($"{b.key}7", b.extraName, PassiveNodeType.Small, dir * 670f, prev, b.extra);
                Add($"{b.key}_keystone", b.keystone, PassiveNodeType.Keystone, dir * 790f, prev, b.keystoneMods);
            }

            // วงเชื่อมระหว่างสาย (ผ่าน node กลางทาง) -> ข้ามไปสายอื่นได้โดยไม่ต้องย้อนกลับ start
            var ringMods = new[]
            {
                new StatModifier(StatType.MaxHealth, ModifierType.Flat, 10f),
                new StatModifier(StatType.Damage, ModifierType.Increased, 8f),
                new StatModifier(StatType.MaxMana, ModifierType.Flat, 10f),
            };
            string[] ringNames = { "Life", "Damage", "Mana" };
            for (int i = 0; i < ringNodes.Count; i++)
            {
                var a = ringNodes[i];
                var b = ringNodes[(i + 1) % ringNodes.Count];
                Vector2 mid = (a.position + b.position) * 0.5f;
                mid = mid.normalized * a.position.magnitude;
                var m = Add($"ring{i + 1}", ringNames[i], PassiveNodeType.Small, mid, a, ringMods[i]);
                m.links.Add(b.id);
            }
        }
        tree.InvalidateCache();

        PassiveNode Add(string id, string name, PassiveNodeType type, Vector2 pos, PassiveNode linkTo, params StatModifier[] mods)
        {
            var n = new PassiveNode { id = id, displayName = name, type = type, position = new Vector2(Mathf.Round(pos.x), Mathf.Round(pos.y)) };
            n.modifiers.AddRange(mods);
            if (linkTo != null) n.links.Add(linkTo.id);
            tree.nodes.Add(n);
            return n;
        }
    }
}
#endif
