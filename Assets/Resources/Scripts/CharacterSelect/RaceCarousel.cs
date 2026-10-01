using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// แถว race แนวนอน: กลางใหญ่สุด ถัดออกไปเล็กลง/มืดลง ริมสุดจางหายไปทางขอบจอ
// คลิกการ์ด (หรือปุ่ม < >) = การ์ดนั้นเด้ง (spring) มาตรงกลางและถูกเลือก / จำนวน race เท่าไรก็ได้ (การ์ด clone จาก cardTemplate)
// ระยะ / ขนาด / ความจาง / ความสว่าง ปรับด้วย curve: แกน X = ห่างจากกลางกี่ช่อง (0 = กลาง, 1 = ข้าง, 2 = ริม)
public class RaceCarousel : MonoBehaviour
{
    public RectTransform cardLayer;
    [Tooltip("การ์ดต้นแบบ (ปิดไว้) ขนาดของมัน = ขนาดการ์ดกลาง")]
    public RaceCardView cardTemplate;
    public Button prevButton;
    public Button nextButton;
    [Tooltip("วนรอบ (ซ้ายสุดต่อขวาสุด) ปิด = หยุดที่ปลาย")]
    public bool loop = true;

    [Header("Layout (X = ห่างจากกลางกี่ช่อง)")]
    [Tooltip("ระยะจากกลาง (px)")]
    public AnimationCurve offsetX = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 390f), new Keyframe(2f, 690f), new Keyframe(3f, 900f));
    public AnimationCurve scale = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.72f), new Keyframe(2f, 0.55f), new Keyframe(3f, 0.45f));
    public AnimationCurve alpha = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f), new Keyframe(2f, 0.5f), new Keyframe(2.6f, 0f));
    public AnimationCurve brightness = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.6f), new Keyframe(2f, 0.4f));

    [Header("Spring (damping ต่ำ = เด้งมาก)")]
    [Min(1f)] public float stiffness = 170f;
    [Min(0f)] public float damping = 15f;

    public event System.Action<int> SelectionChanged;
    public int SelectedIndex { get; private set; } = -1;
    public int Count => _cards.Count;

    private readonly List<RaceCardView> _cards = new List<RaceCardView>();
    private readonly List<RaceCardView> _order = new List<RaceCardView>();
    private float[] _distance = System.Array.Empty<float>();
    private float _pos, _target, _vel;

    private void Awake()
    {
        if (cardTemplate != null) cardTemplate.gameObject.SetActive(false);
        if (prevButton != null) prevButton.onClick.AddListener(Prev);
        if (nextButton != null) nextButton.onClick.AddListener(Next);
    }

    private void OnDestroy()
    {
        if (prevButton != null) prevButton.onClick.RemoveListener(Prev);
        if (nextButton != null) nextButton.onClick.RemoveListener(Next);
    }

    public void SetRaces(IReadOnlyList<CharacterRace> races, int selected)
    {
        if (cardTemplate == null || cardLayer == null) return;
        while (_cards.Count < races.Count)
        {
            var card = Instantiate(cardTemplate, cardLayer);
            card.Clicked = OnCardClicked;
            _cards.Add(card);
        }
        for (int i = _cards.Count - 1; i >= races.Count; i--)
        {
            Destroy(_cards[i].gameObject);
            _cards.RemoveAt(i);
        }
        for (int i = 0; i < _cards.Count; i++)
        {
            _cards[i].gameObject.SetActive(true);
            _cards[i].Set(races[i], i);
        }
        _distance = new float[_cards.Count];

        SelectedIndex = -1;
        if (_cards.Count == 0) return;
        _pos = _target = Mathf.Clamp(selected, 0, _cards.Count - 1);
        _vel = 0f;
        UpdateSelected();
        Layout();
    }

    public void Next() => Step(1);
    public void Prev() => Step(-1);

    public void Step(int dir)
    {
        if (_cards.Count == 0) return;
        _target = loop ? _target + dir : Mathf.Clamp(Mathf.Round(_target) + dir, 0f, _cards.Count - 1);
        UpdateSelected();
    }

    public void Select(int index)
    {
        if (index < 0 || index >= _cards.Count) return;
        // วน: ไปทางที่ใกล้กว่า
        float d = index - _target;
        if (loop) d = Mathf.Round(Wrap(d, _cards.Count));
        _target = loop ? Mathf.Round(_target + d) : index;
        UpdateSelected();
    }

    private void OnCardClicked(RaceCardView card) => Select(card.Index);

    private void UpdateSelected()
    {
        int n = _cards.Count;
        int index = ((Mathf.RoundToInt(_target) % n) + n) % n;
        if (index == SelectedIndex) return;
        SelectedIndex = index;
        SelectionChanged?.Invoke(index);
    }

    private void Update()
    {
        if (_cards.Count == 0) return;
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        if (Mathf.Abs(_target - _pos) > 0.0005f || Mathf.Abs(_vel) > 0.0005f)
        {
            _vel += ((_target - _pos) * stiffness - _vel * damping) * dt;
            _pos += _vel * dt;
        }
        else
        {
            _pos = _target;
            _vel = 0f;
        }
        Layout();
    }

    private void Layout()
    {
        int n = _cards.Count;
        _order.Clear();
        for (int i = 0; i < n; i++)
        {
            float o = i - _pos;
            if (loop && n > 1) o = Wrap(o, n);
            float a = Mathf.Abs(o);
            _distance[i] = a;

            var card = _cards[i];
            card.Rect.anchoredPosition = new Vector2(Mathf.Sign(o) * offsetX.Evaluate(a), 0f);
            float s = scale.Evaluate(a);
            card.Rect.localScale = new Vector3(s, s, 1f);
            card.SetLook(Mathf.Clamp01(alpha.Evaluate(a)), Mathf.Clamp01(brightness.Evaluate(a)), i == SelectedIndex);
            _order.Add(card);
        }

        // ใกล้กลาง = วาดทีหลัง (อยู่บน) -> การ์ดกลางทับข้างๆ และรับคลิกก่อน
        _order.Sort((x, y) => _distance[y.Index].CompareTo(_distance[x.Index]));
        for (int i = 0; i < _order.Count; i++)
            if (_order[i].transform.GetSiblingIndex() != i) _order[i].transform.SetSiblingIndex(i);
    }

    // o ให้อยู่ช่วง [-n/2, n/2)
    private static float Wrap(float o, int n) => Mathf.Repeat(o + n * 0.5f, n) - n * 0.5f;
}
