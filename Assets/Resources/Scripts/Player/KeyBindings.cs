using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// ปุ่มที่ผู้เล่นตั้งเองได้ (setting ของเครื่องนี้ ไม่ส่งไป server) ทุกระบบที่อ่าน input ลงทะเบียน InputAction ของตัวเองที่นี่
// หน้า Controls (KeybindWindow) แสดง/rebind จากรายการนี้ -> เซฟเป็น override ใน PlayerPrefs แล้ว apply ให้ตอนลงทะเบียนครั้งถัดไป
// rebind เฉพาะ binding คีย์บอร์ด/เมาส์ "ตัวแรก" ของแต่ละ action (composite เช่น Move = แยก Up/Down/Left/Right) / gamepad ไม่แตะ
public class KeyBindEntry
{
    public string id;        // ชื่อ action / ชื่อ part (คงที่ ใช้เป็น key เซฟ)
    public string label;     // ชื่อที่แสดง
    public string category;  // หัวข้อในหน้า Controls
    public InputAction action;
    public int bindingIndex;

    public string Path => action.bindings[bindingIndex].effectivePath;
    public string Display => action.GetBindingDisplayString(bindingIndex, InputBinding.DisplayStringOptions.DontIncludeInteractions);
}

public static class KeyBindings
{
    public const string Movement = "Movement";
    public const string Skills = "Skills";
    public const string Interface = "Interface";
    // ลำดับหัวข้อในหน้า Controls (หัวข้ออื่นต่อท้าย)
    public static readonly string[] CategoryOrder = { Movement, Skills, Interface };

    private const string PrefsKey = "KeyBindings";

    [System.Serializable]
    private class SavedBinding
    {
        public string id;
        public string path;
    }

    [System.Serializable]
    private class SaveData
    {
        public List<SavedBinding> bindings = new List<SavedBinding>();
    }

    private static readonly List<KeyBindEntry> _entries = new List<KeyBindEntry>();
    private static Dictionary<string, string> _saved;
    private static InputActionRebindingExtensions.RebindingOperation _op;

