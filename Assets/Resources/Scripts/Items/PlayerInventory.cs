using System.Collections.Generic;
using UnityEngine;

// กระเป๋าของ player (simulation ฝั่ง server): กติกาทั้งหมดของการเก็บ/ใส่/ถอด/ใช้ไอเทม แล้วค่อยเขียนลง IInventoryService
// - ของสวมใส่ / gem แยกช่องละชิ้น / ของกดใช้ / อื่นๆ stack ได้ถึง StackLimit / จำกัดด้วยน้ำหนักรวม (Byte) <= Carry Capacity ของ PlayerStats
// - ของที่สวมอยู่ / gem ที่ใส่ช่องสกิลแล้ว ไม่นับน้ำหนัก / น้ำหนักจำกัดแค่การเก็บของใหม่ (loot) ถอดของ/gem กลับกระเป๋าได้เสมอแม้เกิน
// UI เรียกแค่ Request* ด้วย instanceId / ช่อง (ภายหลัง = ServerRpc) server เช็คเองว่ามีของจริง ใส่ช่องนั้นได้ น้ำหนักพอ (กฎข้อ 7, 8)
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerEquipment))]
public class PlayerInventory : MonoBehaviour
{
    [Tooltip("ของที่ได้ตอนสร้างตัวละครใหม่ครั้งแรก (มีเซฟแล้วไม่ให้ซ้ำ) ของใน Starting Equipment ของ PlayerEquipment ก็ถูกสร้างเป็นชิ้นจริงตอนนั้นด้วย")]
    public List<ItemDefinition> startingItems = new List<ItemDefinition>();

    // กระเป๋า/ของที่สวมเปลี่ยน (UI ฟังตรงนี้)
    public event System.Action<PlayerInventory> Changed;

    public string OwnerId => GameServices.LocalPlayerId; // TODO Netcode: id ของเจ้าของตัวนี้
    public IReadOnlyList<ItemInstance> Items => _service != null ? _service.GetItems(OwnerId) : System.Array.Empty<ItemInstance>();
    public IReadOnlyList<EquippedItem> Equipped => _service != null ? _service.GetEquipped(OwnerId) : System.Array.Empty<EquippedItem>();
    public IReadOnlyList<SocketedGem> Socketed => _service != null ? _service.GetSocketed(OwnerId) : System.Array.Empty<SocketedGem>();
    public float Capacity => _stats != null ? _stats.CarryCapacity : 0f;
    public int UsedWeight
    {
        get
        {
            int total = 0;
            foreach (var item in Items) total += WeightOf(item.itemId) * item.Count;
            return total;
        }
    }

    private IInventoryService _service;
    private PlayerEquipment _equipment;
    private PlayerStats _stats;
    private PlayerConsumables _consumables;
    private PlayerSkills _skills;

    private void Awake()
    {
        _equipment = GetComponent<PlayerEquipment>();
        _stats = GetComponent<PlayerStats>();
        _consumables = GetComponent<PlayerConsumables>();
        _skills = GetComponent<PlayerSkills>();
    }

    private void OnEnable()
    {
        // ดึงตอน OnEnable (recompile ระหว่างเล่น Unity ไม่เรียก Awake ใหม่)
        _service = GameServices.Inventory;
        _service.Changed += OnServiceChanged;
        if (_stats != null) _stats.Changed += OnStatsChanged;
    }

    private void OnDisable()
    {
        if (_service != null) _service.Changed -= OnServiceChanged;
        if (_stats != null) _stats.Changed -= OnStatsChanged;
    }

    private void Start()
    {
        if (!_service.HasData(OwnerId)) CreateNewCharacter();
        SyncEquipment();
        SyncSkills();
    }

    public static int WeightOf(string itemId)
    {
        var def = ItemDatabase.Get(itemId);
        return def != null ? def.weight : 0;
    }

    public bool CanCarry(int extraWeight) => UsedWeight + extraWeight <= Capacity;

    public ItemInstance? GetEquipped(EquipSlot slot)
    {
        foreach (var e in Equipped)
            if (e.slot == slot) return e.item;
        return null;
    }

    // ---------- คำขอจาก client (server ตรวจทุกอย่าง) ----------

    // ใส่ชิ้นในกระเป๋า slot = null -> เลือกช่องเอง (แหวน: ช่องว่างก่อน)
    public bool RequestEquip(string instanceId, EquipSlot? slot = null)
    {
        if (!_service.TryGetItem(OwnerId, instanceId, out var inst)) return false;
        if (!(inst.Definition is EquipmentItem def)) return false;

        EquipSlot target = slot ?? DefaultSlot(def);
        if (!def.CanEquipIn(target)) return false;

        // สลับของ: ของเดิมกลับกระเป๋าเสมอ (น้ำหนักจำกัดแค่ตอนเก็บของเข้ากระเป๋า ไม่ล็อกการจัดของที่มีอยู่แล้ว)
        if (!_service.Equip(OwnerId, instanceId, target, out _)) return false;
        _equipment.Equip(def, target, out _);
        return true;
    }

