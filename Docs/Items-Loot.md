# Items, Loot, UI

## Items (`Scripts/Items/`) — ScriptableObjects
- `ItemDefinition` (base): `id`, `displayName`, `description`, `icon`, `rarity` (Normal/Magic/Rare/Unique, `RarityColor`), `maxStack`.
  - `EquipmentItem`: `slot` (`EquipSlot`; `Ring` fits Ring/Ring2) + `modifiers` (same `StatModifier` as gems). Equipped via `PlayerEquipment.Equip/Unequip` -> PlayerStats source `equip:<slot>`.
  - `ConsumableItem`: `healFlat`, `healPercent`, `buffs` + `buffDuration`, per-type `cooldown`. Used via `PlayerConsumables.RequestUse(item)` (server) -> PlayerStats source `buff:<id>`, re-use refreshes instead of stacking. Caller deducts the item when it returns true.
- Held/saved form = `ItemStack` (item id + count), never asset references. `ItemDatabase.Get(id)` loads every `ItemDefinition` under `Resources/Gameobject/ScriptAbleObject/Items/` (subfolders included); ids must be unique.
- Assets: `Gameobject/ScriptAbleObject/Items/{Equipment,Consumable,LootTables}/`. Placeholder icons: `Textures/ItemIcons/`.

## Loot flow
1. `LootDropper` (on `Enemy.prefab`) on `Health.Died` rolls its `LootTable` (elite = `eliteRollMultiplier`×) — server only.
2. Each stack -> `GameServices.LootStash.Add(ownerId, stack)` (`ILootStashService`). Owner is `GameServices.LocalPlayerId` for now (TODO with Netcode: resolve the killer / party). Static event `LootDropper.AnyDropped` for visuals.
3. Loot does **not** drop on the ground. In the Hideout, `LootChest` (ClickInteractable) opens `LootStashWindow`.
4. `TryClaim(owner, dropId)` removes a drop once (for the future inventory). `Clear(owner)` wipes unclaimed loot.
5. `MapLootReset` (in CreatedMap, on the Enemy Spawner object) clears unclaimed loot in Awake (before enemies spawn) and on map regenerate.
- `LocalLootStash` = JSON at `persistentDataPath/loot_stash.json`, saved on every change.


## Inventory (`Scripts/Items/`)
- `IInventoryService` (`GameServices.Inventory`, local = `inventory.json`): storage only. Bag = list of `ItemInstance` (instanceId + itemId + count). Equipment / gems = one per row; Usable / Other stack up to `ItemDefinition.maxStack` (`IsStackable`, `StackLimit`; `MiscItem` default 20). `Add(itemId, count)` fills existing stacks first, `RemoveCount` lowers a stack (using a consumable takes 1). `Sort` (Sort button -> `PlayerInventory.RequestSort`) merges partial stacks and orders by category, rarity, name, level. The local service also merges stacks on load (old saves / changed maxStack). Equipped = `EquippedItem` (slot + instance). `Equip`/`Unequip` move bag <-> slot in one call (one transaction).
- `PlayerInventory` (on player, server rules): `RequestEquip/Unequip/Use/Discard`, `RequestClaim/ClaimAll(stash)` (loot chest -> bag, a stack drop becomes N pieces). Weight limit: sum of `weight × count` (Byte) of bag items <= `PlayerStats.CarryCapacity`, checked only when picking up loot; unequip / unsocket always succeed even over the limit; equipped and socketed items don't count. New character (no save): `startingItems` + `PlayerEquipment.startingEquipment` become real instances. Start syncs saved equipment into `PlayerEquipment`.
- Categories (`ItemDefinition.Category`): `EquipmentItem` = Equipment, `ConsumableItem` = Usable, everything else (`MiscItem`) = Other.
- Equip slots: Weapon, OffHand, Helmet, BodyArmour, Gloves, Boots, Belt, Amulet, Ring, Ring2, Cloak.
- Skill/Support gems are items too: `SkillGem : ItemDefinition` (weight 1 B by default, categories SkillGem / SupportGem). `ItemDatabase` also loads `Gameobject/ScriptAbleObject/Skills`. `ItemInstance.level` = gem level.
- Gem sockets: `IInventoryService.Socket/Unsocket/GetSocketed` (`SocketedGem`: skillSlot + socket, `ActiveSocket = -1`, supports 0-4). `PlayerInventory.RequestSocketGem/RequestUnsocketGem` validates gem type / slot / weight, then `PlayerSkills.SetActiveGem/SocketSupport`. Socketed gems don't count toward weight. First run with no gems anywhere: the gems set in `PlayerSkills` (Inspector) become real socketed instances.
- `GameServices` lives in `Items/GameServices.cs` (+ `LocalJson` helper).

