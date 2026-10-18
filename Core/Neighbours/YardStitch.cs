// Placemaker neighbourhood polish — yard stitching (pure geometry, engine-free).
//
// The game's houses are fixed models on rectangular lots, each with its own fence/hedge lines and its own lawn. Side by
// side they never join up. Version 2 (after "fences and lawns each doing their own thing look wrong"): ONE plot per house
// first, and everything else follows from it.
//   1. Plot: the house's lot, widened on each side that has a neighbour to the middle line between the two lots (the
//      shared edge on a straight street, the bisector of a curve's wedge), deepened only to the middle line with a house
//      behind it (back to back). Never further back into open land. A plot is a rectangle cut by straight lines: it is
//      convex, never overlaps another plot and never enters another house's lot.
//   2. Fences sit on plot edges: one line per shared side (the nicer of the two houses' side lines, hedge > fence > wall;
//      the other is hidden), the back line on the plot's back edge. Lines ending on the lot edge where the plot reaches
//      further carry on to the plot edge (only as a natural continuation). No line leaves its plot; where two lines lie
//      on one another (back to back) the nicer one stays.
//   3. Lawn fills the plot, around every driveway and paving: lawn and fence share their edges.
//   4. Land a ring of houses leaves in its middle (inside a circle) is one shared green; ground paint blends the rest.
// Frames: a house's local +z points to its street (front), +x to its right; lots are (hx, hz) half sizes in metres.

