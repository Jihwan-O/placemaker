// Placemaker "Zoning" — Level the lots: every lot the Zoning tool zones on sloped land becomes a level pad at the height it
// meets its road (level across the lot, following the road along it), with banks blending into the land around (no steeper
// than the bank setting, past the area where needed, never over buildings, roads or water). A lot that would need more
// earth than the limit is made shallower (down to its zone's smallest depth) or left empty. Lots already level are left
// alone. One continuous height field on a 2 m grid (Common/Sculpt), the bank limits and clash resolution of the road tool's
// terraces (TerraceField.Resolve). Where two lots back onto each other at different heights the bank between them has to
// take ground: the field is laid, each lot keeps the rows from its road that came out level (a lot under its zone's
// smallest depth is left empty), and the field is laid again for the shortened lots (the bank now behind them), up to
// three times; then emitted once, the banks painted like the terraces'. Pure.

using System;
using System.Collections.Generic;
using System.Threading;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Roads;
using Placemaker.Core.Layers.Roads.Terraces;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Zoning
{
    internal static class LotLevel
    {
        /// <summary>A lot already this level (most ground off its pad, m) is left alone.</summary>
        public const float LevelEnough = 0.2f;

        internal sealed class Lot
        {
            public ZoneGrid G;
            public int X0, W, D;
            /// <summary>Its zone's smallest depth: shorter, the lot stays empty.</summary>
            public int MinD = 1;
            public string ZoneId, Stat;
            /// <summary>The cells it claimed (row from the road, square): zoned once the ground is laid, rows under D only.</summary>
            public readonly List<(int Y, float2[] Sq)> Cells = new List<(int, float2[])>();
            /// <summary>Pad height per column (x0 … x0 + w − 1).</summary>
            public float[] Front;
            /// <summary>The lot needs earthwork (not level as it is).</summary>
            public bool Level;
        }

        internal sealed class Options
        {
            public float MaxEarth = 4f;
            public float BankSlope = 0.4f;
            public bool Paint = true;
            public uint Seed = 1;
        }

        /// <summary>The ground the lot meets its road at, per column (1 m in from the front edge, averaged with its neighbours).</summary>
        public static float[] Fronts(ZoneGrid g, Func<float2, float> ground)
        {
            int sx = g.Size.x;
            var raw = new float[sx];
            for (int x = 0; x < sx; x++)
            {
                var c = ZoneGuard.CellCentre(g, x, 0);
                var d = math.normalizesafe(g.Direction);
                raw[x] = ground(c + d * (ZoneGuard.Cell * 0.5f - 1f));
            }
            var f = new float[sx];
            for (int x = 0; x < sx; x++)
            {
                float s = raw[x] * 2f, w = 2f;
                if (x > 0) { s += raw[x - 1]; w += 1f; }
                if (x + 1 < sx) { s += raw[x + 1]; w += 1f; }
                f[x] = s / w;
            }
            return f;
        }

        /// <summary>
        /// How deep (≤ <paramref name="d"/>, ≥ <paramref name="minD"/>) the lot of columns x0 … x0 + w − 1 can be levelled at its
        /// front heights within <paramref name="maxEarth"/> of cut or fill; 0 when not even its smallest depth can. Also
        /// whether it needs levelling at all.
        /// </summary>
        public static int Depth(ZoneGrid g, float[] fronts, int x0, int w, int d, int minD, Func<float2, float> ground, float maxEarth, out bool needs)
        {
            needs = false;
            var dir = math.normalizesafe(g.Direction);
            var right = new float2(dir.y, -dir.x);
            int ok = 0;
            float most = 0f;
            for (int y = 0; y < d; y++)
            {
                float rowMost = 0f;
                for (int x = x0; x < x0 + w; x++)
                {
                    var c = ZoneGuard.CellCentre(g, x, y);
                    float f = fronts[x];
                    for (int k = 0; k < 5; k++)
                    {
                        var p = k == 0 ? c : c + (k == 1 ? dir : k == 2 ? -dir : k == 3 ? right : -right) * 3.5f;
                        rowMost = math.max(rowMost, math.abs(ground(p) - f));
                    }
                }
                if (rowMost > maxEarth) break;
                most = math.max(most, rowMost);
                ok = y + 1;
            }
            if (ok < minD) return 0;
            needs = most > LevelEnough;
            return ok;
        }

        /// <summary>
        /// The pads and their banks as terraform (sloped quads), the bank paint; facts and stats. Returns the ground after (null:
        /// nothing changed).
        /// </summary>
        public static Func<float2, float> Build(LayerContext ctx, List<Lot> lots, IReadOnlyList<(float3[] Line, float Width)> roads, Options o, CancellationToken cancel)
        {
            Sculpt sc = null;
            bool[] pad = null;
            List<Lot> level = null;
            for (int pass = 0; pass < 3; pass++)
            {
                level = lots.FindAll(l => l.Level && l.D > 0);
                if (level.Count == 0) { sc = null; break; }
                sc = Field(ctx, level, roads, o, cancel, out pad);
                // each lot keeps the rows from its road that lie level on this ground
                bool changed = false;
                foreach (var l in lots)
                {
                    if (l.D == 0) continue;
                    int keep = LevelRows(sc, l);
                    if (keep >= l.D) continue;
                    l.D = keep >= l.MinD ? keep : 0;
                    changed = true;
                }
                if (!changed) break;
            }
            Inspect?.Invoke(sc, lots);
            if (sc == null) return null;
            var tr = sc.Emit(ctx, null, Sculpt.MinChange, 2);
            if (tr.Cells == 0) return null;
            var earth = sc.Measure();
            ctx.Plan.Facts["polishLevelLots"] = level.Count;
            ctx.Plan.Facts["polishLevelCut"] = earth.CutVolume;
            ctx.Plan.Facts["polishLevelFill"] = earth.FillVolume;
            ctx.Plan.Facts["polishLevelMaxCut"] = earth.MaxCut;
            ctx.Plan.Facts["polishLevelMaxFill"] = earth.MaxFill;
            ctx.Plan.Stats.Add(new PlanStat(StatLevelled, level.Count, "count"));
            ctx.Plan.Stats.Add(new PlanStat(RoadsPlanner.StatCut, Math.Round(earth.CutVolume / 10.0) * 10.0, "m³"));
            ctx.Plan.Stats.Add(new PlanStat(RoadsPlanner.StatFill, Math.Round(earth.FillVolume / 10.0) * 10.0, "m³"));
            if (o.Paint)
            {
                var g = new TerraceGround { Grid = sc, Terrace = pad, Corridor = new bool[sc.Count], HeightAfter = sc.HeightAfter, Cells = tr.Cells };
                ctx.Plan.Facts["polishLevelPaints"] = TerraceDressing.Paint(ctx.Plan, g, null, new TerraceOptions { Seed = o.Seed });
            }
            return sc.HeightAfter;
        }

        /// <summary>How many rows of the lot, from its road, lie level across (and no steeper along than its road) on the field.</summary>
        [ThreadStatic] internal static Action<Sculpt, List<Lot>> Inspect;

        internal static int LevelRows(Sculpt sc, Lot l)
        {
            var g = l.G;
            var dir = math.normalizesafe(g.Direction);
            var right = new float2(dir.y, -dir.x);
            const float R = 3f;
            for (int y = 0; y < l.D; y++)
                for (int x = l.X0; x < l.X0 + l.W; x++)
                {
                    var c = ZoneGuard.CellCentre(g, x, y);
                    float h = sc.HeightAt(c);
                    // across: level (a little give for the build's raster); along: the road's own grade here and a little more
                    float lim = 0.03f * R + 0.03f;
                    if (math.abs(sc.HeightAt(c + dir * R) - h) > lim || math.abs(sc.HeightAt(c - dir * R) - h) > lim) return y;
                    int i = x - l.X0;
                    float grade = l.W > 1 ? math.abs(l.Front[math.min(i + 1, l.W - 1)] - l.Front[math.max(i - 1, 0)]) / (ZoneGuard.Cell * math.max(1, math.min(i + 1, l.W - 1) - math.max(i - 1, 0))) : 0f;
                    float limA = (grade + 0.04f) * R + 0.06f;
                    if (math.abs(sc.HeightAt(c + right * R) - h) > limA || math.abs(sc.HeightAt(c - right * R) - h) > limA) return y;
                }
            return l.D;
        }

        /// <summary>The pads of the levelled lots and the banks around them (not emitted).</summary>
        private static Sculpt Field(LayerContext ctx, List<Lot> level, IReadOnlyList<(float3[] Line, float Width)> roads, Options o, CancellationToken cancel, out bool[] pad)
        {
            var site = ctx.Input.Site;
            float a = math.clamp(o.BankSlope, 0.1f, 0.46f);
            float margin = o.MaxEarth / a * 1.3f + 6f;
            float2 mn = new float2(float.MaxValue), mx = new float2(float.MinValue);
            var rects = new List<(float2[] r, Lot lot)>();
            foreach (var l in level)
            {
                var r = ZoneGuard.CellRect(l.G, l.X0, l.X0 + l.W - 1, 0, l.D - 1, 0.5f);   // (D: its rows as they stand now)
                rects.Add((r, l));
                foreach (var q in r) { mn = math.min(mn, q); mx = math.max(mx, q); }
            }
            mn -= margin; mx += margin;
            var obstacles = new RoadObstacles(site, mn, mx);
            float2 inMin = mn + 4f, inMax = mx - 4f;
            bool Sculptable(float2 p)
            {
                if (p.x < inMin.x || p.y < inMin.y || p.x > inMax.x || p.y > inMax.y) return false;
                if (obstacles.BlockedAt(p)) return false;
                foreach (var (line, width) in roads)
                    if (Near(line, p, width * 0.5f + 0.5f)) return false;
                return true;
            }
            var sc = new Sculpt(ctx.H, mn, mx, 2f, Sculptable);
            int count = sc.Count;
            var protect = new byte[count];
            pad = new bool[count];
            for (int k = 0; k < count; k++)
            {
                if ((k & 4095) == 0) cancel.ThrowIfCancellationRequested();
                if (!sc.Free[k]) continue;
                var p = sc.Pos(k);
                foreach (var (r, l) in rects)
                {
                    if (!Poly.Contains(r, p)) continue;
                    sc.Target[k] = PadAt(l, p);
                    protect[k] = 1;
                    pad[k] = true;
                    break;
                }
            }
            TerraceField.Resolve(sc, protect, a, cancel, o.MaxEarth);
            for (int k = 0; k < count; k++)
                if (sc.Free[k]) sc.Target[k] = sc.Base[k] + math.clamp(sc.Target[k] - sc.Base[k], -o.MaxEarth, o.MaxEarth);
            TerraceField.Resolve(sc, protect, a, cancel, o.MaxEarth);
            return sc;
        }

        public const string StatLevelled = "Placemaker.Polish.Stat.Levelled";

        /// <summary>The pad height at p inside a lot: its front height, interpolated between the columns (level across the lot).</summary>
        private static float PadAt(Lot l, float2 p)
        {
            var g = l.G;
            var d = math.normalizesafe(g.Direction);
            var r = new float2(d.y, -d.x);
            float u = math.dot(p - g.Position, r);
            float fx = (g.Size.x - 1 - u / (ZoneGuard.Cell * 0.5f)) * 0.5f;
            fx = math.clamp(fx, l.X0, l.X0 + l.W - 1);
            int x = (int)math.floor(fx);
            int x1 = math.min(x + 1, l.X0 + l.W - 1);
            return math.lerp(l.Front[x - l.X0], l.Front[x1 - l.X0], fx - x);
        }

        private static bool Near(float3[] line, float2 p, float r)
        {
            for (int i = 0; i + 1 < line.Length; i++)
            {
                var a = line[i].xz; var e = line[i + 1].xz - a;
                float t = math.saturate(math.dot(p - a, e) / math.max(1e-6f, math.dot(e, e)));
                if (math.distancesq(p, a + e * t) <= r * r) return true;
            }
            return false;
        }
    }
}
