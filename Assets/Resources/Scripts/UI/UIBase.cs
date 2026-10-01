using UnityEngine;
using UnityEngine.UI;

// UI ทุกส่วน (HUD, กระเป๋า, Character Sheet, tooltip) มีตัวเดียวต่อเครื่อง = singleton
// เป็น presentation ของ local player อย่างเดียว (กฎข้อ 3: simulation ไม่อ้าง UI / UI ไม่ใช่ "ผู้เล่นทุกคน")
// ตัวซ้ำ (เช่นวาง prefab ซ้อน) ถูกทำลายทิ้งตอน Awake
public abstract class UISingleton<T> : MonoBehaviour where T : UISingleton<T>
{
    public static T Instance { get; private set; }

    // ตัวนี้คือตัวจริง (ตัวซ้ำได้ false แล้วถูกทำลาย) คลาสลูกเช็คค่านี้ก่อนทำงานใน Awake
    protected bool IsInstance => Instance == this;

    protected virtual void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[{typeof(T).Name}] มีอยู่แล้ว ทำลายตัวซ้ำ '{name}'", this);
            Destroy(gameObject);
            return;
        }
        Instance = (T)this;
    }

    protected virtual void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}

// UI ที่ต้องรู้ว่า local player คือตัวไหน GameUI.Bind ส่งให้ทุกตัวใต้มัน
public interface IPlayerUI
{
    // player = null -> ยกเลิกการผูก (ต้อง -= event ทั้งหมด)
    void Bind(GameObject player);
}

// หน้าต่างที่เปิด/ปิดได้ทั้งหมด (ไม่รวม HUD ที่แสดงตลอด) GameUI ใช้ปิดทีเดียวตอนโหลด scene / กด Esc
public interface IUIWindow
{
    bool IsOpen { get; }
    void Close();
}

public static class UIWindows
{
    private static readonly System.Collections.Generic.List<IUIWindow> _all = new System.Collections.Generic.List<IUIWindow>();

    public static void Register(IUIWindow w) { if (!_all.Contains(w)) _all.Add(w); }
    public static void Unregister(IUIWindow w) => _all.Remove(w);

    public static bool AnyOpen
    {
        get
        {
            foreach (var w in _all) if (w.IsOpen) return true;
            return false;
        }
    }

    public static void CloseAll()
    {
        for (int i = _all.Count - 1; i >= 0; i--) _all[i].Close();
        if (ItemTooltip.Instance != null) ItemTooltip.Instance.Hide();
    }
}

// หน้าต่างเปิด/ปิดได้ (ซ่อนด้วย CanvasGroup ไม่ SetActive -> ยังฟัง event/อัปเดตข้อมูลได้ตอนปิด)
[RequireComponent(typeof(CanvasGroup))]
public abstract class UIWindow<T> : UISingleton<T>, IUIWindow where T : UIWindow<T>
{
    [Tooltip("ปุ่มปิด (X) เว้นว่างได้")]
    public Button closeButton;
    public bool startOpen = false;

    public bool IsOpen { get; private set; }
    public event System.Action<T, bool> OpenChanged;

    private CanvasGroup _group;

    protected override void Awake()
    {
        base.Awake();
        if (!IsInstance) return;
        _group = GetComponent<CanvasGroup>();
        UIWindows.Register(this);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        SetOpen(startOpen);
    }

    protected override void OnDestroy()
    {
        if (IsInstance) UIWindows.Unregister(this);
        if (closeButton != null) closeButton.onClick.RemoveListener(Close);
        base.OnDestroy();
    }

    public void Open() => SetOpen(true);
    public void Close() => SetOpen(false);
    public void Toggle() => SetOpen(!IsOpen);

    public void SetOpen(bool open)
    {
        IsOpen = open;
        _group.alpha = open ? 1f : 0f;
        _group.interactable = open;
        _group.blocksRaycasts = open;
        if (open) transform.SetAsLastSibling();
        else if (ItemTooltip.Instance != null) ItemTooltip.Instance.Hide();
        OnOpenChanged(open);
        OpenChanged?.Invoke((T)this, open);
    }

    protected virtual void OnOpenChanged(bool open) { }
}
