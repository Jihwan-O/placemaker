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
        public static int Score(string name)
        {
            var n = (name ?? "").ToLowerInvariant();
            return n.Contains("hedge") ? 3 : n.Contains("fence") ? 2 : n.Contains("wall") ? 1 : 0;
        }

        /// <summary>A straight line through P along D; a house keeps the side its own lot is on.</summary>
        private sealed class Cut
        {
            public float2 P, D;
        }

        public static StitchPlan Plan(IReadOnlyList<StitchHouse> houses, StitchSettings s = null)
        {
            s = s ?? new StitchSettings();
            var plan = new StitchPlan();
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
                    if (fg > 60f) continue;
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
                            ringCut[k] = new Cut { P = centre + houses[k].Forward * back, D = houses[k].Right };
                            rowDeepen[k] = 2f * houses[k].HalfLot.y + 200f;   // as far as it takes: the ring line cuts it
                        }
                        continue;
                    }
                }
                foreach (int k in row) rowDeepen[k] = ring ? 2f * (depth - houses[k].HalfLot.y) : math.min(2f * (depth - houses[k].HalfLot.y), s.Knob("maxDeepen", 24f));
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

            // ── 2. plots ──
            var cuts = new List<Cut>[n];
            for (int i = 0; i < n; i++) cuts[i] = new List<Cut>();
            var borders = new Dictionary<(int, int), Cut>();
            for (int i = 0; i < n; i++)
            {
                if (right[i] < 0) continue;
                var a = houses[i];
                var b = houses[right[i]];
                float deep = Deep(a, b);
                var f = (a.FrontRight + b.FrontLeft) * 0.5f;
                var k = (BackAt(a, +1, deep) + BackAt(b, -1, deep)) * 0.5f;
                var cut = new Cut { P = f, D = math.normalizesafe(k - f, -a.Forward) };
                borders[(i, right[i])] = cut;
                cuts[i].Add(cut);
                cuts[right[i]].Add(cut);
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
                foreach (var c in cuts[i]) poly = ClipHalfPlane(poly, c.P, c.D, FrontMid(houses[i]));
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
                foreach (var c in cuts[i]) poly = ClipHalfPlane(poly, c.P, c.D, FrontMid(h));
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

            // ── 3. fences on plot edges ──
            var moved = new Dictionary<long, (float2 A, float2 B)>();
            var placed = new HashSet<long>();   // lines laid on a shared edge: they belong to two plots
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
                if (!touched[i]) continue;
                var h = houses[i];
                var plot = Ccw(plots[i]);
                plan.Plots[h.Id] = plot;
                // once the plot reaches past the lot, the lawn fills all of it but driveways and paving (lawn and fence meet)
                var ext = Poly.Boolean(new[] { (IReadOnlyList<float2>)plot }, new[] { (IReadOnlyList<float2>)Ccw(Lot(h)) }, BoolOp.Difference, FillRule.NonZero);
                if (ext.Sum(r => r.Area) < 1f) continue;
                var lawns = h.Lawns.Where(l => l.Ring != null && l.Ring.Length >= 3).OrderByDescending(l => Poly.Area(l.Ring)).ToList();
                var hard = houses.SelectMany(o => o.Hard).Where(r => r != null && r.Length >= 3 && Overlaps(plot, r)).Select(r => new Region(Ccw(r))).ToList();
                var pieces = hard.Count > 0 ? Poly.Boolean(lawnLand[i], hard, BoolOp.Difference) : lawnLand[i];
                pieces = pieces.Where(p => p.Area > 0.5f).ToList();
                // each lawn (largest first) takes the pieces of the plot it lies in; the house's other lawns stay as they are
                foreach (var lawn in lawns)
                {
                    var mine = pieces.Where(p => Overlaps(p.Outer, lawn.Ring)).ToList();
                    if (mine.Count == 0) continue;
                    pieces.RemoveAll(p => mine.Contains(p));
                    var others = lawns.Where(o => o != lawn).Select(o => new Region(Ccw(o.Ring))).ToList();
                    var merged = Poly.Boolean(new[] { new Region(Ccw(lawn.Ring)) }, mine, BoolOp.Union);
                    if (others.Count > 0) merged = Poly.Boolean(merged, others, BoolOp.Difference);
                    merged = Poly.Boolean(merged, lawnLand[i], BoolOp.Intersection);
                    if (merged.Count == 0) { plan.Notes.Add($"{h.Id}: lawn not fitted (nothing left of it in its plot)"); continue; }
                    var keep = Keeping(merged, lawn.Ring);
                    // what the other lawns cut off from it is land no lawn reaches yet
                    pieces.AddRange(merged.Where(r => r != keep));
                    var main = OpenHoles(keep, h);
                    var grownLawn = Placemaker.Core.Build.AreaRings.PrepareSurface(Poly.Simplify(main.Outer, 0.05f), Placemaker.Core.Build.AreaRings.SurfaceMinNodeDistance)
                        .OrderByDescending(r => Poly.Area(r)).FirstOrDefault();
                    if (grownLawn == null || grownLawn.Length < 3) { plan.Notes.Add($"{h.Id}: lawn not fitted (the game would not take its shape: {main.Outer.Length} points, {main.Area:F0} m²)"); continue; }
                    if (math.abs(Poly.Area(grownLawn) - Poly.Area(lawn.Ring)) < 0.5f) continue;
                    plan.LawnAddedM2 += Poly.Area(grownLawn) - Poly.Area(lawn.Ring);
                    plan.Lawns[lawn.Id] = grownLawn;
                }
                // land no lawn of the house reaches (a gravel-yard house, the far side of a driveway): a lawn of its own
                foreach (var piece in pieces.OrderByDescending(p => p.Area))
                {
                    if (piece.Area < s.Knob("minNewLawn", 20f)) continue;
                    var ring = Placemaker.Core.Build.AreaRings.PrepareSurface(Poly.Simplify(OpenHoles(piece, h).Outer, 0.05f), Placemaker.Core.Build.AreaRings.SurfaceMinNodeDistance)
                        .OrderByDescending(r => Poly.Area(r)).FirstOrDefault();
                    if (ring == null || ring.Length < 3) continue;
                    plan.NewLawns.Add((h.Id, ring));
                    plan.LawnAddedM2 += Poly.Area(ring);
                }
            }

            // ── 5. the middle a ring of houses leaves: one shared green ──
            Commons(houses, plots, left, right, plan, s);

            // ── 6. a moved line never runs through a house: it stops at the house ──
            TrimAtHouses(houses, plan, moved, s);
            foreach (var kv in moved) if (!plan.HideLines.Contains(kv.Key)) plan.MoveLines[kv.Key] = kv.Value;
            plan.Notes.Add($"{touched.Count(t => t)} plots: {borders.Count} shared sides, {rearPairs.Count} back to back, {plan.MoveLines.Count} lines moved, {plan.HideLines.Count} hidden, {plan.Lawns.Count} lawns fitted, {plan.NewLawns.Count} new lawns");
            if (s.Knob("paint", 1f) > 0.5f) PlanPaint(plan, s);
            return plan;
        }

        // ───────────── lines lying on one another ─────────────

        /// <summary>
        /// Two houses' lines on the same straight line and overlapping (two back fences on one middle line, a front fence
        /// carried onto a neighbour's): the overlap goes from the lesser line (score, then length); its longer remaining
        /// piece stays.
        /// </summary>
        private static void Dedupe(IReadOnlyList<StitchHouse> houses, bool[] touched, StitchPlan plan, Dictionary<long, (float2 A, float2 B)> moved, StitchSettings s)
        {
            var lines = new List<(int House, StitchLine L)>();
            for (int i = 0; i < houses.Count; i++)
                if (touched[i]) foreach (var l in houses[i].Lines) if (!plan.HideLines.Contains(l.Id)) lines.Add((i, l));
            (float2 A, float2 B) At(StitchLine l) => moved.TryGetValue(l.Id, out var m) ? m : (l.A, l.B);
            var order = lines.OrderByDescending(x => Score(x.L.Name)).ThenByDescending(x => { var p = At(x.L); return math.distance(p.A, p.B); }).ToList();
            for (int k = 0; k < order.Count; k++)
            {
                var keep = order[k];
                if (plan.HideLines.Contains(keep.L.Id)) continue;
                var (ka, kb) = At(keep.L);
                float klen = math.distance(ka, kb);
                if (klen < 1f) continue;
                var d = (kb - ka) / klen;
                var nrm = new float2(-d.y, d.x);
                for (int m = k + 1; m < order.Count; m++)
                {
                    var other = order[m];
                    if (other.House == keep.House || plan.HideLines.Contains(other.L.Id)) continue;
                    var (oa, ob) = At(other.L);
                    if (math.abs(math.dot(oa - ka, nrm)) > 0.6f || math.abs(math.dot(ob - ka, nrm)) > 0.6f) continue;
                    float t0 = math.dot(oa - ka, d), t1 = math.dot(ob - ka, d);
                    bool flip = t0 > t1;
                    float lo = math.min(t0, t1), hi = math.max(t0, t1);
                    float c0 = math.max(lo, 0f), c1 = math.min(hi, klen);
                    if (c1 - c0 < 1f) continue;
                    // what is left of the other line on either side of the overlap
                    float before = c0 - lo, after = hi - c1;
                    if (math.max(before, after) < s.Knob("minLine", 2f)) { plan.HideLines.Add(other.L.Id); continue; }
                    var (p0, p1) = before >= after ? (lo, c0) : (c1, hi);
                    var q0 = ka + d * p0 + nrm * math.dot(oa - ka, nrm);
                    var q1 = ka + d * p1 + nrm * math.dot(ob - ka, nrm);
                    moved[other.L.Id] = flip ? (q1, q0) : (q0, q1);
                }
            }
        }

        // ───────────── the middle of a ring ─────────────

        private static void Commons(IReadOnlyList<StitchHouse> houses, float2[][] plots, int[] left, int[] right, StitchPlan plan, StitchSettings s)
        {
            // houses whose neighbours' street directions part (their backs close in: the inside of a curve or a circle)
            var ends = new List<float2>();
            int inner = 0;
            for (int i = 0; i < houses.Count; i++)
            {
                if (left[i] < 0 || right[i] < 0) continue;
                var h = houses[i];
                if (math.dot(houses[left[i]].Forward - houses[right[i]].Forward, h.Right) > -0.05f) continue;
                if (!BackEdge(h, plots[i], out var e0, out var e1)) continue;
                ends.Add(e0);
                ends.Add(e1);
                inner++;
            }
            if (inner < 4) return;
            var c = ends.Aggregate(float2.zero, (acc, p) => acc + p) / ends.Count;
            var byAngle = ends.OrderBy(p => math.atan2(p.y - c.y, p.x - c.x)).ToList();
            float biggestGap = 0f;
            for (int i = 0; i < byAngle.Count; i++)
            {
                float a0 = math.atan2(byAngle[i].y - c.y, byAngle[i].x - c.x);
                float a1 = i + 1 < byAngle.Count ? math.atan2(byAngle[i + 1].y - c.y, byAngle[i + 1].x - c.x) : math.atan2(byAngle[0].y - c.y, byAngle[0].x - c.x) + 2f * math.PI;
                biggestGap = math.max(biggestGap, a1 - a0);
            }
            if (biggestGap > math.radians(s.Knob("ringGapDegrees", 80f))) return;
            // the ring the back edges draw (a gap between two unpaired houses is bridged straight), minus every plot
            var ring = Poly.Normalize(byAngle.ToArray());
            if (ring.Count == 0) return;
            var taken = plots.Where(p => p != null).Select(p => (IReadOnlyList<float2>)Ccw(p)).ToList();
            var pieces = Poly.Boolean(new[] { ring.OrderByDescending(r => r.Area).First() }, taken.Select(t => new Region(t.ToArray())), BoolOp.Difference);
            var piece = pieces.OrderByDescending(r => r.Area).FirstOrDefault();
            if (piece == null || piece.Area < 30f) return;
            var outer = piece.HasHoles ? Poly.CutHoles(piece, 0f).First() : piece.Outer;
            plan.Commons.Add(outer);
            plan.Notes.Add($"a shared green in the middle ({Poly.Area(outer):F0} m²)");
        }

        // ───────────── ground paint ─────────────

        /// <summary>
        /// Terrain materials (1 dry grass, 3 dirt): a soft dry-grass fade from every fitted lawn out into the field, meadow on
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
                Paint(plan, outer.OrderByDescending(r => Poly.Area(r)).First(), 1, s.Knob("fadeStrength", 0.65f), fade, 2f, 9f, (uint)(kv.Key * 2654435761L));
            }
            foreach (var common in plan.Commons) Paint(plan, common, 1, s.Knob("meadowStrength", 0.85f), 3f, 2f, 8f, 0x51CEu);
            foreach (var pair in plan.Pairs)
            {
                if (pair.Gap != null && Poly.Area(pair.Gap) >= 20f)
                {
                    var region = Poly.Normalize(Ccw(pair.Gap));
                    if (region.Count > 0) Paint(plan, region[0].Outer, 1, s.Knob("meadowStrength", 0.85f), 2.5f, 1.5f, 7f, (uint)((pair.Left * 17 + pair.Right * 3) & 0x7fffffff));
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

        private static void TrimAtHouses(IReadOnlyList<StitchHouse> houses, StitchPlan plan, Dictionary<long, (float2 A, float2 B)> moved, StitchSettings s)
        {
            var footprints = houses.Where(h => h.Footprint != null && h.Footprint.Length >= 3).Select(h => Ccw(h.Footprint)).ToList();
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
            if (trimmed + dropped > 0) plan.Notes.Add($"{trimmed} moved lines stop at a house, {dropped} too short left out");
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
