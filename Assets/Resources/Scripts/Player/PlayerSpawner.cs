using UnityEngine;

// ถ้าในฉากยังไม่มี player (PlayerMovement) จะ spawn ให้ที่จุด Start ของแผนที่
// รันหลัง ProceduralMapGenerator (-1000) เพื่อให้รู้จุด Start แล้ว และก่อน script ทั่วไป
// เพื่อให้กล้อง/script อื่นเจอ PlayerMovement.Instance ตั้งแต่ Start
[DefaultExecutionOrder(-900)]
public class PlayerSpawner : MonoBehaviour
{
    [Tooltip("prefab ของ player (ถ้าไม่มี PlayerMovement จะเพิ่มให้) ถ้าเว้นว่างจะสร้าง capsule ให้อัตโนมัติ")]
    public GameObject playerPrefab;
    [Tooltip("ถ้าเว้นว่างจะใช้ ProceduralMapGenerator.Instance")]
    public ProceduralMapGenerator generator;

    private void Awake()
    {
        EnsurePlayer();
    }

    public PlayerMovement EnsurePlayer()
    {
        if (PlayerMovement.Instance != null) return PlayerMovement.Instance;

        // player ที่วางไว้ในฉากอาจยังไม่ได้ Awake (Instance ยังว่าง) จึงต้องหาในฉากด้วย
        var existing = FindFirstObjectByType<PlayerMovement>();
        if (existing != null) return existing;

        var gen = generator != null ? generator : ProceduralMapGenerator.Instance;
        Vector3 spawnPos = gen != null ? gen.StartPosition : transform.position;

        GameObject playerObj = playerPrefab != null
            ? Instantiate(playerPrefab, spawnPos, Quaternion.identity)
            : CreateFallbackPlayer(spawnPos);
        playerObj.name = "Player";
        if (playerObj.CompareTag("Untagged")) playerObj.tag = "Player";

        // PlayerMovement.Start จะ Teleport ให้เท้าวางบนพื้นที่จุด Start เอง
        var movement = playerObj.GetComponent<PlayerMovement>();
        if (movement == null) movement = playerObj.AddComponent<PlayerMovement>();
        return movement;
    }

    private static GameObject CreateFallbackPlayer(Vector3 position)
    {
        // capsule มี CapsuleCollider มาแล้ว Rigidbody จะถูกเพิ่มโดย RequireComponent ของ PlayerMovement
        var obj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        obj.transform.position = position;
        return obj;
    }
}
