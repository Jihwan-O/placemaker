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

            foreach (var a in houses)
            {
                // a's right-hand neighbour: facing the same way, its left side next to a's right side
                StitchHouse best = null;
                float bestGap = float.MaxValue;
                foreach (var b in houses)
                {
                    if (ReferenceEquals(a, b) || math.dot(a.Forward, b.Forward) < 0.5f) continue;
                    if (math.dot(b.Position - a.Position, a.Right) <= 0f) continue;
                    // side by side, not one behind the other: their street edges line up (lots start at the street, depths differ)
                    var frontA = a.Position + a.Forward * a.HalfLot.y;
                    var frontB = b.Position + b.Forward * b.HalfLot.y;
                    if (math.abs(math.dot(frontB - frontA, a.Forward)) > s.MaxFrontOffset) continue;
                    float fg = math.distance(a.FrontRight, b.FrontLeft), bg = math.distance(BackAt(a, +1, Deep(a, b)), BackAt(b, -1, Deep(a, b)));
                    if (fg > s.MaxFrontGap || bg > s.MaxBackGap || fg >= bestGap) continue;
                    best = b;
                    bestGap = fg;
                }
                if (best == null) continue;
                Stitch(a, best, s, plan, lawnNow, moved, Current);
            }
            foreach (var kv in moved) if (!plan.HideLines.Contains(kv.Key)) plan.MoveLines[kv.Key] = kv.Value;
            return plan;
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

            // lawns that reach the shared side grow up to the border
            GrowLawns(a, +1, Border, s, plan, lawnNow);
            GrowLawns(b, -1, Border, s, plan, lawnNow);
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
                moved[l.Id] = onA ? (border(h, Z(la.y)), c.B) : (c.A, border(h, Z(lb.y)));
            }
        }

        private static void GrowLawns(StitchHouse h, int side, Func<StitchHouse, float, float2> border, StitchSettings s, StitchPlan plan, Dictionary<long, float2[]> lawnNow)
        {
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
                    h.ToWorld(new float2(inner, h.HalfLot.y + 50f)), h.ToWorld(new float2(inner + side * 200f, h.HalfLot.y + 50f)),
                    h.ToWorld(new float2(inner + side * 200f, -h.HalfLot.y - 50f)), h.ToWorld(new float2(inner, -h.HalfLot.y - 50f)),
                };
                var parts = Poly.Boolean(new[] { (IReadOnlyList<float2>)band }, new[] { (IReadOnlyList<float2>)Ccw(beyond) }, BoolOp.Intersection, FillRule.NonZero);
                if (parts.Count == 0) continue;
                var piece = parts.OrderByDescending(r => r.Area).First().Outer;
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
            }
        }

        private static float2[] Ccw(float2[] r) => Poly.IsCcw(r) ? r : r.Reverse().ToArray();
    }
}
