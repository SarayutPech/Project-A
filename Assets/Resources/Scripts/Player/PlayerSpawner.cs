using UnityEngine;

// ถ้ายังไม่มี player (PlayerMovement) จะ spawn ให้ที่จุด Start ของแผนที่ (หรือตำแหน่ง spawner ถ้า scene ไม่มีแผนที่)
// ถ้ามี player ข้ามมาจาก scene ก่อน (persistAcrossScenes) จะย้ายตัวเดิมมาวางแทนการสร้างใหม่
// รันหลัง ProceduralMapGenerator (-1000) เพื่อให้รู้จุด Start แล้ว และก่อน script ทั่วไป
// เพื่อให้กล้อง/script อื่นเจอ PlayerMovement.Instance ตั้งแต่ Start
[DefaultExecutionOrder(-900)]
public class PlayerSpawner : MonoBehaviour
{
    [Tooltip("prefab ของ player (ถ้าไม่มี PlayerMovement จะเพิ่มให้) ถ้าเว้นว่างจะสร้าง capsule ให้อัตโนมัติ")]
    public GameObject playerPrefab;
    [Tooltip("ถ้าเว้นว่างจะใช้ ProceduralMapGenerator.Instance")]
    private ProceduralMapGenerator generator;

    private void Awake()
    {
        EnsurePlayer();
    }

    public PlayerMovement EnsurePlayer()
    {
        var gen = generator != null ? generator : ProceduralMapGenerator.Instance;
        // ตอนเปลี่ยน scene, scene เก่ายังไม่ถูก unload ตอน Awake ของ scene ใหม่ -> Instance อาจเป็นของแผนที่เก่า
        if (gen != null && gen.gameObject.scene != gameObject.scene) gen = null;

        // player ที่วางไว้ในฉากอาจยังไม่ได้ Awake (Instance ยังว่าง) จึงต้องหาในฉากด้วย
        var existing = PlayerMovement.Instance != null ? PlayerMovement.Instance : FindAnyObjectByType<PlayerMovement>();
        if (existing != null)
        {
            // ตัวที่ข้ามมาจาก scene ก่อน -> วางเท้าที่จุดเกิดของ scene นี้
            // (scene ที่มีแผนที่ PlayerMovement วางที่จุด Start เองตอนผูก scene ใหม่)
            if (existing.gameObject.scene != gameObject.scene && gen == null) existing.Teleport(transform.position);
            return existing;
        }

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
