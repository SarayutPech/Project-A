# Procedural map, Navigation, Enemies

## Map (`Scripts/ProceduralMap/`)
- `ProceduralMapGenerator` is a partial class split across `ProceduralMapGenerator*.cs` (Shape / Mesh / Water / Objects / Navigation). The main file `ProceduralMapGenerator.cs` must keep this exact name (Unity binds the MonoBehaviour by file name) and its .meta GUID (scenes reference it). Settings live in `ProceduralMapConfig` (one SO per map variant, e.g. `MapConfig_GrassBiome`).
- Deterministic from (config + seed) via `System.Random` (rule 5). `TerraceLayout` builds levels / ramps / cliffs; ponds flatten their footprint, some are wadeable.
- Terrain is chunked per level (Ground / Ramps / Cliffs) with per-surface physics materials. The generated map is not saved in the scene.
- `MapBuildAnimator` (+ `MapBuildAnimationConfig`) plays the rise-up animation; `onBuildFinished` event. `MapStaticBatcher` combines meshes at runtime. `MapBoundaryBuilder`, `IslandUndersideBuilder`, `ScatterMeshCombiner`.
- Event `MapGenerated` fires on every (re)generate; `StartPosition`, `NavGraph`, `seed` are exposed.

## Navigation (`ProceduralMap/Navigation/`)
- `MapNavGraph`: grid graph for AI, rebuilt from TerraceLayout on every generate (edges Walk / JumpUp / JumpDown). Access via `generator.NavGraph`. Visualise with `MapNavGraphGizmos`.

## Enemies (`Scripts/Enemy/`)
- `EnemyMotor`: Rigidbody movement + `JumpTo` ballistic jumps, `Teleport`, `Die(direction)`.
- `EnemyAI`: Idle/Wander -> Suspicious -> Chase -> Dead. Targets from `Health.All`. `Initialize(graph, seed, pack)`.
- `EnemySpawner`: packs + elites, all rolled from `System.Random(seed ^ const)`; waits for `MapBuildAnimator` to finish; moves spawned enemies into the map scene; respawns on regenerate.
- `EnemyPack`: members wander around a shared centre, one spotting the player alerts the rest.
- `EnemyRank.MakeElite(EliteSettings)`: size / HP / damage / attack speed; `EliteGlow` = visual.
- Enemy prefab: `Gameobject/Prefab/Enemy.prefab` (has `LootDropper`, see Items-Loot.md).
