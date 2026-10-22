# Teardown 11: Outgrow fence gaps, crossovers and flickering outlines

The user, 21 Oct (four screenshots): with Outgrow on, fences have gaps along roads and turning circles and at corners, hedges
of two gardens cross each other, and "all blue outlines flicker when you pan and hover over something", since a trick that tried
to hide the blue outline. Measured in the game runner and offline; **[P]** = proven by a measurement below.

## 1. What was wrong

* **[P] Endless re-plans (the flicker).** In the user's city Brecon, a garden's plan could lie over a neighbour's lot (on a curve
  the middle line between two side neighbours ran 92 m² into the next lot). Every pass that saw it planned that garden again
  (`GardenClashes`), which made the same garden again, for ever: 620 re-plans in 150 s with the city paused, about 45 fence and
  hedge pieces deleted and made again every second. A hovered (lit) house's pieces kept being replaced, so its outline jumped
  (runner: 20 jumps and 27 of 240 frames under half the outline, panning over house 48800).
* **[P] The Real-preview outline trick never hid anything.** It left the Outline layer out of the camera, but the game's outline
  pass asks for that layer itself (`OutlinesWorldUIPass.AggregateCullingParameters`), so HDRP culled the scene a second time
  every frame for it (`CustomPassVolume.Cull`) and still drew every ghost tree and bush in blue and red.
* **[P] Gardens over each other and over houses (the crossovers).** (a) The plan's own gardens could cover another house's lot
  (above). (b) A re-plan only looks 160–400 m around, but a garden can reach 500 m: two big gardens planned in different passes
  lay over each other, and a garden grew straight over houses outside its pass ("houses are the plan's own", never obstacles).
  Fences of one garden then stood inside the other; the game hid pieces standing in another lot (holes). Runner, every reach at
  500 m: 17 gardens over each other, 281 fences inside another garden, 134 Outgrow pieces hidden.
* **[P] Gaps in fence runs.** Each garden edge was closed alone: one piece centred in its open stretch, 20 cm short of both ends
  (a notch at every corner); edges under 1.5 m and stretches under 2 m left out (a curve's or turning circle's short edges were
  gaps); a piece whose middle touched a driveway or road left out whole (a 4 m gap); a run that met another fence offset by up to
  0.9 m stopped there (a step); a line that crossed another was cut 1.5 m short of it.

## 2. What changed

* `YardStitch.Separate`: no garden a plan writes lies over another house's lot or another of its gardens.
* `NeighbourPolishSystem`: a pass also includes (fixed) every house whose saved garden reaches into it, keeps the lots of houses
  it does not have off limits (barriers), keeps simultaneous passes apart by their reach, and never plans one garden again for
  lying over a lot more than three times a minute (`ClashAgain`).
* `YardStitch.CloseRuns` / `LayRun` / `JoinEnds`: closing pieces laid as continuous runs across the plot's corners (pieces end at
  corners, follow bends chord by chord, stop at driveways and houses, end on the fence they meet, finish with a 2 m piece where the
  kind has one), then loose fence ends close together along a garden edge joined. `Dedupe`: a line that crossed another ends on
  it (a T). Knobs `separate`, `closeRuns`, `joinEnds`, `dedupeT` (1 by default) switch them off for a side-by-side check.
* `ShowHiddenPieces`: a garden's own overlapping run pieces never count as a doubled fence. 2 m pieces share the garden's look.
* Real preview: the game's outline pass draws no layer while it shows (`OutlinesWorldUIPass.m_OutlineLayer`), put back after.

## 3. Measurements

| | before | after |
|---|---|---|
| Brecon, lit house panned 240 frames: outline jumps / frames under half | 20 / 27 | 0 / 0 |
| Brecon, 10 s after settling: pieces deleted / made | 477 / 432 | 0 / 0 |
| Real preview: game outlines on ghost trees and bushes | all | none |
| Offline replay, Brecon passes (59): gaps / gardens over a lot | 116 / 133 | 0 / 0 |
| Offline replay, test town passes (43 + 39): gaps | 80 + 53 | 3 + 3 |
| Runner, test town at 500 m reach: gaps / overlaps / fences inside a garden / pieces hidden | 26 / 17 / 281 / 134 | 2 / 0 / 0 / 0 |

## 4. Tools

* `{"do":"lab","op":"fenceMap"}` (`Game/Suite/SuiteLab.FenceMap.cs`): every fence and hedge, garden, road and building near the
  build (or `"street"`, or `"box": "gardens"` in a loaded city); gaps, crossings, doubles, fences inside a garden, gardens over
  each other; close-ups of the worst spots, of where gardens meet (`junctions`) and of edges along roads (`edges`), the same spots
  again in a later map (`spotsFrom`). `node tools/suite/FenceMap.js <fencemap.json> [--at x,z --size m]` draws it.
* `{"do":"lab","op":"outlineFlicker"}` (`SuiteLab.OutlineFlicker.cs`): a house lit as hovered, the camera panned every frame, each
  frame's outline pixels counted.
* `Tests/Placemaker.Tests/FenceContinuity.cs`: the same checks on a plan; `FenceContinuityReplay` replays a run's
  `polishPassDump` passes (`PM_FENCE_PASSES`, `PM_POLISH_KNOBS`, `PM_FENCE_AT`, `PM_FENCE_PROBE`).
* Scenarios: `outgrow-fence-ab.json` (old and new side by side), `outgrow-fence-map.json` (a growing suburb),
  `outgrow-fence-map-city.json` and `outline-flicker-city.json` (the user's city Brecon, read-only, `-Fresh`), `outline-flicker.json`.
