using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// หน้าเลือก/สร้างตัวละคร (scene CharacterSelect) presentation ล้วน: อ่านรายชื่อจาก ICharacterService ส่งคำขอสร้าง/ลบผ่าน CharacterCreation
// เลือก: slot ตาม config.maxCharacterSlots (ช่องว่าง = สร้างใหม่) / Play หรือดับเบิลคลิก = ตั้ง GameServices.LocalPlayerId แล้วโหลด playScene
// สร้าง: RaceCarousel เลือก race + ช่องชื่อ + ปุ่ม Create (Enter ในช่องชื่อ = Create) / ลบ = กด Delete 2 ครั้ง
// prefab ต้นแบบสร้างจาก Tools > Project-A > Build Character Select UI (race เพิ่ม/ลดที่ CharacterCreationConfig ไม่ต้องแก้ UI)
public class CharacterSelectScreen : MonoBehaviour
{
    [Tooltip("ว่าง = โหลดจาก Resources/" + CharacterCreationConfig.DefaultResourcePath)]
    public CharacterCreationConfig config;
    public string playScene = "Hideout";
    [Tooltip("พื้นหลังทั้งจอ (เปลี่ยนตาม CharacterRace.background ถ้ามี)")]
    public Image background;

    [Header("Select")]
    public GameObject selectPanel;
    public RectTransform slotListContent;
    [Tooltip("แถวต้นแบบ (ปิดไว้)")]
    public CharacterSlotView slotTemplate;
    public TextMeshProUGUI slotCountText;
    public Button playButton;
    public Button deleteButton;
    public TextMeshProUGUI deleteLabel;

    [Header("Create")]
    public GameObject createPanel;
    public RaceCarousel carousel;
    public TextMeshProUGUI raceNameText;
    public TextMeshProUGUI raceDescriptionText;
    public TMP_InputField nameField;
    public Button createButton;
    public Button backButton;
    public TextMeshProUGUI errorText;

    [Header("Keys (หน้าสร้าง)")]
    public InputAction prevRace = new InputAction("Prev Race", InputActionType.Button, "<Keyboard>/leftArrow");
    public InputAction nextRace = new InputAction("Next Race", InputActionType.Button, "<Keyboard>/rightArrow");

    private const string LastCharacterKey = "CharacterSelect.Last"; // จำตัวที่เล่นล่าสุด (ความสะดวกของเครื่องนี้เท่านั้น)
    private static string AccountId => GameServices.LocalAccountId;

    private readonly List<CharacterSlotView> _slots = new List<CharacterSlotView>();
    private readonly List<CharacterRace> _races = new List<CharacterRace>();
    private IReadOnlyList<CharacterInfo> _characters = System.Array.Empty<CharacterInfo>();
    private string _selectedId;
    private string _deleteArmedId;
    private Sprite _defaultBackground;
    private Color _defaultBackgroundColor = Color.white;

    private void Awake()
    {
        if (config == null) config = CharacterCreationConfig.LoadDefault();
        if (config == null) Debug.LogError($"[{nameof(CharacterSelectScreen)}] ไม่มี CharacterCreationConfig ที่ Resources/{CharacterCreationConfig.DefaultResourcePath}", this);
        if (background != null)
        {
            _defaultBackground = background.sprite;
            _defaultBackgroundColor = background.color;
        }
        if (slotTemplate != null) slotTemplate.gameObject.SetActive(false);

        if (playButton != null) playButton.onClick.AddListener(Play);
        if (deleteButton != null) deleteButton.onClick.AddListener(Delete);
        if (createButton != null) createButton.onClick.AddListener(Create);
        if (backButton != null) backButton.onClick.AddListener(ShowSelect);
        if (nameField != null)
        {
            nameField.onSubmit.AddListener(OnNameSubmit);
            nameField.onValueChanged.AddListener(OnNameChanged);
            if (config != null) nameField.characterLimit = config.nameMaxLength;
        }
        if (carousel != null) carousel.SelectionChanged += OnRaceChanged;
    }

