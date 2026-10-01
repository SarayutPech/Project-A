# Project-A

Path of Exile-style ARPG (hideout + procedural maps). Unity 6000.6, URP, new Input System.
- **Talk to the user in Thai.** Code comments are written in Thai (match the existing style).
- Status: prototype, single player, no server yet. Target: 2–6 player host-client co-op (Netcode for GameObjects, local testing only) -> later dedicated servers (instance server per party + shared town server) + central backend.
- Player data (inventory, character) is stored locally for now but must sit behind interfaces so it can move to a backend.

## Coding rules (must stay portable to a dedicated server)
1. **Server-authoritative**: anything that affects the game (combat, damage, HP, AI, loot, spawns, map changes) is decided on the server. Check `IsServer`, never `IsHost`. Clients send requests/input (RPC), never results ("attack target X", not "deal 50 damage").
2. **Presentation separate from simulation**: no graphics/audio/particles/UI/camera in server logic. Gameplay code must not depend on `Camera.main`, Renderers, UI or Input (headless server). Visual-only code goes in its own component (or `#if !UNITY_SERVER` / headless check).
3. **No singletons tied to "the one player"**: keep "local player" and "all players" separate. `PlayerMovement.Instance` is legacy — don't add new uses, phase it out.
4. **Input separate from movement/actions**: read input only in the local player's input component and forward it as commands; never read `Keyboard.current` in gameplay code.
5. **Deterministic maps**: a map must rebuild from (config ID + seed) only. Generators use their own `System.Random(seed)`, never `UnityEngine.Random`. Sync seed + config ID over the network, never meshes.
6. **Multiple instances per process**: don't assume one map/scene. Avoid per-map static state in new code (e.g. `ProceduralMapGenerator.Instance`, `GeneratedRoot`); go through the instance's context. Physics queries may use that scene's `PhysicsScene`.
7. **Player data through service interfaces** (e.g. `IInventoryService`, `ILootStashService`; local JSON now, backend later). Items enter via drop -> claim (server creates a drop with an id, player claims by id, once). Clients never add items. Multi-party moves (trades) are a single transaction.
8. **Never trust the client**: validate everything server-side (range, cooldown, ownership).
9. **Tick-based**: gameplay logic is frame-rate independent (FixedUpdate / network tick, ~20–30 Hz on server).
10. **Memory**: runtime-created assets (Mesh, Material, Texture, `ScriptableObject.CreateInstance`) must be destroyed manually; every `+=` needs a `-=`; pool frequently spawned objects.

## Layout (`Assets/Resources/Scripts/`)
| Folder | What | Details |
|---|---|---|
| `ProceduralMap/` | `ProceduralMapGenerator` (partial class, files `ProceduralMapGenerator*.cs`), `ProceduralMapConfig` SO, `TerraceLayout`, `Navigation/MapNavGraph` | [Docs/Map-Enemies.md](Docs/Map-Enemies.md) |
| `Enemy/` | `EnemyMotor`, `EnemyAI`, `EnemySpawner`, `EnemyPack`, `EnemyRank` | [Docs/Map-Enemies.md](Docs/Map-Enemies.md) |
| `Player/`, `Camera/`, `SceneFlow/`, `CharacterSelect/` | movement, stats, input, spawner, camera, scene transitions, `ClickInteractable`, character select/create + races | [Docs/Player-Scenes.md](Docs/Player-Scenes.md) |
| `Combat/`, `Skills/`, `Character/` | `Health`, `MeleeAttack`, PoE-style gems, shared `CharacterAnimator` | [Docs/Combat-Skills.md](Docs/Combat-Skills.md) |
| `Items/`, `UI/` | items (SO), loot tables, loot stash + hideout chest, damage numbers | [Docs/Items-Loot.md](Docs/Items-Loot.md) |
| `Passives/` | passive skill tree (`PassiveTree` SO + editor window, `PlayerPassives`), Str/Dex/Int | [Docs/Passives.md](Docs/Passives.md) |

**Read the matching Docs file before changing a system.** When you add or change a system, update its Docs file (keep this file short).

- Assets (SO, prefabs): `Assets/Resources/Gameobject/` (`ScriptAbleObject/Skills/Active|Support`, `ScriptAbleObject/Items/`, `Prefab/`)
- Scenes: `CharacterSelect` (first), `Hideout`, `CreatedMap` (Git LFS)
- Devlogs: `Devlog/` (outside Assets on purpose — `Assets/Resources` is bundled into builds)

## Working efficiently (token budget)
- Big files: read with offset/limit or Grep first, not whole. Never read `.shadergraph`, `.unity`, `.prefab`, TMP font assets wholesale — grep for what you need.
- Unity MCP `Unity_RunCommand` echoes the full script back; keep commands short and focused.