    public bool RequestUnequip(EquipSlot slot)
    {
        var current = GetEquipped(slot);
        if (!current.HasValue) return false;
        if (!_service.Unequip(OwnerId, slot, out _)) return false;
        _equipment.Unequip(slot);
        return true;
    }

    // ใช้ของกดใช้ 1 ชิ้น (ใช้สำเร็จแล้วชิ้นนั้นหายไป)
    public bool RequestUse(string instanceId)
    {
        if (_consumables == null) return false;
        if (!_service.TryGetItem(OwnerId, instanceId, out var inst)) return false;
        if (!(inst.Definition is ConsumableItem def)) return false;
        if (!_consumables.RequestUse(def)) return false;
        return _service.RemoveCount(OwnerId, instanceId, 1);
    }

    // ทิ้งไอเทมทั้งช่อง (ทั้ง stack) ลบถาวร เช่นลากออกนอกหน้าต่างกระเป๋า
    public bool RequestDiscard(string instanceId) => _service.Remove(OwnerId, instanceId, out _);

    // ทิ้งบางส่วนของ stack (count >= จำนวนในช่อง = ทั้งช่อง)
    public bool RequestDiscard(string instanceId, int count)
    {
        if (!_service.TryGetItem(OwnerId, instanceId, out var inst) || count <= 0) return false;
        return count >= inst.Count ? _service.Remove(OwnerId, instanceId, out _) : _service.RemoveCount(OwnerId, instanceId, count);
    }

    // รวม stack + เรียงกระเป๋า (ข้อมูลผู้เล่น -> ทำใน service เป็น transaction เดียว)
    public void RequestSort() => _service.Sort(OwnerId);

    // เก็บของจากกล่องดรอป 1 drop (stack ใน drop แตกเป็นหลายชิ้น) น้ำหนักไม่พอ = ไม่เก็บทั้ง drop
    // TODO backend: claim + add ควรเป็น transaction เดียวฝั่ง backend (ตอนนี้ local ทำต่อกันทันทีจึงไม่หลุด)
    public bool RequestClaim(ILootStashService stash, string dropId)
    {
        LootDrop found = default;
        bool exists = false;
        foreach (var d in stash.GetPending(OwnerId))
        {
            if (d.dropId != dropId) continue;
            found = d;
            exists = true;
            break;
        }
        if (!exists || ItemDatabase.Get(found.item.itemId) == null) return false;
        if (!CanCarry(WeightOf(found.item.itemId) * Mathf.Max(1, found.item.count))) return false;

        if (!stash.TryClaim(OwnerId, dropId, out var drop)) return false;
        _service.Add(OwnerId, drop.item.itemId, Mathf.Max(1, drop.item.count));
        return true;
    }

    // เก็บทุก drop ที่น้ำหนักยังพอ คืนจำนวน drop ที่เก็บได้
    public int RequestClaimAll(ILootStashService stash)
    {
        var ids = new List<string>();
        foreach (var d in stash.GetPending(OwnerId)) ids.Add(d.dropId);
        int claimed = 0;
        foreach (var id in ids)
            if (RequestClaim(stash, id)) claimed++;
        return claimed;
    }

    // ---------- gem ในช่องสกิล ----------

    public ItemInstance? GetSocketed(int skillSlot, int socket)
    {
        foreach (var s in Socketed)
            if (s.skillSlot == skillSlot && s.socket == socket) return s.item;
        return null;
    }

    // ใส่ gem จากกระเป๋าลงช่องสกิล (socket = SocketedGem.ActiveSocket สำหรับ gem หลัก, 0-4 = support) ของเดิมกลับกระเป๋า
    public bool RequestSocketGem(string instanceId, int skillSlot, int socket)
    {
        if (_skills == null || _skills.GetSlot(skillSlot) == null) return false;
        if (!_service.TryGetItem(OwnerId, instanceId, out var inst)) return false;
        bool active = socket == SocketedGem.ActiveSocket;
        if (active ? !(inst.Definition is ActiveSkillGem) : !(inst.Definition is SupportGem)) return false;
        if (!active && (socket < 0 || socket >= PlayerSkills.MaxSupportSlots)) return false;

        if (!_service.Socket(OwnerId, instanceId, skillSlot, socket, out _)) return false;
        ApplySocket(skillSlot, socket, inst);
        return true;
    }

    public bool RequestUnsocketGem(int skillSlot, int socket)
    {
        var current = GetSocketed(skillSlot, socket);
        if (!current.HasValue) return false;
        if (!_service.Unsocket(OwnerId, skillSlot, socket, out _)) return false;
        ApplySocket(skillSlot, socket, null);
        return true;
    }

    // ---------- Internal ----------