    private void OnDestroy()
    {
        if (playButton != null) playButton.onClick.RemoveListener(Play);
        if (deleteButton != null) deleteButton.onClick.RemoveListener(Delete);
        if (createButton != null) createButton.onClick.RemoveListener(Create);
        if (backButton != null) backButton.onClick.RemoveListener(ShowSelect);
        if (nameField != null)
        {
            nameField.onSubmit.RemoveListener(OnNameSubmit);
            nameField.onValueChanged.RemoveListener(OnNameChanged);
        }
        if (carousel != null) carousel.SelectionChanged -= OnRaceChanged;
    }

    private void OnEnable()
    {
        prevRace.Enable();
        nextRace.Enable();
    }

    private void OnDisable()
    {
        prevRace.Disable();
        nextRace.Disable();
    }

    private void Start()
    {
        if (config == null) return;
        _races.Clear();
        foreach (var r in config.races)
            if (r != null) _races.Add(r);

        _selectedId = PlayerPrefs.GetString(LastCharacterKey, null);
        RefreshCharacters();
        // ยังไม่มีตัวละคร -> ไปหน้าสร้างเลย
        if (_characters.Count == 0) ShowCreate();
        else ShowSelect();
    }

    private void Update()
    {
        if (createPanel == null || !createPanel.activeInHierarchy || carousel == null) return;
        if (nameField != null && nameField.isFocused) return; // ลูกศรเลื่อน caret ในช่องชื่อ
        if (prevRace.WasPressedThisFrame()) carousel.Prev();
        if (nextRace.WasPressedThisFrame()) carousel.Next();
    }

    // ---------- หน้าเลือก ----------

    public void ShowSelect()
    {
        if (selectPanel != null) selectPanel.SetActive(true);
        if (createPanel != null) createPanel.SetActive(false);
        RefreshCharacters();
    }

    private void RefreshCharacters()
    {
        if (config == null) return;
        _characters = GameServices.Character.ListCharacters(AccountId);
        if (!Contains(_selectedId)) _selectedId = _characters.Count > 0 ? _characters[0].id : null;
        if (!Contains(_deleteArmedId)) _deleteArmedId = null;
        if (slotTemplate == null || slotListContent == null) return;

        // แสดงครบทุก slot (ช่องว่าง = สร้างใหม่) / ตัวละครเกิน slot (ลด max ทีหลัง) ยังแสดงครบ
        int rows = Mathf.Max(config.maxCharacterSlots, _characters.Count);
        while (_slots.Count < rows)
        {
            var slot = Instantiate(slotTemplate, slotListContent);
            slot.Clicked = OnSlotClicked;
            slot.DoubleClicked = OnSlotDoubleClicked;
            _slots.Add(slot);
        }
        for (int i = 0; i < _slots.Count; i++)
        {
            bool used = i < rows;
            _slots[i].gameObject.SetActive(used);
            if (!used) continue;
            if (i < _characters.Count)
            {
                _slots[i].SetCharacter(_characters[i], config.GetRace(_characters[i].raceId));
                _slots[i].SetSelected(_characters[i].id == _selectedId);
            }
            else _slots[i].SetEmpty();
        }

        if (slotCountText != null) slotCountText.text = $"Characters {_characters.Count} / {config.maxCharacterSlots}";
        bool hasSelection = _selectedId != null;
        if (playButton != null) playButton.interactable = hasSelection;
        if (deleteButton != null) deleteButton.interactable = hasSelection;
        if (deleteLabel != null) deleteLabel.text = _deleteArmedId != null ? "Confirm Delete" : "Delete";

        if (selectPanel == null || selectPanel.activeSelf) SetBackground(SelectedRace());
    }

    private bool Contains(string id)
    {
        if (id == null) return false;
        foreach (var c in _characters)
            if (c.id == id) return true;
        return false;
    }

    private CharacterRace SelectedRace()
    {
        foreach (var c in _characters)
            if (c.id == _selectedId) return config.GetRace(c.raceId);
        return null;
    }