using System;
using System.Collections.Generic;
using System.Linq;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Neighbours
{
    public static class YardStitch
    {
        /// <summary>The vanilla bushes beds are planted with (StitchPlan.Bushes Kind indexes this).</summary>
        public static readonly string[] BushKinds =
        {
            "GreenBushWild01", "GreenBushWild02", "FlowerBushWild01", "FlowerBushWild02", "GreenBushWildAdult01",   // 0-4 bushes
            "Boulder01", "Boulder02", "Boulder03",                                                                    // 5-7 boulders
            "Rock01", "Rock05", "Rock08", "Rock12", "Rock21",                                                         // 8-12 rocks
            "RubblePileMixed01", "RubblePileMixed02", "RubblePileConcrete01", "RubblePileConcrete02",                // 13-16 rubble
            "Pallet01", "Pallet02", "CratePile01", "CratePile02", "BarrelPile01", "Barrel01",                         // 17-22 yard stock
            "Log01", "Log04", "Log07",                                                                                // 23-25 logs
            "GardenBench01", "GardenBench02", "Flowerbed01",                                                          // 26-28 garden things
        };
        private const int FirstRock = 5, FirstStone = 8, FirstRubble = 13, FirstStock = 17, FirstLog = 23, FirstBench = 26, Flowerbed = 28;

        public static int Score(string name)
        {
            var n = (name ?? "").ToLowerInvariant();
            return n.Contains("hedge") ? 3 : n.Contains("fence") ? 2 : n.Contains("wall") ? 1 : 0;
        }

        /// <summary>A straight line through P along D; a house keeps the side its own lot is on.</summary>
        private sealed class Cut
        {
            public float2 P, D;
            /// <summary>When set, the side kept (else the house's own front middle).</summary>
            public float2? Keep;
        }

        public static StitchPlan Plan(IReadOnlyList<StitchHouse> houses, StitchSettings s = null)
        {
            s = s ?? new StitchSettings();
            var plan = new StitchPlan();
            Feature.Clear();
            int n = houses.Count;

            // ── 1. neighbours: side pairs (i's right side next to j's left side) and back to back ──
            var right = Enumerable.Repeat(-1, n).ToArray();
            var left = Enumerable.Repeat(-1, n).ToArray();
            var rightGap = Enumerable.Repeat(float.MaxValue, n).ToArray();
            for (int i = 0; i < n; i++)
            {
                var a = houses[i];
                string why = null;
                for (int j = 0; j < n; j++)
                {
                    var b = houses[j];
                    if (i == j || math.dot(a.Forward, b.Forward) < 0.5f) continue;
                    if (math.dot(b.Position - a.Position, a.Right) <= 0f) continue;
                    // side by side, not one behind the other: their street edges line up (measured along the two houses'
                    // mean direction: on a tight circle neighbours face up to ~50° apart)
                    var frontA = a.Position + a.Forward * a.HalfLot.y;
                    var frontB = b.Position + b.Forward * b.HalfLot.y;
                    float offset = math.abs(math.dot(frontB - frontA, math.normalizesafe(a.Forward + b.Forward, a.Forward)));
                    float fg = math.distance(a.FrontRight, b.FrontLeft);
                    if (fg > 70f) continue;
                    float deep = Deep(a, b);
                    float bg = math.distance(BackAt(a, +1, deep), BackAt(b, -1, deep));
                    if (offset > s.MaxFrontOffset) { why = why ?? $"{b.Id}: street edges {offset:F1} m apart"; continue; }
                    if (fg > s.MaxFrontGap || bg > s.MaxBackGap) { why = $"{b.Id}: gap {fg:F1} m at the street, {bg:F1} m at the back"; continue; }
                    if (fg >= rightGap[i]) continue;
                    // one left neighbour per house: the nearer claim wins
                    int other = left[j];
                    if (other >= 0 && other != i && rightGap[other] <= fg) continue;
                    if (other >= 0 && other != i) { right[other] = -1; rightGap[other] = float.MaxValue; }
                    if (right[i] >= 0) left[right[i]] = -1;
                    right[i] = j;
                    left[j] = i;
                    rightGap[i] = fg;
                }
                if (right[i] < 0 && why != null) plan.Notes.Add($"{a.Id} has no right neighbour: nearest {why}");
            }
            // a row (houses joined side by side) shares one back line: a shallow lot's garden reaches back to the row's
            // deepest lot (at most maxDeepen further), so back fences line up and a ring's middle is an even shape
            var rowDeepen = new float[n];
            var ringCut = new Cut[n];   // set: the house is on the inside of a closed ring
            var ringOf = Enumerable.Repeat(-1, n).ToArray();
            int rings = 0;
            var seen = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (seen[i] || (left[i] < 0 && right[i] < 0)) continue;
                var row = new List<int>();
                var stack = new Stack<int>();
                stack.Push(i);
                seen[i] = true;
                while (stack.Count > 0)
                {
                    int k = stack.Pop();
                    row.Add(k);
                    foreach (int m in new[] { left[k], right[k] })
                        if (m >= 0 && !seen[m]) { seen[m] = true; stack.Push(m); }
                }
                float depth = row.Max(k => houses[k].HalfLot.y);
                // a closed ring (every house has both neighbours): its gardens all reach one line, however far, so what it
                // encloses is an even shape and not a star
                bool ring = row.Count >= 4 && row.All(k => left[k] >= 0 && right[k] >= 0);
                if (ring)
                {
                    var fronts = row.Select(k => houses[k].Position + houses[k].Forward * houses[k].HalfLot.y).ToList();
                    var centre = fronts.Aggregate(float2.zero, (acc, p) => acc + p) / fronts.Count;
                    bool inside = row.Count(k => math.dot(houses[k].Forward, houses[k].Position - centre) > 0f) >= row.Count * 3 / 4;
                    if (inside)
                    {
                        // the inside of a circle: the slices stop at a circle of half the street's radius (a shared green in
                        // the middle rather than long thin points), nearer only where a house reaches further in
                        float back = s.Knob("ringBack", 0.5f) * fronts.Average(p => math.distance(p, centre));
                        foreach (int k in row)
                            if (houses[k].Footprint != null)
                                foreach (var p in houses[k].Footprint) back = math.min(back, math.dot(p - centre, houses[k].Forward) - s.Knob("ringClear", 3f));
                        back = math.max(back, 4f);
                        foreach (int k in row)
                        {
                            ringOf[k] = rings;
                            ringCut[k] = new Cut { P = centre + houses[k].Forward * back, D = houses[k].Right };
                            rowDeepen[k] = 2f * houses[k].HalfLot.y + 200f;   // as far as it takes: the ring line cuts it
                        }
                        rings++;
                        continue;
                    }
                }
                // (never a shop, office or works: a paved slab behind a short shop reads as an empty car park)
                foreach (int k in row) rowDeepen[k] = !string.IsNullOrEmpty(houses[k].MainSurface) ? 0f : ring ? 2f * (depth - houses[k].HalfLot.y) : math.min(2f * (depth - houses[k].HalfLot.y), s.Knob("maxDeepen", 24f));
            }

            float maxRear = s.Knob("maxRearGap", 24f);
            var rear = Enumerable.Repeat(-1, n).ToArray();
            for (int i = 0; i < n; i++)
            {
                float best = float.MaxValue;
                for (int j = 0; j < n; j++)
                {
                    if (i == j || math.dot(houses[i].Forward, houses[j].Forward) > -0.5f || ringCut[i] != null || ringCut[j] != null) continue;
                    if (!RearGap(houses[i], houses[j], out float gap, out float overlap) || gap > maxRear || overlap < s.Knob("minRearOverlap", 6f)) continue;
                    if (gap < best) { best = gap; rear[i] = j; }
                }
            }
            var rearPairs = new HashSet<(int, int)>();
            for (int i = 0; i < n; i++) if (rear[i] >= 0) rearPairs.Add((math.min(i, rear[i]), math.max(i, rear[i])));
            var touched = new bool[n];
            var paired = new HashSet<(int, int)>();
            for (int i = 0; i < n; i++)
                if (right[i] >= 0) { touched[i] = touched[right[i]] = true; paired.Add((math.min(i, right[i]), math.max(i, right[i]))); }
            foreach (var (i, j) in rearPairs) { touched[i] = touched[j] = true; paired.Add((i, j)); }

            // a side or back still free that faces another house's free side or back (the corners of a block, where houses
            // on two streets meet): both reach the middle line between them
            var free = new bool[n, 3];   // edges: 0 left, 1 right, 2 back
            for (int i = 0; i < n; i++) { free[i, 0] = left[i] < 0; free[i, 1] = right[i] < 0; free[i, 2] = ringCut[i] == null; }
            foreach (var (i, j) in rearPairs) free[i, 2] = free[j, 2] = false;
            var candidates = new List<(int I, int EI, int J, int EJ, float Gap, Cut Cut)>();
            float maxFacing = s.Knob("maxFacingGap", 20f);
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    if (paired.Contains((i, j)) || math.distance(houses[i].Position, houses[j].Position) > math.length(houses[i].HalfLot) + math.length(houses[j].HalfLot) + maxFacing) continue;
                    for (int ei = 0; ei < 3; ei++)
                        for (int ej = 0; ej < 3; ej++)
                        {
                            if (!free[i, ei] || !free[j, ej] || !Faces(houses[i], ei, houses[j], ej, maxFacing, s.Knob("minFacingOverlap", 4f), out float g, out var cut)) continue;
                            candidates.Add((i, ei, j, ej, g, cut));
                        }
                }
            var facing = new List<(int I, int EI, int J, int EJ, float Gap, Cut Cut)>();
            foreach (var c in candidates.OrderBy(c => c.Gap))
            {
                // an edge may face several houses along its length (a long side behind two corner lots): one pair each
                if (paired.Contains((c.I, c.J))) continue;
                paired.Add((c.I, c.J));
                touched[c.I] = touched[c.J] = true;
                facing.Add(c);
            }

            // a shallow lot between two much deeper ones (on a straight bit of street): it keeps its own depth, and the land
            // behind it goes to the deep neighbours, split down its middle (they meet behind it, around it)
            var wraps = new List<(int L, int M, int R, float Ext)>();
            if (s.Knob("wrap", 1f) > 0.5f)
                for (int m = 0; m < n; m++)
                {
                    int l = left[m], r = right[m];
                    if (l < 0 || r < 0 || ringCut[m] != null || !string.IsNullOrEmpty(houses[m].MainSurface)) continue;
                    float dm = 2f * houses[m].HalfLot.y, deep = 2f * math.min(houses[l].HalfLot.y, houses[r].HalfLot.y);
                    if (dm > deep * s.Knob("wrapRatio", 0.7f) || deep - dm < 8f) continue;
                    if (math.dot(houses[l].Forward, houses[m].Forward) < 0.94f || math.dot(houses[r].Forward, houses[m].Forward) < 0.94f) continue;
                    wraps.Add((l, m, r, deep - dm));
                    rowDeepen[m] = 0f;
                }

            var clock = System.Diagnostics.Stopwatch.StartNew();
            var stages = new List<string>();
            void Stage(string name) { stages.Add($"{name} {clock.Elapsed.TotalMilliseconds:F0}"); clock.Restart(); }
            Stage("pairs");
            // ── 2. plots ──
            var cuts = new List<Cut>[n];
            for (int i = 0; i < n; i++) cuts[i] = new List<Cut>();
            var borders = new Dictionary<(int, int), Cut>();
            var steps = new List<(int I, int J, bool AFront, float Deep, uint Hash)>();
            var alleys = new List<(int I, int J)>();
            for (int i = 0; i < n; i++)
            {
                if (right[i] < 0) continue;
                var a = houses[i];
                var b = houses[right[i]];
                float deep = Deep(a, b);
                var f = (a.FrontRight + b.FrontLeft) * 0.5f;
                var k = (BackAt(a, +1, deep) + BackAt(b, -1, deep)) * 0.5f;
                // how the land between them is shared, as people do: a wedge opening to the back (the outside of a curve) is
                // often split into a front part for one and a back part for the other (the fences stay on the houses' own
                // lines, a row of bushes makes the step); a narrow even gap is often left as a walkway between the houses;
                // otherwise the middle line
                {
                    float fg = math.distance(a.FrontRight, b.FrontLeft), bg = math.distance(BackAt(a, +1, deep), BackAt(b, -1, deep));
                    uint ph = Hash(((ulong)a.Id * 0x9E3779B97F4A7C15UL) ^ (ulong)b.Id);
                    bool step = s.Knob("steps", 1f) > 0.5f && ringCut[i] == null && ringCut[right[i]] == null && bg > fg + 6f && ph % 100 < (uint)s.Knob("stepChance", 0f)
                        && SideLines(a, +1, s).Any() && SideLines(b, -1, s).Any();
                    bool alley = !step && s.Knob("alleys", 1f) > 0.5f && fg >= 3f && fg <= 9f && math.abs(bg - fg) < 2f && (ph >> 8) % 100 < (uint)s.Knob("alleyChance", 45f);
                    if (step || alley)
                    {
                        // each keeps to the other's lot line (a step: both reach it, the parts are taken away below), or to
                        // its own (an alley)
                        var ca = step ? new Cut { P = b.FrontLeft, D = b.Forward, Keep = a.Position } : new Cut { P = a.FrontRight, D = a.Forward, Keep = a.Position };
                        var cb = step ? new Cut { P = a.FrontRight, D = a.Forward, Keep = b.Position } : new Cut { P = b.FrontLeft, D = b.Forward, Keep = b.Position };
                        cuts[i].Add(ca);
                        cuts[right[i]].Add(cb);
                        if (step) steps.Add((i, right[i], (ph >> 4 & 1) == 0, deep, ph));
                        else alleys.Add((i, right[i]));
                        continue;
                    }
                }
                var cut = new Cut { P = f, D = math.normalizesafe(k - f, -a.Forward) };
                borders[(i, right[i])] = cut;
                cuts[i].Add(cut);
                cuts[right[i]].Add(cut);
                float frontGap = math.distance(a.FrontRight, b.FrontLeft), backGap = math.distance(BackAt(a, +1, deep), BackAt(b, -1, deep));
                if (frontGap > 1f && frontGap > backGap + 0.5f)
                {
                    var along = math.normalize(b.FrontLeft - a.FrontRight);
                    var inward = math.normalizesafe(-(a.Forward + b.Forward), -a.Forward);
                    var street = new Cut { P = a.FrontRight + inward * s.Knob("streetSetback", 0.6f), D = along, Keep = k };
                    cuts[i].Add(street);
                    cuts[right[i]].Add(street);
                }
            }
            var deepen = new bool[n];
            var extra = new float[n, 3];
            foreach (var c in facing)
            {
                cuts[c.I].Add(c.Cut);
                cuts[c.J].Add(c.Cut);
                extra[c.I, c.EI] = math.max(extra[c.I, c.EI], c.Gap + 2f);
                extra[c.J, c.EJ] = math.max(extra[c.J, c.EJ], c.Gap + 2f);
                plan.Pairs.Add(new StitchPair { Left = houses[c.I].Id, Right = houses[c.J].Id, Rear = true, FrontGap = c.Gap, BackGap = c.Gap });
            }
            foreach (var (i, j) in rearPairs)
            {
                var a = houses[i];
                var b = houses[j];
                var backA = a.Position - a.Forward * a.HalfLot.y;
                var backB = b.Position - b.Forward * b.HalfLot.y;
                var cut = new Cut { P = (backA + backB) * 0.5f, D = math.normalizesafe(a.Right - b.Right, a.Right) };
                cuts[i].Add(cut);
                cuts[j].Add(cut);
                deepen[i] = deepen[j] = true;
                RearGap(a, b, out float gap, out _);
                plan.Pairs.Add(new StitchPair { Left = a.Id, Right = b.Id, Rear = true, FrontGap = gap, BackGap = gap });
            }
            for (int i = 0; i < n; i++) if (ringCut[i] != null) cuts[i].Add(ringCut[i]);
            float2 FrontMid(StitchHouse h) => h.Position + h.Forward * (h.HalfLot.y - 0.5f);
            var kept = new float2[n][];
            for (int i = 0; i < n; i++)
            {
                var poly = Lot(houses[i]).ToList();
                foreach (var c in cuts[i]) poly = ClipHalfPlane(poly, c.P, c.D, c.Keep ?? FrontMid(houses[i]));
                kept[i] = poly.Count >= 3 && Poly.Area(poly) > 1f ? poly.ToArray() : Lot(houses[i]);
            }
            float sideReach = s.Knob("sideReach", 40f);
            var plots = new float2[n][];
            for (int i = 0; i < n; i++)
            {
                var h = houses[i];
                var lot = Lot(h);
                if (!touched[i]) { plots[i] = lot; continue; }
                float xl = -h.HalfLot.x - (left[i] >= 0 ? sideReach : extra[i, 0]);
                float xr = h.HalfLot.x + (right[i] >= 0 ? sideReach : extra[i, 1]);
                float zb = -h.HalfLot.y - math.max(math.max(deepen[i] ? maxRear : 0f, extra[i, 2]), rowDeepen[i]);
                var poly = new List<float2> { h.ToWorld(new float2(xl, zb)), h.ToWorld(new float2(xr, zb)), h.ToWorld(new float2(xr, h.HalfLot.y)), h.ToWorld(new float2(xl, h.HalfLot.y)) };
                foreach (var c in cuts[i]) poly = ClipHalfPlane(poly, c.P, c.D, c.Keep ?? FrontMid(h));
                // never into the part of a lot its own house keeps, when not paired with it
                for (int j = 0; j < n; j++)
                {
                    if (j == i || paired.Contains((math.min(i, j), math.max(i, j)))) continue;
                    var o = houses[j];
                    if (math.distance(o.Position, h.Position) > math.length(o.HalfLot) + math.length(h.HalfLot) + sideReach + maxRear) continue;
                    var lotO = kept[j];
                    if (!Overlaps(poly, lotO)) continue;
                    var (pa, pb) = Closest(kept[i], lotO);
                    var mid = (pa + pb) * 0.5f;
                    var nrm = math.distance(pa, pb) > 0.05f ? math.normalize(pb - pa) : math.normalizesafe(o.Position - h.Position);
                    if (math.distance(pa, pb) <= 0.05f) mid = (h.Position + o.Position) * 0.5f;
                    poly = ClipHalfPlane(poly, mid, new float2(-nrm.y, nrm.x), h.Position);
                }
                plots[i] = poly.Count >= 3 && Poly.Area(poly) > 1f ? poly.ToArray() : lot;
            }
            var stepSplits = new List<(int I, int J, bool AFront, float2 Pa, float2 Pb, float2 S, float2 Inward, uint Hash)>();
            foreach (var (si, sj, aFront, deep, ph) in steps)
            {
                var a = houses[si];
                var b = houses[sj];
                var wedge = new List<float2> { a.FrontRight, b.FrontLeft, BackAt(b, -1, deep), BackAt(a, +1, deep) };
                var m = math.normalizesafe(a.Forward + b.Forward, a.Forward);
                var front = (a.FrontRight + b.FrontLeft) * 0.5f;
                float frac = 0.42f + (ph >> 12 & 15) / 15f * 0.2f;
                var split = front - m * (2f * deep * frac);
                var dir = new float2(-m.y, m.x);
                var frontPart = ClipHalfPlane(wedge, split, dir, front);
                var backPart = ClipHalfPlane(wedge, split, dir, front - m * (4f * deep));
                int ft = aFront ? si : sj, bt = aFront ? sj : si;
                plots[ft] = Subtract(plots[ft], backPart);
                plots[bt] = Subtract(plots[bt], frontPart);
                // where the step meets each house's lot line
                var pa = LineHit(split, dir, a.FrontRight, a.Forward);
                var pb = LineHit(split, dir, b.FrontLeft, b.Forward);
                stepSplits.Add((si, sj, aFront, pa, pb, split, -m, ph));
            }

            Stage("plots");
            foreach (var (l, m, r, ext) in wraps)
            {
                var h = houses[m];
                float hx = h.HalfLot.x, hz = h.HalfLot.y;
                // each half reaches a little past the lot's side, so no sliver stays between the halves and the neighbours
                var leftHalf = new[] { h.ToWorld(new float2(-hx - 6f, -hz)), h.ToWorld(new float2(0f, -hz)), h.ToWorld(new float2(0f, -hz - ext)), h.ToWorld(new float2(-hx - 6f, -hz - ext)) };
                var rightHalf = new[] { h.ToWorld(new float2(0f, -hz)), h.ToWorld(new float2(hx + 6f, -hz)), h.ToWorld(new float2(hx + 6f, -hz - ext)), h.ToWorld(new float2(0f, -hz - ext)) };
                plots[l] = Grow(plots, l, leftHalf);
                plots[r] = Grow(plots, r, rightHalf);
            }

            // ── 3. fences on plot edges ──
            var moved = new Dictionary<long, (float2 A, float2 B)>();
            var placed = new HashSet<long>();   // lines laid on a shared edge: they belong to two plots
            var stepBeds = new List<(int House, float2 A, float2 B, float2 Inward)>();
            foreach (var (si, sj, aFront, pa, pb, split, inward, ph) in stepSplits)
            {
                var a = houses[si];
                var b = houses[sj];
                var ar = SideLines(a, +1, s).OrderByDescending(l => Score(l.Name)).ThenByDescending(l => math.distance(l.A, l.B)).First();
                var bl = SideLines(b, -1, s).OrderByDescending(l => Score(l.Name)).ThenByDescending(l => math.distance(l.A, l.B)).First();
                foreach (var l in SideLines(a, +1, s).Concat(SideLines(b, -1, s))) if (l.Id != ar.Id && l.Id != bl.Id) plan.HideLines.Add(l.Id);
                // the front taker's neighbour keeps its line on its own lot from the street to the step; the front taker's
                // line runs on its own lot from the step to the back
                var (aSeg, bSeg) = aFront ? ((pa, a.BackRight), (b.FrontLeft, pb)) : ((a.FrontRight, pa), (pb, b.BackLeft));
                moved[ar.Id] = Along(ar, aSeg.Item1, aSeg.Item2);
                moved[bl.Id] = Along(bl, bSeg.Item1, bSeg.Item2);
                placed.Add(ar.Id);
                placed.Add(bl.Id);
                // the step itself: a row of bushes in the back taker's garden
                stepBeds.Add((aFront ? sj : si, pa, pb, inward));
                plan.Pairs.Add(new StitchPair { Left = a.Id, Right = b.Id, Front = (a.FrontRight + b.FrontLeft) * 0.5f, Back = split, FrontGap = math.distance(a.FrontRight, b.FrontLeft), BackGap = math.distance(a.BackRight, b.BackLeft) });
                plan.Notes.Add($"{a.Id}/{b.Id}: the gap split, {(aFront ? a.Id : b.Id)} takes the front");
            }
            foreach (var (ai, bi) in alleys)
            {
                var a = houses[ai];
                var b = houses[bi];
                // a walkway between the two, from the street to the shallower house's back; their own fences stay
                float hz = math.min(a.HalfLot.y, b.HalfLot.y);
                var ring = Ccw(new[] { a.FrontRight, b.FrontLeft, BackAt(b, -1, hz), BackAt(a, +1, hz) });
                var inset = Poly.Offset(ring, 0.35f).OrderByDescending(r => Poly.Area(r)).FirstOrDefault();
                var path = inset == null ? null : Prepared(inset);
                if (path != null && path.Length >= 3) plan.Paths.Add((a.Id, path, s.Knob("alleyTiles", 0f) > 0.5f ? "Tiles Surface 01" : "Pavement Surface 01"));
                plan.Pairs.Add(new StitchPair { Left = a.Id, Right = b.Id, Rear = true, FrontGap = math.distance(a.FrontRight, b.FrontLeft), BackGap = math.distance(a.BackRight, b.BackLeft) });
                plan.Notes.Add($"{a.Id}/{b.Id}: a walkway between them");
            }
            foreach (var kv in borders)
            {
                var (i, j) = kv.Key;
                var a = houses[i];
                var b = houses[j];
                float deep = Deep(a, b);
                var pair = new StitchPair
                {
                    Left = a.Id, Right = b.Id,
                    FrontGap = math.distance(a.FrontRight, b.FrontLeft), BackGap = math.distance(BackAt(a, +1, deep), BackAt(b, -1, deep)),
                    Gap = new[] { a.FrontRight, b.FrontLeft, BackAt(b, -1, deep), BackAt(a, +1, deep) },
                };
                plan.Pairs.Add(pair);
                bool ea = EdgeOn(plots[i], kv.Value, out float ta0, out float ta1);
                bool eb = EdgeOn(plots[j], kv.Value, out float tb0, out float tb1);
                if (!ea && !eb) continue;
                float t0 = math.min(ea ? ta0 : tb0, eb ? tb0 : ta0), t1 = math.max(ea ? ta1 : tb1, eb ? tb1 : ta1);
                pair.Front = kv.Value.P + kv.Value.D * t0;
                pair.Back = kv.Value.P + kv.Value.D * t1;
                var all = SideLines(a, +1, s).Select(l => (a, l)).Concat(SideLines(b, -1, s).Select(l => (b, l))).ToList();
                if (all.Count == 0) continue;
                var keep = all.OrderByDescending(x => Score(x.l.Name)).ThenByDescending(x => math.distance(x.l.A, x.l.B)).First();
                pair.KeptLine = keep.l.Id;
                foreach (var x in all) if (x.l.Id != keep.l.Id) plan.HideLines.Add(x.l.Id);
                if (pair.FrontGap < s.TouchingGap && pair.BackGap < s.TouchingGap) continue;   // lots touch: only the duplicate goes
                // keep the line's direction: its end nearer the street goes to the street end
                float da = math.distance(keep.l.A, pair.Front) + math.distance(keep.l.B, pair.Back);
                float db = math.distance(keep.l.B, pair.Front) + math.distance(keep.l.A, pair.Back);
                moved[keep.l.Id] = da <= db ? (pair.Front, pair.Back) : (pair.Back, pair.Front);
                placed.Add(keep.l.Id);
            }
            // around a shallow lot: the lines shared with it stop at its back corners, and a line (one the stitching freed) runs
            // down the middle behind it, between the two deep neighbours
            foreach (var (l, m, r, ext) in wraps)
            {
                var h = houses[m];
                foreach (var (a, b, corner) in new[] { (l, m, h.BackLeft), (m, r, h.BackRight) })
                {
                    var pair = plan.Pairs.FirstOrDefault(p => !p.Rear && p.Left == houses[a].Id && p.Right == houses[b].Id);
                    if (pair == null || pair.KeptLine < 0 || plan.HideLines.Contains(pair.KeptLine)) continue;
                    var line = houses[a].Lines.Concat(houses[b].Lines).FirstOrDefault(x => x.Id == pair.KeptLine);
                    if (line == null) continue;
                    var cur = moved.TryGetValue(line.Id, out var mv) ? mv : (line.A, line.B);
                    var dl = cur.B - cur.A;
                    float t = math.saturate(math.dot(corner - cur.A, dl) / math.max(1e-6f, math.dot(dl, dl)));
                    var at = cur.A + dl * t;
                    bool aIsStreet = math.dot(cur.A - h.Position, h.Forward) > math.dot(cur.B - h.Position, h.Forward);
                    moved[line.Id] = aIsStreet ? (cur.A, at) : (at, cur.B);
                    placed.Add(line.Id);
                    pair.Back = at;
                }
                var from = h.ToWorld(new float2(0f, -h.HalfLot.y));
                var to = h.ToWorld(new float2(0f, -h.HalfLot.y - ext));
                var spare = new[] { m, l, r }.SelectMany(k => houses[k].Lines.Where(x => plan.HideLines.Contains(x.Id))).OrderByDescending(x => Score(x.Name)).FirstOrDefault();
                if (spare != null)
                {
                    plan.HideLines.Remove(spare.Id);
                    moved[spare.Id] = (from, to);
                    placed.Add(spare.Id);
                }
                plan.Notes.Add($"{houses[l].Id} and {houses[r].Id} meet behind {h.Id}");
            }
            // a side facing another house's side or back: its line onto the plot's edge on the middle line (overlaps go below)
            foreach (var c in facing)
                foreach (var (k, e) in new[] { (c.I, c.EI), (c.J, c.EJ) })
                {
                    if (e == 2) continue;
                    var h = houses[k];
                    var lines = SideLines(h, e == 0 ? -1 : +1, s).Where(l => !plan.HideLines.Contains(l.Id)).ToList();
                    if (lines.Count == 0 || !EdgeOn(plots[k], c.Cut, out float t0, out float t1)) continue;
                    var line = lines.OrderByDescending(l => Score(l.Name)).ThenByDescending(l => math.distance(l.A, l.B)).First();
                    var p0 = c.Cut.P + c.Cut.D * t0;
                    var p1 = c.Cut.P + c.Cut.D * t1;
                    moved[line.Id] = math.distance(line.A, p0) + math.distance(line.B, p1) <= math.distance(line.A, p1) + math.distance(line.B, p0) ? (p0, p1) : (p1, p0);
                    foreach (var l in lines) if (l.Id != line.Id) plan.HideLines.Add(l.Id);
                }
            // each house's back line on its plot's back edge, when that edge is not the lot's own
            for (int i = 0; i < n; i++)
            {
                if (!touched[i]) continue;
                var h = houses[i];
                var backs = EdgeLines(h, back: true, s).Where(l => !plan.HideLines.Contains(l.Id)).ToList();
                if (backs.Count == 0) continue;
                if (!BackEdge(h, plots[i], out var e0, out var e1)) { foreach (var l in backs) plan.HideLines.Add(l.Id); continue; }
                bool e0Left = h.ToLocal(e0).x < h.ToLocal(e1).x;
                var el = e0Left ? e0 : e1;
                var er = e0Left ? e1 : e0;
                if (math.distance(el, h.BackLeft) < 0.3f && math.distance(er, h.BackRight) < 0.3f) continue;
                var line = backs.OrderByDescending(l => math.distance(l.A, l.B)).First();
                moved[line.Id] = h.ToLocal(line.A).x < h.ToLocal(line.B).x ? (el, er) : (er, el);
                foreach (var l in backs) if (l.Id != line.Id) plan.HideLines.Add(l.Id);
            }
            // a line ending on the lot's edge where the plot reaches further carries on to the plot's edge
            for (int i = 0; i < n; i++)
            {
                if (!touched[i]) continue;
                var h = houses[i];
                foreach (var l in h.Lines)
                {
                    if (moved.ContainsKey(l.Id) || plan.HideLines.Contains(l.Id)) continue;
                    var next = (l.A, l.B);
                    foreach (bool atA in new[] { true, false })
                    {
                        var end = atA ? next.Item1 : next.Item2;
                        var from = atA ? next.Item2 : next.Item1;
                        // the line reaches the lot's edge here, and carried on it would run further inside the plot
                        float back = s.EdgeTolerance / math.max(0.5f, math.distance(from, end));
                        if (!OnLotEdge(h, end, s.EdgeTolerance) || !RayToPlotEdge(Lot(h), from, end, out var lotHit, 1f - back) || math.distance(lotHit, end) > s.EdgeTolerance) continue;
                        if (!RayToPlotEdge(plots[i], from, end, out var hit, 1f - back) || math.dot(hit - lotHit, end - from) < 0.5f * math.distance(from, end)) continue;
                        if (atA) next.Item1 = hit; else next.Item2 = hit;
                    }
                    if ((next.Item1.Equals(l.A) && next.Item2.Equals(l.B)) || !NaturalStretch((l.A, l.B), next, s)) continue;
                    moved[l.Id] = next;
                }
            }
            // no line leaves its own plot (a shared edge's line belongs to both)
            for (int i = 0; i < n; i++)
            {
                if (!touched[i]) continue;
                var grown = Poly.Offset(Ccw(plots[i]), -0.3f);
                if (grown.Count == 0) continue;
                var ring = grown.OrderByDescending(r => Poly.Area(r)).First();
                foreach (var l in houses[i].Lines)
                {
                    if (placed.Contains(l.Id) || plan.HideLines.Contains(l.Id)) continue;
                    var (a, b) = moved.TryGetValue(l.Id, out var m) ? m : (l.A, l.B);
                    float len = math.distance(a, b);
                    if (len < 0.5f) continue;
                    var inside = Poly.ClipSegmentIntervals(ring, a, b);
                    float2 best = default;
                    foreach (var iv in inside) if (iv.y - iv.x > best.y - best.x) best = iv;
                    if (best.x <= 0.001f && best.y >= 0.999f) continue;
                    if ((best.y - best.x) * len < s.Knob("minLine", 2f)) { plan.HideLines.Add(l.Id); continue; }
                    moved[l.Id] = (math.lerp(a, b, best.x), math.lerp(a, b, best.y));
                }
            }
            Dedupe(houses, touched, plan, moved, s);

            // a garden open at the back (no line of anyone's along its back edge) closes with a line of its own that the
            // stitching freed (a duplicate side line it hid, its own or a side neighbour's): every garden ends in a fence or hedge
            if (s.Knob("closeBacks", 1f) > 0.5f)
                for (int i = 0; i < n; i++)
                {
                    if (!touched[i] || !BackEdge(houses[i], plots[i], out var e0, out var e1) || math.distance(e0, e1) < 3f) continue;
                    var h = houses[i];
                    var drawn = houses.SelectMany(o => o.Lines).Where(l => !plan.HideLines.Contains(l.Id)).Select(l => moved.TryGetValue(l.Id, out var m) ? m : (l.A, l.B));
                    if (Covered(e0, e1, drawn) >= 0.6f * math.distance(e0, e1)) continue;
                    // its own first, else one its side neighbours freed (the duplicate on their shared side)
                    var spare = new[] { i, left[i], right[i] }.Where(k => k >= 0).SelectMany(k => houses[k].Lines.Where(l => plan.HideLines.Contains(l.Id)).OrderByDescending(l => Score(l.Name)).ThenByDescending(l => math.distance(l.A, l.B))).FirstOrDefault();
                    if (spare == null) continue;
                    plan.HideLines.Remove(spare.Id);
                    bool e0Left = h.ToLocal(e0).x < h.ToLocal(e1).x;
                    moved[spare.Id] = e0Left ? (e0, e1) : (e1, e0);
                    plan.Notes.Add($"{h.Id}: its open back closed with a freed {spare.Name}");
                }

            // a moved line never runs through a house, a shed, a pool or a deck, nor across a driveway: it stops there
            Stage("fences");
            TrimAtObstacles(houses, plan, moved, s);
            Stage("trim");

            // planted beds with bushes along the new fences and hedges of extended gardens
            var beds = new List<Region>[n];
            for (int i = 0; i < n; i++) beds[i] = new List<Region>();
            var reserved = new List<Region>[n];   // features' ground (a sand pit): never under a lawn, kept through bed fitting
            for (int i = 0; i < n; i++) reserved[i] = new List<Region>();
            if (s.Knob("plant", 1f) > 0.5f)
            {
                var built = new Obstacle(houses);
                foreach (var (hi, a, b, inward) in stepBeds) if (houses[hi].Edit) StepRow(houses[hi], plots[hi], a, b, inward, plan, beds[hi], built, s);
                Plant(houses, plots, touched, plan, moved, beds, built, s);
                if (s.Knob("features", 1f) > 0.5f) Features(houses, plots, touched, plan, beds, reserved, built, s);
                Paths(houses, plots, touched, left, right, plan, beds, built, s);
                Rocks(houses, plots, plan, moved, built, s);
            }

            Stage("plant");
            // ── 4. lawns fill their plots, around driveways and paving ──
            // a small pocket no plot takes (where four corners meet) joins the nearest plot's lawn
            var lawnLand = new List<Region>[n];
            for (int i = 0; i < n; i++) lawnLand[i] = Poly.Normalize(Ccw(plots[i]));
            foreach (var region in Poly.UnionAll(Enumerable.Range(0, n).Select(i => (IReadOnlyList<float2>)Ccw(plots[i]))))
                foreach (var hole in region.Holes)
                {
                    if (Poly.Area(hole) > s.Knob("pocketMax", 150f)) continue;
                    var c = Poly.Centroid(hole);
                    int best = -1;
                    float bestD = float.MaxValue;
                    for (int i = 0; i < n; i++)
                    {
                        if (!touched[i]) continue;
                        float d = DistanceToRing(plots[i], c);
                        if (d < bestD) { bestD = d; best = i; }
                    }
                    if (best < 0) continue;
                    lawnLand[best] = Poly.Boolean(lawnLand[best], new[] { new Region(Ccw(hole)) }, BoolOp.Union);
                }
            for (int i = 0; i < n; i++)
            {
                if (!touched[i] || !houses[i].Edit) continue;
                var h = houses[i];
                var plot = Ccw(plots[i]);
                plan.Plots[h.Id] = plot;
                if (s.Knob("lawns", 1f) < 0.5f) continue;   // the player keeps lawns as the houses come
                // once the plot reaches past the lot, the lawn fills all of it but driveways and paving (lawn and fence meet)
                var ext = Poly.Boolean(new[] { (IReadOnlyList<float2>)plot }, new[] { (IReadOnlyList<float2>)Ccw(Lot(h)) }, BoolOp.Difference, FillRule.NonZero);
                if (ext.Sum(r => r.Area) < 1f) continue;
                var hard0 = houses.SelectMany(o => o.Hard).Where(r => r != null && r.Length >= 3 && Overlaps(plot, r)).Select(r => new Region(Ccw(r))).Concat(beds[i]).Concat(reserved[i]).ToList();
                // a shop, office or works: its extra land in the surface most of its ground already is (its car park), not lawn
                if (!string.IsNullOrEmpty(h.MainSurface) && s.Knob("pavedFill", 1f) > 0.5f)
                {
                    var spare = Poly.Boolean(lawnLand[i], new[] { new Region(Ccw(Lot(h))) }, BoolOp.Difference);
                    float spare0 = spare.Sum(r => r.Area);
                    var off = hard0.Concat(h.Lawns.Where(l => l.Ring != null && l.Ring.Length >= 3).Select(l => new Region(Ccw(l.Ring)))).ToList();
                    if (off.Count > 0) spare = Poly.Boolean(spare, off, BoolOp.Difference);
                    int made = 0, failed = 0;
                    foreach (var r in spare.Where(r => r.Area >= 10f))
                    {
                        var ring = Prepared(r.HasHoles ? Poly.CutHoles(r, 0f).First() : r.Outer);
                        if (ring != null) { plan.Paths.Add((h.Id, ring, h.MainSurface)); made++; } else failed++;
                    }
                    plan.Notes.Add($"{h.Id}: its extra land in {h.MainSurface} ({made} pieces{(failed > 0 ? $", {failed} the game would not take" : "")}; land {spare.Sum(r => r.Area):F0} m² in {spare.Count}, {spare0:F0} m² before paving, plot {Poly.Area(plot):F0}, ext {ext.Sum(r => r.Area):F0}, landAll {lawnLand[i].Sum(r => r.Area):F0})");
                    continue;
                }
                // variety, as streets are: some gardens (about a third) are painted, not drawn: the house's own grass surfaces
                // are tucked away under the house and the whole garden (minus the paving) is painted lush with soft edges
                if (Hash((ulong)h.Id ^ 0x9A1D7UL) % 100 < (uint)s.Knob("paintedGardens", 33f))
                {
                    var garden = hard0.Count > 0 ? Poly.Boolean(lawnLand[i], hard0, BoolOp.Difference) : lawnLand[i];
                    foreach (var r in garden.Where(r => r.Area >= 6f))
                        Paint(plan, r.Outer, (int)s.Knob("lushLayer", 2f), s.Knob("gardenPaint", 0.85f), 1.6f, 1.4f, 5f, (uint)(h.Id * 2246822519L) ^ (uint)Poly.Area(r.Outer));
                    var under = UnderHouse(h);
                    if (under != null && s.Knob("hideOwnLawn", 1f) > 0.5f)
                        foreach (var l in h.Lawns.Where(l => l.Ring != null && l.Ring.Length >= 3)) plan.Lawns[l.Id] = under;
                    plan.Notes.Add($"{h.Id}: a painted garden");
                    continue;
                }
                var lawns = h.Lawns.Where(l => l.Ring != null && l.Ring.Length >= 3).OrderByDescending(l => Poly.Area(l.Ring)).ToList();
                var hard = houses.SelectMany(o => o.Hard).Where(r => r != null && r.Length >= 3 && Overlaps(plot, r)).Select(r => new Region(Ccw(r))).Concat(beds[i]).Concat(reserved[i]).ToList();
                var pieces = hard.Count > 0 ? Poly.Boolean(lawnLand[i], hard, BoolOp.Difference) : lawnLand[i];
                pieces = pieces.Where(p => p.Area > 0.5f).ToList();
                if (s.Knob("debugLawns", 0f) > 0.5f) plan.Notes.Add($"{h.Id}: land {lawnLand[i].Sum(r => r.Area):F0} m², {pieces.Count} pieces ({string.Join(", ", pieces.Select(p => p.Area.ToString("F0")))}), {lawns.Count} lawns ({string.Join(", ", lawns.Select(l => Poly.Area(l.Ring).ToString("F0")))})");
                // each lawn (largest first) takes the pieces of the plot it lies in; the house's other lawns stay as they are
                foreach (var lawn in lawns)
                {
                    var mine = pieces.Where(p => Overlaps(p.Outer, lawn.Ring)).ToList();
                    if (mine.Count == 0) { plan.Notes.Add($"{h.Id}: lawn not fitted (no free land touches it)"); continue; }
                    pieces.RemoveAll(p => mine.Contains(p));
                    var others = lawns.Where(o => o != lawn).Select(o => new Region(Ccw(o.Ring))).ToList();
                    var merged = Poly.Boolean(new[] { new Region(Ccw(lawn.Ring)) }, mine, BoolOp.Union);
                    if (others.Count > 0 || beds[i].Count > 0) merged = Poly.Boolean(merged, others.Concat(beds[i]), BoolOp.Difference);
                    merged = Poly.Boolean(merged, lawnLand[i], BoolOp.Intersection);
                    if (merged.Count == 0) { plan.Notes.Add($"{h.Id}: lawn not fitted (nothing left of it in its plot)"); continue; }
                    var keep = Keeping(merged, lawn.Ring);
                    // what the other lawns cut off from it is land no lawn reaches yet
                    pieces.AddRange(merged.Where(r => r != keep));
                    var main = OpenHoles(keep, h);
                    var grownLawn = Prepared(main.Outer);
                    bool overBeds = grownLawn != null && beds[i].Count > 0 && Poly.Boolean(new[] { new Region(Ccw(grownLawn)) }, beds[i], BoolOp.Intersection).Sum(r => r.Area) > 0.5f;
                    if (overBeds)
                    {
                        // the lawn's cleaned outline reaches a little over some beds: the beds give way (trimmed round the
                        // lawn; one that loses most of itself goes, with its bushes)
                        FitBedsAround(h, grownLawn, plan, beds[i]);
                        overBeds = false;
                    }
                    if ((grownLawn == null || grownLawn.Length < 3 || overBeds) && beds[i].Count > 0)
                    {
                        // the beds leave a shape the game will not take: the garden goes without them
                        plan.Beds.RemoveAll(b => b.House == h.Id);
                        plan.Bushes.RemoveAll(b => b.House == h.Id);
                        beds[i].Clear();
                        var plain = Poly.Boolean(new[] { new Region(Ccw(lawn.Ring)) }, mine, BoolOp.Union);
                        if (others.Count > 0) plain = Poly.Boolean(plain, others, BoolOp.Difference);
                        plain = Poly.Boolean(plain, lawnLand[i], BoolOp.Intersection);
                        if (plain.Count > 0)
                        {
                            main = OpenHoles(Keeping(plain, lawn.Ring), h);
                            grownLawn = Prepared(main.Outer);
                        }
                        plan.Notes.Add($"{h.Id}: its beds left out (the lawn would not take them{(overBeds ? ": they would sit under it" : "")})");
                    }
                    if (grownLawn == null || grownLawn.Length < 3) { plan.Notes.Add($"{h.Id}: lawn not fitted (the game would not take its shape: {main.Outer.Length} points, {main.Area:F0} m²)"); continue; }
                    if (math.abs(Poly.Area(grownLawn) - Poly.Area(lawn.Ring)) < 0.5f) { plan.Notes.Add($"{h.Id}: lawn unchanged ({Poly.Area(grownLawn):F0} m²)"); continue; }
                    plan.LawnAddedM2 += Poly.Area(grownLawn) - Poly.Area(lawn.Ring);
                    plan.Lawns[lawn.Id] = grownLawn;
                }
                // land no lawn of the house reaches (a gravel-yard house, the far side of a driveway): a lawn of its own
                foreach (var piece in pieces.OrderByDescending(p => p.Area))
                {
                    if (piece.Area < s.Knob("minNewLawn", 20f)) continue;
                    var ring = Prepared(OpenHoles(piece, h).Outer);
                    if (ring == null || ring.Length < 3) continue;
                    plan.NewLawns.Add((h.Id, ring));
                    plan.LawnAddedM2 += Poly.Area(ring);
                }
            }

            // ground paint round every polished house, joined or not: the gardens of a pass as one shape, grown outward with
            // rounded corners into a near band and a wider, fainter one, minus the gardens themselves (lush, soft, uneven)
            if (s.Knob("paint", 1f) > 0.5f && s.Knob("blendWidth", 12f) > 0.5f)
            {
                var gardens = Enumerable.Range(0, n).Where(i => houses[i].Edit && plots[i] != null).Select(i => (IReadOnlyList<float2>)Ccw(plots[i])).ToList();
                var allPlots = Enumerable.Range(0, n).Where(i => plots[i] != null).Select(i => new Region(Ccw(plots[i]))).ToList();
                var joined = gardens.Count > 0 ? Poly.UnionAll(gardens) : new List<Region>();
                float near = s.Knob("blendWidth", 12f), far = s.Knob("blendFar", 22f);
                foreach (var (width, strength, feather, seed) in new[] { (far, s.Knob("blendFarStrength", 0.32f), far * 0.45f, 0xFA2u), (near, s.Knob("blendStrength", 0.6f), near * 0.55f, 0xB1EDu) })
                    foreach (var g in joined)
                        foreach (var grown in Poly.Offset(g.Outer, -width, OffsetJoin.Round, 4f, 0.6f))
                        {
                            var band = Poly.Boolean(new[] { grown }, allPlots, BoolOp.Difference);
                            foreach (var r in band.Where(r => r.Area >= 12f))
                                foreach (var piece in r.HasHoles ? Poly.CutHoles(r, 0f) : new List<float2[]> { r.Outer })
                                    if (Poly.Area(piece) >= 12f)
                                        Paint(plan, Poly.Simplify(piece, 0.3f), (int)s.Knob("lushLayer", 2f), strength, feather, width * 0.25f, 9f + width * 0.2f, seed ^ (uint)math.round(Poly.Centroid(piece).x * 7f) ^ (uint)math.round(Poly.Centroid(piece).y * 13f));
                        }
            }
            Stage("lawns");
            // ── 5. the middle a ring of houses leaves: one shared green ──
            Commons(houses, plots, ringOf, plan, s);

            // ground paint along the outside of back fences that face open land (a worn edge where the field meets the gardens)
            if (s.Knob("paint", 1f) > 0.5f)
                for (int i = 0; i < n; i++)
                {
                    if (!touched[i] || !BackEdge(houses[i], plots[i], out var e0, out var e1) || math.distance(e0, e1) < 4f) continue;
                    var mid = (e0 + e1) * 0.5f;
                    var outward = math.normalizesafe(mid - houses[i].Position);
                    var d = math.normalize(e1 - e0);
                    var nrm = new float2(-d.y, d.x);
                    if (math.dot(nrm, outward) < 0f) nrm = -nrm;
                    if (Enumerable.Range(0, n).Any(k => k != i && Poly.Contains(plots[k], mid + nrm * 1.5f))) continue;
                    float w = s.Knob("edgeWidth", 2.4f);
                    Paint(plan, new[] { e0, e1, e1 + nrm * w, e0 + nrm * w }, 3, s.Knob("edgeStrength", 0.45f), 1.2f, 0.9f, 2.5f, (uint)(houses[i].Id & 0x7fffffff));
                }

            Dedupe(houses, touched, plan, moved, s);
            foreach (var kv in moved) if (!plan.HideLines.Contains(kv.Key)) plan.MoveLines[kv.Key] = kv.Value;
            plan.Notes.Add($"{touched.Count(t => t)} plots: {borders.Count} shared sides, {rearPairs.Count} back to back, {plan.MoveLines.Count} lines moved, {plan.HideLines.Count} hidden, {plan.Lawns.Count} lawns fitted, {plan.NewLawns.Count} new lawns, {plan.Beds.Count} beds, {plan.Bushes.Count} bushes and rocks, {plan.Paths.Count} paths");
            if (s.Knob("paint", 1f) > 0.5f) PlanPaint(plan, s);
            Stage("rest");
            plan.Notes.Add("time (ms): " + string.Join(", ", stages));
            return plan;
        }

        // ───────────── lines lying on one another ─────────────

        /// <summary>
        /// No two lines on top of or through one another: where a line runs alongside a nicer one (within about a metre, nearly
        /// parallel) or crosses it, that stretch goes from the lesser line (score, then length) and its longest remaining piece
        /// stays (a piece too short goes). A line merely ending on another (a corner, a T) is left as it is.
        /// </summary>
        private static void Dedupe(IReadOnlyList<StitchHouse> houses, bool[] touched, StitchPlan plan, Dictionary<long, (float2 A, float2 B)> moved, StitchSettings s)
        {
            var lines = new List<(int House, StitchLine L)>();
            for (int i = 0; i < houses.Count; i++)
                if (touched[i]) foreach (var l in houses[i].Lines) if (!plan.HideLines.Contains(l.Id)) lines.Add((i, l));
            (float2 A, float2 B) At(StitchLine l) => moved.TryGetValue(l.Id, out var m) ? m : (l.A, l.B);
            var order = lines.OrderByDescending(x => Score(x.L.Name)).ThenByDescending(x => { var p = At(x.L); return math.distance(p.A, p.B); }).ToList();
            float tol = s.Knob("lineClearance", 1.2f), minLine = s.Knob("minLine", 2f);
            for (int k = 0; k < order.Count; k++)
            {
                var keep = order[k];
                if (plan.HideLines.Contains(keep.L.Id)) continue;
                var (ka, kb) = At(keep.L);
                float klen = math.distance(ka, kb);
                if (klen < 1f) continue;
                var kd = (kb - ka) / klen;
                var lo = math.min(ka, kb) - tol - 1f;
                var hi = math.max(ka, kb) + tol + 1f;
                for (int m = k + 1; m < order.Count; m++)
                {
                    var other = order[m];
                    if (other.L.Id == keep.L.Id || plan.HideLines.Contains(other.L.Id)) continue;
                    var (oa, ob) = At(other.L);
                    if (math.max(oa.x, ob.x) < lo.x || math.min(oa.x, ob.x) > hi.x || math.max(oa.y, ob.y) < lo.y || math.min(oa.y, ob.y) > hi.y) continue;
                    float olen = math.distance(oa, ob);
                    if (olen < 0.5f) continue;
                    float sin = math.abs(kd.x * (ob - oa).y / olen - kd.y * (ob - oa).x / olen);
                    bool parallel = sin < math.sin(math.radians(25f));
                    // which stretches of the other line come too close to the kept one
                    int steps = math.clamp((int)(olen / 0.4f), 4, 240);
                    var near = new bool[steps + 1];
                    bool any = false;
                    for (int t = 0; t <= steps; t++)
                    {
                        var p = math.lerp(oa, ob, t / (float)steps);
                        // nearly parallel: only alongside the kept line (two fences carrying on end to end are fine)
                        float along = math.dot(p - ka, kd);
                        near[t] = DistanceToSegment(p, ka, kb) < tol && (!parallel || (along > 0.3f && along < klen - 0.3f));
                        any |= near[t];
                    }
                    if (!any) continue;
                    // a line ending on the kept one at an angle is a corner or a T: fine
                    if (!parallel)
                    {
                        int first = Array.IndexOf(near, true), last = Array.LastIndexOf(near, true);
                        bool onlyAtStart = first == 0 && Enumerable.Range(0, steps + 1).Where(t => near[t]).All(t => t * olen / steps <= tol / math.max(sin, 0.2f) + 0.5f);
                        bool onlyAtEnd = last == steps && Enumerable.Range(0, steps + 1).Where(t => near[t]).All(t => (steps - t) * olen / steps <= tol / math.max(sin, 0.2f) + 0.5f);
                        if (onlyAtStart || onlyAtEnd) continue;
                        // a corner of the kept line resting on the other one (the kept line ends there) is a T too
                        if (DistanceToSegment(ka, oa, ob) < tol || DistanceToSegment(kb, oa, ob) < tol)
                        {
                            float tA = math.dot(ka - oa, ob - oa) / (olen * olen), tB = math.dot(kb - oa, ob - oa) / (olen * olen);
                            bool aOn = DistanceToSegment(ka, oa, ob) < tol, bOn = DistanceToSegment(kb, oa, ob) < tol;
                            if ((aOn && !bOn) || (bOn && !aOn)) continue;
                        }
                    }
                    // the longest run of the other line clear of the kept one
                    int bestStart = -1, bestLen = 0, run = 0;
                    for (int t = 0; t <= steps; t++)
                    {
                        if (!near[t]) { run++; if (run > bestLen) { bestLen = run; bestStart = t - run + 1; } }
                        else run = 0;
                    }
                    float keepLen = bestLen <= 1 ? 0f : (bestLen - 1) * olen / steps;
                    if (keepLen < minLine) { plan.HideLines.Add(other.L.Id); continue; }
                    float u0 = bestStart / (float)steps, u1 = (bestStart + bestLen - 1) / (float)steps;
                    // pull back a little from the kept line where it was cut (not at the line's own ends)
                    float pad = math.min(0.3f / olen, (u1 - u0) * 0.2f);
                    if (u0 > 0f) u0 += pad;
                    if (u1 < 1f) u1 -= pad;
                    moved[other.L.Id] = (math.lerp(oa, ob, u0), math.lerp(oa, ob, u1));
                }
            }
        }

        // ───────────── the middle of a ring ─────────────

        /// <summary>
        /// The land a closed ring of houses encloses (only a ring seen whole: a pass that sees part of it plans no green),
        /// inside the back edges and outside every plot: one shared green, its outline cleaned into a shape the game takes.
        /// </summary>
        private static void Commons(IReadOnlyList<StitchHouse> houses, float2[][] plots, int[] ringOf, StitchPlan plan, StitchSettings s)
        {
            foreach (var members in Enumerable.Range(0, houses.Count).Where(i => ringOf[i] >= 0).GroupBy(i => ringOf[i]).Select(g => g.ToList()))
            {
                if (members.Count < 4) continue;
                var ends = new List<float2>();
                foreach (int i in members) if (BackEdge(houses[i], plots[i], out var e0, out var e1)) { ends.Add(e0); ends.Add(e1); }
                if (ends.Count < 8) continue;
                var c = ends.Aggregate(float2.zero, (acc, p) => acc + p) / ends.Count;
                var ring = Poly.Normalize(ends.OrderBy(p => math.atan2(p.y - c.y, p.x - c.x)).ToArray());
                if (ring.Count == 0) continue;
                var taken = plots.Where(p => p != null).Select(p => new Region(Ccw(p))).ToList();
                var piece = Poly.Boolean(new[] { ring.OrderByDescending(r => r.Area).First() }, taken, BoolOp.Difference).OrderByDescending(r => r.Area).FirstOrDefault();
                if (piece == null || piece.Area < 30f) continue;
                // half a metre in from the fences, cleaned (no slivers, no needle points) so the game draws it
                var inset = Poly.Offset(piece.HasHoles ? Poly.CutHoles(piece, 0f).First() : piece.Outer, 0.5f).OrderByDescending(r => Poly.Area(r)).FirstOrDefault();
                if (inset == null) continue;
                var green = Prepared(inset);
                if (green == null || green.Length < 3) continue;
                plan.Commons.Add(green);
                plan.Notes.Add($"a shared green in the middle ({Poly.Area(green):F0} m²)");
            }
        }

        // ───────────── planting ─────────────

        /// <summary>
        /// A planted bed (forest floor) along some of the fences and hedges round an extended garden, on the garden's side,
        /// clear of the corners, the house, its sheds and paving; bushes along it (two kinds per bed, a garden's look rather
        /// than a park's). Which fences get one is decided by the line and the house, so every pass plants the same.
        /// </summary>
        private static void Plant(IReadOnlyList<StitchHouse> houses, float2[][] plots, bool[] touched, StitchPlan plan,
            Dictionary<long, (float2 A, float2 B)> moved, List<Region>[] beds, Obstacle built, StitchSettings s)
        {
            var drawn = houses.SelectMany(h => h.Lines).Where(l => !plan.HideLines.Contains(l.Id))
                .Select(l => (l.Id, P: moved.TryGetValue(l.Id, out var m) ? m : (l.A, l.B))).OrderBy(x => x.Id).ToList();
            int maxBushes = (int)s.Knob("bushesPerGarden", 14f);
            for (int i = 0; i < houses.Count; i++)
            {
                if (!touched[i] || !houses[i].Edit || !string.IsNullOrEmpty(houses[i].MainSurface)) continue;
                var h = houses[i];
                var plot = Ccw(plots[i]);
                // only gardens the stitching made bigger: a lot left as it was keeps the game's own planting
                var ext = Poly.Boolean(new[] { (IReadOnlyList<float2>)plot }, new[] { (IReadOnlyList<float2>)Ccw(Lot(h)) }, BoolOp.Difference, FillRule.NonZero);
                if (ext.Sum(r => r.Area) < s.Knob("plantMinExtra", 40f)) continue;
                var centre = Poly.Centroid(plot);
                int bushes = 0;
                foreach (var (id, (a, b)) in drawn)
                {
                    float len = math.distance(a, b);
                    if (len < 6f || DistanceToRing(plot, a) > 0.8f || DistanceToRing(plot, b) > 0.8f || DistanceToRing(plot, (a + b) * 0.5f) > 0.8f) continue;
                    uint hash = Hash((ulong)id ^ ((ulong)h.Id * 0x9E3779B97F4A7C15UL));
                    if (hash % 100 >= (uint)s.Knob("bedChance", 65f)) continue;
                    var d = (b - a) / len;
                    var nrm = new float2(-d.y, d.x);
                    if (math.dot(centre - (a + b) * 0.5f, nrm) < 0f) nrm = -nrm;
                    float width = 1.2f + (hash >> 8 & 7) * 0.08f;
                    var e0 = a + d * 1.4f;
                    var e1 = b - d * 1.4f;
                    // an organic edge: the bed swells and narrows along the fence and tapers into the lawn at its ends
                    float blen = math.distance(e0, e1), phase = (hash >> 24 & 255) / 255f * 6.28f;
                    float Width(float t) => width * (0.35f + 0.65f * math.sqrt(math.max(0f, math.sin(math.PI * t)))) * (1f + 0.22f * math.sin(t * blen / 2.6f + phase));
                    var edge = new List<float2> { e0 - nrm * 1f };
                    int segs = math.max(4, (int)(blen / 0.9f));
                    for (int k = 0; k <= segs; k++) { float t = k / (float)segs; edge.Add(math.lerp(e0, e1, t) + nrm * Width(t)); }
                    edge.Add(e1 - nrm * 1f);
                    var rect = Ccw(edge.ToArray());
                    var region = Poly.Boolean(new[] { new Region(rect) }, new[] { new Region(plot) }, BoolOp.Intersection);
                    // only on bare land: never over a lawn the house keeps as it is
                    var near = built.Near(rect).Concat(beds[i]).Concat(h.Lawns.Where(l => l.Ring != null && l.Ring.Length >= 3).Select(l => new Region(Ccw(l.Ring)))).ToList();
                    if (near.Count > 0) region = Poly.Boolean(region, near, BoolOp.Difference);
                    var piece = region.OrderByDescending(r => r.Area).FirstOrDefault();
                    if (piece == null || piece.Area < 4f) continue;
                    // a bed reaches the garden's edge (one cut off from it by a driveway would be an island the lawn cannot go round)
                    if (!piece.Outer.Any(q => DistanceToRing(plot, q) < 0.15f)) continue;
                    var ring = Prepared(piece.HasHoles ? Poly.CutHoles(piece, 0f).First() : piece.Outer);
                    if (ring == null || ring.Length < 3) continue;
                    plan.Beds.Add((h.Id, ring));
                    beds[i].Add(new Region(Ccw(ring)));
                    // bushes along the bed's middle, two kinds alternating
                    int k0 = (int)(hash >> 12) % FirstRock, k1 = (k0 + 1 + (int)(hash >> 16) % (FirstRock - 1)) % FirstRock;   // plants only (rocks come after them)
                    float spacing = 2.3f + (hash >> 20 & 7) * 0.12f;
                    int count = 0;
                    for (float t = spacing * 0.5f; t < blen && bushes < maxBushes; t += spacing)
                    {
                        uint hb = Hash(hash + (uint)count * 7919u);
                        float tt = math.saturate((t + ((hb & 255) / 255f - 0.5f) * 0.5f) / blen);
                        var p = math.lerp(e0, e1, tt) + nrm * (Width(tt) * (0.5f + ((hb >> 8 & 255) / 255f - 0.5f) * 0.3f));
                        if (!Poly.Contains(ring, p)) continue;
                        plan.Bushes.Add((h.Id, p, (hb >> 16 & 1023) / 1023f * 2f * math.PI, count % 2 == 0 ? k0 : k1));
                        bushes++;
                        count++;
                    }
                }
            }
        }

        /// <summary>Everything built on the houses' lots (houses, sheds, pools, decks, driveways, paving) with their bounds.</summary>
        private sealed class Obstacle
        {
            private readonly List<(float2 Min, float2 Max, Region R)> _all = new List<(float2, float2, Region)>();

            public Obstacle(IReadOnlyList<StitchHouse> houses)
            {
                foreach (var h in houses)
                    foreach (var r in (h.Footprint != null ? new[] { h.Footprint } : new float2[0][]).Concat(h.Props).Concat(h.Hard))
                    {
                        if (r == null || r.Length < 3) continue;
                        var ring = Ccw(r);
                        _all.Add((ring.Aggregate(new float2(float.MaxValue), math.min), ring.Aggregate(new float2(float.MinValue), math.max), new Region(ring)));
                    }
            }

            /// <summary>The obstacles whose bounds meet the bounds of these points.</summary>
            public List<Region> Near(IReadOnlyList<float2> pts)
            {
                var lo = pts.Aggregate(new float2(float.MaxValue), math.min);
                var hi = pts.Aggregate(new float2(float.MinValue), math.max);
                var list = new List<Region>();
                foreach (var (min, max, r) in _all)
                    if (min.x <= hi.x && max.x >= lo.x && min.y <= hi.y && max.y >= lo.y) list.Add(r);
                return list;
            }
        }

        /// <summary>
        /// Big new land gets something to look at, by what the building is. A home's garden: a rock garden, a sand pit, a wild
        /// corner (logs, big bushes) or a bench nook (a bench facing the house, a flower bed). Industry: a storage yard (rubble,
        /// pallets, crates, barrels) on worn ground. Placed at the roomiest spot of the new land, clear of everything built.
        /// </summary>
        /// <summary>Objects a feature placed (not a bed's planting): kept when beds are fitted round a lawn. Per plan, on its thread.</summary>
        [ThreadStatic] private static HashSet<(long, float2)> s_feature;
        private static HashSet<(long, float2)> Feature => s_feature ?? (s_feature = new HashSet<(long, float2)>());

        private static void Features(IReadOnlyList<StitchHouse> houses, float2[][] plots, bool[] touched, StitchPlan plan, List<Region>[] beds, List<Region>[] reserved, Obstacle built, StitchSettings s)
        {
            for (int i = 0; i < houses.Count; i++)
            {
                var h = houses[i];
                if (!touched[i] || !h.Edit || plots[i] == null) continue;
                bool industry = h.Zone == "industry";
                if (!industry && !string.IsNullOrEmpty(h.MainSurface)) continue;   // shops and offices: paving only
                uint hash = Hash((ulong)h.Id * 0x7F4A7C15UL ^ 0xFEA7UL);
                if (hash % 100 >= (uint)s.Knob("featureChance", 55f)) continue;
                var plot = Ccw(plots[i]);
                var land = Poly.Boolean(new[] { new Region(plot) }, new[] { new Region(Ccw(Lot(h))) }, BoolOp.Difference);
                if (land.Sum(r => r.Area) < s.Knob("featureMinLand", 140f)) continue;
                var keepOff = built.Near(plot).Concat(beds[i]).Concat(reserved[i]).ToList();
                if (keepOff.Count > 0) land = Poly.Boolean(land, keepOff, BoolOp.Difference);
                // the roomiest spot: the grid point farthest from the land's edge
                float2 best = default;
                float room = 0f;
                foreach (var r in land)
                {
                    var lo = r.Outer.Aggregate(new float2(float.MaxValue), math.min);
                    var hi = r.Outer.Aggregate(new float2(float.MinValue), math.max);
                    for (float x = lo.x + 0.5f; x < hi.x; x += 1f)
                        for (float y = lo.y + 0.5f; y < hi.y; y += 1f)
                        {
                            var p = new float2(x, y);
                            if (!Poly.Contains(r.Outer, p) || r.Holes.Any(hl => Poly.Contains(hl, p))) continue;
                            float c = DistanceToRing(r.Outer, p);
                            foreach (var hl in r.Holes) c = math.min(c, DistanceToRing(hl, p));
                            if (c > room) { room = c; best = p; }
                        }
                }
                if (room < 2.2f) continue;
                float rad = math.min(room - 0.6f, industry ? 6f : 3.2f);
                float Turn(uint k) => (Hash(hash + k) & 1023) / 1023f * 2f * math.PI;
                float2 Around(uint k, float maxR) { float a = Turn(k), d = maxR * math.sqrt(((Hash(hash ^ (k * 2654435761u)) >> 4) & 1023) / 1023f); return best + new float2(math.cos(a), math.sin(a)) * d; }
                void Put(int kind, float2 at, uint k) { plan.Bushes.Add((h.Id, at, Turn(k + 97u), kind)); Feature.Add((h.Id, at)); }
                if (industry)
                {
                    // a storage yard: a few piles and stock, on worn ground
                    int count = 4 + (int)(hash >> 8 & 3);
                    for (int k = 0; k < count; k++)
                    {
                        int kind = (Hash(hash + (uint)k * 31u) % 3) switch { 0 => FirstRubble + (int)(Hash(hash + (uint)k) % 4), 1 => FirstStock + (int)(Hash(hash + (uint)k) % 4), _ => FirstStock + 4 + (int)(Hash(hash + (uint)k) % 2) };
                        Put(kind, Around((uint)k, rad), (uint)k);
                    }
                    Paint(plan, Blob(best, rad + 1.5f, hash, 12), 3, s.Knob("yardDirt", 0.7f), 2f, 1.2f, 3.5f, hash);
                    plan.Notes.Add($"{h.Id}: a storage yard");
                    continue;
                }
                switch (hash >> 12 & 3)
                {
                    case 0:   // a rock garden: a boulder and stones on a dirt patch, a bush or two
                        Put(FirstRock + (int)(hash >> 16) % 3, best, 1u);
                        for (int k = 0; k < 3 + (int)(hash >> 18 & 1); k++) Put(FirstStone + (int)(Hash(hash + (uint)k) % 5), Around((uint)k + 5u, rad), (uint)k + 5u);
                        Put((int)(hash >> 20) % 2 == 0 ? 0 : 4, Around(17u, rad), 17u);
                        Paint(plan, Blob(best, rad + 0.5f, hash, 10), 3, s.Knob("rockDirt", 0.55f), 1.5f, 0.8f, 3f, hash);
                        plan.Notes.Add($"{h.Id}: a rock garden");
                        break;
                    case 1:   // a sand pit, a bench beside it
                    {
                        var pit = Blob(best, math.min(rad, 2.2f), hash, 10);
                        var ring = Prepared(pit);
                        if (ring == null) break;
                        plan.Paths.Add((h.Id, ring, "Sand Surface 01"));
                        reserved[i].Add(new Region(Ccw(ring)));
                        var toHouse = math.normalizesafe(h.Position - best);
                        Put(FirstBench + (int)(hash >> 16) % 2, best + new float2(-toHouse.y, toHouse.x) * (math.min(rad, 2.2f) + 1.1f), 3u);
                        plan.Notes.Add($"{h.Id}: a sand pit");
                        break;
                    }
                    case 2:   // a wild corner: logs and big bushes on lush ground
                        for (int k = 0; k < 2; k++) Put(FirstLog + (int)(Hash(hash + (uint)k) % 3), Around((uint)k + 11u, rad * 0.8f), (uint)k + 11u);
                        for (int k = 0; k < 3; k++) Put(4, Around((uint)k + 21u, rad), (uint)k + 21u);
                        Paint(plan, Blob(best, rad + 1.5f, hash, 10), (int)s.Knob("lushLayer", 2f), 0.8f, 2f, 1.5f, 4f, hash);
                        plan.Notes.Add($"{h.Id}: a wild corner");
                        break;
                    default:   // a bench nook: a bench facing the house with a flower bed beside it
                    {
                        var toHouse = math.normalizesafe(h.Position - best);
                        float yaw = math.atan2(toHouse.x, toHouse.y);
                        plan.Bushes.Add((h.Id, best, yaw, FirstBench + (int)(hash >> 16) % 2));
                        plan.Bushes.Add((h.Id, best + new float2(-toHouse.y, toHouse.x) * 1.8f, yaw, Flowerbed));
                        Feature.Add((h.Id, best));
                        Feature.Add((h.Id, best + new float2(-toHouse.y, toHouse.x) * 1.8f));
                        Put(2 + (int)(hash >> 18) % 2, best - toHouse * 1.2f + new float2(toHouse.y, -toHouse.x) * 1.4f, 9u);
                        plan.Notes.Add($"{h.Id}: a bench nook");
                        break;
                    }
                }
            }
        }

        /// <summary>A soft, uneven round shape (a pond's or a patch's outline).</summary>
        private static float2[] Blob(float2 c, float r, uint seed, int points)
        {
            var ring = new float2[points];
            for (int k = 0; k < points; k++)
            {
                float a = k * 2f * math.PI / points;
                float wobble = 0.82f + 0.3f * ((Hash(seed + (uint)k * 977u) & 255) / 255f);
                ring[k] = c + new float2(math.cos(a), math.sin(a)) * r * wobble;
            }
            return Ccw(ring);
        }

        /// <summary>A step between two gardens: a planted bed with bushes close together, like a young hedge.</summary>
        private static void StepRow(StitchHouse h, float2[] plot, float2 a, float2 b, float2 inward, StitchPlan plan, List<Region> beds, Obstacle built, StitchSettings s)
        {
            if (math.distance(a, b) < 2f) return;
            var d = math.normalize(b - a);
            var nrm = new float2(-d.y, d.x);
            if (math.dot(nrm, inward) < 0f) nrm = -nrm;
            if (!Poly.Contains(plot, (a + b) * 0.5f + nrm * 0.6f)) nrm = -nrm;
            var rect = Ccw(new[] { a - nrm * 0.3f, b - nrm * 0.3f, b + nrm * 1.3f, a + nrm * 1.3f });
            var region = Poly.Boolean(new[] { new Region(rect) }, new[] { new Region(Ccw(plot)) }, BoolOp.Intersection);
            var near = built.Near(rect);
            if (near.Count > 0) region = Poly.Boolean(region, near, BoolOp.Difference);
            var piece = region.OrderByDescending(r => r.Area).FirstOrDefault();
            if (piece == null || piece.Area < 2f) return;
            var ring = Prepared(piece.Outer);
            if (ring == null || ring.Length < 3) return;
            plan.Beds.Add((h.Id, ring));
            beds.Add(new Region(Ccw(ring)));
            uint hash = Hash((ulong)h.Id ^ 0x5EEDUL);
            int kind = new[] { 0, 1, 4 }[hash % 3];
            int count = 0;
            for (float t = 0.8f; t < math.distance(a, b) - 0.5f; t += 1.5f)
            {
                var p = a + d * t + nrm * 0.55f;
                if (!Poly.Contains(ring, p)) continue;
                plan.Bushes.Add((h.Id, p, (Hash(hash + (uint)count) & 1023) / 1023f * 2f * math.PI, kind));
                count++;
            }
        }

        /// <summary>
        /// A garden path in some big new gardens: paving from the back of the house to the back fence, with a dog-leg
        /// half way (people walk round the beds), clear of everything built.
        /// </summary>
        private static void Paths(IReadOnlyList<StitchHouse> houses, float2[][] plots, bool[] touched, int[] left, int[] right, StitchPlan plan, List<Region>[] beds, Obstacle built, StitchSettings s)
        {
            // who gets a garden path (decided by the house, so every pass agrees)
            bool Wants(int k)
            {
                var hk = houses[k];
                if (!touched[k] || !string.IsNullOrEmpty(hk.MainSurface) || hk.Footprint == null || hk.Footprint.Length < 3) return false;
                if (Hash((ulong)hk.Id * 0x2545F4914F6CDD1DUL) % 100 >= (uint)s.Knob("pathChance", 45f)) return false;
                return Poly.Area(plots[k]) - 4f * hk.HalfLot.x * hk.HalfLot.y >= s.Knob("pathMinExtra", 120f);
            }
            for (int i = 0; i < houses.Count; i++)
            {
                var h = houses[i];
                if (!h.Edit || !Wants(i)) continue;
                uint hash = Hash((ulong)h.Id * 0x2545F4914F6CDD1DUL);
                var plot = Ccw(plots[i]);
                if (!BackEdge(h, plots[i], out var e0, out var e1)) continue;
                // a neighbour with a path too: both paths meet where their shared fence reaches the back (the right-hand
                // neighbour first), rather than each going out back on its own
                int mate = right[i] >= 0 && Wants(right[i]) ? right[i] : left[i] >= 0 && Wants(left[i]) ? left[i] : -1;
                StitchPair shared = mate < 0 ? null : plan.Pairs.FirstOrDefault(p => !p.Rear && ((p.Left == h.Id && p.Right == houses[mate].Id) || (p.Right == h.Id && p.Left == houses[mate].Id)));
                if (shared != null && math.lengthsq(shared.Back - shared.Front) > 4f)
                {
                    float backOfHouse0 = h.Footprint.Min(p => h.ToLocal(p).y);
                    var start0 = h.ToWorld(new float2(0f, backOfHouse0 - 0.4f));
                    var meet = shared.Back + math.normalizesafe(shared.Front - shared.Back) * 0.9f;
                    var sideIn = math.normalizesafe(h.Position - meet);
                    sideIn = math.dot(sideIn, h.Right) > 0f ? h.Right : -h.Right;
                    var meetIn = meet + sideIn * 0.7f;
                    var turn = h.ToWorld(new float2(0f, h.ToLocal(meetIn).y));
                    if (math.distance(start0, turn) > 2f && math.distance(turn, meetIn) > 1f)
                    {
                        var legs = Poly.UnionAll(new[] { Strip(start0, turn, 1.0f), Strip(turn, meetIn, 1.0f) }.Select(p => (IReadOnlyList<float2>)p));
                        legs = Poly.Boolean(legs, new[] { new Region(plot) }, BoolOp.Intersection);
                        var nearLegs = built.Near(new[] { start0, turn, meetIn }).Concat(beds[i]).ToList();
                        if (nearLegs.Count > 0) legs = Poly.Boolean(legs, nearLegs, BoolOp.Difference);
                        var leg = legs.OrderByDescending(r => r.Area).FirstOrDefault();
                        var legRing = leg == null || leg.Area < 4f ? null : Prepared(leg.HasHoles ? Poly.CutHoles(leg, 0f).First() : leg.Outer);
                        if (legRing != null)
                        {
                            plan.Paths.Add((h.Id, legRing, "Tiles Surface 01"));
                            beds[i].Add(new Region(Ccw(legRing)));
                            continue;
                        }
                    }
                }
                // from the middle of the house's back wall
                float backOfHouse = h.Footprint.Min(p => h.ToLocal(p).y);
                var start = h.ToWorld(new float2(0f, backOfHouse - 0.4f));
                // to the back fence itself (a path that stops short would leave the lawn wrapped round it)
                var end = (e0 + e1) * 0.5f - math.normalizesafe(h.Position - (e0 + e1) * 0.5f) * 0.6f;
                if (math.distance(start, end) < 6f) continue;
                float jog = ((hash >> 8 & 1) == 0 ? 1f : -1f) * (1.5f + (hash >> 9 & 3) * 0.5f);
                var mid0 = math.lerp(start, end, 0.5f);
                var mid1 = mid0 + h.Right * jog;
                var pieces = new[] { Strip(start, mid0, 1.0f), Strip(mid0, mid1, 1.0f), Strip(mid1, end + h.Right * jog, 1.0f) };
                var path = Poly.UnionAll(pieces.Select(p => (IReadOnlyList<float2>)p));
                path = Poly.Boolean(path, new[] { new Region(plot) }, BoolOp.Intersection);
                var nearPath = built.Near(pieces.SelectMany(p => p).ToArray()).Concat(beds[i]).ToList();
                if (nearPath.Count > 0) path = Poly.Boolean(path, nearPath, BoolOp.Difference);
                var piece = path.OrderByDescending(r => r.Area).FirstOrDefault();
                if (piece == null || piece.Area < 5f) continue;
                var ring = Prepared(piece.HasHoles ? Poly.CutHoles(piece, 0f).First() : piece.Outer);
                if (ring == null || ring.Length < 3) continue;
                plan.Paths.Add((h.Id, ring, "Tiles Surface 01"));
                beds[i].Add(new Region(Ccw(ring)));
            }
        }

        /// <summary>A rock or two where some shared fences meet the street (in the garden, clear of driveways).</summary>
        private static void Rocks(IReadOnlyList<StitchHouse> houses, float2[][] plots, StitchPlan plan, Dictionary<long, (float2 A, float2 B)> moved, Obstacle built, StitchSettings s)
        {
            // a rock never sits by a hedge (a rock against a hedge looks placed, not found)
            var hedges = houses.SelectMany(h => h.Lines).Where(l => Score(l.Name) == 3 && !plan.HideLines.Contains(l.Id))
                .Select(l => moved.TryGetValue(l.Id, out var m) ? m : (l.A, l.B)).ToList();
            var byId = new Dictionary<long, int>();
            for (int i = 0; i < houses.Count; i++) byId[houses[i].Id] = i;
            foreach (var pair in plan.Pairs)
            {
                if (pair.Rear || math.lengthsq(pair.Back - pair.Front) < 1f) continue;
                uint hash = Hash(((ulong)pair.Left << 20) ^ (ulong)pair.Right ^ 0xB0D1UL);
                if (hash % 100 >= (uint)s.Knob("rockChance", 12f)) continue;
                long owner = (hash >> 7 & 1) == 0 ? pair.Left : pair.Right;
                if (!byId.TryGetValue(owner, out int oi)) continue;
                var d = math.normalizesafe(pair.Back - pair.Front);
                var toPlot = math.normalizesafe(houses[oi].Position - pair.Front);
                var side = new float2(-d.y, d.x);
                if (math.dot(side, toPlot) < 0f) side = -side;
                var p = pair.Front + d * (1.6f + (hash >> 9 & 3) * 0.3f) + side * 1.2f;
                if (!Poly.Contains(plots[oi], p) || built.Near(new[] { p - 0.1f, p + 0.1f }).Any(r => Poly.Contains(r.Outer, p))
                    || plan.Beds.Any(bd => Poly.Contains(bd.Ring, p)) || plan.Paths.Any(pt => Poly.Contains(pt.Ring, p))) continue;
                if (hedges.Any(hl => DistanceToSegment(p, hl.A, hl.B) < 4f)) continue;
                plan.Bushes.Add((owner, p, (hash >> 12 & 1023) / 1023f * 2f * math.PI, FirstRock + (int)((hash >> 22) % 3)));
            }
        }

        /// <summary>A plot grown by a piece of land (whatever of it no other plot holds), as one outline.</summary>
        private static float2[] Grow(float2[][] plots, int owner, float2[] piece)
        {
            var others = Enumerable.Range(0, plots.Length).Where(k => k != owner && plots[k] != null).Select(k => new Region(Ccw(plots[k]))).ToList();
            var free = Poly.Boolean(new[] { new Region(Ccw(piece)) }, others, BoolOp.Difference);
            if (free.Count == 0) return plots[owner];
            var merged = Poly.Boolean(new[] { new Region(Ccw(plots[owner])) }, free, BoolOp.Union);
            var main = merged.OrderByDescending(r => r.Area).FirstOrDefault();
            if (main == null || main.Area < Poly.Area(plots[owner])) return plots[owner];
            return Poly.Simplify(main.Outer, 0.05f);
        }

        private static float DistanceToSegment(float2 p, float2 a, float2 b)
        {
            var ab = b - a;
            float t = math.saturate(math.dot(p - a, ab) / math.max(1e-6f, math.dot(ab, ab)));
            return math.distance(p, a + ab * t);
        }

        private static float2[] Strip(float2 a, float2 b, float width)
        {
            var d = math.normalizesafe(b - a, new float2(1f, 0f));
            var n = new float2(-d.y, d.x) * (width * 0.5f);
            var ext = d * (width * 0.5f);
            return Ccw(new[] { a - ext - n, b + ext - n, b + ext + n, a - ext + n });
        }

        private static float2[] Subtract(float2[] plot, List<float2> part)
        {
            if (part == null || part.Count < 3 || Poly.Area(part) < 0.5f) return plot;
            var left = Poly.Boolean(new[] { (IReadOnlyList<float2>)Ccw(plot) }, new[] { (IReadOnlyList<float2>)Ccw(part.ToArray()) }, BoolOp.Difference, FillRule.NonZero);
            var keep = left.OrderByDescending(r => r.Area).FirstOrDefault();
            return keep == null || keep.Area < 1f ? plot : keep.Outer;
        }

        /// <summary>Where the line p + t·d meets the line q + u·e.</summary>
        private static float2 LineHit(float2 p, float2 d, float2 q, float2 e)
        {
            float den = d.x * e.y - d.y * e.x;
            if (math.abs(den) < 1e-6f) return p;
            var w = q - p;
            float t = (w.x * e.y - w.y * e.x) / den;
            return p + d * t;
        }

        /// <summary>A line laid on a new segment, keeping its direction (its end nearer a goes to a).</summary>
        private static (float2 A, float2 B) Along(StitchLine l, float2 a, float2 b) =>
            math.distance(l.A, a) + math.distance(l.B, b) <= math.distance(l.A, b) + math.distance(l.B, a) ? (a, b) : (b, a);

