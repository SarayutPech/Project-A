using UnityEngine;

// ชี้ว่าเกมใช้ PassiveTree ตัวไหน (asset เดียวใน Resources) ตั้งจาก Tree Editor ปุ่ม "Use Tree"
// ไม่มี asset นี้ / ว่าง = ใช้ tree ตัวเริ่มต้น (PassiveTree.DefaultResourcePath) เหมือนเดิม
[CreateAssetMenu(menuName = "Passives/Passive Tree Selection", fileName = "ActivePassiveTree")]
public class PassiveTreeSelection : ScriptableObject
{
    public const string ResourcePath = "Gameobject/ScriptAbleObject/PassiveTree/ActivePassiveTree";

    public PassiveTree tree;
}
