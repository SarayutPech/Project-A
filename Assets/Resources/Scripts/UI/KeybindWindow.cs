using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// หน้า Controls (เปิดจาก PauseMenu): รายการปุ่มจาก KeyBindings แยกหัวข้อ (Movement / Skills / Interface)
// คลิกปุ่มในแถว = รอกดปุ่มใหม่ (Esc = ยกเลิก) / ปุ่มชนกัน = ตัวแดง (ยังใช้ได้ทั้งคู่ ให้ผู้เล่นแก้เอง) / Reset = ค่าเริ่มต้นทั้งหมด
public class KeybindWindow : UIWindow<KeybindWindow>
{
    public RectTransform listContent;
    [Tooltip("แถวต้นแบบ (ปิดไว้)")]
    public KeybindRowView rowTemplate;
    [Tooltip("หัวข้อต้นแบบ (ปิดไว้)")]
    public TextMeshProUGUI headerTemplate;
    public Button resetButton;
    public string waitingText = "Press a key...";

    private readonly List<KeybindRowView> _rows = new List<KeybindRowView>();
    private readonly List<TextMeshProUGUI> _headers = new List<TextMeshProUGUI>();
    private readonly List<string> _categories = new List<string>();

    protected override void Awake()
    {
        base.Awake();
        if (!IsInstance) return;
        if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);
        if (headerTemplate != null) headerTemplate.gameObject.SetActive(false);
        if (resetButton != null) resetButton.onClick.AddListener(AskReset);
        KeyBindings.Changed += Refresh;
    }

    protected override void OnDestroy()
    {
        if (resetButton != null) resetButton.onClick.RemoveListener(AskReset);
        KeyBindings.Changed -= Refresh;
        base.OnDestroy();
    }

    protected override void OnOpenChanged(bool open)
    {
        if (open) Refresh();
        else KeyBindings.CancelRebind();
    }

    private void AskReset()
    {
        if (ConfirmDialog.Instance != null) ConfirmDialog.Ask("Reset Controls", "Reset all keys to default?", "Reset", KeyBindings.ResetAll);
        else KeyBindings.ResetAll();
    }

    private void OnRowClicked(KeybindRowView row)
    {
        if (KeyBindings.IsRebinding) KeyBindings.CancelRebind();
        else KeyBindings.StartRebind(row.Entry);
    }

    public void Refresh()
    {
        if (!IsOpen || listContent == null || rowTemplate == null) return;

        // หัวข้อตามลำดับ CategoryOrder แล้วหัวข้ออื่นที่มีคนลงทะเบียน
        _categories.Clear();
        _categories.AddRange(KeyBindings.CategoryOrder);
        foreach (var e in KeyBindings.Entries)
            if (!_categories.Contains(e.category)) _categories.Add(e.category);

        int row = 0, header = 0, sibling = 0;
        foreach (var category in _categories)
        {
            bool any = false;
            foreach (var e in KeyBindings.Entries)
            {
                if (e.category != category) continue;
                if (!any && headerTemplate != null)
                {
                    var h = GetHeader(header++);
                    h.text = category;
                    h.transform.SetSiblingIndex(sibling++);
                }
                any = true;
                var r = GetRow(row++);
                r.Set(e, KeyBindings.RebindingEntry == e, waitingText, KeyBindings.FindConflict(e));
                r.transform.SetSiblingIndex(sibling++);
            }
        }
        for (int i = row; i < _rows.Count; i++) _rows[i].gameObject.SetActive(false);
        for (int i = header; i < _headers.Count; i++) _headers[i].gameObject.SetActive(false);
    }

    private KeybindRowView GetRow(int i)
    {
        while (_rows.Count <= i)
        {
            var r = Instantiate(rowTemplate, listContent);
            r.Clicked = OnRowClicked;
            _rows.Add(r);
        }
        _rows[i].gameObject.SetActive(true);
        return _rows[i];
    }

    private TextMeshProUGUI GetHeader(int i)
    {
        while (_headers.Count <= i) _headers.Add(Instantiate(headerTemplate, listContent));
        _headers[i].gameObject.SetActive(true);
        return _headers[i];
    }
}