    private void ApplySocket(int skillSlot, int socket, ItemInstance? item)
    {
        if (_skills == null) return;
        if (socket == SocketedGem.ActiveSocket)
            _skills.SetActiveGem(skillSlot, item.HasValue ? item.Value.Definition as ActiveSkillGem : null, item.HasValue ? item.Value.Level : 1);
        else
            _skills.SocketSupport(skillSlot, socket, item.HasValue ? item.Value.Definition as SupportGem : null, item.HasValue ? item.Value.Level : 1);
    }

    // ให้ PlayerSkills ตรงกับ gem ที่เซฟไว้ (ช่องที่ไม่มีในเซฟ = ว่าง)
    // ครั้งแรกที่ยังไม่มี gem เลยทั้งกระเป๋าและช่อง: gem ที่ตั้งไว้ใน Inspector ของ PlayerSkills กลายเป็นชิ้นจริงที่ใส่อยู่ (ชุดเริ่มต้น)
    private void SyncSkills()
    {
        if (_skills == null) return;
        if (!HasAnyGem()) SeedGemsFromSkills();

        for (int slot = 0; slot < _skills.SlotCount; slot++)
        {
            ApplySocket(slot, SocketedGem.ActiveSocket, GetSocketed(slot, SocketedGem.ActiveSocket));
            for (int s = 0; s < PlayerSkills.MaxSupportSlots; s++) ApplySocket(slot, s, GetSocketed(slot, s));
        }
    }

    private bool HasAnyGem()
    {
        if (Socketed.Count > 0) return true;
        foreach (var i in Items)
            if (i.Definition is SkillGem) return true;
        return false;
    }

    private void SeedGemsFromSkills()
    {
        for (int slot = 0; slot < _skills.SlotCount; slot++)
        {
            var s = _skills.GetSlot(slot);
            Seed(s.activeGem, slot, SocketedGem.ActiveSocket);
            if (s.supportGems == null) continue;
            for (int k = 0; k < s.supportGems.Length && k < PlayerSkills.MaxSupportSlots; k++) Seed(s.supportGems[k], slot, k);
        }

        void Seed(GemInstance g, int slot, int socket)
        {
            if (g.gem == null || ItemDatabase.Get(g.gem.id) == null) return;
            var inst = _service.Add(OwnerId, g.gem.id);
            _service.Socket(OwnerId, inst.instanceId, slot, socket, out _);
        }
    }

    private EquipSlot DefaultSlot(EquipmentItem def)
    {
        if (!def.CanEquipIn(EquipSlot.Ring)) return def.slot;
        return !GetEquipped(EquipSlot.Ring).HasValue ? EquipSlot.Ring
             : !GetEquipped(EquipSlot.Ring2).HasValue ? EquipSlot.Ring2
             : EquipSlot.Ring;
    }

    // ตัวละครใหม่: ของเริ่มต้นเข้ากระเป๋า + Starting Equipment ของ PlayerEquipment กลายเป็นชิ้นจริงที่สวมอยู่
    private void CreateNewCharacter()
    {
        _service.EnsureCreated(OwnerId);
        foreach (var def in startingItems)
            if (def != null) _service.Add(OwnerId, def.id);
        foreach (var e in _equipment.startingEquipment)
        {
            if (e.item == null) continue;
            var inst = _service.Add(OwnerId, e.item.id);
            _service.Equip(OwnerId, inst.instanceId, e.item.CanEquipIn(e.slot) ? e.slot : e.item.slot, out _);
        }
    }

    // ให้ PlayerEquipment (ตัวใส่ stat) ตรงกับที่เซฟไว้: ถอดช่องที่ไม่มีในเซฟ ใส่ช่องที่มี
    private void SyncEquipment()
    {
        var saved = new Dictionary<EquipSlot, EquipmentItem>();
        foreach (var e in Equipped)
            if (e.item.Definition is EquipmentItem def) saved[e.slot] = def;

        foreach (EquipSlot slot in System.Enum.GetValues(typeof(EquipSlot)))
        {
            saved.TryGetValue(slot, out var want);
            if (_equipment.Get(slot) == want) continue;
            if (want != null) _equipment.Equip(want, slot, out _);
            else _equipment.Unequip(slot);
        }
    }

    private void OnServiceChanged(string ownerId)
    {
        if (ownerId == OwnerId) Changed?.Invoke(this);
    }

    private void OnStatsChanged(PlayerStats stats) => Changed?.Invoke(this); // capacity อาจเปลี่ยน

    [ContextMenu("Give One Of Every Item")]
    private void DebugGiveAll()
    {
        foreach (var def in ItemDatabase.All) _service.Add(OwnerId, def.id);
    }

    [ContextMenu("Clear Bag")]
    private void DebugClear()
    {
        var ids = new List<string>();
        foreach (var i in Items) ids.Add(i.instanceId);
        foreach (var id in ids) _service.Remove(OwnerId, id, out _);
    }
}
