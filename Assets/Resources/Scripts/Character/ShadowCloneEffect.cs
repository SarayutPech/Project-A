using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// ร่างเงาช่วยตี (presentation อย่างเดียว server headless ปิดตัวเอง) วางบน Visual (ข้าง CharacterAnimator)
// จำนวน/ตำแหน่ง/ข้างของร่างอ่านจาก MeleeAttack (ActiveCloneCount / GetClonePosition / IsCloneMirrored) ซึ่งเป็นตัวที่ตีจริงฝั่ง server
// ภาพ: โมเดลเดียวกับตัวละคร ใส่ material เงาโปร่งแสง เล่นท่า "อีกท่า" ของสกิล (สุ่มต่างจากเรา) ช้ากว่าเรานิดนึง -> ดูเป็นภาพลวงตารุมต่อย
// ใช้ pool: สร้างครั้งแรกที่ต้องใช้ แล้วเปิด/ปิดซ้ำ (กฎข้อ 10) override controller / material ที่สร้างเองลบตอน OnDestroy
[RequireComponent(typeof(Animator))]
public class ShadowCloneEffect : MonoBehaviour
{
    [Tooltip("โมเดลที่ใช้เป็นร่างเงา (fbx ตัวเดียวกับตัวละคร มี Animator + SkinnedMeshRenderer)")]
    public GameObject cloneModel;
    [Tooltip("material ของร่างเงา (shader ProjectA/Afterimage) เว้นว่าง = สร้างให้")]
    public Material ghostMaterial;
    [ColorUsage(true, true)] public Color color = new Color(0.45f, 0.25f, 1f, 0.9f);
    [Tooltip("ชื่อ property สีใน ghost material")]
    public string colorProperty = "_Color";
    [Min(0.01f)] public float fadeInTime = 0.08f;
    [Min(0.01f)] public float fadeOutTime = 0.25f;
    [Tooltip("ร่างแต่ละตัวช้ากว่าร่างก่อนหน้าเท่านี้ (สัดส่วนของท่า) -> หมัดรัวเป็นจังหวะ")]
    [Range(0f, 0.3f)] public float timeDelay = 0.06f;
    [Tooltip("ความไวในการตามตำแหน่ง (ยิ่งมากยิ่งติดตัว)")]
    [Min(0f)] public float followSharpness = 18f;

    private static readonly int AttackState = Animator.StringToHash("Base Layer.Attack");
    private static readonly int AttackTimeHash = Animator.StringToHash("AttackTime");
    private static readonly int AttackMirrorHash = Animator.StringToHash("AttackMirror");
    private static readonly int AttackBlendHash = Animator.StringToHash("AttackBlend");
    private static readonly int AttackingHash = Animator.StringToHash("Attacking");
    private static readonly int GroundedHash = Animator.StringToHash("Grounded");

    private class Clone
    {
        public GameObject go;
        public Transform tr;
        public Animator anim;
        public AnimatorOverrideController oc;
        public Renderer[] renderers;
        public float alpha;
        public float time;
        public int seenPresentation = -1;
    }

    private readonly List<Clone> _clones = new List<Clone>();
    private MeleeAttack _attack;
    private CharacterAnimator _characterAnimator;
    private Animator _ownerAnimator;
    private MaterialPropertyBlock _block;
    private Material _ownedMaterial;
    private int _colorId;

