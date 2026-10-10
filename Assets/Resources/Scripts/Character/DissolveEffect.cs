using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// ตัวละครสลายเป็นเม็ดแล้วลอยขึ้นฟ้า (presentation อย่างเดียว server headless ปิดตัวเอง) วางบน Visual (ข้าง Animator)
// ภาพ: สลับ material ทุก renderer เป็น ProjectA/Dissolve (ก๊อป texture/สีเดิมให้) ไล่ตัดจากเท้าขึ้นหัวตาม noise ขอบเรืองแสง
//      + Particle เม็ดจุดเกิดบนผิวโมเดล (Shape = Skinned/Mesh Renderer) ลอยขึ้น
// เริ่มเอง: ฟัง Health.Died ของ parent (playOnDeath) หลังหน่วง deathDelay ให้ท่าตายเล่นก่อน หรือเรียก Play() เอง (วาร์ป/เทเลพอร์ต)
// server เป็นคนตัดสินว่าตายเมื่อไหร่ (Health) ตัวนี้แค่เล่นภาพ ไม่เขียนผลกลับเข้า simulation
// กฎข้อ 10: material/particle สร้างครั้งเดียวแล้วใช้ซ้ำ ลบตอน Restore/OnDestroy ทุก subscribe มี unsubscribe
public class DissolveEffect : MonoBehaviour
{
    [Header("Trigger")]
    [Tooltip("เล่นเองตอน Health (ของ parent) ตาย")]
    public bool playOnDeath = true;
    [Tooltip("หน่วงหลังตายก่อนเริ่มสลาย (วินาที) ให้ท่าตายเล่นก่อน")]
    [Min(0f)] public float deathDelay = 1.2f;

    [Header("Scene transition")]
    [Tooltip("เข้าร่วมตอนสลับ scene ผ่าน SceneTransition: สลายหายก่อนจอดำ (รอจบก่อนโหลด) แล้วค่อยๆ ปรากฏตอน scene ใหม่เปิด (ใส่ที่ player)")]
    public bool playOnSceneTransition = false;
    [Min(0.1f)] public float transitionDuration = 1.2f;

    [Header("Dissolve")]
    [Min(0.1f)] public float duration = 2f;
    [Tooltip("ความคืบหน้าตามเวลา (แกน X 0..1 เวลา / แกน Y 0..1 ค่า _Dissolve)")]
    public AnimationCurve progress = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [ColorUsage(true, true)] public Color edgeColor = new Color(0.3f, 2.2f, 3f, 1f);
    [Range(0f, 0.3f)] public float edgeWidth = 0.06f;
    [Tooltip("จำนวนก้อน noise ต่อความสูงตัว (มาก = ลายละเอียดขึ้น)")]
    [Min(0.5f)] public float noiseCells = 5f;
    [Range(0f, 1f)] public float noiseWeight = 0.45f;

    [Header("Particles")]
    public bool emitParticles = true;
    [Tooltip("จำนวนเม็ดต่อวินาที ณ ช่วงที่สลายแรงสุด (รวมทุก renderer)")]
    [Min(0f)] public float particlesPerSecond = 260f;
    [Tooltip("ความเร็วลอยขึ้น (เท่าของความสูงตัวต่อวินาที)")]
    [Min(0f)] public float riseSpeed = 0.7f;
    public Vector2 lifetime = new Vector2(1.2f, 2.2f);
    [Tooltip("ขนาดเม็ด (สัดส่วนของความสูงตัว)")]
    public Vector2 dotSize = new Vector2(0.015f, 0.035f);
    [Tooltip("เว้นว่าง = สร้าง material ProjectA/DissolveParticle ให้")]
    public Material particleMaterial;
    [Tooltip("จำนวน particle system สูงสุด (เลือกชิ้นที่ใหญ่สุดก่อน)")]
    [Min(1)] public int maxEmitters = 5;

