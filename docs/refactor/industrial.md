# Industrial category (2026-10-19)

The user: "a whole new Industrial category ... industrial awesomeness ... it shouldn't just look like a random scattered
blotchy patch of garbage. Like, it should be like someone handcrafted it." And: "not just surface drawn ... surface painted
elegantly ... dirt pile, mud puddle, garbage, clean, dirty ... mound of dirt that's painted, certain sections of concrete,
dead trees".

## The shelf

`Recipes/shelf/industry.json`: shown again, named **Industrial**, last in the Make row (order 9). It shows the `industrial`
group: the four new yards first (generator order 64–67), then the old industrial types that were hidden before (logistics
yard, scrapyard, landfill, quarry, construction site, abandoned lot). Nothing is hidden yet; hide styles there after the
user has seen them. The zoning type **Industrial estate** (`zoningIndustrial`) stays in Zoning: it zones lots, it does not
build a yard (ask the user before moving it).

## The new types (Core/Layers/Types/Industrial)

| type | styles |
|---|---|
| Container yard (`containerYard`) | terminal (harbour stacks, 2 high at the street to 6 high at the back), depot (20 ft rows, 2-high stacks, 40 ft lines, container wall), storage (self storage rows, trees) |
| Car yard (`carYard`) | salvage (pick-your-part double rows, parts bays), crusher (crusher pad with the metal mountain, crushed-metal heaps), breakers (carports with a car under each, parts bays) |
| Junk yard (`junkYard`) | sorted (concrete-block bays, one material each, the mountain, skips), reclaim (timber, pipes, pallets, drums, crates in blocks), tip (skips, bin lorries, rubbish bays) |
| Industrial works (`industrialWorks`) | recycling (plant, skips, lorries, sorted bays), salvage (wrecks one side, containers the other, mountain), works (plant with tall chimney, stacks, rubble heaps) |

One layout for all (`IndustrialSite`): a frame square to the street at the gate (the frame direction with the most yard
ahead of the gate), a front yard (barrier, gate booth, office row end-on to a paved forecourt, staff parking, weighbridge),
a spine, a header aisle, cross aisles between storage bands, column aisles on wide yards, storage blocks that lie wholly
inside the yard, and the front corners beside the office used as a band too. Every filler (`YardFill`) lays props on an
exact grid, one kind per row or bay, filling from the spine outward. The ground (`YardGround`): painted terrain instead of
a flat surface (dirt or gravel base, mud strips along the aisles, mud at the crossings and the gate, oily rings round heaps
and machines, worn dirt under rows, dry grass at the fence), concrete only as pads; spoil mounds (real terraform) only on
left-over ground with a dead tree, dead bushes and a fallen log; dead trees in the edge corners; puddle / oil / litter /
crack decals placed on purpose (asset packs only).

## The props and why (vanilla first; better ones preferred where they exist)

- **Containers**: `ShippingContainer01-03` (20 ft, 2.44 × 6.04), `TrainCargoCar01_Container01` (40 ft, 2.64 × 12.58), the
  harbour stacks `ShippingContainerPile{Small,Medium,Large}{Low,Medium,High}01` (13.9 / 34.9 / 70 m long; the two-high
  small and medium stacks sit 1.32 m off their pivot), `ContainerGridDecal02` for empty painted slots.
- **Skips and bins**: `LargeOpenContainer01` (roll-off skip), `TrashContainerEmpty03/04`.
- **Vehicles**: none in the base game as props, so Placemaker makes them (`Game/Assets/VehiclePropSystem.cs`): the game's
  own cars, vans, trucks, bin lorries, tractors, campers and loaders as static props, named `Placemaker Vehicle <Kind> <n>`.
  Fallbacks: the `Prop_*` vehicle props of asset packs, then a vanilla stand-in of the same size.
- **Offices**: a container (`ShippingContainer01`) as a site office, `ParkingLotBooth01/02` (gate booth), `ToolShed03`,
  campers as mobile homes, `PortableToilet01`, `FlagPole01`, `Trashbin01`.
- **Edge**: `ConcreteBarrier01-03` walls (bays, wheel stops, the edge), `FenceIndustrialLow02` (chain-link, GritAssets),
  `LightpoleIndustrial01/02` masts at the aisle ends.
- **Heaps**: `OreMiningTruck01_OrePile02/03` (rusty metal), `PileOreSmall01` / `PileOreMedium01` (the mountain),
  `RubblePileConcrete/Mixed/Wood`, `PileStoneSmall01`, `CoalTruck01_*Pile01` truck loads (tyres stand in as the dark
  loads), `Trashpile*`.
- **Goods**: `Pallet01-04`, `CratePile01-03`, `BarrelPile01-03`, `Barrel01-03`, `GasBottlePallet01`, `CableReel02`,
  `OilPipe01`, `ForestForwarderTrailer01_LogPile*` (timber), `FirewoodPile*`, `CardboardBoxPile*`.
- **Plant**: `IndustrialManufacturingDecoration0x_*` (the industrial buildings' machinery), `OilTankSmall*`,
  `IndustrialChimney{Small,Medium,Large}01 Agriculture`, `OreConveyorBelt01`, `Carport02` (dismantling bays).
- **Nature**: dead trees (any tree at age 4 = dead, `PlannedAge.Dead`), `FlowerBushWildDead02`, `Log02/05/08`.