    private void Awake()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            enabled = false; // server headless
            return;
        }
        _attack = GetComponentInParent<MeleeAttack>();
        _characterAnimator = GetComponent<CharacterAnimator>();
        _ownerAnimator = GetComponent<Animator>();
        _block = new MaterialPropertyBlock();
        _colorId = Shader.PropertyToID(colorProperty);

        if (ghostMaterial == null)
        {
            _ownedMaterial = new Material(Shader.Find("ProjectA/Afterimage")) { name = "Shadow Clone (auto)" };
            _ownedMaterial.SetFloat("_CoreOpacity", 0.45f);
            _ownedMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            ghostMaterial = _ownedMaterial;
        }
    }

    private void OnDisable()
    {
        foreach (var c in _clones)
        {
            c.alpha = 0f;
            if (c.go != null) c.go.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        foreach (var c in _clones)
        {
            if (c.go != null) Destroy(c.go);
            if (c.oc != null) Destroy(c.oc);
        }
        _clones.Clear();
        if (_ownedMaterial != null) Destroy(_ownedMaterial);
    }

    private void LateUpdate()
    {
        if (_attack == null || cloneModel == null) return;

        int want = _attack.ActiveCloneCount;
        while (_clones.Count < want)
        {
            var c = CreateClone();
            if (c == null) return;
            _clones.Add(c);
        }

        // ส่วนต่างระหว่างราก (ตัว simulation) กับโมเดล -> วางร่างเงาให้โมเดลอยู่ตำแหน่งเดียวกับที่ของเราอยู่
        Transform root = _attack.transform;
        Vector3 visualOffset = transform.position - root.position;
        float ownerT = _attack.IsAttacking ? _attack.PredictNormalizedTime(Time.time - Time.fixedTime) : 1f;

        for (int i = 0; i < _clones.Count; i++)
        {
            Clone c = _clones[i];
            bool on = i < want;
            c.alpha = Mathf.MoveTowards(c.alpha, on ? 1f : 0f, Time.deltaTime / (on ? fadeInTime : fadeOutTime));
            if (c.alpha <= 0f)
            {
                if (c.go.activeSelf) c.go.SetActive(false);
                c.seenPresentation = -1;
                continue;
            }

            if (on)
            {
                Vector3 target = _attack.GetClonePosition(i) + visualOffset;
                if (!c.go.activeSelf)
                {
                    c.tr.position = target; // โผล่ที่ตำแหน่งเลย ไม่ลอยมาจากจุดเก่า
                    c.go.SetActive(true);
                }
                else
                {
                    c.tr.position = Vector3.Lerp(c.tr.position, target, 1f - Mathf.Exp(-followSharpness * Time.deltaTime));
                }
                // หันเข้าหาจุดรวมหน้าตัวเรา (ทิศเดียวกับที่ร่างตีจริง)
                Vector3 cloneAim = _attack.GetCloneAim(i);
                if (cloneAim.sqrMagnitude > 0.0001f) c.tr.rotation = Quaternion.LookRotation(cloneAim);
                c.tr.localScale = transform.lossyScale;

                // ท่าใหม่ของเรา -> ร่างเล่น "อีกท่า" ของสกิล ข้างสลับกัน
                if (_attack.PresentationCount != c.seenPresentation)
                {
                    c.seenPresentation = _attack.PresentationCount;
                    PlayCloneMove(c, i);
                }
                c.time = Mathf.Clamp01(ownerT - timeDelay * (i + 1));
            }

            c.anim.SetFloat(AttackTimeHash, c.time);
            c.anim.SetBool(AttackingHash, true);
            c.anim.SetBool(GroundedHash, true);
            ApplyAlpha(c);
        }
    }

    private void PlayCloneMove(Clone c, int index)
    {
        ResolvedSkill skill = _attack.ActiveSkill;
        if (skill == null || _characterAnimator == null) return;
        int count = skill.AnimationCount;
        int variant = count > 1 ? (_attack.AnimationVariant + index + 1) % count : 0;
        AnimationClip clip = skill.GetAnimation(variant);
        if (clip != null && _characterAnimator.attackSlotClip != null) c.oc[_characterAnimator.attackSlotClip] = clip;
        if (skill.BlendAnimation != null && _characterAnimator.blendSlotClip != null) c.oc[_characterAnimator.blendSlotClip] = skill.BlendAnimation;
        c.anim.SetBool(AttackMirrorHash, _attack.IsCloneMirrored(index));
        c.anim.SetFloat(AttackBlendHash, _attack.BlendWeight);
        c.anim.CrossFadeInFixedTime(AttackState, 0.06f, 0);
    }

    private void ApplyAlpha(Clone c)
    {
        _block ??= new MaterialPropertyBlock(); // recompile ระหว่าง Play ล้างค่านี้โดยไม่เรียก Awake ซ้ำ
        Color col = color;
        col.a *= c.alpha;
        foreach (var r in c.renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_block);
            _block.SetColor(_colorId, col);
            r.SetPropertyBlock(_block);
        }
    }

    private Clone CreateClone()
    {
        RuntimeAnimatorController ctrl = _ownerAnimator.runtimeAnimatorController;
        if (ctrl == null) return null;
        if (ctrl is AnimatorOverrideController aoc) ctrl = aoc.runtimeAnimatorController;

        var go = Instantiate(cloneModel);
        go.name = $"{_attack.name} Shadow Clone {_clones.Count + 1}";
        SceneManager.MoveGameObjectToScene(go, gameObject.scene); // ไปกับ scene ของเจ้าของ (player ข้าม scene ก็ไปด้วย)

        var anim = go.GetComponent<Animator>();
        if (anim == null) anim = go.AddComponent<Animator>();
        anim.avatar = _ownerAnimator.avatar;
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var oc = new AnimatorOverrideController(ctrl) { name = "Shadow Clone (Skill)" };
        anim.runtimeAnimatorController = oc;

        var renderers = go.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            var mats = r.sharedMaterials;
            for (int m = 0; m < mats.Length; m++) mats[m] = ghostMaterial;
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        go.SetActive(false);
        return new Clone { go = go, tr = go.transform, anim = anim, oc = oc, renderers = renderers };
    }
}
