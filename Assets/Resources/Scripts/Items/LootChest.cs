using UnityEngine;

// กล่องรับของดรอปใน hideout: เดินเข้าระยะแล้วคลิก -> เปิดรายการของที่ดรอปมาจาก map (LootStashWindow)
// เดินออกนอกระยะ / กด Esc / ปุ่ม X = ปิด
// ต้องมี Collider (เป้าคลิก) ระบบคลิก/ระยะอยู่ใน ClickInteractable
public class LootChest : ClickInteractable
{
    [Tooltip("เว้นว่าง = หา/สร้าง LootStashWindow ใน scene เดียวกันให้เอง")]
    public LootStashWindow window;
    [Tooltip("แสดงตอนกล่องมีของรออยู่ เช่น แสง/ป้าย (เว้นว่างได้)")]
    public GameObject hasLootIndicator;

    private ILootStashService _stash;

    protected override void Awake()
    {
        base.Awake();
        _stash = GameServices.LootStash;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        _stash.Changed += OnStashChanged;
        RefreshIndicator();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        _stash.Changed -= OnStashChanged;
        if (window != null && window.Opener == this) window.Close();
    }

    protected override void Update()
    {
        base.Update();
        if (window != null && window.IsOpen && window.Opener == this && !PlayerInRange) window.Close();
    }

    protected override void Interact()
    {
        if (window == null) window = LootStashWindow.FindOrCreate(this);
        if (window.IsOpen && window.Opener == this) window.Close();
        else window.Open(this, _stash, GameServices.LocalPlayerId);
    }

    private void OnStashChanged(string ownerId)
    {
        if (ownerId == GameServices.LocalPlayerId) RefreshIndicator();
    }

    private void RefreshIndicator()
    {
        if (hasLootIndicator == null) return;
        bool any = _stash.GetPending(GameServices.LocalPlayerId).Count > 0;
        if (hasLootIndicator.activeSelf != any) hasLootIndicator.SetActive(any);
    }
}
