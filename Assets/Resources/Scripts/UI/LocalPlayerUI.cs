using UnityEngine;

// ใส่บน player: ถ้าตัวนี้เป็น local player -> สร้าง GameUI (ตัวเดียวต่อเครื่อง) แล้วผูกกับตัวนี้
// server แบบ headless ไม่สร้าง UI (กฎข้อ 2) / ภายหลังทำ Netcode: สร้างเฉพาะตัวที่ IsOwner
[DisallowMultipleComponent]
public class LocalPlayerUI : MonoBehaviour
{
    [Tooltip("เว้นว่าง = โหลดจาก Resources/" + GameUI.ResourcePath)]
    public GameUI prefab;

    private GameUI _ui;

    private void Start()
    {
        if (Application.isBatchMode) return;

        _ui = GameUI.Instance;
        if (_ui == null)
        {
            var source = prefab != null ? prefab : Resources.Load<GameUI>(GameUI.ResourcePath);
            if (source == null)
            {
                Debug.LogWarning($"[{nameof(LocalPlayerUI)}] ไม่พบ prefab GameUI (สร้างจากเมนู Tools > Project-A > Build UI Template)", this);
                return;
            }
            _ui = Instantiate(source);
            _ui.name = source.name;
        }
        _ui.Bind(gameObject);
    }

    private void OnDestroy()
    {
        if (_ui != null && _ui.BoundPlayer == gameObject) Destroy(_ui.gameObject);
    }
}
