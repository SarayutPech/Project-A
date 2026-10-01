using UnityEngine;

// EXP ที่ให้คนฆ่าตัวนี้ (ไม่ใส่ = ใช้ defaultExpPerKill ของ PlayerExperience) elite คูณเพิ่มที่ฝั่ง PlayerExperience
[DisallowMultipleComponent]
public class ExperienceReward : MonoBehaviour
{
    [Min(0f)] public float experience = 10f;
}