## UI (`Scripts/UI/`)
- **GameUI** (`Scripts/UI/`): one Canvas prefab `Resources/Gameobject/Prefab/UI/GameUI.prefab`, built once by **Tools > Project-A > Build UI Template** then edited by hand (textures = swap Source Image; bars need Image Type Filled + sprite). Every part is a singleton (`UISingleton<T>`, duplicates destroyed). `LocalPlayerUI` (on Player.prefab) instantiates it (DontDestroyOnLoad) and `Bind(player)` -> every `IPlayerUI`. Headless (`isBatchMode`) creates no UI.
  - `StatusHud` (top-left HP/Mana/EXP/level), `SkillBarView` + `SkillSlotView` (bottom-right 5 slots; hold Ctrl = set II = `PlayerSkills.slots[5..9]`), `InventoryWindow` (I; tabs Equipment/Usable/Other, rows from `RowTemplate`, weight bar), `CharacterWindow` (C; 11 `EquipSlotView` + stats), `ItemTooltip` (hover, follows cursor, own nested canvas on top). Windows = `UIWindow<T>` (CanvasGroup show/hide, Esc closes). RMB / double-click = equip/use/unequip.
  - Scene load (Single) -> `UIWindows.CloseAll()` (every `UIWindow<T>` registers itself); HUD + minimap stay. `GameUI.SceneChanged` event.
  - Drag a bag row with LMB and release outside any UI = asks `ConfirmDialog` first (stack > 1 -> `AskAmount` slider + number field), then `RequestDiscard(id, count)` (permanent). `ConfirmDialog.Ask(title, message, confirmText, onConfirm)` = shared modal (full-screen blocker). `LocalInputGate.PointerCaptured` blocks attacks while dragging; rows that cannot be dragged forward the drag to the list ScrollRect.
  - Inventory: 5 tabs (Equip / Usable / Other / Skill / Support) + search per tab (`SearchQuery.Matches`: spaces = LIKE wildcard, tokens in order, "a ppl" matches "Apple").
  - `SkillGemWindow` (K, or click a HUD skill slot): left = all skill slots, center = active socket + 5 supports with link lines + resolved stats, right = gems in the bag that fit the selected socket (search). Click socket -> click gem = socket; right-click socket = remove.
  - `PassiveTreeWindow` (P) -> see Passives.md.
  - `MinimapView` (top-right) + full-screen overlay (Tab, no raycasts, opacity from the small vertical slider right of the minimap, saved in PlayerPrefs). Map image = height layers (white = high, dark = low, darker edge where the layer changes, transparent = no ground) built once per scene / `MapGenerated` after `MapBuildAnimator` finishes: downward raycasts in the scene `PhysicsScene` (spread over frames, characters and triggers ignored), layer height = `config.terraceHeight` on terrace maps else `layerHeight`. Texture is in world X/Z and the whole map is rotated by the game camera yaw in UI. Hideout bounds = colliders except huge floors (`fallbackFloorSize`) + margin. Live icons: player arrow, enemies (elite bigger), `ClickInteractable.All`. Scroll = zoom.
  - Typing in a search field sets `LocalInputGate.KeyboardCaptured` -> movement / skill keys / UI hotkeys ignore the keyboard.
  - **Tools > Project-A > Setup Player Prefab For UI** adds Mana / PlayerConsumables / PlayerInventory / PlayerExperience / LocalPlayerUI, fills 10 skill slots, moves dash off Ctrl.
- `UIToast.Show(msg)` = short message top-centre (bag full, cannot socket, no passive points...).
- Selection: `GameUI` clears the EventSystem selection after mouse release (except input fields) and the builder sets every Selectable navigation to None -> WASD never drives UI (minimap opacity slider bug).
- **Tools > Project-A > Export UI Art List** (also runs after Build UI Template): `ArtExport/UI/UI-Art-List.md` (every Image: path, px size at 1920x1080, Sliced/Filled, colour) + empty PNG frames at real size per window, outside Assets.
- `LootStashWindow`: part of the GameUI prefab (UIWindow singleton, art-editable). Rows = `InventoryRowView` grouped by item id; click = claim one drop, Take All. `LootChest` opens it via `LootStashWindow.FindOrCreate` (returns the GameUI instance).
- `DamageNumbers`: listens to `Health.AnyDamaged`, pooled `BillboardText`. Do not use `TMP.alpha` with the BillboardText material (face goes transparent) — use `color.a`.
- `BillboardText`: camera-facing world text (bob/pulse/outline, optional overlay material).

## Not done yet
Potion hotbar, drop popups, rolled affixes, drag & drop, item discard UI, Thai TMP font for the GameUI prefab.
