# Custom assets on the fly: what a CS2 code mod can do (R&D, 2026-10-18)

Question from the user: when a game asset gets placed, what can a mod hook into, and can a mod make custom assets on
the fly (e.g. buildings shaped to their land instead of the "square world")? Not a Placemaker feature: a possible new
mod. Sources: decompiled Game.dll 1.6.2f1 (`%TEMP%\pm-decomp\game`), Colossal.IO.AssetDatabase / AssetPipeline, and the
user's installed mods (ExtraAssetsImporter, RoadBuilder, Recolor, Platter, ParkingLotTool, MoveIt, …). The spike is
`Game/Suite/SuiteLabShapeSystem.cs` + `SuiteLab.cs` + `SuiteLabMesh.cs`, run by `suite/scenarios/lab-shapes.json`.

## 1. Hooks when anything is placed

Everything placed (by a player tool or by the simulation: a growable spawning, a tree, a road) goes through the same
pipeline, and a mod can add its own ECS systems at every stage:

| Stage | Where | What a mod can do there |
|---|---|---|
| Definitions | `CreationDefinition` + `ObjectDefinition`/`NetCourse` entities, before `GenerateObjectsSystem` (Modification1). Zone spawns come from `ZoneSpawnSystem.SpawnBuildingJob` the same way. | Swap the prefab, move/rotate, change the random seed or tree age, add or delete definitions. The cleanest hook: the object is created with the new prefab from the start. |
| New entity | Modification2, before `SubObjectSystem` | Re-roll `PseudoRandomSeed` (colours, props, window lights), set `CustomMeshColor`, `Stack`, `Tree`, add own components, queue a level-up target (`UnderConstruction.m_NewPrefab`). |
| Children | after `SubObjectSystem` / `LaneSystem` (Modification3+) | Hide, replace, recolour the building's props; they are regenerated on every owner `Updated`, so redo it then. |
| Validation | after `ValidationSystem` | Add `Error` to a preview to forbid a placement. |
| Simulation | `BuildingConstructionSystem`, `BuildingUpkeepSystem` (level-up) | Choose the next prefab of a level-up (same zone and lot size, renters kept). |
| Rendering | `MeshColorSystem`, `BatchInstanceSystem` | Per-instance colour via `CustomMeshColor` + `BatchesUpdated` (Recolor does this; survives removing the mod). |

Harmony ships with mods (`0Harmony.dll`), but nearly all of this logic runs in Burst jobs that Harmony cannot patch.
Managed code (tool `OnUpdate`s, raycasts, prefab APIs, UI, material/mesh loading in `ManagedBatchSystem`/`BatchMeshSystem`)
can be patched; mods like Platter, ExtraDetailingTools, Anarchy patch `ObjectToolSystem`, `ToolBaseSystem.GetRaycastResult`.

Per instance there is only: transform, random seed, colour (`MeshColor`/`CustomMeshColor`), one stretch axis (`Stack`),
tree/quantity/damage states, light intensities. **Mesh and material are per prefab.** No per-instance scale, texture or
shape.

## 2. Making new assets while the game runs

Proven by shipped mods:
* **New prefabs from vanilla pieces** (clone, change components, swap meshes/materials): RoadBuilder (whole roads),
  TownRoadLane, ParkingLotTool, Platter, DrivewayEntries, and Placemaker itself (decal opacity copies, surface copies,
  park companions).
* **Geometry built in code, drawn by the game's renderer**: ExtraAssetsImporter makes a `GeometryAsset` from a Unity
  `Mesh` (`SetData(Mesh[])`), saves it into its own registered file database, wraps it in a `RenderPrefab` +
  `StaticObjectPrefab`. Its only mesh is a decal box.
* **Textures and materials from files**: EAI imports PNGs with the game's own importer and builds `SurfaceAsset`s from the
  game's material library templates by name.
