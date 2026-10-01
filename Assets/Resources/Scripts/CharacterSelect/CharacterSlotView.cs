using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// แถว 1 slot ในหน้าเลือกตัวละคร: มีตัวละคร = [รูป race] ชื่อ / Lv x Race, ว่าง = "Create New Character"
// คลิก = เลือก (ช่องว่าง = ไปหน้าสร้าง) / ดับเบิลคลิกตัวละคร = เข้าเกม
public class CharacterSlotView : MonoBehaviour, IPointerClickHandler
{
    public Image portrait;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI detailText;
    [Tooltip("แสดงตอนมีตัวละคร")]
    public GameObject filledState;
    [Tooltip("แสดงตอนช่องว่าง")]
    public GameObject emptyState;
    public GameObject selectedState;
    public Sprite fallbackPortrait;

    public bool IsEmpty { get; private set; }
    public string CharacterId { get; private set; }
    public System.Action<CharacterSlotView> Clicked;
    public System.Action<CharacterSlotView> DoubleClicked;

    public void SetCharacter(CharacterInfo info, CharacterRace race)
    {
        IsEmpty = false;
        CharacterId = info.id;
        if (nameText != null) nameText.text = info.name;
        if (detailText != null) detailText.text = $"Level {info.level}  {(race != null ? race.DisplayName : info.raceId)}";
        if (portrait != null)
        {
            portrait.sprite = race != null && race.portrait != null ? race.portrait : fallbackPortrait;
            portrait.enabled = portrait.sprite != null;
        }
        SetState();
    }

    public void SetEmpty()
    {
        IsEmpty = true;
        CharacterId = null;
        SetState();
    }

    public void SetSelected(bool selected)
    {
        if (selectedState != null && selectedState.activeSelf != selected) selectedState.SetActive(selected);
    }

    private void SetState()
    {
        if (filledState != null) filledState.SetActive(!IsEmpty);
        if (emptyState != null) emptyState.SetActive(IsEmpty);
        if (IsEmpty) SetSelected(false);
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        if (e.clickCount >= 2 && !IsEmpty) DoubleClicked?.Invoke(this);
        else Clicked?.Invoke(this);
    }
}
