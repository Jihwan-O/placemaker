// Placemaker neighbourhood polish — yard stitching (pure geometry, engine-free).
//
// The game's houses are fixed models on rectangular lots, each with its own fence/hedge lines on the lot edges and its own
// lawn. Side by side they never join up: two fences 20 cm apart on a straight street, a bare wedge between diverging
// fences on a curve, lawns stopping short of the neighbour. This plans what the game layer edits on the houses' OWN
// parts (never new houses, never zoning):
//   * every pair of side neighbours gets one border line, from the middle of their front corners to the middle of
//     their back corners (the shared edge on a straight street, the bisector of the wedge on a curve);
//   * the nicer of the two side lines (hedge over fence over wall, then the longer) is moved onto that border, the
//     other house's side lines there are hidden;
//   * back and front lines ending at the pair's corners are stretched to meet the border;
//   * each lawn that reaches the shared side grows up to the border (its half of the gap only).
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

        public static StitchPlan Plan(IReadOnlyList<StitchHouse> houses, StitchSettings s = null)
        {
            s = s ?? new StitchSettings();
            var plan = new StitchPlan();
            var lawnNow = new Dictionary<long, float2[]>();
            foreach (var h in houses) foreach (var l in h.Lawns) lawnNow[l.Id] = l.Ring;
            var moved = new Dictionary<long, (float2 A, float2 B)>();
            (float2 A, float2 B) Current(StitchLine l) => moved.TryGetValue(l.Id, out var m) ? m : (l.A, l.B);

            var touchedBy = new List<(StitchPair Pair, HashSet<long> Lines)>();
            void Track(Action stitch)
            {
                var movedBefore = new Dictionary<long, (float2, float2)>(moved);
                var hiddenBefore = new HashSet<long>(plan.HideLines);
                int pairs = plan.Pairs.Count;
                stitch();
                if (plan.Pairs.Count == pairs) return;
                var lines = new HashSet<long>();
                foreach (var kv in moved) if (!movedBefore.TryGetValue(kv.Key, out var was) || !was.Equals(kv.Value)) lines.Add(kv.Key);
                foreach (var id in plan.HideLines) if (!hiddenBefore.Contains(id)) lines.Add(id);
                touchedBy.Add((plan.Pairs[plan.Pairs.Count - 1], lines));
            }

            foreach (var a in houses)
            {
                // a's right-hand neighbour: facing the same way, its left side next to a's right side
                StitchHouse best = null;
                float bestGap = float.MaxValue;
                string why = null;
                foreach (var b in houses)
                {
                    if (ReferenceEquals(a, b) || math.dot(a.Forward, b.Forward) < 0.5f) continue;
                    if (math.dot(b.Position - a.Position, a.Right) <= 0f) continue;
                    // side by side, not one behind the other: their street edges line up (lots start at the street, depths differ)
                    var frontA = a.Position + a.Forward * a.HalfLot.y;
                    var frontB = b.Position + b.Forward * b.HalfLot.y;
                    // measured along the two houses' mean direction: on a tight circle neighbours face up to ~50° apart
                    float offset = math.abs(math.dot(frontB - frontA, math.normalizesafe(a.Forward + b.Forward, a.Forward)));
                    float fg = math.distance(a.FrontRight, b.FrontLeft), bg = math.distance(BackAt(a, +1, Deep(a, b)), BackAt(b, -1, Deep(a, b)));
                    if (fg > 60f) continue;
                    if (offset > s.MaxFrontOffset) { why = why ?? $"{b.Id}: street edges {offset:F1} m apart"; continue; }
                    if (fg > s.MaxFrontGap || bg > s.MaxBackGap) { why = $"{b.Id}: gap {fg:F1} m at the street, {bg:F1} m at the back"; continue; }
                    if (fg >= bestGap) continue;
                    best = b;
                    bestGap = fg;
                }
                if (best == null) { if (why != null) plan.Notes.Add($"{a.Id} has no right neighbour: nearest {why}"); continue; }
                var bb = best;
                Track(() => Stitch(a, bb, s, plan, lawnNow, moved, Current));
            }
            // back to back across the middle of a block (or the centre of a circle)
            if (s.Knob("rear", 1f) > 0.5f)
                for (int i = 0; i < houses.Count; i++)
                    for (int j = i + 1; j < houses.Count; j++)
                    {
                        var a = houses[i];
                        var b = houses[j];
                        Track(() => StitchRear(a, b, s, plan, lawnNow, moved, Current));
                    }

            // pie slices: inside a circle (or any fan that closes behind the houses) a house's two side borders meet
            // behind it; its garden runs to that point and the slices meet in the middle
            if (s.Knob("pie", 1f) > 0.5f) PieSlices(houses, s, plan, lawnNow, moved, Current);

            // a moved fence or hedge never runs through a house: it stops at the house (the longest clear piece stays)
            var footprints = houses.Where(h => h.Footprint != null && h.Footprint.Length >= 3).Select(h => (h.Id, Ring: Ccw(h.Footprint))).ToList();
            int trimmed = 0, dropped = 0;
            foreach (var id in moved.Keys.ToList())
            {
                if (plan.HideLines.Contains(id)) continue;
                var m = moved[id];
                float len = math.distance(m.A, m.B);
                if (len < 0.5f) continue;
                var inside = new List<float2>();
                foreach (var fp in footprints) inside.AddRange(Poly.ClipSegmentIntervals(fp.Ring, m.A, m.B));
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
                // pull back from the wall a little
                float pad = math.min(0.4f / len, (best1 - best0) * 0.25f);
                if ((best1 - best0) * len < s.Knob("minLine", 2f)) { plan.HideLines.Add(id); dropped++; continue; }
                moved[id] = (math.lerp(m.A, m.B, best0 > 0f ? best0 + pad : 0f), math.lerp(m.A, m.B, best1 < 1f ? best1 - pad : 1f));
                trimmed++;
            }
            if (trimmed + dropped > 0) plan.Notes.Add($"{trimmed} moved lines stop at a house, {dropped} too short left out");
            foreach (var kv in moved) if (!plan.HideLines.Contains(kv.Key)) plan.MoveLines[kv.Key] = kv.Value;
            if (s.Knob("paint", 1f) > 0.5f) PlanPaint(plan, s);
            return plan;
        }

        /// <summary>
        /// Ground paint (terrain materials 1 dry grass, 3 dirt): a soft dry-grass fade from every grown lawn out into the
        /// field (painted under the lawn too, where the lawn hides it), and small worn dirt patches where a border meets
        /// the back fence and at its street end. Painting is idempotent (the game keeps the max), so repeats cost nothing.
        /// </summary>
        private static void PlanPaint(StitchPlan plan, StitchSettings s)
        {
            float fade = s.Knob("fadeWidth", 4f);
            foreach (var kv in plan.Lawns)
            {
                var outer = Poly.Offset(Ccw(kv.Value), -fade);
                if (outer.Count == 0) continue;
                var ring = outer.OrderByDescending(r => Poly.Area(r)).First();
                plan.Paints.Add(new Placemaker.Core.PlannedPaint
                {
                    Ring = ring.Select(p => new float3(p.x, 0f, p.y)).ToArray(), Layer = 1, Strength = s.Knob("fadeStrength", 0.5f),
                    Feather = fade, Noise = 1.2f, NoiseScale = 6f, Seed = (uint)(kv.Key * 2654435761L),
                });
            }
            // the land between two lots that no lawn covers reads as meadow, not as a hole: side gaps, the strip between
            // back-to-back lots, and the pie slices behind houses inside a circle
            foreach (var pair in plan.Pairs)
            {
                if (pair.Gap == null && pair.Rear)
                {
                    // the strip between the two back edges, along the middle line
                    var d = math.normalizesafe(pair.Back - pair.Front);
                    var n = new float2(-d.y, d.x) * (pair.FrontGap * 0.5f + 1f);
                    pair.Gap = new[] { pair.Front - n, pair.Back - n, pair.Back + n, pair.Front + n };
                }
                if (pair.Gap == null || Poly.Area(pair.Gap) < 20f) continue;
                var region = Poly.Normalize(Ccw(pair.Gap));
                if (region.Count == 0) continue;
                plan.Paints.Add(new Placemaker.Core.PlannedPaint
                {
                    Ring = region[0].Outer.Select(p => new float3(p.x, 0f, p.y)).ToArray(), Layer = 1, Strength = s.Knob("meadowStrength", 0.7f),
                    Feather = 2.5f, Noise = 1.5f, NoiseScale = 7f, Seed = (uint)((pair.Left * 17 + pair.Right * 3) & 0x7fffffff),
                });
            }
            foreach (var pair in plan.Pairs)
            {
                if (pair.Reverted) continue;
                foreach (var (at, r) in new[] { (pair.Back, s.Knob("dirtBack", 2.6f)), (pair.Front, s.Knob("dirtFront", 1.8f)) })
                {
                    if (r <= 0.1f) continue;
                    var blob = new float3[10];
                    for (int i = 0; i < blob.Length; i++)
                    {
                        float ang = i * math.PI * 2f / blob.Length;
                        blob[i] = new float3(at.x + math.cos(ang) * r, 0f, at.y + math.sin(ang) * r);
                    }
                    plan.Paints.Add(new Placemaker.Core.PlannedPaint
                    {
                        Ring = blob, Layer = 3, Strength = s.Knob("dirtStrength", 0.55f), Feather = r * 0.8f, Noise = r * 0.3f, NoiseScale = 3f,
                        Seed = (uint)((pair.Left * 31 + pair.Right) & 0x7fffffff),
                    });
                }
            }
        }

        /// <summary>
        /// Two houses back to back (facing away from each other, back edges overlapping side to side): one line on the
        /// middle line between their back edges, the other house's back line hidden, side lines meeting it, lawns to it.
        /// </summary>
        private static void StitchRear(StitchHouse a, StitchHouse b, StitchSettings s, StitchPlan plan, Dictionary<long, float2[]> lawnNow,
            Dictionary<long, (float2 A, float2 B)> moved, Func<StitchLine, (float2 A, float2 B)> current)
        {
            if (math.dot(a.Forward, b.Forward) > -0.5f) return;
            var backA = a.Position - a.Forward * a.HalfLot.y;
            var backB = b.Position - b.Forward * b.HalfLot.y;
            // b's back edge behind a's: both backs face each other, and not too far apart
            float gapA = math.dot(backB - backA, -a.Forward), gapB = math.dot(backA - backB, -b.Forward);
            float maxGap = s.Knob("maxRearGap", 24f);
            if (gapA < -2f || gapB < -2f || gapA > maxGap || gapB > maxGap) return;
            var mid = (backA + backB) * 0.5f;
            var dir = math.normalizesafe(a.Right - b.Right, a.Right);
            float Along(float2 p) => math.dot(p - mid, dir);
            float a0 = math.min(Along(a.BackLeft), Along(a.BackRight)), a1 = math.max(Along(a.BackLeft), Along(a.BackRight));
            float b0 = math.min(Along(b.BackLeft), Along(b.BackRight)), b1 = math.max(Along(b.BackLeft), Along(b.BackRight));
            float o0 = math.max(a0, b0), o1 = math.min(a1, b1);
            if (o1 - o0 < s.Knob("minRearOverlap", 6f)) return;
            float2 OnMid(float2 p) => mid + dir * Along(p);   // straight across onto the middle line
            var pair = new StitchPair { Left = a.Id, Right = b.Id, Rear = true, Front = mid + dir * o0, Back = mid + dir * o1, FrontGap = math.abs(gapA), BackGap = math.abs(gapA) };
            plan.Pairs.Add(pair);

            var backLinesA = EdgeLines(a, back: true, s).ToList();
            var backLinesB = EdgeLines(b, back: true, s).ToList();
            var all = backLinesA.Select(l => (House: a, Line: l)).Concat(backLinesB.Select(l => (House: b, Line: l))).ToList();
            bool touching = math.abs(gapA) < s.TouchingGap;
            if (all.Count > 0)
            {
                var keep = all.OrderByDescending(x => Score(x.Line.Name)).ThenByDescending(x => math.distance(x.Line.A, x.Line.B)).First();
                pair.KeptLine = keep.Line.Id;
                foreach (var x in all)
                {
                    if (x.House != keep.House)
                    {
                        // only the part of the other house's back line that the kept one covers goes
                        var c = current(x.Line);
                        if (Along(c.A) >= o0 - 1f && Along(c.A) <= o1 + 1f && Along(c.B) >= o0 - 1f && Along(c.B) <= o1 + 1f) plan.HideLines.Add(x.Line.Id);
                        continue;
                    }
                    if (touching) continue;
                    var cur = current(x.Line);
                    moved[x.Line.Id] = (OnMid(cur.A), OnMid(cur.B));
                }
            }
            if (touching) return;
            // side lines ending at a back corner inside the overlap reach the middle line
            foreach (var h in new[] { a, b })
                foreach (var l in h.Lines)
                {
                    if (all.Any(x => x.Line.Id == l.Id)) continue;
                    var la = h.ToLocal(l.A);
                    var lb = h.ToLocal(l.B);
                    bool atA = math.abs(la.y + h.HalfLot.y) < s.EdgeTolerance, atB = math.abs(lb.y + h.HalfLot.y) < s.EdgeTolerance;
                    if (atA == atB) continue;
                    var c = current(l);
                    var end = atA ? c.A : c.B;
                    float t = Along(end);
                    if (t < o0 - 1f || t > o1 + 1f) continue;
                    moved[l.Id] = atA ? (OnMid(end), c.B) : (c.A, OnMid(end));
                }
            // lawns touching the back edge grow to the middle line, over the overlap
            foreach (var h in new[] { a, b })
                foreach (var lawn in h.Lawns)
                {
                    var ring = lawnNow[lawn.Id];
                    if (!ring.Any(p => math.abs(h.ToLocal(p).y + h.HalfLot.y) < s.LawnContact)) continue;
                    float inner = -h.HalfLot.y + s.LawnContact;
                    // the band: from just inside the back edge to the middle line, between the overlap's ends
                    var p0 = mid + dir * o0;
                    var p1 = mid + dir * o1;
                    float2 Inside(float2 m) { var l = h.ToLocal(m); return h.ToWorld(new float2(l.x, inner)); }
                    var band = new[] { Inside(p0), Inside(p1), p1, p0 };
                    var avoid = a.Hard.Concat(b.Hard).Concat(new[] { a, b }.Where(o => o != h).SelectMany(o => o.Lawns.Select(x => lawnNow[x.Id]))).ToList();
                    var near = avoid.Where(r => r.Length >= 3).Select(r => (IReadOnlyList<float2>)Ccw(r)).ToList();
                    var pieces = near.Count > 0 ? Poly.Boolean(new[] { (IReadOnlyList<float2>)Ccw(band) }, near, BoolOp.Difference, FillRule.NonZero) : Poly.Normalize(Ccw(band));
                    if (pieces.Count == 0) continue;
                    var piece = pieces.OrderByDescending(r => r.Area).First().Outer;
                    if (Poly.Area(piece) < 1f) continue;
                    var merged = Poly.Boolean(new[] { Ccw(ring) }, new[] { Ccw(piece) }, BoolOp.Union);
                    if (merged.Count == 0) continue;
                    var main = merged.OrderByDescending(r => r.Area).First();
                    if (main.HasHoles) continue;
                    var grown = Placemaker.Core.Build.AreaRings.PrepareSurface(Poly.Simplify(main.Outer, 0.05f), Placemaker.Core.Build.AreaRings.SurfaceMinNodeDistance)
                        .OrderByDescending(r => Poly.Area(r)).FirstOrDefault();
                    if (grown == null || Poly.Area(grown) <= Poly.Area(ring) + 0.5f) continue;
                    plan.LawnAddedM2 += Poly.Area(grown) - Poly.Area(ring);
                    lawnNow[lawn.Id] = grown;
                    plan.Lawns[lawn.Id] = grown;
                }
        }

        /// <summary>
        /// Pie slices: a house whose two side borders meet behind it (inside a circle, a fan closing towards a centre) gets
        /// the slice up to that point: both borders' kept lines run on to it, its back fence goes, its lawn fills the slice.
        /// </summary>
        private static void PieSlices(IReadOnlyList<StitchHouse> houses, StitchSettings s, StitchPlan plan, Dictionary<long, float2[]> lawnNow,
            Dictionary<long, (float2 A, float2 B)> moved, Func<StitchLine, (float2 A, float2 B)> current)
        {
            float maxDepth = s.Knob("pieReach", 60f);     // how far behind the lots two borders may meet at all
            float runOn = s.Knob("pieDepth", 8f);        // how far a border runs on; meeting closer than that = a point
            var side = plan.Pairs.Where(p => !p.Rear).ToList();
            var slices = new List<(StitchHouse H, StitchPair L, StitchPair R)>();
            foreach (var h in houses)
            {
                var left = side.FirstOrDefault(p => p.Right == h.Id);
                var right = side.FirstOrDefault(p => p.Left == h.Id);
                if (left == null || right == null) continue;
                if (!RaysMeet(left.Back, math.normalizesafe(left.Back - left.Front), right.Back, math.normalizesafe(right.Back - right.Front), out var x, out float tl, out float tr)) continue;
                if (tl <= 0.5f || tr <= 0.5f || tl > maxDepth || tr > maxDepth) continue;   // they part (outside of a curve), or meet too far
                if (math.dot(x - h.Position, h.Forward) > -h.HalfLot.y) continue;            // the tip must be behind the lot
                foreach (var p in new[] { left, right })
                    if (!p.Tip.HasValue || math.distance(p.Back, x) < math.distance(p.Back, p.Tip.Value)) p.Tip = x;
                slices.Add((h, left, right));
            }
            // real cul-de-sacs: a border runs on a little and the house's back fence joins the two ends (a point only when
            // the borders meet within that run)
            foreach (var p in side)
            {
                if (!p.Tip.HasValue) continue;
                float toTip = math.distance(p.Back, p.Tip.Value);
                if (toTip > runOn) p.Tip = p.Back + math.normalizesafe(p.Back - p.Front) * runOn;
            }
            foreach (var p in side)
            {
                if (!p.Tip.HasValue || p.KeptLine < 0) continue;
                // the kept line runs on from the border's back point to the tip
                var line = houses.SelectMany(h => h.Lines).FirstOrDefault(l => l.Id == p.KeptLine);
                if (line == null || plan.HideLines.Contains(line.Id)) continue;
                var c = current(line);
                if (math.distance(c.B, p.Back) < 1.5f) moved[line.Id] = (c.A, p.Tip.Value);
                else if (math.distance(c.A, p.Back) < 1.5f) moved[line.Id] = (p.Tip.Value, c.B);
            }
            foreach (var (h, left, right) in slices)
            {
                var tipL = left.Tip ?? left.Back;
                var tipR = right.Tip ?? right.Back;
                // the back fence joins the two border ends (gone where they meet in a point)
                var backs = EdgeLines(h, back: true, s).ToList();
                if (backs.Count == 0 || math.distance(tipL, tipR) < 1f) foreach (var l in backs) plan.HideLines.Add(l.Id);
                else
                {
                    var first = backs.OrderByDescending(l => math.distance(l.A, l.B)).First();
                    // keep its direction: the end nearer the back-left corner goes to the left border's end
                    bool aLeft = math.distance(first.A, h.BackLeft) < math.distance(first.B, h.BackLeft);
                    var join = aLeft ? (tipL, tipR) : (tipR, tipL);
                    if (NaturalStretch(current(first), join, s, maxTurnDegrees: 40f, maxGrowth: s.Knob("pieDepth", 8f) * 2.5f))
                    {
                        moved[first.Id] = join;
                        foreach (var l in backs) if (l.Id != first.Id) plan.HideLines.Add(l.Id);
                    }
                }
                float c = s.LawnContact;
                var slice = new[] { h.ToWorld(new float2(-h.HalfLot.x, -h.HalfLot.y + c)), left.Back, tipL, tipR, right.Back, h.ToWorld(new float2(h.HalfLot.x, -h.HalfLot.y + c)) };
                foreach (var lawn in h.Lawns)
                {
                    var ring = lawnNow[lawn.Id];
                    if (!ring.Any(p => h.ToLocal(p).y < -h.HalfLot.y + c)) continue;
                    var region = Poly.Normalize(slice.Distinct().ToArray());
                    if (region.Count == 0) continue;
                    var piece = region.OrderByDescending(r => r.Area).First().Outer;
                    var merged = Poly.Boolean(new[] { Ccw(ring) }, new[] { Ccw(piece) }, BoolOp.Union);
                    if (merged.Count == 0) continue;
                    var main = merged.OrderByDescending(r => r.Area).First();
                    if (main.HasHoles) continue;
                    var grown = Placemaker.Core.Build.AreaRings.PrepareSurface(Poly.Simplify(main.Outer, 0.05f), Placemaker.Core.Build.AreaRings.SurfaceMinNodeDistance)
                        .OrderByDescending(r => Poly.Area(r)).FirstOrDefault();
                    if (grown == null || Poly.Area(grown) <= Poly.Area(ring) + 0.5f) continue;
                    plan.LawnAddedM2 += Poly.Area(grown) - Poly.Area(ring);
                    lawnNow[lawn.Id] = grown;
                    plan.Lawns[lawn.Id] = grown;
                    break;
                }
            }
            foreach (var (h, left, right) in slices)
            {
                var tipL = left.Tip ?? left.Back;
                var tipR = right.Tip ?? right.Back;
                var region = Poly.Normalize(new[] { h.BackLeft, left.Back, tipL, tipR, right.Back, h.BackRight }.Distinct().ToArray());
                if (region.Count == 0 || region[0].Area < 20f) continue;
                plan.Paints.Add(new Placemaker.Core.PlannedPaint
                {
                    Ring = region[0].Outer.Select(p => new float3(p.x, 0f, p.y)).ToArray(), Layer = 1, Strength = s.Knob("meadowStrength", 0.7f),
                    Feather = 2.5f, Noise = 1.5f, NoiseScale = 7f, Seed = (uint)(h.Id & 0x7fffffff),
                });
            }
            // the middle the slices leave inside a ring (they surround it): a shared green, painted
            if (slices.Count >= 4)
            {
                var ends = side.Where(p => p.Tip.HasValue).Select(p => p.Tip.Value).ToList();
                var c = ends.Aggregate(float2.zero, (acc, p) => acc + p) / math.max(1, ends.Count);
                var angles = ends.Select(p => math.atan2(p.y - c.y, p.x - c.x)).OrderBy(x => x).ToList();
                float biggestGap = 0f;
                for (int i = 0; i < angles.Count; i++) biggestGap = math.max(biggestGap, (i + 1 < angles.Count ? angles[i + 1] : angles[0] + 2f * math.PI) - angles[i]);
                if (biggestGap < math.radians(80f))
                {
                    var hull = Placemaker.Core.Build.AreaRings.ConvexHull(ends);
                    if (hull != null && hull.Length >= 3 && Poly.Area(hull) > 30f)
                    {
                        plan.Paints.Add(new Placemaker.Core.PlannedPaint
                        {
                            Ring = hull.Select(p => new float3(p.x, 0f, p.y)).ToArray(), Layer = 1, Strength = s.Knob("meadowStrength", 0.7f),
                            Feather = 3f, Noise = 2f, NoiseScale = 8f, Seed = 0x51CEu,
                        });
                        plan.Commons.Add(hull);
                        plan.Notes.Add($"a shared green in the middle ({Poly.Area(hull):F0} m²)");
                    }
                }
            }
            if (slices.Count > 0) plan.Notes.Add($"{slices.Count} pie slices");
        }

        /// <summary>Where the rays p + t·u and q + s·v meet (t, s ≥ 0 ahead of each); false when parallel.</summary>
        private static bool RaysMeet(float2 p, float2 u, float2 q, float2 v, out float2 x, out float t, out float s)
        {
            x = default; t = s = 0f;
            float den = u.x * v.y - u.y * v.x;
            if (math.abs(den) < 1e-4f) return false;
            var d = q - p;
            t = (d.x * v.y - d.y * v.x) / den;
            s = (d.x * u.y - d.y * u.x) / den;
            x = p + u * t;
            return true;
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

        private static void Stitch(StitchHouse a, StitchHouse b, StitchSettings s, StitchPlan plan, Dictionary<long, float2[]> lawnNow,
            Dictionary<long, (float2 A, float2 B)> moved, Func<StitchLine, (float2 A, float2 B)> current)
        {
            var pair = new StitchPair
            {
                Left = a.Id, Right = b.Id,
                Front = (a.FrontRight + b.FrontLeft) * 0.5f, Back = (BackAt(a, +1, Deep(a, b)) + BackAt(b, -1, Deep(a, b))) * 0.5f,
                FrontGap = math.distance(a.FrontRight, b.FrontLeft), BackGap = math.distance(BackAt(a, +1, Deep(a, b)), BackAt(b, -1, Deep(a, b))),
            };
            pair.Gap = new[] { a.FrontRight, b.FrontLeft, BackAt(b, -1, Deep(a, b)), BackAt(a, +1, Deep(a, b)) };
            plan.Pairs.Add(pair);
            bool touching = pair.FrontGap < s.TouchingGap && pair.BackGap < s.TouchingGap;
            // the border runs the deeper lot's full depth; a point at local z of either house sits at the same distance from
            // the street along it (a shallow neighbour's back corner lands part way down)
            float deep = 2f * Deep(a, b);
            float2 Border(StitchHouse h, float localZ) => math.lerp(pair.Front, pair.Back, math.saturate((h.HalfLot.y - localZ) / deep));

            // the side lines on the shared side: a's right edge (+x), b's left edge (-x)
            var sideA = SideLines(a, +1, s).ToList();
            var sideB = SideLines(b, -1, s).ToList();
            var all = sideA.Select(l => (House: a, Line: l)).Concat(sideB.Select(l => (House: b, Line: l))).ToList();
            if (all.Count > 0)
            {
                var keep = all.OrderByDescending(x => Score(x.Line.Name)).ThenByDescending(x => math.distance(x.Line.A, x.Line.B)).First();
                pair.KeptLine = keep.Line.Id;
                foreach (var x in all)
                {
                    if (x.House != keep.House) { plan.HideLines.Add(x.Line.Id); continue; }
                    if (touching) continue;
                    var la = x.House.ToLocal(x.Line.A);
                    var lb = x.House.ToLocal(x.Line.B);
                    moved[x.Line.Id] = (Border(x.House, la.y), Border(x.House, lb.y));
                }
            }
            if (touching) return;

            // every other line ending on the shared side (back and front fences at the corners, the short pieces that join
            // an inset hedge to the side) follows to the same place on the border
            SnapToBorder(a, +1, Border, s, moved, current, sideA);
            SnapToBorder(b, -1, Border, s, moved, current, sideB);

            // lawns that reach the shared side grow up to the border, around either house's driveway and paving
            var avoid = a.Hard.Concat(b.Hard).ToList();
            bool grewA = GrowLawns(a, +1, Border, s, plan, lawnNow, avoid);
            bool grewB = GrowLawns(b, -1, Border, s, plan, lawnNow, avoid);
            // a house with no lawn on the shared side leaves its half bare: the neighbour's lawn takes the whole gap
            if (s.Knob("takeWholeGap", 1f) > 0.5f)
            {
                if (grewA && !grewB) GrowLawns(a, +1, (h, z) => FarEdge(b, -1, h, z, Border), s, plan, lawnNow, avoid.Concat(b.Lawns.Select(l => lawnNow[l.Id])).ToList());
                if (grewB && !grewA) GrowLawns(b, -1, (h, z) => FarEdge(a, +1, h, z, Border), s, plan, lawnNow, avoid.Concat(a.Lawns.Select(l => lawnNow[l.Id])).ToList());
            }
        }

        /// <summary>The other house's lot edge at the same distance from the street; past its depth, the border.</summary>
        private static float2 FarEdge(StitchHouse other, int otherSide, StitchHouse h, float z, Func<StitchHouse, float, float2> border)
        {
            float d = h.HalfLot.y - z;
            return d > 2f * other.HalfLot.y ? border(h, z) : other.ToWorld(new float2(otherSide * other.HalfLot.x, other.HalfLot.y - d));
        }

        /// <summary>A line's new place reads as the same fence carried on: it turns at most a little and grows at most a little.</summary>
        private static bool NaturalStretch((float2 A, float2 B) was, (float2 A, float2 B) next, StitchSettings s, float maxTurnDegrees = 20f, float maxGrowth = -1f)
        {
            if (maxGrowth < 0f) maxGrowth = s.Knob("maxStretch", 12f);
            float l0 = math.distance(was.A, was.B), l1 = math.distance(next.A, next.B);
            if (l1 - l0 > maxGrowth) return false;
            if (l0 < 0.5f || l1 < 0.5f) return true;
            float cos = math.dot((was.B - was.A) / l0, (next.B - next.A) / l1);
            return cos >= math.cos(math.radians(maxTurnDegrees));
        }

        private static float Deep(StitchHouse a, StitchHouse b) => math.max(a.HalfLot.y, b.HalfLot.y);

        /// <summary>A house's side edge (+1 right, -1 left) followed from its street edge to <paramref name="halfDepth"/> × 2 deep.</summary>
        private static float2 BackAt(StitchHouse h, int side, float halfDepth) => h.ToWorld(new float2(side * h.HalfLot.x, h.HalfLot.y - 2f * halfDepth));

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

        private static void SnapToBorder(StitchHouse h, int side, Func<StitchHouse, float, float2> border, StitchSettings s, Dictionary<long, (float2 A, float2 B)> moved,
            Func<StitchLine, (float2 A, float2 B)> current, List<StitchLine> sideLines)
        {
            float x = side * h.HalfLot.x;
            foreach (var l in h.Lines)
            {
                if (sideLines.Contains(l)) continue;
                // the line's ORIGINAL ends decide (a line moved for the other side keeps that move at its other end)
                var la = h.ToLocal(l.A);
                var lb = h.ToLocal(l.B);
                var c = current(l);
                bool onA = math.abs(la.x - x) < s.EdgeTolerance, onB = math.abs(lb.x - x) < s.EdgeTolerance;
                if (onA == onB) continue;   // neither end on this side, or the whole line on it (a side line)
                // an end at a lot corner goes exactly to the border's front or back point
                float Z(float z) => math.abs(z - h.HalfLot.y) < s.EdgeTolerance ? h.HalfLot.y : math.abs(z + h.HalfLot.y) < s.EdgeTolerance ? -h.HalfLot.y : z;
                var next = onA ? (border(h, Z(la.y)), c.B) : (c.A, border(h, Z(lb.y)));
                // only a natural continuation: the fence keeps its direction and grows a little (never a long diagonal)
                if (!NaturalStretch(c, next, s)) continue;
                moved[l.Id] = next;
            }
        }

        private static bool GrowLawns(StitchHouse h, int side, Func<StitchHouse, float, float2> border, StitchSettings s, StitchPlan plan, Dictionary<long, float2[]> lawnNow,
            List<float2[]> avoid)
        {
            bool grew = false;
            float x = side * h.HalfLot.x;
            foreach (var lawn in h.Lawns)
            {
                var ring = lawnNow[lawn.Id];
                // where the lawn touches the shared side (its local z range)
                float zmin = float.MaxValue, zmax = float.MinValue;
                foreach (var p in ring)
                {
                    var l = h.ToLocal(p);
                    if (math.abs(l.x - x) > s.LawnContact) continue;
                    zmin = math.min(zmin, l.y);
                    zmax = math.max(zmax, l.y);
                }
                if (zmax - zmin < 1f) continue;
                float inner = x - side * s.LawnContact;
                var band = new[]
                {
                    h.ToWorld(new float2(inner, zmax)), border(h, zmax), border(h, zmin), h.ToWorld(new float2(inner, zmin)),
                };
                // where lots overlap (the back of a curve's inside) the border crosses the lot edge: only the part beyond the edge
                var beyond = new[]
                {
                    h.ToWorld(new float2(inner, h.HalfLot.y + 50f)), h.ToWorld(new float2(x + side * s.Knob("lawnReach", 10f), h.HalfLot.y + 50f)),
                    h.ToWorld(new float2(x + side * s.Knob("lawnReach", 10f), -h.HalfLot.y - 50f)), h.ToWorld(new float2(inner, -h.HalfLot.y - 50f)),
                };
                var parts = Poly.Boolean(new[] { (IReadOnlyList<float2>)band }, new[] { (IReadOnlyList<float2>)Ccw(beyond) }, BoolOp.Intersection, FillRule.NonZero);
                if (parts.Count == 0) continue;
                var piece = parts.OrderByDescending(r => r.Area).First().Outer;
                if (avoid != null && avoid.Count > 0)
                {
                    var (lo, hi) = (math.min(math.min(band[0], band[1]), math.min(band[2], band[3])), math.max(math.max(band[0], band[1]), math.max(band[2], band[3])));
                    var near = avoid.Where(r => r.Length >= 3 && r.Any(p => p.x >= lo.x - 1f && p.x <= hi.x + 1f && p.y >= lo.y - 1f && p.y <= hi.y + 1f)).Select(r => (IReadOnlyList<float2>)Ccw(r)).ToList();
                    if (near.Count > 0)
                    {
                        var cut = Poly.Boolean(new[] { (IReadOnlyList<float2>)Ccw(piece) }, near, BoolOp.Difference, FillRule.NonZero);
                        if (cut.Count == 0) continue;
                        // the piece that still touches the lawn
                        piece = cut.OrderByDescending(r => r.Area).First().Outer;
                    }
                }
                if (Poly.Area(piece) < 1f) continue;
                var merged = Poly.Boolean(new[] { Ccw(ring) }, new[] { Ccw(piece) }, BoolOp.Union);
                if (merged.Count == 0) continue;
                var main = merged.OrderByDescending(r => r.Area).First();
                if (main.HasHoles) continue;
                // the game drops a surface it cannot triangulate: only shapes it accepts (split pieces keep the largest)
                var grown = Placemaker.Core.Build.AreaRings.PrepareSurface(Poly.Simplify(main.Outer, 0.05f), Placemaker.Core.Build.AreaRings.SurfaceMinNodeDistance)
                    .OrderByDescending(r => Poly.Area(r)).FirstOrDefault();
                if (grown == null || grown.Length < 3 || Poly.Area(grown) <= Poly.Area(ring) + 0.5f) continue;
                plan.LawnAddedM2 += Poly.Area(grown) - Poly.Area(ring);
                lawnNow[lawn.Id] = grown;
                plan.Lawns[lawn.Id] = grown;
                grew = true;
            }
            return grew;
        }

        private static float2[] Ccw(float2[] r) => Poly.IsCcw(r) ? r : r.Reverse().ToArray();
    }
}
