# Player, Camera, Scene flow

## Player (`Scripts/Player/`) — still single-player code (see coding rules 3–4)
- One local player persists across scenes: `PlayerMovement.persistAcrossScenes` -> DontDestroyOnLoad. The new scene's `PlayerSpawner` moves the existing player instead of spawning a new one.
- Anything the player carries across scenes must re-bind to the new scene in `sceneLoaded`. Beware objects of the old scene that are not unloaded yet during Awake (e.g. `ProceduralMapGenerator.Instance`).
- `PlayerStats`: the single stat sheet. Base Max HP / Movement Speed / Attack Speed + `AddModifier(source, mod)` / `RemoveModifiers(source)` -> pushed to `Health`, `PlayerMovement.moveSpeed`, `PlayerSkills` (Attack Speed is the base for every skill; moves/hitboxes/animation scale with it).
  - Never edit `Health.maxHealth` / `PlayerMovement.moveSpeed` directly (the sheet overwrites them) — change `PlayerStats`.
  - Modifier sources in use: `equip:<slot>` (PlayerEquipment), `buff:<itemId>` (PlayerConsumables), `level` (PlayerExperience).
  - Stats: MaxHealth, MovementSpeed, AttackSpeed, MaxMana, ManaRegen, CarryCapacity (Byte). `Mana` component = like Health (TrySpend/Restore, regen in FixedUpdate); skills do not cost mana yet.
- `PlayerExperience`: level/EXP, gains EXP from `Health.AnyDied` when `LastDamage.source` is this player (`ExperienceReward` on enemy or `defaultExpPerKill`, elite ×3). Saved via `ICharacterService` (`GameServices.Character`, local `character.json`). Level adds HP/Mana per level.
- Skill sets: `PlayerAttackInput` 5 buttons (LMB, RMB, Q, E, R); hold `alternateSetAction` (Ctrl) = slot i+5. Dash / sprint on Left Shift (tap / hold).
- `PlayerAttackInput` = attack input receiver (rebindable InputActions) -> `PlayerCombat.RequestAttack` (server-validated). It ignores clicks on UI and on in-range `ClickInteractable`s.

## Camera (`Scripts/Camera/`)
- `IsometricCameraController`.
- `OcclusionOutlineController`: objects between camera and player fade / outline / cut out. Per material slot mode Outline / Fade / Cutout. Cutout = `Shaders/OcclusionCutout.hlsl` (Shader Graph custom function, opaque + alpha clip, dithered circle around the player, globals `_OcclusionCutCenter/Radius/Softness`).

## Scene flow (`Scripts/SceneFlow/`)
- `SceneTransition.Load(name)`: fade out -> async load (Single) -> fade in. Self-creating, DontDestroyOnLoad.
- `ClickInteractable` (abstract): walk within `interactRange`, left-click the collider -> `Interact()`. Static `IsInteractableUnder(ray)` lets other click systems skip. Subclasses: `ScenePortal` (warp), `LootChest`.

## Character select (`Scripts/CharacterSelect/`)
- Scene `CharacterSelect` = first scene in the build (Tools > Project-A > **Build Character Select Scene** creates config + 5 sample races + scene and puts it at index 0). UI prefab `Prefab/UI/CharacterSelectUI.prefab` (**Build Character Select UI**, separate Canvas from GameUI because there is no player yet); included in **Export UI Art List** (+ a Races section with portrait/background sizes).
- Data: `CharacterCreationConfig` (`Resources/Gameobject/ScriptAbleObject/Characters/CharacterCreationConfig.asset`): `maxCharacterSlots`, name length, `races` (list order = carousel order). `CharacterRace` SO per race: `id` (saved, don't change), name, description, `portrait`, `background` (full screen, optional), `accentColor`, `passiveStartNode` (dropdown of passive-tree start nodes). Add/remove a race = edit the list, no UI work.
- Characters: `ICharacterService.ListCharacters / TryGetCharacter / CreateCharacter / DeleteCharacter` (per account `GameServices.LocalAccountId`). Character id = ownerId of inventory / progress / passives / loot. Rules (name 3–20 letters/digits/_/-, unique per account, free slot, race exists) in `CharacterCreation` (server side); delete also wipes inventory + loot stash.
- `GameServices.LocalPlayerId` is now settable: Play sets it to the chosen character id before loading `playScene` (Hideout). Starting Play directly in Hideout keeps `"local"` (old save, no race -> start node still free to pick).
- UI: `CharacterSelectScreen` (select panel: slots + Play/Delete (press twice), double-click = play, empty slot = create; create panel: carousel + name + Create/Enter, Back). `RaceCarousel`: cards cloned from `cardTemplate`; X/scale/alpha/brightness by AnimationCurve over distance from center, spring (stiffness/damping) for the bounce, loop option, click / `<` `>` / arrow keys. Edge fade = `Prefab/UI/Art/EdgeFade.png` overlays tinted with the background color.
- Back to character select: Pause > Save & Character Select (`PauseMenu.EndLocalSession` destroys the persistent player + GameUI after the fade, via `SceneTransition.Load(scene, beforeLoad)`).

## Pause menu + key bindings
- Esc (`GameUI.HandleEscape`): closes the top layer first (ConfirmDialog > Controls > Pause = resume > other windows); nothing open = open `PauseMenu`. Esc is not rebindable.
- `PauseMenu` (UIWindow in GameUI): Resume / Controls / Return to Hideout (disabled in Hideout) / Save & Character Select. Character data is already saved by the services on every change; "Save" only flushes PlayerPrefs.
- `GamePause.Set(bool)`: sets `LocalInputGate.GameplayBlocked` (movement, skills, hotkeys ignore input) and `Time.timeScale = 0` while `CanFreezeTime` (single player; TODO Netcode: never freeze the server, only block local input).
- `KeyBindings` (Player/): every local input component registers its `InputAction`s in OnEnable / unregisters in OnDisable (category Movement / Skills / Interface). The first keyboard/mouse binding of each action is rebindable (composites like Move = one row per part); overrides saved in PlayerPrefs `KeyBindings` and re-applied on register. Gamepad bindings are not touched.
- `PlayerMovement` input is now `moveAction` (WASD + arrows + left stick), `jumpAction`, `sprintAction`, `dashAction` (old `Key` fields removed). Read through `LocalInputGate.Allows/Held/Pressed` (typing / pause = ignored).
- `KeybindWindow` (Controls): rows from `KeyBindings.Entries`, click = wait for key (Esc cancels), red = same key used twice, Reset (confirm). Existing GameUI.prefab: **Tools > Project-A > Add Pause Menu To GameUI** adds both windows without rebuilding the prefab.

## Saving (local)
- Local services write their JSON on every change (`LocalJson.Save`, `LocalLootStash`). WebGL keeps `persistentDataPath` in memory until synced: every write calls `LocalJson.Flush()` -> `Assets/Plugins/WebGL/LocalSaveSync.jslib` (`FS.syncfs`, overlapping calls coalesced). Without it F5 loses the save.
- Checkpoint `GameServices.SaveAll()` (flush + `PlayerPrefs.Save`): on every load of Hideout (`SceneFlow/CheckpointSave`, no scene component needed), on Play from character select, and on Pause > Save & Character Select.
- Default movement keys: Left Shift = tap dash / hold sprint (`dashSprintSameKey` = true; false = separate Sprint / Dash actions).
