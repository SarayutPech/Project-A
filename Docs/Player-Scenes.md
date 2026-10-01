# Player, Camera, Scene flow

## Player (`Scripts/Player/`) — still single-player code (see coding rules 3–4)
- One local player persists across scenes: `PlayerMovement.persistAcrossScenes` -> DontDestroyOnLoad. The new scene's `PlayerSpawner` moves the existing player instead of spawning a new one.
- Anything the player carries across scenes must re-bind to the new scene in `sceneLoaded`. Beware objects of the old scene that are not unloaded yet during Awake (e.g. `ProceduralMapGenerator.Instance`).
- `PlayerStats`: the single stat sheet. Base Max HP / Movement Speed / Attack Speed + `AddModifier(source, mod)` / `RemoveModifiers(source)` -> pushed to `Health`, `PlayerMovement.moveSpeed`, `PlayerSkills` (Attack Speed is the base for every skill; moves/hitboxes/animation scale with it).
  - Never edit `Health.maxHealth` / `PlayerMovement.moveSpeed` directly (the sheet overwrites them) — change `PlayerStats`.
  - Modifier sources in use: `equip:<slot>` (PlayerEquipment), `buff:<itemId>` (PlayerConsumables), `level` (PlayerExperience).
  - Stats: MaxHealth, MovementSpeed, AttackSpeed, MaxMana, ManaRegen, CarryCapacity (Byte). `Mana` component = like Health (TrySpend/Restore, regen in FixedUpdate); skills do not cost mana yet.
- `PlayerExperience`: level/EXP, gains EXP from `Health.AnyDied` when `LastDamage.source` is this player (`ExperienceReward` on enemy or `defaultExpPerKill`, elite ×3). Saved via `ICharacterService` (`GameServices.Character`, local `character.json`). Level adds HP/Mana per level.
- Skill sets: `PlayerAttackInput` 5 buttons (LMB, RMB, Q, E, R); hold `alternateSetAction` (Ctrl) = slot i+5. Dash moved to LeftAlt.
- `PlayerAttackInput` = attack input receiver (rebindable InputActions) -> `PlayerCombat.RequestAttack` (server-validated). It ignores clicks on UI and on in-range `ClickInteractable`s.

## Camera (`Scripts/Camera/`)
- `IsometricCameraController`.
- `OcclusionOutlineController`: objects between camera and player fade / outline / cut out. Per material slot mode Outline / Fade / Cutout. Cutout = `Shaders/OcclusionCutout.hlsl` (Shader Graph custom function, opaque + alpha clip, dithered circle around the player, globals `_OcclusionCutCenter/Radius/Softness`).

## Scene flow (`Scripts/SceneFlow/`)
- `SceneTransition.Load(name)`: fade out -> async load (Single) -> fade in. Self-creating, DontDestroyOnLoad.
- `ClickInteractable` (abstract): walk within `interactRange`, left-click the collider -> `Interact()`. Static `IsInteractableUnder(ray)` lets other click systems skip. Subclasses: `ScenePortal` (warp), `LootChest`.