    public static IReadOnlyList<KeyBindEntry> Entries => _entries;
    public static event System.Action Changed;
    public static bool IsRebinding => _op != null;
    // entry ที่กำลังรอกดปุ่มใหม่ (null = ไม่ได้ rebind)
    public static KeyBindEntry RebindingEntry { get; private set; }
    // เฟรมที่ rebind จบ (Esc ที่ใช้ยกเลิก rebind ไม่ใช่การปิดเมนู)
    public static int LastRebindFrame { get; private set; } = -10;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _entries.Clear();
        _saved = null;
        _op = null;
        RebindingEntry = null;
        Changed = null;
        LastRebindFrame = -10;
    }

    // ลงทะเบียน action (เรียกตอน OnEnable) แล้ว apply ปุ่มที่เซฟไว้ / ลงซ้ำ = ข้าม
    public static void Register(string category, InputAction action, string label = null)
    {
        if (action == null) return;
        foreach (var e in _entries)
            if (e.action == action) return;
        LoadSaved();

        var bindings = action.bindings;
        bool added = false;
        for (int i = 0; i < bindings.Count && !added; i++)
        {
            var b = bindings[i];
            if (b.isComposite)
            {
                // composite แรกที่ part เป็นคีย์บอร์ด/เมาส์ -> แยกทุก part
                if (i + 1 >= bindings.Count || !IsKeyboardMouse(bindings[i + 1].path)) continue;
                for (int p = i + 1; p < bindings.Count && bindings[p].isPartOfComposite; p++)
                    Add(category, action, p, $"{label ?? action.name} {Capitalize(bindings[p].name)}", $"{action.name}/{bindings[p].name}");
                added = true;
            }
            else if (!b.isPartOfComposite && IsKeyboardMouse(b.path))
            {
                Add(category, action, i, label ?? action.name, action.name);
                added = true;
            }
        }
        if (added) Changed?.Invoke();
    }

    public static void Unregister(InputAction action)
    {
        if (action == null) return;
        if (_op != null && _entries.Exists(e => e.action == action)) _op.Cancel();
        if (_entries.RemoveAll(e => e.action == action) > 0) Changed?.Invoke();
    }

    private static void Add(string category, InputAction action, int index, string label, string id)
    {
        if (_saved.TryGetValue(id, out var path)) action.ApplyBindingOverride(index, path);
        _entries.Add(new KeyBindEntry { id = id, label = label, category = category, action = action, bindingIndex = index });
    }

    // entry อื่นที่ใช้ปุ่มเดียวกัน (ไว้เตือนในหน้า Controls) null = ไม่ชน
    public static KeyBindEntry FindConflict(KeyBindEntry entry)
    {
        string path = entry.Path;
        if (string.IsNullOrEmpty(path)) return null;
        foreach (var e in _entries)
            if (e != entry && string.Equals(e.Path, path, System.StringComparison.OrdinalIgnoreCase)) return e;
        return null;
    }

    // รอกดปุ่มใหม่ (Esc = ยกเลิก) done(true) = เปลี่ยนแล้ว
    public static void StartRebind(KeyBindEntry entry, System.Action<bool> done = null)
    {
        if (_op != null || entry == null) return;
        var action = entry.action;
        bool wasEnabled = action.enabled;
        action.Disable(); // rebind ได้เฉพาะ action ที่ปิดอยู่

        var op = action.PerformInteractiveRebinding(entry.bindingIndex)
            .WithControlsHavingToMatchPath("<Keyboard>")
            .WithControlsHavingToMatchPath("<Mouse>")
            .WithControlsExcluding("<Mouse>/position")
            .WithControlsExcluding("<Mouse>/delta")
            .WithControlsExcluding("<Mouse>/scroll")
            .WithControlsExcluding("<Mouse>/press") // ซ้ำกับ leftButton
            .WithControlsExcluding("<Keyboard>/anyKey")
            .WithCancelingThrough("<Keyboard>/escape")
            .OnMatchWaitForAnother(0.05f);
        if (action.bindings[entry.bindingIndex].isPartOfComposite) op.WithExpectedControlType("Button");
        op.OnComplete(_ => Finish(true)).OnCancel(_ => Finish(false));
        _op = op;
        RebindingEntry = entry;
        Changed?.Invoke();
        op.Start();

        void Finish(bool ok)
        {
            _op?.Dispose();
            _op = null;
            RebindingEntry = null;
            LastRebindFrame = Time.frameCount;
            if (wasEnabled) action.Enable();
            if (ok) Save();
            Changed?.Invoke();
            done?.Invoke(ok);
        }
    }

    public static void CancelRebind() => _op?.Cancel();

    // กลับเป็นปุ่มเริ่มต้นทั้งหมด
    public static void ResetAll()
    {
        CancelRebind();
        foreach (var e in _entries) e.action.RemoveBindingOverride(e.bindingIndex);
        LoadSaved();
        _saved.Clear();
        PlayerPrefs.DeleteKey(PrefsKey);
        Changed?.Invoke();
    }

    private static void LoadSaved()
    {
        if (_saved != null) return;
        _saved = new Dictionary<string, string>();
        string json = PlayerPrefs.GetString(PrefsKey, "");
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var data = JsonUtility.FromJson<SaveData>(json);
            if (data != null)
                foreach (var b in data.bindings)
                    if (!string.IsNullOrEmpty(b.id)) _saved[b.id] = b.path;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[{nameof(KeyBindings)}] อ่านปุ่มที่เซฟไว้ไม่ได้: {ex.Message}");
        }
    }

    // เซฟเฉพาะที่ต่างจากค่าเริ่มต้น (entry ที่ไม่ได้ลงทะเบียนตอนนี้ยังเก็บค่าเดิมไว้)
    private static void Save()
    {
        LoadSaved();
        foreach (var e in _entries)
        {
            string over = e.action.bindings[e.bindingIndex].overridePath;
            if (string.IsNullOrEmpty(over)) _saved.Remove(e.id);
            else _saved[e.id] = over;
        }
        var data = new SaveData();
        foreach (var kv in _saved) data.bindings.Add(new SavedBinding { id = kv.Key, path = kv.Value });
        PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(data));
        PlayerPrefs.Save();
    }

    private static bool IsKeyboardMouse(string path) =>
        !string.IsNullOrEmpty(path) && (path.StartsWith("<Keyboard>") || path.StartsWith("<Mouse>"));

    private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
}
