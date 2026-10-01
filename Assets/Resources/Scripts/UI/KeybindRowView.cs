using TMPro;
using UnityEngine;
using UnityEngine.UI;

// แถว 1 ปุ่มในหน้า Controls: [ชื่อ action]  [ปุ่มที่ใช้อยู่ (คลิก = เปลี่ยน)]  / ชนกับ action อื่น = แสดง conflictState
public class KeybindRowView : MonoBehaviour
{
    public TextMeshProUGUI labelText;
    public Button keyButton;
    public TextMeshProUGUI keyText;
    [Tooltip("แสดงตอนปุ่มซ้ำกับ action อื่น (เว้นว่างได้)")]
    public GameObject conflictState;
    public Color normalKeyColor = Color.white;
    public Color waitingKeyColor = new Color(1f, 0.85f, 0.4f);
    public Color conflictKeyColor = new Color(1f, 0.45f, 0.4f);

    public KeyBindEntry Entry { get; private set; }
    public System.Action<KeybindRowView> Clicked;

    private void Awake()
    {
        if (keyButton != null) keyButton.onClick.AddListener(OnClick);
    }

    private void OnDestroy()
    {
        if (keyButton != null) keyButton.onClick.RemoveListener(OnClick);
    }

    private void OnClick() => Clicked?.Invoke(this);

    public void Set(KeyBindEntry entry, bool waiting, string waitingText, KeyBindEntry conflict)
    {
        Entry = entry;
        if (labelText != null) labelText.text = entry.label;
        if (keyText != null)
        {
            string display = entry.Display;
            keyText.text = waiting ? waitingText : string.IsNullOrEmpty(display) ? "-" : display;
            keyText.color = waiting ? waitingKeyColor : conflict != null ? conflictKeyColor : normalKeyColor;
        }
        if (conflictState != null) conflictState.SetActive(!waiting && conflict != null);
    }
}
