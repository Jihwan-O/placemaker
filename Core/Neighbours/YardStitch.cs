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
    public sealed class StitchLine
    {
        public long Id;
        public string Name;
        public float2 A, B;
    }

    public sealed class StitchLawn
    {
        public long Id;
        public float2[] Ring;
    }

    public sealed class StitchHouse
    {
        public long Id;
        public float2 Position;
        /// <summary>Unit direction to the house's street (local +z).</summary>
        public float2 Forward;
        /// <summary>Half the lot: (width, depth) / 2 in metres.</summary>
        public float2 HalfLot;
        public List<StitchLine> Lines = new List<StitchLine>();
        public List<StitchLawn> Lawns = new List<StitchLawn>();

        public float2 Right => new float2(Forward.y, -Forward.x);
        public float2 ToLocal(float2 w) { var d = w - Position; return new float2(math.dot(d, Right), math.dot(d, Forward)); }
        public float2 ToWorld(float2 l) => Position + Right * l.x + Forward * l.y;
        public float2 FrontRight => ToWorld(new float2(HalfLot.x, HalfLot.y));
        public float2 FrontLeft => ToWorld(new float2(-HalfLot.x, HalfLot.y));
        public float2 BackRight => ToWorld(new float2(HalfLot.x, -HalfLot.y));
        public float2 BackLeft => ToWorld(new float2(-HalfLot.x, -HalfLot.y));
    }

    public sealed class StitchSettings
    {
        /// <summary>Largest gap between two neighbours' front corners that still makes them a pair (m).</summary>
        public float MaxFrontGap = 10f;
        /// <summary>Largest gap between their back corners (wedges on curves widen to the back) (m).</summary>
        public float MaxBackGap = 32f;
        /// <summary>How far from a lot edge a line still counts as lying on it (m).</summary>
        public float EdgeTolerance = 1.3f;
        /// <summary>Below this gap (m) the lots touch: only the duplicate line goes, nothing moves.</summary>
        public float TouchingGap = 0.6f;
        /// <summary>A lawn reaching this close to the shared edge grows to the border (m).</summary>
        public float LawnContact = 1.6f;
    }

    public sealed class StitchPair
    {
        public long Left, Right;
        public float2 Front, Back;
        public float FrontGap, BackGap;
        public long KeptLine = -1;
    }

    public sealed class StitchPlan
    {
        public readonly List<StitchPair> Pairs = new List<StitchPair>();
        public readonly HashSet<long> HideLines = new HashSet<long>();
        public readonly Dictionary<long, (float2 A, float2 B)> MoveLines = new Dictionary<long, (float2, float2)>();
        public readonly Dictionary<long, float2[]> Lawns = new Dictionary<long, float2[]>();
        public float LawnAddedM2;
    }

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
                    float fg = math.distance(a.FrontRight, b.FrontLeft), bg = math.distance(a.BackRight, b.BackLeft);
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
                Front = (a.FrontRight + b.FrontLeft) * 0.5f, Back = (a.BackRight + b.BackLeft) * 0.5f,
                FrontGap = math.distance(a.FrontRight, b.FrontLeft), BackGap = math.distance(a.BackRight, b.BackLeft),
            };
            plan.Pairs.Add(pair);
            bool touching = pair.FrontGap < s.TouchingGap && pair.BackGap < s.TouchingGap;
            float2 Border(StitchHouse h, float localZ) => math.lerp(pair.Front, pair.Back, math.saturate((h.HalfLot.y - localZ) / (2f * h.HalfLot.y)));

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

            // back and front lines ending at the pair's corners meet the border
            Snap(a, a.BackRight, pair.Back, s, moved, current, sideA);
            Snap(a, a.FrontRight, pair.Front, s, moved, current, sideA);
            Snap(b, b.BackLeft, pair.Back, s, moved, current, sideB);
            Snap(b, b.FrontLeft, pair.Front, s, moved, current, sideB);

            // lawns that reach the shared side grow up to the border
            GrowLawns(a, +1, Border, s, plan, lawnNow);
            GrowLawns(b, -1, Border, s, plan, lawnNow);
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

        private static void Snap(StitchHouse h, float2 corner, float2 target, StitchSettings s, Dictionary<long, (float2 A, float2 B)> moved,
            Func<StitchLine, (float2 A, float2 B)> current, List<StitchLine> sideLines)
        {
            foreach (var l in h.Lines)
            {
                if (sideLines.Contains(l)) continue;
                var c = current(l);
                if (math.distance(c.A, corner) < s.EdgeTolerance) moved[l.Id] = (target, c.B);
                else if (math.distance(c.B, corner) < s.EdgeTolerance) moved[l.Id] = (c.A, target);
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
                if (Poly.Area(band) < 1f) continue;
                var merged = Poly.Boolean(new[] { Ccw(ring) }, new[] { Ccw(band) }, BoolOp.Union);
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
