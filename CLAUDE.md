# Project-A

ARPG แนว Path of Exile (hideout + procedural map) — Unity 6000.6, URP, Input System (ตัวใหม่)
คุยกับผู้ใช้เป็นภาษาไทย / comment ในโค้ดเขียนภาษาไทยตามสไตล์เดิม

## สถานะ: Prototype (ยังไม่ใช้ server)
- Multiplayer เป้าหมายตอนนี้: co-op ปาร์ตี้ 2–6 คน แบบ host-client (Netcode for GameObjects) ทดสอบในเครื่องด้วย Multiplayer Play Mode / direct connect ยังไม่ใช้ Relay / Cloud
- ข้อมูลผู้เล่น (inventory, character) ตอนนี้เก็บ local แต่ต้องอยู่หลัง interface เพื่อสลับเป็น DB กลางทีหลัง
- เป้าหมายระยะยาว: scale เป็น **dedicated server** (instance server ต่อปาร์ตี้ + town server รวม) + backend กลาง

## กฎการเขียนโค้ด (เผื่อ scale เป็น dedicated server)
โค้ดใหม่ทุกชิ้นต้องเขียนให้ย้ายไปรันบน dedicated server ได้โดยไม่ต้องเขียนใหม่:

1. **Server-authoritative**: logic ที่มีผลกับเกม (combat, damage, HP, AI มอนสเตอร์, loot, spawn, การเปลี่ยน map) ตัดสินฝั่ง server เท่านั้น
   - เช็คด้วย `IsServer` ไม่ใช่ `IsHost` (host = server + client ตอนนี้ แต่ dedicated server ไม่มี local player)
   - client ส่งแค่ "คำขอ/input" (Rpc) ห้ามส่งผลลัพธ์ เช่น ส่ง "ขอโจมตีเป้า X" ไม่ใช่ "ทำดาเมจ 50"
2. **แยก presentation ออกจาก simulation**: กราฟิก/เสียง/particle/UI/กล้อง ห้ามอยู่ใน logic ที่ server ต้องรัน
   - server build เป็น headless: โค้ด gameplay ต้องไม่พึ่ง `Camera.main`, Renderer, UI, Input
   - ของที่เป็น visual อย่างเดียวให้แยก component (หรือข้ามด้วย `#if !UNITY_SERVER` / เช็ค headless)
3. **ห้าม singleton ผูกกับ "player คนเดียว"**: ใช้แนวคิด local player (ตัวที่เครื่องนี้ควบคุม) กับรายการ player ทั้งหมดแยกกัน
   - `PlayerMovement.Instance` เป็นโค้ดเดิมยุค single-player — โค้ดใหม่ห้ามพึ่งเพิ่ม และค่อย ๆ ย้ายออก
4. **Input แยกจาก movement/action**: อ่าน input ในตัวรับ input ของ local player แล้วส่งเป็นคำสั่ง ไม่อ่าน `Keyboard.current` ในโค้ด gameplay
5. **Determinism ของ procedural map**: map ต้องสร้างซ้ำได้จาก (config ID + seed) เท่านั้น
   - generator ต้องใช้ random ของตัวเอง (`System.Random` จาก seed) ห้ามใช้ `UnityEngine.Random` ตัวกลาง
   - sync ผ่านเน็ตแค่ seed + config ID ไม่ส่ง mesh
6. **รองรับหลาย instance ใน process เดียว**: อย่าสมมติว่ามี map เดียว/scene เดียวในโลก
   - หลีกเลี่ยง static state ต่อ map (เช่น `ProceduralMapGenerator.Instance`, `GeneratedRoot`) ในโค้ดใหม่ ให้อ้างผ่าน context ของ instance นั้นแทน
   - physics query ใช้ `PhysicsScene` ของ scene นั้นได้