    [Header("Light beam (scene transition only)")]
    [Tooltip("สลายเสร็จแล้วมีลำแสงพุ่งขึ้นฟ้า / ตอนมาถึงมีลำแสงตกลงมาที่พื้นแล้วตัวค่อยๆ ปรากฏ")]
    public bool useBeamOnTransition = true;
    [ColorUsage(true, true)] public Color beamColor = new Color(0.4f, 1.6f, 3f, 1f);
    [Tooltip("ความสูงของลำแสง (หน่วย world) ปลายบนจางหายไปในฟ้า")]
    [Min(1f)] public float beamHeight = 40f;
    [Tooltip("รัศมีลำแสง (สัดส่วนของความสูงตัว)")]
    [Min(0.01f)] public float beamRadius = 0.22f;
    [Tooltip("เวลาที่หัวลำแสงวิ่งขึ้น/ลง (วินาที)")]
    [Min(0.1f)] public float beamTravelTime = 0.5f;
    [Tooltip("ความยาวหัวลำแสงที่วิ่ง (สัดส่วนของความสูงลำ)")]
    [Range(0.05f, 1f)] public float streakLength = 0.35f;

    public event Action Finished;
    public bool IsPlaying => _state == State.Waiting || _state == State.Running;

    private enum State { Idle, Waiting, Running, Done }

    // ตัวที่ติ๊ก playOnSceneTransition (ปกติ = player ของเครื่องนี้) ลงทะเบียนตอน OnEnable ถอนตอน OnDisable
    private static readonly List<DissolveEffect> TransitionTargets = new List<DissolveEffect>();

    // SceneTransition เรียก: เริ่มสลายออกทุกตัว (ใช้เวลาจริง ไม่ติด Time.timeScale = 0 ตอน pause) / true = มีตัวที่เริ่มเล่น
    public static bool BeginLeaveTransition()
    {
        bool any = false;
        foreach (var t in TransitionTargets)
        {
            if (t == null || !t.isActiveAndEnabled || t.IsPlaying) continue;
            t.StartInternal(0f, false, true, t.transitionDuration, t.useBeamOnTransition);
            any |= t.IsPlaying;
        }
        return any;
    }

    public static bool IsTransitionBusy
    {
        get
        {
            foreach (var t in TransitionTargets) if (t != null && t.IsPlaying) return true;
            return false;
        }
    }

    // SceneTransition เรียกหลังโหลด scene ใหม่: ซ่อนตัวทันที (ก่อนเฟรมแรกถูกวาด) แล้วค่อยๆ ปรากฏ
    public static void BeginArriveTransition()
    {
        foreach (var t in TransitionTargets)
        {
            if (t == null || !t.isActiveAndEnabled) continue;
            t.StartInternal(0f, true, true, t.transitionDuration, t.useBeamOnTransition);
        }
    }

    private enum Phase { Dissolve, Launch, Descend }

    private bool _reverse;
    private bool _unscaled;
    private bool _beam;
    private Phase _phase;
    private float _phaseT;
    private float _runDuration = 1f;

    private static readonly int TailId = Shader.PropertyToID("_Tail");
    private static readonly int HeadId = Shader.PropertyToID("_Head");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int StripeTimeId = Shader.PropertyToID("_StripeTime");

    private GameObject _beamGo;
    private Material _beamMat;
    private Mesh _beamMesh;
    private float _groundY;

    private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
    private static readonly int YMinId = Shader.PropertyToID("_YMin");
    private static readonly int YMaxId = Shader.PropertyToID("_YMax");
    private static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");
    private static readonly int NoiseWeightId = Shader.PropertyToID("_NoiseWeight");
    private static readonly int EdgeWidthId = Shader.PropertyToID("_EdgeWidth");
    private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private class Swapped
    {
        public Renderer renderer;
        public Material[] original;
        public bool wasEnabled;
    }

    private State _state = State.Idle;
    private Health _health;
    private Shader _shader;
    private float _delay;
    private float _t;
    private float _height;
    private float _emitCarry;

    private readonly List<Swapped> _swapped = new List<Swapped>();
    private readonly Dictionary<Material, Material> _materials = new Dictionary<Material, Material>();
    private readonly List<ParticleSystem> _emitters = new List<ParticleSystem>();
    private readonly List<float> _weights = new List<float>();
    private readonly List<float> _weightCarry = new List<float>();
    private Material _ownedParticleMaterial;