    private void OnSlotClicked(CharacterSlotView slot)
    {
        if (slot.IsEmpty)
        {
            ShowCreate();
            return;
        }
        if (slot.CharacterId != _deleteArmedId) _deleteArmedId = null;
        _selectedId = slot.CharacterId;
        RefreshCharacters();
    }

    private void OnSlotDoubleClicked(CharacterSlotView slot)
    {
        _selectedId = slot.CharacterId;
        Play();
    }

    public void Play()
    {
        if (_selectedId == null || SceneTransition.IsTransitioning) return;
        GameServices.LocalPlayerId = _selectedId;
        PlayerPrefs.SetString(LastCharacterKey, _selectedId);
        GameServices.SaveAll();
        SceneTransition.Load(playScene);
    }

    // กด 1 ครั้ง = ถามยืนยัน (ปุ่มเปลี่ยนเป็น Confirm Delete) / กดซ้ำ = ลบ
    public void Delete()
    {
        if (_selectedId == null) return;
        if (_deleteArmedId != _selectedId)
        {
            _deleteArmedId = _selectedId;
            RefreshCharacters();
            return;
        }
        CharacterCreation.TryDelete(AccountId, _selectedId);
        _deleteArmedId = null;
        _selectedId = null;
        RefreshCharacters();
    }

    // ---------- หน้าสร้าง ----------

    public void ShowCreate()
    {
        if (_characters.Count >= config.maxCharacterSlots)
        {
            ShowSelect();
            return;
        }
        _deleteArmedId = null;
        if (selectPanel != null) selectPanel.SetActive(false);
        if (createPanel != null) createPanel.SetActive(true);
        if (nameField != null) nameField.text = "";
        SetError(_races.Count == 0 ? "No races in CharacterCreationConfig" : null);
        // ยังไม่มีตัวละคร = ปุ่ม Back ไม่มีที่ให้กลับ (หน้าเลือกว่างเปล่า) แต่ยังให้กดได้
        if (carousel != null)
        {
            int keep = carousel.SelectedIndex >= 0 && carousel.Count == _races.Count ? carousel.SelectedIndex : 0;
            carousel.SetRaces(_races, keep);
            OnRaceChanged(carousel.SelectedIndex);
        }
    }

    private void OnRaceChanged(int index)
    {
        var race = index >= 0 && index < _races.Count ? _races[index] : null;
        if (raceNameText != null)
        {
            raceNameText.text = race != null ? race.DisplayName : "";
            if (race != null) raceNameText.color = race.accentColor;
        }
        if (raceDescriptionText != null) raceDescriptionText.text = race != null ? race.description : "";
        if (createPanel == null || createPanel.activeSelf) SetBackground(race);
    }

    private void OnNameSubmit(string _) => Create();

    private void OnNameChanged(string _) => SetError(null);

    public void Create()
    {
        if (carousel == null || carousel.SelectedIndex < 0 || carousel.SelectedIndex >= _races.Count) return;
        string raceId = _races[carousel.SelectedIndex].Id;
        string name = nameField != null ? nameField.text : "";
        if (!CharacterCreation.TryCreate(GameServices.Character, config, AccountId, name, raceId, out var created, out var error))
        {
            SetError(error);
            return;
        }
        _selectedId = created.id;
        ShowSelect();
    }

    private void SetError(string message)
    {
        if (errorText == null) return;
        errorText.text = message ?? "";
        errorText.gameObject.SetActive(!string.IsNullOrEmpty(message));
    }

    private void SetBackground(CharacterRace race)
    {
        if (background == null) return;
        var sprite = race != null && race.background != null ? race.background : _defaultBackground;
        background.sprite = sprite;
        // พื้นต้นแบบเป็นสีทึบ (ไม่มีรูป) -> รูปของ race แสดงสีจริง
        background.color = sprite == _defaultBackground ? _defaultBackgroundColor : Color.white;
    }
}