7. **ข้อมูลผู้เล่นผ่าน service interface**: เช่น `IInventoryService` — ตอนนี้ implement แบบ local (JSON) ภายหลังเป็น backend
   - ของเข้า inventory ผ่าน flow แบบ drop → claim (server สร้าง drop มี id, ผู้เล่นขอเก็บด้วย id, เก็บได้ครั้งเดียว) ห้ามให้ client เพิ่มของเอง
   - operation ที่ย้ายของหลายฝั่ง (เทรด) ต้องทำเป็น transaction เดียว
8. **ไม่ trust client**: ค่าจาก client ต้อง validate ฝั่ง server เสมอ (ระยะ, cooldown, สิทธิ์ ownership)
9. **Tick-based**: logic gameplay ไม่ผูกกับ frame rate (ใช้ FixedUpdate / network tick) server ตั้ง tick ~20–30Hz
10. **Memory**: asset ที่สร้างตอน runtime (Mesh, Material, Texture, ScriptableObject.CreateInstance) ต้อง Destroy เอง / event ที่ `+=` ต้อง `-=` / ของที่ spawn บ่อยใช้ pooling

## โครงสร้างหลัก
- `Assets/Resources/Scripts/ProceduralMap/` — `ProceduralMapGenerator` (ไฟล์ `ProceduralWanderingGround.cs`) ค่าตั้งอยู่ใน `ProceduralMapConfig` (ScriptableObject ต่อ variant)
- `Assets/Resources/Scripts/SceneFlow/` — `SceneTransition` (โหลด scene + fade), `ScenePortal` (คลิกในระยะเพื่อวาร์ป)
- `Assets/Resources/Scripts/Player/`, `Camera/` — ยังเป็นแบบ single-player (ดูกฎข้อ 3–4)
  - local player เป็นตัวเดียวอยู่ข้าม scene (`PlayerMovement.persistAcrossScenes` → DontDestroyOnLoad) `PlayerSpawner` ของ scene ใหม่ย้ายตัวเดิมมาวาง ไม่สร้างใหม่
  - ของที่ player ถือข้าม scene ต้องผูกกับ scene ใหม่ใน `sceneLoaded` และระวังของ scene เก่าที่ยังไม่ unload ตอน Awake (เช่น `ProceduralMapGenerator.Instance`)
  - `CharacterAnimator` (ใช้ร่วม player/ศัตรู อ่าน `ICharacterLocomotion` + `MeleeAttack` + `Health`) / `DashAfterimage` / `CharacterAppearance` = presentation อย่างเดียว
  - `PlayerAttackInput` = ตัวรับ input โจมตี (InputAction rebind ได้) -> `PlayerCombat.RequestAttack` (validate ฝั่ง server)
- `Assets/Resources/Scripts/Combat/` — `Health` (HP/ทีม, `Health.All` ใช้หาเป้าแทน `PlayerMovement.Instance`), `MeleeAttack` (hitbox หลายอัน จังหวะเป็นสัดส่วนของท่า ไม่พึ่ง animation event / channel = กดค้างวนช่วง loopStart-loopEnd / Animator state Attack ใช้ Motion Time = `AttackTime` จาก simulation)
- `Assets/Resources/Scripts/Skills/` — gem แบบ PoE: `ActiveSkillGem` (ข้อมูลท่า + animation) / `SupportGem` (StatModifier: Flat / Increased% / More%) / `ResolvedSkill` (รวมค่าสุดท้าย) asset อยู่ `Gameobject/ScriptAbleObject/Skills/`
  - player: `PlayerSkills.slots` หลาย slot (แต่ละ slot = active 1 + support 5 ช่องของตัวเอง) ปุ่มต่อ slot อยู่ `PlayerAttackInput.slotBindings` (index ตรงกัน) -> `PlayerCombat.RequestAttack(slot, aim, held)` -> `MeleeAttack.TryAttack(ResolvedSkill, ...)` cooldown แยกต่อสกิล
  - ศัตรูใช้ `MeleeAttack.defaultSkill` + `AddModifier` (elite)