    private void Awake()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            enabled = false; // server headless
            return;
        }
        _health = GetComponentInParent<Health>();
        _shader = Shader.Find("ProjectA/Dissolve");
        if (_shader == null) Debug.LogWarning("[DissolveEffect] ไม่พบ shader ProjectA/Dissolve", this);
    }

    private void OnEnable()
    {
        if (_health != null) _health.Died += OnDied;
        if (playOnSceneTransition && !TransitionTargets.Contains(this)) TransitionTargets.Add(this);
    }

    private void OnDisable()
    {
        if (_health != null) _health.Died -= OnDied;
        TransitionTargets.Remove(this);
        Restore();
    }

    private void OnDestroy()
    {
        Restore();
        DestroyEmitters();
        if (_ownedParticleMaterial != null) Destroy(_ownedParticleMaterial);
        if (_beamGo != null) Destroy(_beamGo);
        if (_beamMat != null) Destroy(_beamMat);
        if (_beamMesh != null) Destroy(_beamMesh);
    }

    private void OnDied(Health h)
    {
        if (playOnDeath) Play(deathDelay);
    }

    // เริ่มสลาย (delay = รอก่อนกี่วินาที) เรียกซ้ำระหว่างเล่น = ข้าม
    public void Play(float delay = 0f)
    {
        if (!enabled || _shader == null || IsPlaying) return;
        StartInternal(delay, false, false, duration, false);
    }

    // reverse = ปรากฏ (ค่า _Dissolve 1 -> 0 เม็ดตกลง) แทนสลาย / unscaled = ใช้เวลาจริง / beam = มีลำแสง (ขึ้นหลังสลาย / ลงก่อนปรากฏ)
    private void StartInternal(float delay, bool reverse, bool unscaled, float runDuration, bool beam)
    {
        if (!enabled || _shader == null) return;
        Restore();
        _beam = beam;
        _reverse = reverse;
        _unscaled = unscaled;
        _runDuration = Mathf.Max(0.05f, runDuration);
        _delay = Mathf.Max(0f, delay);
        _state = State.Waiting;
        if (_delay <= 0f) Begin();
    }

    // คืน material เดิม เก็บเม็ด (ใช้ตอนเกิดใหม่จาก pool หรือยกเลิก)
    public void Restore(bool clearParticles = true)
    {
        _state = State.Idle;
        for (int i = 0; i < _swapped.Count; i++)
        {
            var s = _swapped[i];
            if (s.renderer == null) continue;
            s.renderer.sharedMaterials = s.original;
            s.renderer.enabled = s.wasEnabled;
        }
        _swapped.Clear();
        if (_beamGo != null) _beamGo.SetActive(false);
        foreach (var m in _materials.Values) if (m != null) Destroy(m);
        _materials.Clear();
        for (int i = 0; i < _emitters.Count; i++)
            if (_emitters[i] != null)
                _emitters[i].Stop(true, clearParticles ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
    }

    private void Begin()
    {
        var renderers = new List<Renderer>();
        foreach (var r in GetComponentsInChildren<Renderer>(false))
            if (r is SkinnedMeshRenderer || r is MeshRenderer) renderers.Add(r);
        if (renderers.Count == 0) { _state = State.Idle; return; }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Count; i++) bounds.Encapsulate(renderers[i].bounds);
        _height = Mathf.Max(0.1f, bounds.size.y);

        foreach (var r in renderers)
        {
            var original = r.sharedMaterials;
            var swapped = new Material[original.Length];
            for (int i = 0; i < original.Length; i++) swapped[i] = GetDissolveMaterial(original[i], bounds);
            _swapped.Add(new Swapped { renderer = r, original = original, wasEnabled = r.enabled });
            r.sharedMaterials = swapped;
        }

        if (_reverse) foreach (var m in _materials.Values) m.SetFloat(DissolveId, 1f); // ซ่อนตัวตั้งแต่เฟรมแรก
        if (emitParticles) SetupEmitters(renderers);
        _groundY = bounds.min.y;
        _phase = _reverse && _beam ? Phase.Descend : Phase.Dissolve; // ปรากฏ: ลำแสงตกลงมาก่อน แล้วตัวค่อยๆ ขึ้น
        _phaseT = 0f;
        if (_beam) ShowBeam();
        _t = 0f;
        _emitCarry = 0f;
        _state = State.Running;
    }

    private Material GetDissolveMaterial(Material source, Bounds bounds)
    {
        if (source == null) return null; // ช่อง material ว่าง คงไว้เหมือนเดิม
        if (_materials.TryGetValue(source, out var cached)) return cached;

        var m = new Material(_shader) { name = source.name + " (Dissolve)" };
        Texture tex = null;
        if (source.HasProperty(BaseMapId)) tex = source.GetTexture(BaseMapId);
        else if (source.HasProperty(MainTexId)) tex = source.GetTexture(MainTexId);
        if (tex != null)
        {
            m.SetTexture(BaseMapId, tex);
            int id = source.HasProperty(BaseMapId) ? BaseMapId : MainTexId;
            m.SetTextureScale(BaseMapId, source.GetTextureScale(id));
            m.SetTextureOffset(BaseMapId, source.GetTextureOffset(id));
        }
        Color c = Color.white;
        if (source.HasProperty(BaseColorId)) c = source.GetColor(BaseColorId);
        else if (source.HasProperty(ColorId)) c = source.GetColor(ColorId);
        m.SetColor(BaseColorId, c);

        m.SetFloat(DissolveId, 0f);
        m.SetFloat(YMinId, bounds.min.y);
        m.SetFloat(YMaxId, bounds.max.y);
        m.SetFloat(NoiseScaleId, noiseCells / _height);
        m.SetFloat(NoiseWeightId, noiseWeight);
        m.SetFloat(EdgeWidthId, edgeWidth);
        m.SetColor(EdgeColorId, edgeColor);
        _materials[source] = m;
        return m;
    }

    private void Update()
    {
        float dt = _unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
        if (_state == State.Waiting)
        {
            _delay -= dt;
            if (_delay <= 0f) Begin();
            return;
        }
        if (_state != State.Running) return;

        if (_beam) FollowBeam();
        if (_phase == Phase.Descend)
        {
            // หัวลำแสงตกจากฟ้าลงพื้นแบบเร่งความเร็ว ถึงพื้นแล้วเริ่มปรากฏตัว + เม็ดพุ่งกระจาย
            _phaseT += dt / beamTravelTime;
            float k = Mathf.Clamp01(_phaseT);
            float lower = 1f - k * k;
            SetBeam(lower, Mathf.Clamp01(lower + streakLength), 1f);
            if (_phaseT >= 1f)
            {
                _phase = Phase.Dissolve;
                _t = 0f;
                BurstDots(90);
            }
            return;
        }
        if (_phase == Phase.Launch)
        {
            // หัวลำแสงพุ่งขึ้นฟ้าแบบเร่งความเร็ว (เริ่มจากความสูงของคอลัมน์ที่ชาร์จไว้ ต่อเนื่องไม่กระโดด)
            _phaseT += dt / beamTravelTime;
            float k = Mathf.Clamp01(_phaseT);
            float head = Mathf.Lerp(ColumnTop(), 1f + streakLength, k * k);
            SetBeam(Mathf.Clamp01(head - streakLength), Mathf.Clamp01(head), 1f);
            if (_phaseT >= 1f)
            {
                _state = State.Done;
                if (_beamGo != null) _beamGo.SetActive(false);
                Finished?.Invoke();
            }
            return;
        }

        _t += dt / _runDuration;
        float t = Mathf.Clamp01(_t);
        float p = Mathf.Clamp01(progress.Evaluate(_reverse ? 1f - t : t));
        foreach (var m in _materials.Values) if (m != null) m.SetFloat(DissolveId, p);

        if (_beam)
        {
            // สลาย: คอลัมน์แสงรอบตัวค่อยๆ สว่างขึ้น / ปรากฏ: คอลัมน์ที่เหลือจางลง
            if (!_reverse) SetBeam(0f, ColumnTop(), Smooth((t - 0.35f) / 0.65f));
            else SetBeam(0f, Mathf.Lerp(streakLength, ColumnTop(), Smooth(t * 2.5f)), 1f - Smooth(t));
        }

        if (emitParticles && _emitters.Count > 0)
        {
            // แรงสุดช่วงกลาง (sin) เก็บเศษเม็ดข้ามเฟรมให้ไม่ขึ้นกับ frame rate
            _emitCarry += particlesPerSecond * Mathf.Sin(Mathf.PI * t) * dt;
            int total = Mathf.FloorToInt(_emitCarry);
            _emitCarry -= total;
            // แบ่งตามขนาดของชิ้น (ชิ้นใหญ่ปล่อยมากกว่า) เศษที่ปัดทิ้งสะสมต่อใน _weightCarry
            for (int i = 0; i < _emitters.Count; i++)
            {
                _weightCarry[i] += total * _weights[i];
                int n = Mathf.FloorToInt(_weightCarry[i]);
                _weightCarry[i] -= n;
                if (n > 0 && _emitters[i] != null) _emitters[i].Emit(n);
            }
        }

        if (_t >= 1f)
        {
            if (_reverse)
            {
                Restore(false); // ตัวกลับมาครบ คืน material เดิม เม็ดที่เหลือค่อยๆ หมดเอง
                Finished?.Invoke();
                return;
            }
            foreach (var s in _swapped) if (s.renderer != null) s.renderer.enabled = false;
            if (_beam)
            {
                _phase = Phase.Launch; // ตัวหายหมดแล้ว ปล่อยลำแสงพุ่งฟ้า (ยังนับว่าเล่นอยู่ SceneTransition ถึงรอ)
                _phaseT = 0f;
                return;
            }
            _state = State.Done;
            Finished?.Invoke();
        }
    }

    // ---------- Light beam ----------

    private static float Smooth(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    // ความสูงคอลัมน์แสงรอบตัว (สัดส่วนของความสูงลำ)
    private float ColumnTop() => Mathf.Clamp01(_height * 1.4f / beamHeight);

    private void ShowBeam()
    {
        if (_beamGo == null)
        {
            var sh = Shader.Find("ProjectA/LightBeam");
            if (sh == null) { _beam = false; return; }
            _beamMat = new Material(sh) { name = "Light Beam (auto)" };
            _beamMesh = BuildTubeMesh(24);
            _beamGo = new GameObject("Dissolve Beam");
            _beamGo.AddComponent<MeshFilter>().sharedMesh = _beamMesh;
            var mr = _beamGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _beamMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
        _beamMat.SetColor(ColorId, beamColor);
        float r = beamRadius * _height;
        _beamGo.transform.localScale = new Vector3(r, beamHeight, r);
        _beamGo.SetActive(true);
        FollowBeam();
        SetBeam(0f, 0f, 0f);
    }

    private void FollowBeam()
    {
        if (_beamGo == null) return;
        var p = transform.position;
        _beamGo.transform.position = new Vector3(p.x, _groundY, p.z);
    }

    private void SetBeam(float lower, float upper, float intensity)
    {
        if (_beamMat == null) return;
        _beamMat.SetFloat(TailId, lower);
        _beamMat.SetFloat(HeadId, upper);
        _beamMat.SetFloat(IntensityId, intensity);
        _beamMat.SetFloat(StripeTimeId, Time.realtimeSinceStartup);
    }

    // ท่อทรงกระบอกรัศมี 1 สูง 1 (y 0..1) ไม่มีฝา uv.x = รอบ uv.y = ความสูง
    private static Mesh BuildTubeMesh(int segments)
    {
        var verts = new Vector3[(segments + 1) * 2];
        var norms = new Vector3[verts.Length];
        var uvs = new Vector2[verts.Length];
        var tris = new int[segments * 6];
        for (int i = 0; i <= segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            verts[i * 2] = dir;
            verts[i * 2 + 1] = dir + Vector3.up;
            norms[i * 2] = norms[i * 2 + 1] = dir;
            uvs[i * 2] = new Vector2(i / (float)segments, 0f);
            uvs[i * 2 + 1] = new Vector2(i / (float)segments, 1f);
        }
        for (int i = 0; i < segments; i++)
        {
            int b = i * 2;
            int o = i * 6;
            tris[o] = b; tris[o + 1] = b + 1; tris[o + 2] = b + 2;
            tris[o + 3] = b + 1; tris[o + 4] = b + 3; tris[o + 5] = b + 2;
        }
        var mesh = new Mesh { name = "Light Beam Tube (auto)" };
        mesh.vertices = verts;
        mesh.normals = norms;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.bounds = new Bounds(new Vector3(0f, 0.5f, 0f), new Vector3(2f, 1f, 2f));
        return mesh;
    }

    // ปล่อยเม็ดพรวดเดียว (ตอนลำแสงถึงพื้น)
    private void BurstDots(int count)
    {
        if (!emitParticles) return;
        for (int i = 0; i < _emitters.Count; i++)
            if (_emitters[i] != null) _emitters[i].Emit(Mathf.Max(1, Mathf.RoundToInt(count * _weights[i])));
    }

    // ---------- Particle ----------

    private void SetupEmitters(List<Renderer> renderers)
    {
        DestroyEmitters();
        if (particleMaterial == null && _ownedParticleMaterial == null)
        {
            var sh = Shader.Find("ProjectA/DissolveParticle");
            if (sh != null) _ownedParticleMaterial = new Material(sh) { name = "Dissolve Dots (auto)" };
        }
        Material mat = particleMaterial != null ? particleMaterial : _ownedParticleMaterial;
        if (mat == null) return;
        if (mat == _ownedParticleMaterial) mat.SetColor(ColorId, edgeColor);

        // เลือกชิ้นที่ใหญ่สุด maxEmitters ชิ้น (ชิ้นเล็กไม่คุ้มสร้าง particle system)
        var sorted = new List<Renderer>(renderers);
        sorted.Sort((a, b) => b.bounds.size.sqrMagnitude.CompareTo(a.bounds.size.sqrMagnitude));
        int count = Mathf.Min(maxEmitters, sorted.Count);
        _weights.Clear();
        _weightCarry.Clear();
        float sum = 0f;
        for (int i = 0; i < count; i++) sum += sorted[i].bounds.size.sqrMagnitude;
        for (int i = 0; i < count; i++)
        {
            _emitters.Add(CreateEmitter(sorted[i], mat));
            _weights.Add(sum > 0f ? sorted[i].bounds.size.sqrMagnitude / sum : 1f / count);
            _weightCarry.Add(0f);
        }
        foreach (var ps in _emitters) ps.Play();
    }

    private ParticleSystem CreateEmitter(Renderer r, Material mat)
    {
        var go = new GameObject("Dissolve Dots");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;              // ไม่ปล่อยเอง (emission ปิด) ค้างไว้ให้ Emit() จากโค้ด
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(dotSize.x * _height, dotSize.y * _height);
        main.startColor = Color.white;
        main.maxParticles = 800;
        main.useUnscaledTime = _unscaled;

        var emission = ps.emission;
        emission.enabled = false;

        var shape = ps.shape;
        shape.enabled = true;
        if (r is SkinnedMeshRenderer smr)
        {
            shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
            shape.skinnedMeshRenderer = smr;
        }
        else
        {
            shape.shapeType = ParticleSystemShapeType.MeshRenderer;
            shape.meshRenderer = (MeshRenderer)r;
        }
        shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;

        float rise = (_reverse ? -riseSpeed : riseSpeed) * _height; // ปรากฏ = เม็ดตกลงมา
        float drift = 0.08f * _height;
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-drift, drift);
        vel.y = new ParticleSystem.MinMaxCurve(rise * 0.5f, rise * 1.2f);
        vel.z = new ParticleSystem.MinMaxCurve(-drift, drift);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.12f * _height;
        noise.frequency = 0.6f;
        noise.quality = ParticleSystemNoiseQuality.Low;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var pr = go.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.sharedMaterial = mat;
        pr.shadowCastingMode = ShadowCastingMode.Off;
        pr.receiveShadows = false;
        return ps;
    }

    private void DestroyEmitters()
    {
        for (int i = 0; i < _emitters.Count; i++)
            if (_emitters[i] != null) Destroy(_emitters[i].gameObject);
        _emitters.Clear();
    }
}