* **Saves**: prefabs are referenced by type + name. Generated prefabs must exist before a save loads (rebuild them at
  startup from a list, as RoadBuilder, EAI and Placemaker do). Without the mod, objects of a missing prefab become
  invisible placeholders that stay in the save and come back when the mod returns.

The engine rules found (BatchMeshSystem, GeometryAsset):
* The renderer **streams geometry from files** and unloads it again, so a generated mesh must be saved into a registered
  database (`AssetDatabase.global.RegisterDatabase` + `FileSystemDataSource`); a purely in-memory geometry stalls a loading
  slot (30 slots for the whole game) or vanishes.
* Vanilla materials expect **octahedral normals/tangents** (`_TANGENTSPACE_OCTO`): pack them with the game's own
  `NativeMath.ArrayNormalsToOctahedral` / `ArrayTangentsToOctahedral`.
* Reusing a vanilla building material: use the **shared** `SurfaceAsset` (a copy loses virtual texturing); keep UVs in
  0..1.
* One shape = one prefab (two prefab entities, one geometry file, one GPU mesh, its own draw batch). Fine for dozens or
  hundreds; not for one unique mesh per house in a 100k city.

## 3. Spike: generated 3D objects in the game

`lab-shapes`: five outlines (box, wedge, L-block, octagonal tower, free polygon) extruded into walls and a roof in code,
each made into a new prefab at runtime with a borrowed vanilla building material, placed on open land. Result (run
`send-lab-yards-1018-102928`): all five made, placed and drawn by the game's renderer (1 mesh batch each, lit, shadowed),
no errors in Player.log. The borrowed office surface (template "Default", VT, `_TANGENTSPACE_OCTO`) stretches one
facade texture over each face (UV 0..1 per face): it proves the pipeline, not the look. Of 400 building surfaces, 209
have no interior-window/emissive/wind channels (templates: Windows 184, Default 168).

## 5. Yard stitching test (same run)

`lab-yards`: a curved (R 90 m) and a straight Small Road zoned low-density residential, grown, then every house's own
lawn (Grass Surface 01/02) stretched into the bare land within 10 m of its lot and 56 m of the street, cut around every
other surface. Curve: 24 houses, 21 lawns stretched (+12,800 m²); straight: 16 houses, 16 stretched (+10,100 m²); all
drawn. Findings:
* It reads as painted ground (the user's verdict, right): lawns run past the houses' own fences and hedges into open
  land, the 1 m split leaves staircase edges, and the mowed-stripe texture over whole wedges looks fake. The cut around
  other surfaces also shattered the curve's lawns into 428 extra pieces.
* Houses still under construction lose the edit: 10 of 21 stretched lawns were gone 30 s later (construction finishing
  recreates every sub-area). Stitching must wait for finished houses and re-apply after a rebuild or level-up.
* Next (what the user meant): the houses' own parts. Hedges and fences are lanes owned by the house (rebuilt on every
  update): hide them and lay free-standing ones along the stitched borders (one shared hedge between neighbours, a hedge
  closing each curve wedge), plant bushes/trees along them, and stretch lawns only up to those borders.

## 6. Neighbourhood polish (built: setting "Stitch neighbouring houses", off by default)