- `Player/PlayerStats` — stat sheet ที่เดียวของ player: ค่าฐาน (Max HP / Movement Speed / Attack Speed) + `AddModifier(source, mod)` / `RemoveModifiers(source)` -> push ไป Health, PlayerMovement.moveSpeed, PlayerSkills (Attack Speed = ฐานของทุกสกิล ท่า/hitbox/animation scale ตาม)
  - ค่าตัวละครห้ามแก้ตรงที่ Health.maxHealth / PlayerMovement.moveSpeed (sheet เขียนทับ) ให้แก้ที่ PlayerStats
  - `CharacterAnimator` สลับ clip ช่อง `attackSlotClip` เป็นท่าของสกิลผ่าน runtime AnimatorOverrideController
  - gem: `attackSpeed` (ความเร็วฐานสกิล × attack speed ตัวละคร), `animationVariants` (สุ่มท่าไม่ซ้ำติดกัน), `mirror` (Alternate/Random), `upperBodyOnly` (เล่นที่ layer `UpperBody` + mask `3DModel/Player/UpperBody.mask` ขาเดินตาม locomotion)
  - การสุ่มท่า/กลับข้างตัดสินใน `MeleeAttack` (AnimationVariant / Mirrored / BlendWeight, PresentationCount เพิ่มทุกครั้งที่เลือกใหม่) ไม่ใช่ฝั่งภาพ -> sync ผ่านเน็ตได้
  - channel + หลายท่า: ทุกรอบที่วนสุ่มท่าใหม่ / `blendAnimation` ผสมทับท่าหลัก (state โจมตีเป็น blend tree: ช่องท่า + `AttackBlendSlot`)
  - layer `UpperBody` มี `AttackA`/`AttackB` สลับกัน (ช่อง `Attack_Kick` / `AttackSlotB`) แต่ละ state มี param เวลา/กลับข้าง/น้ำหนักผสมของตัวเอง เข้าด้วย CrossFade จาก `CharacterAnimator`
  - Animator param: `Attacking` = ท่าเต็มตัว (บล็อกท่ากระโดด) / `AttackingUpper` = ท่าท่อนบน (ไม่บล็อก)
  - gem `areaOfEffect` = พื้นที่ฐาน (รัศมี × √area) / `teleportToTarget` = วาร์ปไปข้างศัตรูก่อนตี (Flicker Strike) ย้ายตัวผ่าน `ISkillMover` / `shadowCloneCount` = ร่างเงาตีเพิ่มตอนกดค้าง
  - ร่างเงา: ตำแหน่ง/ทิศ/ข้างคำนวณใน `MeleeAttack` (GetClonePosition / GetCloneAim / IsCloneMirrored) ใช้ทั้ง hitbox จริงและภาพ (`ShadowCloneEffect` บน Visual)
- `UI/DamageNumbers` — เลขดาเมจ (ฟัง `Health.AnyDamaged` pool BillboardText) ห้ามใช้ `TMP.alpha` กับ material BillboardText (หน้าตัวอักษรโปร่ง) ใช้ color.a แทน
- `Assets/Resources/Scripts/Enemy/` — `EnemyMotor` (Rigidbody + `JumpTo` วิถีโค้ง), `EnemyAI` (Idle/Wander → Suspicious → Chase → Dead), `EnemySpawner` (เกิดเป็น pack + elite สุ่มจาก seed), `EnemyPack` (เดินเล่นรอบจุดกลาง/เรียกเพื่อนรุม), `EnemyRank` (elite: ขนาด/HP/ดาเมจ) + `EliteGlow` (presentation)
- `ProceduralMap/Navigation/MapNavGraph` — grid graph ของ AI สร้างจาก TerraceLayout ทุกครั้งที่ generate (edge Walk / JumpUp / JumpDown) อ้างผ่าน `generator.NavGraph` ดูกราฟด้วย `MapNavGraphGizmos`
- Animator: `PlayerController` (state Attack/Death, param Attack/AttackSpeed/Dead) ศัตรูใช้ `EnemyController.overrideController` (สลับท่าโจมตีเป็น Boxing)
- Scenes: `Hideout`, `CreatedMap` (ไฟล์ scene เก็บผ่าน Git LFS)
