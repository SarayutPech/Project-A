# Combat, Skills, Character animation

## Combat (`Scripts/Combat/`)
- `Health`: HP + team. `Health.All` = every live character; use it to find targets instead of `PlayerMovement.Instance`. Events `Damaged` / `Died` / `Revived`, static `AnyDamaged`. `ApplyDamage` / `Heal` / `Revive` are server-only.
- `MeleeAttack`: several hitboxes per move, timed as fractions of the move (no animation events). Channel = hold to loop between `loopStart`–`loopEnd`. Per-skill cooldowns. The Animator Attack state uses Motion Time = `AttackTime` from the simulation.
- Animation variant / mirroring / blend weight are decided in `MeleeAttack` (`AnimationVariant`, `Mirrored`, `BlendWeight`; `PresentationCount` increments on every new pick), not in the visual layer -> network-syncable.
- Shadow clones: position/aim/side computed in `MeleeAttack` (`GetClonePosition` / `GetCloneAim` / `IsCloneMirrored`), used for both real hitboxes and visuals (`ShadowCloneEffect` on Visual).
- Flicker Strike (`teleportToTarget`): moves the attacker through `ISkillMover`; picks a landing spot on standable ground beside the target (skips targets with no valid landing).

## Skills (`Scripts/Skills/`) — PoE-style gems
- Damage is a range everywhere: gem `baseDamageMin/Max` (+`damagePerLevel` to both); every Flat Damage modifier (items, support gems, consumable buffs, passive nodes) uses `StatModifier.valueMax` — the Inspector (`Editor/StatModifierDrawer`) shows Min / Max fields for Damage+Flat, Value for everything else ("Adds 3-7 Damage"; other stats ignore it). `StatMath.ApplyRange` -> `ResolvedSkill.DamageMin/Max`; `MeleeAttack` rolls `RollDamage()` per target hit (server).
- `ActiveSkillGem` (move data + animation), `SupportGem` (`StatModifier`: Flat / Increased% / More%), `ResolvedSkill` (final combined values). `StatMath` = the one formula used everywhere: (base + Σflat) × (1 + Σincreased%) × Π(1 + more%).
- Assets: `Gameobject/ScriptAbleObject/Skills/Active/` and `.../Support/`. Gems are `ItemDefinition`s (inventory items, see Items-Loot.md); the player socket them through `PlayerInventory`, not the Inspector (Inspector loadout = starting gems only).
- Player: `PlayerSkills.slots` (each slot = 1 active + 5 supports). Keys per slot in `PlayerAttackInput.slotBindings` (same index) -> `PlayerCombat.RequestAttack(slot, aim, held)` (server validates) -> `MeleeAttack.TryAttack(ResolvedSkill, ...)`.
- Enemies: `MeleeAttack.defaultSkill` + `AddModifier` (elites).
- Gem fields:
  - `attackSpeed` = skill base speed × character attack speed
  - `animationVariants` = random move, never the same twice in a row; `mirror` = Alternate / Random
  - `upperBodyOnly` = plays on Animator layer `UpperBody` with mask `3DModel/Player/UpperBody.mask`, legs follow locomotion
  - channel + several variants: re-pick every loop; `blendAnimation` blends over the main move (attack state is a blend tree: move slot + `AttackBlendSlot`)
  - `areaOfEffect` = base area (radius × √area); `teleportToTarget` = Flicker Strike; `shadowCloneCount` = extra clone strikes while held
  - `spawnOnCast` = object spawned in front of the player when the move starts (server, `PlayerCombat.SpawnOnCast`, one per gem if `singleSpawnInstance`). A `ScenePortal` that leads to the current scene is rejected before the move starts. **Recall** gem (`ActiveGem_Recall`, id `recall`) = no hitboxes, spawns `WarpToHideout` (unusable in the Hideout).

## Character visuals (`Scripts/Character/`) — presentation only
- `CharacterAnimator` (shared by player and enemies): reads `ICharacterLocomotion` + `MeleeAttack` + `Health`. Swaps clip `attackSlotClip` to the skill's move through a runtime AnimatorOverrideController.
- Layer `UpperBody` has `AttackA` / `AttackB` alternating (slots `Attack_Kick` / `AttackSlotB`), each with its own time / mirror / blend-weight params, entered by CrossFade from `CharacterAnimator`.
- Params: `Attacking` = full-body move (blocks jump anims), `AttackingUpper` = upper-body move (doesn't block).
- Animator `PlayerController` (states Attack/Death, params Attack/AttackSpeed/Dead). Enemies use `EnemyController.overrideController` (attack swapped to Boxing).
- `DashAfterimage`, `CharacterAppearance`, `EliteGlow`, `ShadowCloneEffect` = visual only.
