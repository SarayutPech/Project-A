using TMPro;
using UnityEngine;
using UnityEngine.UI;

// กล่องถามยืนยัน (ตัวเดียวต่อเครื่อง) เช่น "ทิ้ง Life Flask?" [ยืนยัน] [ยกเลิก]
// แบบเลือกจำนวน (AskAmount): มี slider + ช่องพิมพ์ตัวเลข 1..max เช่นทิ้งของ stack บางส่วน
// เปิดอยู่ = มีพื้นทึบเต็มจอกันคลิกทะลุ / Esc, โหลด scene, กดยกเลิก = ไม่ทำอะไร
public class ConfirmDialog : UIWindow<ConfirmDialog>
{
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI messageText;
    public Button confirmButton;
    public TextMeshProUGUI confirmLabel;
    public Button cancelButton;

    [Header("Amount (เลือกจำนวน)")]
    [Tooltip("กลุ่มเลือกจำนวน (ซ่อนตอนถามธรรมดา)")]
    public GameObject amountGroup;
    public Slider amountSlider;
    public TMP_InputField amountField;
    public TextMeshProUGUI amountMaxText;

    private System.Action _onConfirm;
    private System.Action<int> _onConfirmAmount;
    private int _max = 1;

    public int Amount { get; private set; } = 1;

    protected override void Awake()
    {
        base.Awake();
        if (!IsInstance) return;
        if (confirmButton != null) confirmButton.onClick.AddListener(Confirm);
        if (cancelButton != null) cancelButton.onClick.AddListener(Close);
        if (amountSlider != null)
        {
            amountSlider.wholeNumbers = true;
            amountSlider.onValueChanged.AddListener(OnSliderChanged);
        }
        if (amountField != null)
        {
            amountField.contentType = TMP_InputField.ContentType.IntegerNumber;
            amountField.onEndEdit.AddListener(OnFieldChanged);
            amountField.onValueChanged.AddListener(OnFieldChanged);
        }
    }

    protected override void OnDestroy()
    {
        if (confirmButton != null) confirmButton.onClick.RemoveListener(Confirm);
        if (cancelButton != null) cancelButton.onClick.RemoveListener(Close);
        if (amountSlider != null) amountSlider.onValueChanged.RemoveListener(OnSliderChanged);
        if (amountField != null)
        {
            amountField.onEndEdit.RemoveListener(OnFieldChanged);
            amountField.onValueChanged.RemoveListener(OnFieldChanged);
        }
        base.OnDestroy();
    }

    // ไม่มี dialog ใน prefab -> ทำทันที (ไม่บล็อกการเล่น)
    public static void Ask(string title, string message, string confirmText, System.Action onConfirm)
    {
        if (Instance == null)
        {
            onConfirm?.Invoke();
            return;
        }
        Instance.Show(title, message, confirmText, onConfirm, null, 1);
    }

    // ถามจำนวน 1..max (เริ่มที่ max) ยืนยันแล้วส่งจำนวนที่เลือกกลับ
    public static void AskAmount(string title, string message, int max, string confirmText, System.Action<int> onConfirm)
    {
        if (Instance == null)
        {
            onConfirm?.Invoke(Mathf.Max(1, max));
            return;
        }
        Instance.Show(title, message, confirmText, null, onConfirm, max);
    }

    private void Show(string title, string message, string confirmText, System.Action onConfirm, System.Action<int> onAmount, int max)
    {
        _onConfirm = onConfirm;
        _onConfirmAmount = onAmount;
        _max = Mathf.Max(1, max);
        if (titleText != null) titleText.text = title;
        if (messageText != null) messageText.text = message;
        if (confirmLabel != null) confirmLabel.text = string.IsNullOrEmpty(confirmText) ? "OK" : confirmText;

        bool amountMode = onAmount != null;
        if (amountGroup != null) amountGroup.SetActive(amountMode);
        if (amountSlider != null)
        {
            amountSlider.minValue = 1;
            amountSlider.maxValue = _max;
        }
        if (amountMaxText != null) amountMaxText.text = $"/ {_max}";
        SetAmount(_max);
        Open();
    }

    private void SetAmount(int value)
    {
        Amount = Mathf.Clamp(value, 1, _max);
        if (amountSlider != null) amountSlider.SetValueWithoutNotify(Amount);
        if (amountField != null && amountField.text != Amount.ToString()) amountField.SetTextWithoutNotify(Amount.ToString());
    }

    private void OnSliderChanged(float v) => SetAmount(Mathf.RoundToInt(v));

    private void OnFieldChanged(string text)
    {
        // กำลังพิมพ์ (ช่องว่าง) ยังไม่บังคับค่า / พิมพ์เกิน max = ปัดลง
        if (int.TryParse(text, out int v)) SetAmount(v);
    }

    private void Confirm()
    {
        var action = _onConfirm;
        var amountAction = _onConfirmAmount;
        int amount = Amount;
        Close();
        action?.Invoke();
        amountAction?.Invoke(amount);
    }

    protected override void OnOpenChanged(bool open)
    {
        if (open) return;
        _onConfirm = null; // ปิดโดยไม่กดยืนยัน = ยกเลิก
        _onConfirmAmount = null;
    }
}
