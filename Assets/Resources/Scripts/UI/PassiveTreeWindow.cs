using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// หน้าต่าง passive skill tree (กด P): node เล็ก / Notable / Keystone ตามตำแหน่งใน PassiveTree asset
// คลิกซ้าย = ลงแต้ม (หรือเลือกจุด start ตอนยังไม่ลงแต้ม) / คลิกขวา = คืนแต้ม / ลากพื้น = เลื่อน / ล้อเมาส์ = ซูม
// UI ส่งคำขอ PlayerPassives.Request* เท่านั้น (server ตรวจแต้ม/เส้นเชื่อม)
public class PassiveTreeWindow : UIWindow<PassiveTreeWindow>, IPlayerUI, IDragHandler, IScrollHandler
{
    [Header("View")]
    [Tooltip("กรอบตัดภาพ (RectMask2D)")]
    public RectTransform viewport;
    [Tooltip("ตัวเลื่อน/ซูม (anchor กลาง viewport) node กับเส้นอยู่ข้างใน")]
    public RectTransform content;
    [Tooltip("ที่วางเส้น (ลูกของ content อยู่ใต้ node)")]
    public RectTransform lineLayer;
    [Tooltip("ที่วาง node (ลูกของ content)")]
    public RectTransform nodeLayer;
    public Vector2 zoomRange = new Vector2(0.3f, 2f);
    [Min(0.01f)] public float zoomStep = 0.1f;

    [Header("Templates (ปิดไว้)")]
    public PassiveNodeView smallTemplate;
    public PassiveNodeView notableTemplate;
    public PassiveNodeView keystoneTemplate;
    public Image lineTemplate;
    [Min(1f)] public float lineWidth = 6f;
    public Color lineLocked = new Color(1f, 1f, 1f, 0.12f);
    public Color lineAvailable = new Color(1f, 1f, 1f, 0.35f);
    public Color lineAllocated = new Color(0.95f, 0.8f, 0.4f, 1f);

    [Header("Header")]
    public TextMeshProUGUI pointsText;
    public Button resetButton;

    private PlayerPassives _passives;
    private PassiveTree _builtTree;
    private float _zoom = 0.5f;
    private readonly Dictionary<string, PassiveNodeView> _views = new Dictionary<string, PassiveNodeView>();
    private readonly List<(Image line, string a, string b)> _lines = new List<(Image, string, string)>();
    private readonly StringBuilder _sb = new StringBuilder();

    protected override void Awake()
    {
        base.Awake();
        if (!IsInstance) return;
        foreach (var t in new Component[] { smallTemplate, notableTemplate, keystoneTemplate, lineTemplate })
            if (t != null) t.gameObject.SetActive(false);
        if (resetButton != null) resetButton.onClick.AddListener(AskReset);
    }

    protected override void OnDestroy()
    {
        if (resetButton != null) resetButton.onClick.RemoveListener(AskReset);
        base.OnDestroy();
    }

    public void Bind(GameObject player)
    {
        if (_passives != null) _passives.Changed -= OnChanged;
        _passives = player != null ? player.GetComponent<PlayerPassives>() : null;
        if (_passives != null) _passives.Changed += OnChanged;
        Refresh();
    }

    protected override void OnOpenChanged(bool open)
    {
        if (!open) return;
        Refresh();
        CenterOn(_passives != null ? _passives.StartNode : null);
    }

    private void OnChanged(PlayerPassives _) { if (IsOpen) Refresh(); }

    // ---------- สร้าง / อัปเดต ----------

    public void Refresh()
    {
        if (_passives == null || _passives.Tree == null) return;
        if (_builtTree != _passives.Tree) Build(_passives.Tree);

        foreach (var kv in _views)
        {
            string id = kv.Key;
            bool allocated = _passives.IsAllocated(id);
            bool available = _passives.CanAllocate(id) || _passives.CanChooseStart(id);
            var node = _passives.Tree.Get(id);
            kv.Value.SetState(allocated, available, node != null && node.isStart);
        }
        foreach (var (line, a, b) in _lines)
        {
            bool ia = _passives.IsAllocated(a), ib = _passives.IsAllocated(b);
            line.color = ia && ib ? lineAllocated : ia || ib ? lineAvailable : lineLocked;
        }
        if (pointsText != null)
            pointsText.text = $"Passive Points: <color=#FFF>{_passives.PointsLeft}</color> <size=75%><color=#999>/ {_passives.TotalPoints}</color></size>";
    }