The answer to "modify houses on the fly so they snap together": the game's own houses are never replaced; their own parts
are edited after they are placed (Game/Neighbours, Core/Neighbours/YardStitch, 12 tests):
* **One plot per house first** (version 2, after "lawns and fences each doing their own thing look wrong"): the lot,
  widened on each side with a neighbour to the middle line between the two lots (the shared edge on a straight street,
  the bisector of a curve's wedge), deepened to the middle line with a house behind it (back to back) or facing it
  side-on (the corners of a block, where two streets' houses meet; one side may face several houses). A row of side
  neighbours shares one back line (a shallow lot reaches the row's deepest, at most 16 m further). The inside of a closed
  ring (a circle street) stops at one ring line at half the street's radius (never through a house): even slices round
  a shared green, not long points. A rectangle cut by straight lines: convex, no overlaps, never into the part of a lot
  another house keeps.
* **Fence and hedge lines** are utility lanes (UtilityTypes.Fence) owned by the house, and they go on the plot's edges:
  one line per shared side (the nicer, hedge > fence > wall; the other becomes a 2 cm stub, drawn as nothing), the back
  line on the plot's back edge. A line ending on the lot edge where the plot reaches further carries on to the plot edge
  (≤ 20° turn, ≤ 12 m longer; straight on up to 30 m). No line leaves its plot; two lines on one another (back to back)
  keep the nicer. A moved line stops at a house model.
* **Lawns** (the house's own Grass Surface) fill the plot once it reaches past the lot, around every driveway and paving,
  never past the plot: lawn and fence share their edges. Land no lawn of the house reaches (a gravel-yard house, the far
  side of a driveway) gets a lawn OWNED by the house (a permanent area with the house as its owner: listed among its own
  areas, deleted with it, made again after a rebuild). A hole (a patio ringed by lawn) is opened with a 0.4 m slit where
  it shows least (through the house costs nothing).
* **The middle of a ring**: the land the back edges enclose, minus every plot, is one shared green (vanilla lawn + a
  mulch bed, permanent definitions as zone spawns are made).
* **Ground paint** (terrain materials, as builds paint): dry-grass fades (8 m) from every fitted lawn into the field,
  meadow on shared greens and wedges with no lawn, worn dirt where a shared side meets the back fence and the street.
  Vanilla has no decorative ground decals (docs/DECALS.md); decals from the player's packs could be added on top but
  cannot be seen in the game runner (clean profile).
* A pass plans with every house within 110 m of the houses that changed but edits only those within 40 m, so a house at
  the edge of a pass is never planned without its neighbours.
* Offline replay: lab op "polishDump" records the houses as the planner sees them; tools/hot/Replay-Polish.ps1 -Run <run>
  replays the planner on them and draws each plan (plots, lawns, fences, hidden lines) in seconds.
  suite/scenarios/polish-more.json adds a very tight curve (R 30), a dead-end street, medium density, row houses, high
  density, low commercial and a level-up (lab op "levelUp": the game's own debug level-up).
* Re-applied in the same frame whenever the game rebuilds a house's lanes (construction end, level-up, any Updated):
  NeighbourPolishSystem runs in Modification4B after LaneSystem; lawn writes wait for the next frame before
  Areas.GeometrySystem (Modification2B) — writing them later crashed the game natively.
* Fast loop: tools/hot/Send-Hot.ps1 builds YardStitch.cs alone and loads it into the open game (lab op "hotload"); live
  knobs via lab op "polishSettings"; suite/scenarios/polish-shapes.json runs a tight curve, a gentle curve, a full circle
  and a square block in one launch (~1.5 min in an open game).

## 4. What a mod could do, in order of risk

1. **Context-aware placement (low risk, no new assets)**: on spawn or level-up pick the variant that fits (neighbours,
   slope, district, curve), re-roll seeds so a row matches, recolour by street or district, add/hide props, age trees.
2. **Gardens and fill (low risk)**: reshape a growable's own lawn into the gaps (the vanilla "Surface Area" tool does the
   same; must be re-applied after a level-up) and fill leftover wedges with free-standing surfaces, hedges, trees.
3. **Kit-bashed variants (medium)**: clone growables with combined vanilla meshes/props; need a full level chain per
   variant or they never level up.
4. **Generated buildings (high, the "grand" idea)**: buildings whose mesh is generated for their exact land. Possible as
   placed (ploppable) objects; as real growables they still sit on rectangular zoning lots (`VacantLot`, `ZoneCheckSystem`),
   so a generated shape would be the visual of a rectangular lot, or a ploppable "fill building" next to growables.
   Costs: a prefab per shape, saves depend on the mod, materials limited to what vanilla textures allow.
