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
    private const float InspectorWidth = 320f;

    private PassiveTree _tree;
    private Vector2 _pan;
    private float _zoom = 0.6f;
    private bool _snap = true;
    private float _grid = 20f;
    private int _selected = -1;
    private bool _dragging;
    private Vector2 _inspectorScroll;
    private SerializedObject _so;

    [MenuItem("Tools/Project-A/Passive Tree Editor")]
    public static void Open()
    {
        var w = GetWindow<PassiveTreeEditorWindow>("Passive Tree");
        w.minSize = new Vector2(700f, 400f);
        if (w._tree == null) w._tree = AssetDatabase.LoadAssetAtPath<PassiveTree>(DefaultAssetPath);
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

        var canvas = new Rect(0f, EditorStyles.toolbar.fixedHeight, position.width - InspectorWidth, position.height - EditorStyles.toolbar.fixedHeight);
        var inspector = new Rect(canvas.xMax, canvas.y, InspectorWidth, canvas.height);
        DrawCanvas(canvas);
        DrawInspector(inspector);
    }

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
            GUI.enabled = true;
            GUILayout.Space(10f);
            _snap = GUILayout.Toggle(_snap, "Snap", EditorStyles.toolbarButton);
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

    // ---------- Canvas ----------

    private Vector2 ToScreen(Rect canvas, Vector2 p) => canvas.center + _pan + new Vector2(p.x, -p.y) * _zoom;
    private Vector2 ToTree(Rect canvas, Vector2 s) { var v = (s - canvas.center - _pan) / _zoom; return new Vector2(v.x, -v.y); }

    private static float Radius(PassiveNodeType t) => t switch
    {
        PassiveNodeType.Notable => 22f,
        PassiveNodeType.Keystone => 30f,
        _ => 12f,
    };

    private static Color Fill(PassiveNode n) => n.type switch
    {
        PassiveNodeType.Notable => new Color(0.85f, 0.7f, 0.3f),
        PassiveNodeType.Keystone => new Color(0.6f, 0.35f, 0.85f),
        _ => new Color(0.55f, 0.55f, 0.55f),
    };

    private void DrawCanvas(Rect canvas)
    {
        EditorGUI.DrawRect(canvas, new Color(0.12f, 0.12f, 0.13f));
        GUI.BeginClip(canvas);
        var local = new Rect(0f, 0f, canvas.width, canvas.height);
        HandleInput(local, canvas);
        DrawGrid(local);

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
            float r = Radius(n.type) * Mathf.Max(0.4f, _zoom);
            if (!local.Overlaps(new Rect(c.x - r * 3f, c.y - r, r * 6f, r * 3f))) continue;
            if (n.isStart)
            {
                Handles.color = new Color(0.3f, 1f, 0.4f);
                Handles.DrawSolidDisc(c, Vector3.forward, r + 5f);
            }
            if (i == _selected)
            {
                Handles.color = Color.yellow;
                Handles.DrawSolidDisc(c, Vector3.forward, r + 3f);
            }
            Handles.color = Fill(n);
            Handles.DrawSolidDisc(c, Vector3.forward, r);
            Handles.color = new Color(0f, 0f, 0f, 0.6f);
            Handles.DrawWireDisc(c, Vector3.forward, r);
            // art ของ node (frame แล้ว icon ทับ)
            DrawSprite(n.frame, c, r);
            DrawSprite(n.icon, c, r * 0.75f);

            if (_zoom >= 0.35f || n.type != PassiveNodeType.Small)
            {
                string text = string.IsNullOrEmpty(n.displayName) ? n.id : n.displayName;
                GUI.Label(new Rect(c.x - 80f, c.y + r + 2f, 160f, 16f), text, label);
            }
        }
        Handles.EndGUI();
        GUI.EndClip();
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
            float r = Radius(n.type) * Mathf.Max(0.4f, _zoom) + 3f;
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
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Delete"), false, () => DeleteNode(index));
        }
        else
        {
            menu.AddItem(new GUIContent("Add Small"), false, () => AddNode(PassiveNodeType.Small, at, false));
            menu.AddItem(new GUIContent("Add Notable"), false, () => AddNode(PassiveNodeType.Notable, at, false));
            menu.AddItem(new GUIContent("Add Keystone"), false, () => AddNode(PassiveNodeType.Keystone, at, false));
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
            _tree.nodes.Add(node);
            _selected = _tree.nodes.Count - 1;
        });
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
        float w = position.width - InspectorWidth - 80f, h = position.height - 100f;
        Vector2 size = Vector2.Max(max - min, Vector2.one * 100f);
        _zoom = Mathf.Clamp(Mathf.Min(w / size.x, h / size.y), 0.1f, 3f);
        Vector2 c = (min + max) * 0.5f;
        _pan = -new Vector2(c.x, -c.y) * _zoom;
        Repaint();
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
