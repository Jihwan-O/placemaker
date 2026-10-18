# The strip mall, started over (shops4, 2026-10-17)

## The user

> A strip mall should be a PARKING LOT with shop buildings in and around it. [The old one] looks like a park with some
> parking on it ... why is it so much of a loop, how do the cars even get in there, what are those weird green rectangles,
> why is there a path going nowhere? ... I wouldn't even mind if you deleted all of them and we just started over with a
> better strip mall. Let's just perfect one thing and then make permutations of it.

Later in the same session: "maybe 50 % shops, 50 % parking lot", "try to only use painting" (no reshaping but the parking
lot itself), "a pond could be cool".

## Why the old one read as a park

The rf-shops3 centre (`StripMallCentre.cs`, still in the code, no longer called) laid out a ring road round the site, a
12 m planted frontage, a walkway spine, a plaza, islands, pads and corner courts first, and planned the lot last on what
was left. The lot planner failed on the fragments: on the 150 x 140 m block 13 000 m² of 16 000 m² was planted and 64
stalls were built (game run `shops4-base`). The leftover pieces were drawn as lawn surfaces and groves.

## The new layout (`Core/Layers/Commercial/LotCentre.cs`)

- **The lot first, one piece**, entered straight from the player's entrances (the parking planner's own connectors, as the
  parking type). The lot's own drive runs round it (no aisle ends in a turning circle).
- **One shop lane at the back** (the side away from the car entrances), the shops behind it facing the lot. The lot's
  aisles join the lane at both ends: no loop, no dead end.
- **Where the row goes** is scored: shop ground and lot in balance, less the land left behind the row (a round site's long
  chord, not its short rim). Every car entrance's own side is tried as the front, plus the outline's long edges; the
  layout that connects every entrance wins.
- **Big sites**: double rows (shops back to back, ringed by their own lane, three cells deep) between bands of lot one
  aisle pair deep: about half shops on 300 x 280 m. **Mid-size sites (over 2.2 ha)** too shallow for a double row: the U
  (side rows of smaller shops), unless an entrance is on a side.
- **Ground**: nothing is reshaped on land that rises and falls less than 1 m under the lot; on a slope only the lot is
  levelled (parking grading); lanes are never laid steeper than 8 % (else "too steep"). Lawn is terrain paint kept 2 m off
  every road (it was a lawn surface that spilled onto roads).
- **Refusals the player sees**: too small (under 1600 m²), "no room" (too shallow behind the entrances or too narrow: a
  plain sentence with the metres needed), "too steep", "footpath behind the shops". A refused plan shows no ghost (the
  panel said "Updating preview..." for ever before).
- **Extras off by default** (walkways, plaza, pads, water, plot trees, building sites). New extra: **Square** in the middle
  of the back row: plaza / park / garden / pond. Presets `plazaRow`, `parkRow`, `pondRow` (hidden until seen in the game).

## The shelf

Only the strip mall (classic) shows in Shops & squares while it is perfected (`Recipes/shelf/shops.json`). Mall district,
mall park, market square and big box are hidden, code kept: they come back as variations of this layout (mall park = the
square as a park, district = denser rows, market = the square as a market).

## Game runs (`C:\Users\jack\cs2-area-tool\.forensics\suite\runs\`)

`shops4-base` (before), `shops4-it3` .. `shops4-it7`. Scenario generator: `.forensics/shops4/gen.js` (styles x lands x
entrance sets: block, tiny 40x30, small 72x55, thin 170x32, triangle, odd concave, L, circle, big, mid, long, 6 % slope;
`oneway`, `corners`, `back` entrance sets). Offline renders: `.forensics/shops4/gal.sh` (land-tri-*, land-odd-* added to
the gallery).