    private void Build(PassiveTree tree)
    {
        foreach (var v in _views.Values) if (v != null) Destroy(v.gameObject);
        foreach (var (line, _, _) in _lines) if (line != null) Destroy(line.gameObject);
        _views.Clear();
        _lines.Clear();
        _builtTree = tree;

        // เส้น (คู่ละครั้ง)
        var done = new HashSet<(string, string)>();
        foreach (var n in tree.nodes)
        {
            foreach (var other in tree.Neighbors(n.id))
            {
                var key = string.CompareOrdinal(n.id, other) < 0 ? (n.id, other) : (other, n.id);
                if (!done.Add(key) || lineTemplate == null) continue;
                var o = tree.Get(other);
                var line = Instantiate(lineTemplate, lineLayer != null ? lineLayer : content);
                line.gameObject.SetActive(true);
                line.raycastTarget = false;
                PlaceLine(line.rectTransform, n.position, o.position);
                _lines.Add((line, n.id, other));
            }
        }

        // node
        foreach (var n in tree.nodes)
        {
            var template = n.type switch
            {
                PassiveNodeType.Notable => notableTemplate,
                PassiveNodeType.Keystone => keystoneTemplate,
                _ => smallTemplate,
            };
            if (template == null || string.IsNullOrEmpty(n.id)) continue;
            var view = Instantiate(template, nodeLayer != null ? nodeLayer : content);
            view.gameObject.SetActive(true);
            view.name = $"Node_{n.id}";
            var rt = (RectTransform)view.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = n.position;
            view.Init(n);
            view.Clicked = OnNodeClicked;
            view.RightClicked = OnNodeRightClicked;
            view.Hovered = OnNodeHovered;
            _views[n.id] = view;
        }
        ApplyZoom();
    }

    private void PlaceLine(RectTransform rt, Vector2 a, Vector2 b)
    {
        Vector2 d = b - a;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = (a + b) * 0.5f;
        rt.sizeDelta = new Vector2(d.magnitude, lineWidth);
        rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
    }

    // ---------- คลิก / tooltip ----------

    private void OnNodeClicked(PassiveNodeView v)
    {
        if (_passives == null) return;
        if (_passives.CanChooseStart(v.NodeId)) _passives.RequestChooseStart(v.NodeId);
        else if (!_passives.IsAllocated(v.NodeId) && !_passives.RequestAllocate(v.NodeId))
            UIToast.Show(_passives.PointsLeft <= 0 ? "No passive points left" : "Must connect to an allocated node");
    }

    private void OnNodeRightClicked(PassiveNodeView v)
    {
        if (_passives != null && _passives.IsAllocated(v.NodeId) && !_passives.RequestRefund(v.NodeId))
            UIToast.Show(v.NodeId == _passives.StartNode ? "Cannot refund the starting point" : "Other nodes depend on this");
    }

    private void OnNodeHovered(PassiveNodeView v)
    {
        if (ItemTooltip.Instance == null || _passives == null) return;
        var n = _passives.Tree.Get(v.NodeId);
        if (n == null) return;

        _sb.Clear();
        foreach (var m in n.modifiers)
        {
            if (_sb.Length > 0) _sb.Append('\n');
            _sb.Append("<color=#8888FF>").Append(StatMath.Describe(m)).Append("</color>");
        }
        if (!string.IsNullOrEmpty(n.description)) _sb.Append("\n\n<i><color=#A69980>").Append(n.description).Append("</color></i>");

        string hint = _passives.IsAllocated(v.NodeId)
            ? (v.NodeId == _passives.StartNode ? "Starting point" : _passives.CanRefund(v.NodeId) ? "Right-click to refund" : "Other nodes depend on this")
            : _passives.CanChooseStart(v.NodeId) ? "Click to start here"
            : _passives.CanAllocate(v.NodeId) ? "Click to allocate"
            : _passives.PointsLeft <= 0 ? "No passive points" : "Not connected";
        _sb.Append("\n\n<color=#8C8C8C>").Append(hint).Append("</color>");

        Color tint = n.type switch
        {
            PassiveNodeType.Keystone => new Color(0.75f, 0.5f, 1f),
            PassiveNodeType.Notable => new Color(0.95f, 0.8f, 0.4f),
            _ => Color.white,
        };
        string sub = n.isStart ? "Start" : n.type == PassiveNodeType.Small ? "Passive" : n.type.ToString();
        ItemTooltip.Instance.ShowText(string.IsNullOrEmpty(n.displayName) ? n.id : n.displayName, sub, _sb.ToString(), tint);
    }

    private void AskReset()
    {
        if (_passives == null || _passives.SpentPoints == 0) return;
        var p = _passives;
        ConfirmDialog.Ask("Reset Passives", $"Refund all {p.SpentPoints} passive points?", "Reset", p.RequestResetAll);
    }

    // ---------- เลื่อน / ซูม ----------

    public void OnDrag(PointerEventData e)
    {
        if (content == null) return;
        content.anchoredPosition += e.delta / CanvasScale();
    }

    public void OnScroll(PointerEventData e)
    {
        if (content == null || viewport == null) return;
        // ซูมรอบเคอร์เซอร์: จุดใต้เมาส์อยู่ที่เดิมหลังซูม
        RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, e.position, null, out Vector2 mouse);
        Vector2 before = (mouse - content.anchoredPosition) / _zoom;
        _zoom = Mathf.Clamp(_zoom + Mathf.Sign(e.scrollDelta.y) * zoomStep * _zoom, zoomRange.x, zoomRange.y);
        ApplyZoom();
        content.anchoredPosition = mouse - before * _zoom;
    }

    private void ApplyZoom()
    {
        if (content != null) content.localScale = Vector3.one * _zoom;
    }

    private void CenterOn(string id)
    {
        if (content == null || _passives == null || _passives.Tree == null) return;
        var n = id != null ? _passives.Tree.Get(id) : null;
        content.anchoredPosition = n != null ? -n.position * _zoom : Vector2.zero;
    }

    private float CanvasScale()
    {
        var c = GetComponentInParent<Canvas>();
        return c != null ? c.rootCanvas.scaleFactor : 1f;
    }
}
