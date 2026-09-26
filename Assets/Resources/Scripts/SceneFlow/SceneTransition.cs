using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// ตัวจัดการสลับ scene (สร้างตัวเองอัตโนมัติ + DontDestroyOnLoad มีตัวเดียวตลอดเกม)
// จอดำ fade out -> โหลด scene ใหม่แบบ async -> fade in
// ใช้ LoadSceneMode.Single: scene เก่าถูก unload ทั้งหมด และ Unity เรียก Resources.UnloadUnusedAssets ให้เอง
// (mesh/material ที่ generate ตอน runtime ที่ไม่มีใครอ้างอิงแล้วจะถูกล้างด้วย)
public class SceneTransition : MonoBehaviour
{
    [Min(0f)] public float fadeDuration = 0.35f;
    public Color fadeColor = Color.black;

    public static bool IsTransitioning { get; private set; }

    private static SceneTransition _instance;
    private CanvasGroup _fadeGroup;

    private static SceneTransition Instance
    {
        get
        {
            if (_instance == null)
            {
                var obj = new GameObject(nameof(SceneTransition));
                _instance = obj.AddComponent<SceneTransition>();
                DontDestroyOnLoad(obj);
            }
            return _instance;
        }
    }

    // เรียกจากที่ไหนก็ได้ เช่น SceneTransition.Load("Hideout")
    public static bool Load(string sceneName)
    {
        if (IsTransitioning) return false;
        if (string.IsNullOrEmpty(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"[{nameof(SceneTransition)}] โหลด scene '{sceneName}' ไม่ได้ ตรวจว่าเพิ่มไว้ใน File > Build Profiles (Scene List) แล้ว");
            return false;
        }
        Instance.StartCoroutine(Instance.LoadRoutine(sceneName));
        return true;
    }

    private void Awake()
    {
        BuildFadeOverlay();
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
            IsTransitioning = false;
        }
    }

    private IEnumerator LoadRoutine(string sceneName)
    {
        IsTransitioning = true;
        if (PlayerMovement.Instance != null) PlayerMovement.Instance.CanMove = false;

        yield return Fade(1f);

        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        while (!op.isDone) yield return null;

        // รอให้ Awake/Start ของ scene ใหม่ (generate map, spawn player) ทำงานก่อนเปิดจอ
        yield return null;

        yield return Fade(0f);
        IsTransitioning = false;
    }

    private IEnumerator Fade(float target)
    {
        _fadeGroup.blocksRaycasts = true;
        float start = _fadeGroup.alpha;
        for (float t = 0f; t < fadeDuration; t += Time.unscaledDeltaTime)
        {
            _fadeGroup.alpha = Mathf.Lerp(start, target, t / fadeDuration);
            yield return null;
        }
        _fadeGroup.alpha = target;
        _fadeGroup.blocksRaycasts = target > 0f;
    }

    // สร้าง overlay ครั้งเดียว อยู่ข้าม scene ไม่ได้สร้างใหม่ทุกครั้งที่สลับ
    private void BuildFadeOverlay()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        _fadeGroup = gameObject.AddComponent<CanvasGroup>();
        _fadeGroup.alpha = 0f;
        _fadeGroup.blocksRaycasts = false;

        var imageObj = new GameObject("Fade", typeof(RectTransform), typeof(Image));
        imageObj.transform.SetParent(transform, false);
        var rect = (RectTransform)imageObj.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        imageObj.GetComponent<Image>().color = fadeColor;
    }
}