/// <summary>
        /// A ring cleaned into a shape the game draws (PrepareSurface: node spacing, triangulation): tried as it is, then
        /// simplified more and more (a wavy bed edge or a cut-out leaves tiny edges the game will not take); the largest
        /// accepted piece, or null when nothing near the asked-for size is left.
        /// </summary>
        private static float2[] Prepared(IReadOnlyList<float2> ring)
        {
            if (ring == null || ring.Count < 3) return null;
            float want = Poly.Area(ring);
            foreach (float tol in new[] { 0.05f, 0.25f, 0.6f })
            {
                var r = Placemaker.Core.Build.AreaRings.PrepareSurface(Poly.Simplify(ring, tol), Placemaker.Core.Build.AreaRings.SurfaceMinNodeDistance)
                    .OrderByDescending(x => Poly.Area(x)).FirstOrDefault();
                if (r != null && r.Length >= 3 && Poly.Area(r) >= want * 0.6f) return r;
            }
            return null;
        }

        /// <summary>The house's beds trimmed round its lawn; a bed left with under half of itself goes, and bushes off a bed go too.</summary>
        private static void FitBedsAround(StitchHouse h, float2[] lawn, StitchPlan plan, List<Region> beds)
        {
            var lawnRegion = new[] { new Region(Ccw(lawn)) };
            beds.Clear();
            for (int k = plan.Beds.Count - 1; k >= 0; k--)
            {
                var (house, ring) = plan.Beds[k];
                if (house != h.Id) continue;
                var left = Poly.Boolean(new[] { new Region(Ccw(ring)) }, lawnRegion, BoolOp.Difference).OrderByDescending(r => r.Area).FirstOrDefault();
                var fitted = left != null && left.Area >= Poly.Area(ring) * 0.5f ? Prepared(left.HasHoles ? Poly.CutHoles(left, 0f).First() : left.Outer) : null;
                if (fitted == null) plan.Beds.RemoveAt(k);
                else { plan.Beds[k] = (house, fitted); beds.Add(new Region(Ccw(fitted))); }
            }
            plan.Bushes.RemoveAll(b => b.House == h.Id && b.Kind < FirstRock && !plan.Beds.Any(bd => bd.House == h.Id && Poly.Contains(bd.Ring, b.Position)) && !Feature.Contains((b.House, b.Position)));
        }

        /// <summary>A small triangle well inside the house's outline: where a garden's own grass is tucked away (drawn under the
        /// house, so not seen; given back as it was when the garden is no longer painted).</summary>
        private static float2[] UnderHouse(StitchHouse h)
        {
            if (h.Footprint == null || h.Footprint.Length < 3) return null;
            var c = Poly.Centroid(h.Footprint);
            if (!Poly.Contains(h.Footprint, c)) return null;
            var f = h.Forward;
            var r = h.Right;
            return Ccw(new[] { c + f * 0.6f, c - f * 0.3f + r * 0.52f, c - f * 0.3f - r * 0.52f });
        }

        private static uint Hash(ulong x)
        {
            x ^= x >> 33; x *= 0xff51afd7ed558ccdUL; x ^= x >> 33; x *= 0xc4ceb9fe1a85ec53UL; x ^= x >> 33;
            return (uint)x;
        }

        // ───────────── ground paint ─────────────

        /// <summary>
        /// Terrain materials (2 lush grass, 3 dirt): a soft lush fade from every fitted lawn out into the field, lush meadow on
        /// shared greens and on any wedge no lawn covers, worn dirt where a shared side meets the back fence. Painting keeps
        /// the max per texel, so painting again changes nothing.
        /// </summary>
        private static void PlanPaint(StitchPlan plan, StitchSettings s)
        {
            float fade = s.Knob("fadeWidth", 8f);
            foreach (var kv in plan.Lawns.Select(x => (Key: x.Key, Value: x.Value)).Concat(plan.NewLawns.Select(x => (Key: x.House, Value: x.Ring))))
            {
                var outer = Poly.Offset(Ccw(kv.Value), -fade);
                if (outer.Count == 0) continue;
                Paint(plan, outer.OrderByDescending(r => Poly.Area(r)).First(), (int)s.Knob("lushLayer", 2f), s.Knob("fadeStrength", 0.65f), fade, 2f, 9f, (uint)(kv.Key * 2654435761L));
            }
            foreach (var common in plan.Commons) Paint(plan, common, (int)s.Knob("lushLayer", 2f), s.Knob("meadowStrength", 0.9f), 3f, 2f, 8f, 0x51CEu);
            foreach (var pair in plan.Pairs)
            {
                // (the gap between two houses is their lawns now: painting it too left dark patches behind when a plan changed)
                if (s.Knob("gapPaint", 0f) > 0.5f && pair.Gap != null && Poly.Area(pair.Gap) >= 20f)
                {
                    var region = Poly.Normalize(Ccw(pair.Gap));
                    if (region.Count > 0) Paint(plan, region[0].Outer, (int)s.Knob("lushLayer", 2f), s.Knob("meadowStrength", 0.9f), 2.5f, 1.5f, 7f, (uint)((pair.Left * 17 + pair.Right * 3) & 0x7fffffff));
                }
                if (pair.Rear || math.lengthsq(pair.Back - pair.Front) < 1f) continue;
                float r = s.Knob("dirtBack", 3.2f);
                if (r <= 0.1f) continue;
                var blob = new float2[10];
                for (int i = 0; i < blob.Length; i++) { float ang = i * math.PI * 2f / blob.Length; blob[i] = pair.Back + new float2(math.cos(ang), math.sin(ang)) * r; }
                Paint(plan, blob, 3, s.Knob("dirtStrength", 0.6f), r * 0.8f, r * 0.35f, 3f, (uint)((pair.Left * 31 + pair.Right) & 0x7fffffff));
                // a smaller worn patch where the shared fence meets the street
                float rf = s.Knob("dirtFront", 1.8f);
                if (rf > 0.1f)
                {
                    var front = new float2[10];
                    for (int i = 0; i < front.Length; i++) { float ang = i * math.PI * 2f / front.Length; front[i] = pair.Front + new float2(math.cos(ang), math.sin(ang)) * rf; }
                    Paint(plan, front, 3, s.Knob("dirtStrength", 0.6f) * 0.8f, rf * 0.8f, rf * 0.35f, 3f, (uint)((pair.Left * 7 + pair.Right * 13) & 0x7fffffff));
                }
            }
        }

        private static void Paint(StitchPlan plan, float2[] ring, int layer, float strength, float feather, float noise, float noiseScale, uint seed) =>
            plan.Paints.Add(new Placemaker.Core.PlannedPaint
            {
                Ring = ring.Select(p => new float3(p.x, 0f, p.y)).ToArray(), Layer = layer, Strength = strength, Feather = feather, Noise = noise, NoiseScale = noiseScale, Seed = seed,
            });

        // ───────────── houses ─────────────

        /// <summary>Everything a fence must not cross: houses, sheds, pools, decks, driveways and paving (30 cm in from their edges, so a fence along one is fine).</summary>
        private static List<float2[]> Obstacles(IReadOnlyList<StitchHouse> houses)
        {
            var list = new List<float2[]>();
            foreach (var h in houses)
            {
                if (h.Footprint != null && h.Footprint.Length >= 3) list.Add(Ccw(h.Footprint));
                foreach (var r in h.Props.Concat(h.Hard))
                {
                    if (r == null || r.Length < 3 || Poly.Area(r) < 1f) continue;
                    var inset = Poly.Offset(Ccw(r), 0.3f).OrderByDescending(x => Poly.Area(x)).FirstOrDefault();
                    if (inset != null && inset.Length >= 3) list.Add(Ccw(inset));
                }
            }
            return list;
        }

        private static void TrimAtObstacles(IReadOnlyList<StitchHouse> houses, StitchPlan plan, Dictionary<long, (float2 A, float2 B)> moved, StitchSettings s)
        {
            var footprints = Obstacles(houses);
            int trimmed = 0, dropped = 0;
            foreach (var id in moved.Keys.ToList())
            {
                if (plan.HideLines.Contains(id)) continue;
                var m = moved[id];
                float len = math.distance(m.A, m.B);
                if (len < 0.5f) continue;
                var inside = new List<float2>();
                foreach (var fp in footprints) inside.AddRange(Poly.ClipSegmentIntervals(fp, m.A, m.B));
                if (inside.Sum(iv => iv.y - iv.x) * len < 0.3f) continue;
                // the clear stretches between the houses' intervals; keep the longest
                inside.Sort((p, q) => p.x.CompareTo(q.x));
                float best0 = 0f, best1 = 0f, from = 0f;
                foreach (var iv in inside)
                {
                    if (iv.x - from > best1 - best0) { best0 = from; best1 = iv.x; }
                    from = math.max(from, iv.y);
                }
                if (1f - from > best1 - best0) { best0 = from; best1 = 1f; }
                if ((best1 - best0) * len < s.Knob("minLine", 2f)) { plan.HideLines.Add(id); dropped++; continue; }
                // pull back from the wall a little
                float pad = math.min(0.4f / len, (best1 - best0) * 0.25f);
                moved[id] = (math.lerp(m.A, m.B, best0 > 0f ? best0 + pad : 0f), math.lerp(m.A, m.B, best1 < 1f ? best1 - pad : 1f));
                trimmed++;
            }
            if (trimmed + dropped > 0) plan.Notes.Add($"{trimmed} moved lines stop at a house, shed or driveway, {dropped} too short left out");
        }

        /// <summary>
        /// A surface cannot have holes: each one (a patio or the house inside a lawn that now goes all the way round) is
        /// opened with a 0.4 m slit to the outside, in whichever of the house's four directions costs the least lawn.
        /// </summary>
        private static Region OpenHoles(Region main, StitchHouse h)
        {
            for (int guard = 0; guard < 6 && main.HasHoles; guard++)
            {
                var hole = main.Holes.OrderByDescending(r => Poly.Area(r)).First();
                var c = Poly.Centroid(hole);
                if (!Poly.Contains(hole, c)) c = hole[0];
                Region best = null;
                float bestLoss = float.MaxValue;
                foreach (var dir in new[] { h.Forward, -h.Forward, h.Right, -h.Right })
                {
                    var nrm = new float2(-dir.y, dir.x) * 0.2f;
                    var far = c + dir * 400f;
                    var strip = Ccw(new[] { c - nrm, far - nrm, far + nrm, c + nrm });
                    var parts = Poly.Boolean(new[] { main }, new[] { new Region(strip) }, BoolOp.Difference);
                    var keep = parts.OrderByDescending(r => r.Area).FirstOrDefault();
                    if (keep == null || keep.Holes.Count >= main.Holes.Count) continue;
                    float loss = main.Area - keep.Area;
                    if (h.Footprint != null && h.Footprint.Length >= 3)
                    {
                        var removed = Poly.Boolean(new[] { main }, new[] { keep }, BoolOp.Difference);
                        loss -= Poly.Boolean(removed, new[] { new Region(Ccw(h.Footprint)) }, BoolOp.Intersection).Sum(r => r.Area);
                    }
                    if (loss < bestLoss) { bestLoss = loss; best = keep; }
                }
                if (best == null) return new Region(Poly.CutHoles(main, 0f).First());
                main = best;
            }
            return main.HasHoles ? new Region(main.Outer) : main;
        }

        // ───────────── geometry helpers ─────────────

        /// <summary>How much of the segment a..b the given lines cover (lying along it within 0.6 m), in metres.</summary>
        private static float Covered(float2 a, float2 b, IEnumerable<(float2 A, float2 B)> lines)
        {
            float len = math.distance(a, b);
            var d = (b - a) / len;
            var nrm = new float2(-d.y, d.x);
            var spans = new List<float2>();
            foreach (var (p, q) in lines)
            {
                if (math.abs(math.dot(p - a, nrm)) > 0.6f || math.abs(math.dot(q - a, nrm)) > 0.6f) continue;
                float t0 = math.dot(p - a, d), t1 = math.dot(q - a, d);
                float lo = math.max(0f, math.min(t0, t1)), hi = math.min(len, math.max(t0, t1));
                if (hi > lo) spans.Add(new float2(lo, hi));
            }
            spans.Sort((x, y) => x.x.CompareTo(y.x));
            float total = 0f, end = 0f;
            foreach (var sp in spans)
            {
                if (sp.y <= end) continue;
                total += sp.y - math.max(sp.x, end);
                end = sp.y;
            }
            return total;
        }

        private static float DistanceToRing(float2[] ring, float2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < ring.Length; i++)
            {
                var a = ring[i];
                var e = ring[(i + 1) % ring.Length] - a;
                float t = math.saturate(math.dot(p - a, e) / math.max(1e-6f, math.dot(e, e)));
                best = math.min(best, math.distance(p, a + e * t));
            }
            return best;
        }

        /// <summary>A lot edge (0 left, 1 right, 2 back) with its outward normal.</summary>
        private static (float2 A, float2 B, float2 N) Edge(StitchHouse h, int e) =>
            e == 0 ? (h.FrontLeft, h.BackLeft, -h.Right) : e == 1 ? (h.FrontRight, h.BackRight, h.Right) : (h.BackLeft, h.BackRight, -h.Forward);

        /// <summary>
        /// Edge ei of a faces edge ej of b: (nearly) opposite outward normals, b's edge in front of a's (at most
        /// <paramref name="maxGap"/>), overlapping along it by <paramref name="minOverlap"/>. The cut is their middle line.
        /// </summary>
        private static bool Faces(StitchHouse a, int ei, StitchHouse b, int ej, float maxGap, float minOverlap, out float gap, out Cut cut)
        {
            gap = 0f; cut = null;
            var (a0, a1, na) = Edge(a, ei);
            var (b0, b1, nb) = Edge(b, ej);
            if (math.dot(na, nb) > -0.9f) return false;   // within ~25° of facing
            gap = math.dot((b0 + b1) * 0.5f - (a0 + a1) * 0.5f, na);
            if (gap < -1f || gap > maxGap || math.dot((a0 + a1) * 0.5f - (b0 + b1) * 0.5f, nb) < -1f) return false;
            float len = math.distance(a0, a1);
            var d = (a1 - a0) / len;
            float u0 = math.dot(b0 - a0, d), u1 = math.dot(b1 - a0, d);
            float lo = math.max(0f, math.min(u0, u1)), hi = math.min(len, math.max(u0, u1));
            if (hi - lo < minOverlap) return false;
            gap = math.max(0f, gap);
            var nrm = math.normalizesafe(na - nb, na);
            cut = new Cut { P = a0 + d * ((lo + hi) * 0.5f) + na * (gap * 0.5f), D = new float2(-nrm.y, nrm.x) };
            return true;
        }

        private static float Deep(StitchHouse a, StitchHouse b) => math.max(a.HalfLot.y, b.HalfLot.y);

        private static float2[] Lot(StitchHouse h) => new[] { h.BackLeft, h.BackRight, h.FrontRight, h.FrontLeft };

        /// <summary>A house's side edge (+1 right, -1 left) followed from its street edge to <paramref name="halfDepth"/> × 2 deep.</summary>
        private static float2 BackAt(StitchHouse h, int side, float halfDepth) => h.ToWorld(new float2(side * h.HalfLot.x, h.HalfLot.y - 2f * halfDepth));

        private static bool OnLotEdge(StitchHouse h, float2 p, float tol)
        {
            var l = h.ToLocal(p);
            bool inX = math.abs(l.x) <= h.HalfLot.x + tol, inZ = math.abs(l.y) <= h.HalfLot.y + tol;
            return (inX && math.abs(math.abs(l.y) - h.HalfLot.y) < tol) || (inZ && math.abs(math.abs(l.x) - h.HalfLot.x) < tol);
        }

        /// <summary>Two houses back to back: the gap between their back edges and how far those overlap side to side.</summary>
        private static bool RearGap(StitchHouse a, StitchHouse b, out float gap, out float overlap)
        {
            gap = overlap = 0f;
            var backA = a.Position - a.Forward * a.HalfLot.y;
            var backB = b.Position - b.Forward * b.HalfLot.y;
            float gapA = math.dot(backB - backA, -a.Forward), gapB = math.dot(backA - backB, -b.Forward);
            if (gapA < -2f || gapB < -2f) return false;
            gap = math.max(0f, (gapA + gapB) * 0.5f);
            var dir = math.normalizesafe(a.Right - b.Right, a.Right);
            var mid = (backA + backB) * 0.5f;
            float Along(float2 p) => math.dot(p - mid, dir);
            float a0 = math.min(Along(a.BackLeft), Along(a.BackRight)), a1 = math.max(Along(a.BackLeft), Along(a.BackRight));
            float b0 = math.min(Along(b.BackLeft), Along(b.BackRight)), b1 = math.max(Along(b.BackLeft), Along(b.BackRight));
            overlap = math.min(a1, b1) - math.max(a0, b0);
            return true;
        }

        /// <summary>Sutherland–Hodgman: the part of a convex polygon on the side of the line (p, d) where <paramref name="keep"/> lies.</summary>
        private static List<float2> ClipHalfPlane(List<float2> poly, float2 p, float2 d, float2 keep)
        {
            float Side(float2 x) => d.x * (x.y - p.y) - d.y * (x.x - p.x);
            float sign = math.sign(Side(keep));
            if (sign == 0f || poly.Count < 3) return poly;
            var result = new List<float2>();
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                float sa = Side(a) * sign, sb = Side(b) * sign;
                if (sa >= 0f) result.Add(a);
                if ((sa >= 0f) != (sb >= 0f)) result.Add(a + (b - a) * (sa / (sa - sb)));
            }
            return result;
        }

        /// <summary>The stretch of a plot's boundary lying on a cut line, as parameters along the line (false: none).</summary>
        private static bool EdgeOn(float2[] plot, Cut cut, out float t0, out float t1)
        {
            t0 = float.MaxValue; t1 = float.MinValue;
            var nrm = new float2(-cut.D.y, cut.D.x);
            foreach (var v in plot)
            {
                if (math.abs(math.dot(v - cut.P, nrm)) > 0.05f) continue;
                float t = math.dot(v - cut.P, cut.D);
                t0 = math.min(t0, t);
                t1 = math.max(t1, t);
            }
            return t1 - t0 > 0.5f;
        }

        /// <summary>The plot's edge across the house's back: the edge running side to side, behind the lot's middle, furthest back
        /// (a ring line can end a plot before the lot's own back).</summary>
        private static bool BackEdge(StitchHouse h, float2[] plot, out float2 e0, out float2 e1)
        {
            e0 = e1 = default;
            float back = float.MaxValue;
            for (int i = 0; i < plot.Length; i++)
            {
                var a = plot[i];
                var b = plot[(i + 1) % plot.Length];
                var la = h.ToLocal(a);
                var lb = h.ToLocal(b);
                if (la.y > -0.5f || lb.y > -0.5f || math.distance(a, b) < 0.5f) continue;
                if (math.abs(la.x - lb.x) < 2f * math.abs(la.y - lb.y)) continue;   // across the back, not down a side
                float y = (la.y + lb.y) * 0.5f;
                if (y < back) { back = y; e0 = a; e1 = b; }
            }
            return back < float.MaxValue;
        }

        /// <summary>Where a line, carried on from <paramref name="from"/> through <paramref name="end"/>, leaves the plot.</summary>
        private static bool RayToPlotEdge(float2[] plot, float2 from, float2 end, out float2 hit, float minT = 0.999f)
        {
            hit = end;
            var d = end - from;
            if (math.lengthsq(d) < 1e-4f) return false;
            float best = float.MaxValue;
            for (int i = 0; i < plot.Length; i++)
            {
                var a = plot[i];
                var e = plot[(i + 1) % plot.Length] - a;
                float den = d.x * e.y - d.y * e.x;
                if (math.abs(den) < 1e-6f) continue;
                var w = a - from;
                float t = (w.x * e.y - w.y * e.x) / den, u = (w.x * d.y - w.y * d.x) / den;
                if (u < -1e-4f || u > 1f + 1e-4f || t < minT) continue;
                if (t < best) { best = t; hit = from + d * t; }
            }
            return best < float.MaxValue;
        }

        private static bool Overlaps(IReadOnlyList<float2> a, IReadOnlyList<float2> b)
        {
            foreach (var p in a) if (Poly.Contains(b, p)) return true;
            foreach (var p in b) if (Poly.Contains(a, p)) return true;
            for (int i = 0; i < a.Count; i++)
                for (int j = 0; j < b.Count; j++)
                    if (SegmentsCross(a[i], a[(i + 1) % a.Count], b[j], b[(j + 1) % b.Count])) return true;
            return false;
        }

        private static bool SegmentsCross(float2 p, float2 p2, float2 q, float2 q2)
        {
            float C(float2 o, float2 x, float2 y) => (x.x - o.x) * (y.y - o.y) - (x.y - o.y) * (y.x - o.x);
            return C(p, p2, q) * C(p, p2, q2) < 0f && C(q, q2, p) * C(q, q2, p2) < 0f;
        }

        /// <summary>The closest pair of points between two polygons' outlines.</summary>
        private static (float2, float2) Closest(float2[] a, float2[] b)
        {
            float best = float.MaxValue;
            (float2, float2) r = (a[0], b[0]);
            void Try(float2[] from, float2[] to, bool swap)
            {
                foreach (var p in from)
                    for (int i = 0; i < to.Length; i++)
                    {
                        var s0 = to[i];
                        var e = to[(i + 1) % to.Length] - s0;
                        float t = math.saturate(math.dot(p - s0, e) / math.max(1e-6f, math.dot(e, e)));
                        var q = s0 + e * t;
                        float d = math.distancesq(p, q);
                        if (d < best) { best = d; r = swap ? (q, p) : (p, q); }
                    }
            }
            Try(a, b, false);
            Try(b, a, true);
            return r;
        }

        private static IEnumerable<StitchLine> SideLines(StitchHouse h, int side, StitchSettings s)
        {
            float x = side * h.HalfLot.x;
            foreach (var l in h.Lines)
            {
                var a = h.ToLocal(l.A);
                var b = h.ToLocal(l.B);
                if (math.abs(a.x - x) < s.EdgeTolerance && math.abs(b.x - x) < s.EdgeTolerance && math.abs(a.y - b.y) > 1f) yield return l;
            }
        }

        /// <summary>A house's lines lying along its back edge (or front edge).</summary>
        private static IEnumerable<StitchLine> EdgeLines(StitchHouse h, bool back, StitchSettings s)
        {
            float z = back ? -h.HalfLot.y : h.HalfLot.y;
            foreach (var l in h.Lines)
            {
                var a = h.ToLocal(l.A);
                var b = h.ToLocal(l.B);
                if (math.abs(a.y - z) < s.EdgeTolerance && math.abs(b.y - z) < s.EdgeTolerance && math.abs(a.x - b.x) > 1f) yield return l;
            }
        }

        /// <summary>A line's new place reads as the same fence carried on: it turns at most a little and grows at most a little.</summary>
        private static bool NaturalStretch((float2 A, float2 B) was, (float2 A, float2 B) next, StitchSettings s, float maxTurnDegrees = 20f, float maxGrowth = -1f)
        {
            if (maxGrowth < 0f) maxGrowth = s.Knob("maxStretch", 12f);
            float l0 = math.distance(was.A, was.B), l1 = math.distance(next.A, next.B);
            if (l0 < 0.5f || l1 < 0.5f) return l1 - l0 <= maxGrowth;
            float cos = math.dot((was.B - was.A) / l0, (next.B - next.A) / l1);
            // a fence carried straight on reads as the same fence however far (a garden made deeper)
            if (cos >= math.cos(math.radians(3f))) maxGrowth = math.max(maxGrowth, s.Knob("maxStraightStretch", 30f));
            return l1 - l0 <= maxGrowth && cos >= math.cos(math.radians(maxTurnDegrees));
        }

        /// <summary>The merged region that still holds the original lawn (a piece that does not touch it never replaces it).</summary>
        private static Region Keeping(List<Region> merged, float2[] ring)
        {
            var probe = Poly.Centroid(ring);
            if (!Poly.Contains(ring, probe)) probe = ring[0] + (ring[1] - ring[0]) * 0.5f + (ring[2] - ring[1]) * 0.01f;
            return merged.FirstOrDefault(r => Poly.Contains(r.Outer, probe)) ?? merged.OrderByDescending(r => r.Area).First();
        }

        private static float2[] Ccw(float2[] r) => Poly.IsCcw(r) ? r : r.Reverse().ToArray();
        private static float2[] Ccw(List<float2> r) => Ccw(r.ToArray());
    }
}
