// shops4 — the strip mall, started over: a PARKING LOT with a row of shops behind it. The user (2026-10-17): "a strip mall should
// be a parking lot with shop buildings in and around it"; of the older layout (a ring road round the site, a planted
// frontage, a walkway spine, a plaza, islands and pads, the lot planned last on what was left): "why is it so much of a loop,
// how do the cars even get in there, what are those green rectangles, why is there a path going nowhere". So:
//   • the lot is planned first and is one piece: from the street the player's entrances are on, straight back to the shops;
//     cars come in through the player's entrances only (the parking planner's own connectors, as the parking type);
//   • one shop lane runs along the back of the site (the side away from the entrances), the shops behind it facing the lot;
//     the lot's aisles join it at both ends and between (no dead end, no loop);
//   • when it does not fit, nothing is built and the panel says why (an error: Build waits).
// The frame (which side is the front) is the side most car entrances are on; when that does not fit, each car entrance's own
// side is tried, and the layout with the most shops and stalls is kept.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Commercial
{
    public abstract partial class CommercialLayer
    {
        /// <summary>How a type lays out its lot and shops (<see cref="LotCentre"/>).</summary>
        protected sealed class LotLayout
        {
            /// <summary>Widest plot in cells (big boxes 6) and the anchors' extra width at the row ends.</summary>
            public int MaxPiece = 3;
            public float AnchorWeight = 1.8f;
        }

        public const string LotFact = "lotCentre";
        /// <summary>(shops4) No layout fits: the area is too shallow behind its entrances, or too narrow (an error: Build waits).</summary>
        public const string NoRoomKey = K + "Issue.NoRoom";

        /// <summary>The layout, else an error saying what the site lacks. True when laid out.</summary>
        protected static bool LotCentre(LayerContext ctx, Mall mall, List<StreetShops.Run> runs, LotLayout o)
        {
            mall.LotFirstMode = true;
            if (CompactLot(ctx, mall, runs, o)) { ctx.Plan.Facts[LotFact] = 1; return true; }
            // What the best frame lacked: depth behind the entrances (m), the least needed.
            var (asset, h) = Mall.ShopLane(ctx);
            float need = CompactDepth(h, 2);
            float have = 0f;
            foreach (var r in Frames(ctx, mall, runs)) { Extent(mall, r, out _, out _, out float t0, out float t1); have = math.max(have, t1 - math.max(t0, -2f)); }
            ctx.Plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Error, Key = NoRoomKey, Args = new object[] { (int)math.ceil(need), (int)math.floor(have) } });
            return false;
        }

        /// <summary>Depth (m) the compact layout needs from the entrance edge to the back: one aisle of lot, the lane, the plots.</summary>
        private static float CompactDepth(float h, int cells) => 1f + Mall.LotDepth(1) + 2f * h + Mall.ZoneOffset(h) - h + cells * Mall.Cell + 1.5f;

        private static void Extent(Mall mall, StreetShops.Run r, out float s0, out float s1, out float t0, out float t1)
        {
            s0 = float.MaxValue; s1 = float.MinValue; t0 = float.MaxValue; t1 = float.MinValue;
            foreach (var p in mall.Region.Ring)
            {
                float s = math.dot(p - r.A, r.Dir), t = math.dot(p - r.A, r.In);
                s0 = math.min(s0, s); s1 = math.max(s1, s); t0 = math.min(t0, t); t1 = math.max(t1, t);
            }
        }

        // ───────────── compact ─────────────

        /// <summary>The frames a compact layout is tried in: the main frame, then each car entrance's own (its edge as the front).</summary>
        private static List<StreetShops.Run> Frames(LayerContext ctx, Mall mall, List<StreetShops.Run> runs)
        {
            // Square to the outline, never to a street run's own slightly skewed line (a lot askew to its own site): each car entrance's
            // edge first, then the longest edges of the outline (the preview before any entrance, and the other ways round).
            var list = new List<StreetShops.Run>();
            void Add(float2 at, float2 inward)
            {
                var f = new StreetShops.Run { A = at, In = inward, Dir = new float2(inward.y, -inward.x), From = -50f, To = 50f };
                foreach (var e in list) if (math.dot(e.In, f.In) > 0.999f) return;
                list.Add(f);
            }
            if (mall.Gates != null) foreach (var g in mall.Gates) if (g.Cars) Add(g.At, g.In);
            var ring = mall.Region.Ring;
            var edges = new List<int>();
            for (int i = 0; i < ring.Length; i++) edges.Add(i);
            edges.Sort((x, y) => math.distance(ring[(y + 1) % ring.Length], ring[y]).CompareTo(math.distance(ring[(x + 1) % ring.Length], ring[x])));
            for (int k = 0; k < edges.Count && list.Count < 6; k++)
            {
                int i = edges[k];
                var p = ring[i]; var q = ring[(i + 1) % ring.Length];
                if (math.distance(p, q) < 20f) break;
                Add(math.lerp(p, q, 0.5f), mall.Region.InwardNormal(i));
            }
            return list;
        }

        private static bool CompactLot(LayerContext ctx, Mall mall, List<StreetShops.Run> runs, LotLayout o)
        {
            var planMark = PlanMark.Of(ctx.Plan); var mallMark = mall.Mark();
            var gates = mall.Gates;
            float bestScore = 0f; int bestK = -1;
            var frames = Frames(ctx, mall, runs);
            var tries = new List<float>();
            // Every frame is planned and scored (shop ground plus stalls), then the best planned again for real.
            for (int k = 0; k < frames.Count; k++)
            {
                ctx.Check();
                float score = 0f; int lostMask = 0;
                if (CompactIn(ctx, mall, frames[k], o))
                {
                    // Every entrance the player placed must lead somewhere: a layout that leaves one unconnected loses to one that does not.
                    int lost = 0; foreach (var g in gates) if ((g.Cars || g.Kind == ShopEntrances.Footpath) && g.Landed == "none") { lost++; lostMask += 1 << g.Index; }
                    score = math.max(1f, 2f * mall.ShopArea() + 26f * mall.Stalls - 100000f * lost);
                }
                tries.Add(math.round(score) + lostMask * 0.01f);
                planMark.Restore(ctx.Plan); mall.Rewind(mallMark); mall.ResetDesign(); mall.RowHolds.Clear(); mall.Gates = gates;
                foreach (var g in gates) g.Landed = "none";
                if (score > bestScore) { bestScore = score; bestK = k; }
            }
            if (bestK < 0) return false;
            bool ok = CompactIn(ctx, mall, frames[bestK], o);
            ctx.Plan.Facts["compactFrames"] = frames.Count; ctx.Plan.Facts["compactFrame"] = bestK;
            for (int k = 0; k < tries.Count; k++) ctx.Plan.Facts["compactTry" + k] = tries[k];
            return ok;
        }

        /// <summary>
        /// The compact layout in frame <paramref name="r"/> (s along the front, t inward): the deepest lane line whose plots behind it
        /// stay inside the site, the lot in front of it entered from the player's entrances on its edge, the rest routed to the lane.
        /// </summary>
        private static bool CompactIn(LayerContext ctx, Mall mall, StreetShops.Run r, LotLayout o)
        {
            var (asset, h) = Mall.ShopLane(ctx);
            mall.MainDir = r.Dir;
            mall.AnchorWeight = o.AnchorWeight;
            Extent(mall, r, out float sMin, out float sMax, out float tMin, out float tMax);
            float zo = Mall.ZoneOffset(h);
            for (int cells = Cells(ctx); cells >= 2; cells--)
            {
                float d = cells * Mall.Cell;
                float back = zo + d + 1f;     // from the lane's centre line to the back of the plots
                float front = Mall.LotDepth(1) + h + 1f;
                // Lines to try, deepest first; before them the lines of car entrances on a side (the lane runs straight into them).
                var lines = new List<(float tl, float2 gate)>();
                foreach (var g in mall.Gates)
                {
                    if (!g.Cars || math.abs(math.dot(g.In, r.Dir)) < 0.7f) continue;
                    float tg = math.dot(g.At - r.A, r.In);
                    if (tg <= tMax - back && tg >= tMin + front) lines.Add((tg, g.At));
                }
                for (float tl = tMax - back; tl >= tMin + front; tl -= 2f) lines.Add((tl, new float2(float.NaN)));
                foreach (var (tl, gate) in lines)
                {
                    // The longest stretch along this line where the lane, its plots and a strip of lot in front are all inside.
                    float a = float.NaN, b = float.NaN, ca = float.NaN;
                    for (float s = sMin; s <= sMax + 1f; s += 1f)
                    {
                        bool ok = s <= sMax && mall.Region.Contains(r.At(s, tl - h - 6f)) && mall.Region.Contains(r.At(s, tl)) && mall.Region.Contains(r.At(s, tl + back))
                            // and clear of the first zone rows of a city street behind or beside the plots (the street's own zone block
                            // takes those cells: a plot reaching into them is zoned short)
                            && mall.KerbMargin(r.At(s, tl + back), r.Dir) <= 0f && mall.KerbMargin(r.At(s, tl + back), r.In) <= 0f && mall.KerbMargin(r.At(s, tl + h), r.In) <= 0f;
                        if (ok) { if (float.IsNaN(ca)) ca = s; }
                        else if (!float.IsNaN(ca))
                        {
                            if (float.IsNaN(a) || s - ca > b - a) { a = ca; b = s - 1f; }
                            ca = float.NaN;
                        }
                    }
                    if (float.IsNaN(a) || b - a < 3f * Mall.Cell + 2f * h + 2f) continue;
                    a += 1f; b -= 1f;
                    if (!float.IsNaN(gate.x))
                    {
                        // the lane stops short of the side entrance by a drive's length (the drive joins its end)
                        float sg = math.dot(gate - r.A, r.Dir);
                        if (sg > 0.5f * (a + b)) b = math.min(b, sg - h - 6f); else a = math.max(a, sg + h + 6f);
                        if (b - a < 3f * Mall.Cell + 2f * h + 2f) continue;
                    }
                    if (Compose(ctx, mall, r, o, tl, a, b, cells, h, asset, sMin, sMax, tMin)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The layout behind lane line <paramref name="tl"/>: the back row, and on deep sites as many double rows as fit between bands of
        /// lot two aisles deep at least (the user: "maybe 50 % shops, 50 % parking lot ... for larger areas it just seems like a big
        /// wasteland of parking and one or two shops"). Most rows first.
        /// </summary>
        private static bool Compose(LayerContext ctx, Mall mall, StreetShops.Run r, LotLayout o, float tl, float a, float b, int cells, float h, AssetRef asset,
            float sMin, float sMax, float tMin)
        {
            float d = cells * Mall.Cell, zo = Mall.ZoneOffset(h);
            float island = 2f * (zo + d) + 2f * h;
            float lotMin = Mall.LotDepth(2) + 2f;
            float front = (tl - h) - tMin;
            int kMax = math.clamp((int)math.floor((front - lotMin) / (lotMin + island)), 0, 8);
            for (int k = kMax; k >= 0; k--)
                if (ComposeRows(ctx, mall, r, o, tl, a, b, cells, h, asset, sMin, sMax, tMin, k)) return true;
            return false;
        }

        /// <summary>
        /// <paramref name="k"/> double rows: each a block of shops back to back, ringed by its own lane (the shops face the lots on
        /// both sides), set evenly between the front lot and the back row. Every band of lot between is one lot entered from the lanes
        /// either side (their ends and corners: no dead end); the front band also from the player's entrances.
        /// </summary>
        private static bool ComposeRows(LayerContext ctx, Mall mall, StreetShops.Run r, LotLayout o, float tl, float a, float b, int cells, float h, AssetRef asset,
            float sMin, float sMax, float tMin, int k)
        {
            var planMark = PlanMark.Of(ctx.Plan); var mallMark = mall.Mark();
            bool Fail(int step) { planMark.Restore(ctx.Plan); mall.Rewind(mallMark); mall.RowHolds.Clear(); foreach (var g in mall.Gates) g.Landed = "none"; ctx.Plan.Facts["compactFail"] = step; return false; }
            float d = cells * Mall.Cell, zo = Mall.ZoneOffset(h);
            float span = 2f * (zo + d), island = span + 2f * h;
            var lane = mall.Connect(Mall.End.Free(r.At(a, tl)), null, Mall.End.Free(r.At(b, tl)), NetRole.Alley, asset, h);
            if (lane == null) return Fail(1);
            if (mall.HoldRow(Mall.Rect(r, a + h + 0.5f, b - h - 0.5f, tl + h, tl + zo + d + 1f)) == null) return Fail(2);

            // ── the double rows ──
            float front = (tl - h) - tMin;
            float band = (front - k * island) / (k + 1);
            var loops = new List<(Mall.Lane loop, float y1, float y2, float ia, float ib)>();
            for (int j = 0; j < k; j++)
            {
                float y1 = tMin + (j + 1) * band + j * island + h, y2 = y1 + span;
                // the longest stretch where the ring and its lots either side stay inside the site
                float ia = float.NaN, ib = float.NaN, ca = float.NaN;
                for (float s = sMin; s <= sMax + 1f; s += 1f)
                {
                    bool ok = s <= sMax;
                    if (ok) foreach (float t in new[] { y1 - h - 6f, y1, y1 + zo + d, y2, y2 + h + 6f }) if (!mall.Region.Contains(r.At(s, t)) || mall.KerbMargin(r.At(s, t), r.In) > 0f) { ok = false; break; }
                    if (ok) { if (float.IsNaN(ca)) ca = s; }
                    else if (!float.IsNaN(ca)) { if (float.IsNaN(ia) || s - ca > ib - ia) { ia = ca; ib = s - 1f; } ca = float.NaN; }
                }
                if (float.IsNaN(ia)) return Fail(10);
                // a planted end and room to walk round it, not a lane along the street
                ia += 8f; ib -= 8f;
                if (ib - ia < 6f * Mall.Cell + 2f * h) return Fail(11);
                var loop = mall.Loop(r.At(ia, y1), new[] { r.At(ib, y1), r.At(ib, y2), r.At(ia, y2) }, NetRole.Alley, asset, h);
                if (loop == null) return Fail(12);
                if (mall.HoldRow(Mall.Rect(r, ia + h + 0.5f, ib - h - 0.5f, y1 + h, y1 + zo + d)) == null) return Fail(13);
                if (mall.HoldRow(Mall.Rect(r, ia + h + 0.5f, ib - h - 0.5f, y2 - zo - d, y2 - h)) == null) return Fail(14);
                loops.Add((loop, y1, y2, ia, ib));
            }

            // ── the lots: one band in front of each row ──
            var bands = new List<(float2[] ring, List<(float2 at, string kind)> entries)>();
            void Stubs(List<(float2 at, string kind)> e, float s0, float s1, float t)
            {
                // the lane's ends (its end nodes or the ring's corners become corners of the lot's aisles), then one per 80 m on long rows
                e.Add((r.At(s0 + 0.5f, t), "alley"));
                e.Add((r.At(s1 - 0.5f, t), "alley"));
                float len = s1 - s0;
                int n = len > 160f ? (int)math.floor(len / 80f) : 1;
                for (int q = 1; q < n; q++) e.Add((r.At(s0 + len * q / n, t), "alley"));
            }
            for (int j = 0; j <= k; j++)
            {
                float lo = j == 0 ? tMin - 5f : loops[j - 1].y2 + h + 0.3f;
                float hi = j == k ? tl - h - 0.3f : loops[j].y1 - h - 0.3f;
                float2[] lot;
                try { lot = Mall.Largest(Poly.Intersection(mall.Region.Ring, Mall.Rect(r, sMin - 5f, sMax + 5f, lo, hi)), 300f); }
                catch (Exception) { lot = null; }
                if (lot == null) return Fail(3);
                var e = new List<(float2 at, string kind)>();
                if (j == k) Stubs(e, a, b, hi); else Stubs(e, loops[j].ia, loops[j].ib, hi);
                if (j > 0) Stubs(e, loops[j - 1].ia, loops[j - 1].ib, lo);
                bands.Add((lot, e));
            }

            // Car entrances and footpaths on the front lot's own edge come in through it (the parking planner's connectors, as the
            // parking type); the others are routed to the nearest lane.
            var rest = new List<ShopEntrances.Gate>();
            bool cars = false;
            float frontHi = k == 0 ? tl - h - 0.3f : loops[0].y1 - h - 0.3f;
            foreach (var g in mall.Gates)
            {
                if ((g.Cars || g.Kind == ShopEntrances.Footpath) && Poly.DistanceToBoundary(bands[0].ring, g.At) < 1.5f && math.dot(g.At - r.A, r.In) < frontHi - 4f)
                { bands[0].entries.Add((g.At, g.Kind)); g.Landed = "lot"; if (g.Cars) cars = true; }
                else rest.Add(g);
            }
            var all = mall.Gates;
            mall.Gates = rest;
            try { mall.RouteGates(); }
            finally { mall.Gates = all; }

            // The ground is painted, never reshaped (the user: "try to only use painting"); a lot is levelled only on a real slope.
            mall.Grading = false;
            var lots = new List<Mall.LotTry>();
            for (int j = 0; j < bands.Count; j++)
            {
                // The drives and driveways routed above took their corridors: the lot keeps off them.
                var lot = Minus(bands[j].ring, mall.Taken);
                var parking = Mall.Parking(ctx);
                if (Relief(ctx, lot) < FlatRelief && StreetsLevel(ctx, mall)) { parking.Values["grading"] = false; ctx.Plan.Facts["flatGround"] = 1; }
                var t = BestLot(mall, lot, bands[j].entries, parking, Hashing.Mix(ctx.Seed, 0xC0Cu + (uint)j));
                if (t == null) return Fail(4);
                mall.CommitLot(t);
                lots.Add(t);
            }
            // No car entrance yet (the preview before the player places one: Build waits for it) is fine; placed ones must reach.
            bool anyCars = false; foreach (var g in mall.Gates) if (g.Cars) anyCars = true;
            if (anyCars && !cars && !LaneReached(mall)) return Fail(5);
            WalksToTheLot(ctx, mall, lots[0].Ring);
            ctx.Plan.Facts["gates"] = mall.Gates.Count;

            // ── the shops ──
            mall.ReleaseRows();
            int rows = mall.LaneRows(lane, cells, null, +1, 0f, 0f, o.MaxPiece, 2) > 0 ? 1 : 0;
            foreach (var (loop, _, _, _, _) in loops) rows += mall.LaneRows(loop, cells, null, +1, 0f, 0f, o.MaxPiece, 2, i => i % 2 == 0) > 0 ? 2 : 0;
            if (mall.Shops.Count == 0) return Fail(6);
            ctx.Plan.Facts[StripMallLayer.RowsFact] = rows;
            ctx.Plan.Facts["doubleRows"] = k;
            ctx.Plan.Facts["compactDepth"] = math.round(tl - tMin);
            return true;
        }

        /// <summary>The largest simple part of <paramref name="ring"/> outside everything in <paramref name="taken"/> (the ring itself when nothing cuts it).</summary>
        private static float2[] Minus(float2[] ring, List<float2[]> taken)
        {
            if (taken.Count == 0) return ring;
            var clip = new List<IReadOnlyList<float2>>();
            foreach (var tk in taken) clip.Add(Poly.EnsureCcw(tk));
            try
            {
                float2[] cut = null; float ca = 0f;
                foreach (var p in Poly.Boolean(new[] { (IReadOnlyList<float2>)ring }, clip, BoolOp.Difference))
                    if (p?.Outer != null && p.Holes.Count == 0 && Poly.Area(p.Outer) > ca) { ca = Poly.Area(p.Outer); cut = Poly.EnsureCcw(Poly.Clean(p.Outer, 0.05f)); }
                return cut != null && cut.Length >= 3 ? cut : ring;
            }
            catch (Exception) { return ring; }
        }

        /// <summary>Below this rise and fall (m) under the lot the ground is not reshaped at all.</summary>
        private const float FlatRelief = 1.0f;

        /// <summary>Every street a car entrance joins lies at the ground's level there (a street on a bank or in a cut needs its ramp).</summary>
        private static bool StreetsLevel(LayerContext ctx, Mall mall)
        {
            foreach (var g in mall.Gates)
            {
                if (!g.Cars || !g.HasStreet) continue;
                float hs = MallGrading.StreetHeight(ctx, g.StreetId, g.StreetT, g.StreetPoint);
                if (math.abs(hs - ctx.H(g.At)) > 0.3f) return false;
            }
            return true;
        }

        /// <summary>Highest minus lowest ground (m) over a ring, sampled every 4 m.</summary>
        private static float Relief(LayerContext ctx, float2[] ring)
        {
            float2 mn = float.MaxValue, mx = float.MinValue;
            foreach (var v in ring) { mn = math.min(mn, v); mx = math.max(mx, v); }
            float lo = float.MaxValue, hi = float.MinValue;
            for (float x = mn.x; x <= mx.x; x += 4f)
                for (float y = mn.y; y <= mx.y; y += 4f)
                {
                    var p = new float2(x, y);
                    if (!Poly.Contains(ring, p)) continue;
                    float hgt = ctx.H(p);
                    lo = math.min(lo, hgt); hi = math.max(hi, hgt);
                }
            return hi > lo ? hi - lo : 0f;
        }

        /// <summary>
        /// A footpath entrance that found no way to the shop lane without cutting through the row of shops (beside or behind it)
        /// walks into the parking lot instead: a short path from the sidewalk to the lot's nearest edge, people cross the lot to the
        /// shops as they would in a real one. Its warning goes.
        /// </summary>
        private static void WalksToTheLot(LayerContext ctx, Mall mall, float2[] lot)
        {
            foreach (var g in mall.Gates)
            {
                if (g.Kind != ShopEntrances.Footpath || g.Landed != "none") continue;
                var q = Poly.ClosestPointOnBoundary(lot, g.At, out _, out _);
                float len = math.distance(q, g.At);
                if (len < 2f || len > 30f) continue;
                var dir = (q - g.At) / len; var n = new float2(-dir.y, dir.x);
                var strip = Poly.EnsureCcw(new[] { g.At - n * 1.5f, q - n * 1.5f, q + n * 1.5f, g.At + n * 1.5f });
                bool blocked = false;
                foreach (var shop in mall.Shops) if (StreetShops.ConvexOverlap(shop, strip, 0.05f)) { blocked = true; break; }
                if (blocked || mall.PathChain(new List<float2> { g.At, q }, -g.In, null) == null) continue;
                g.Landed = "walk";
                ctx.Plan.Issues.RemoveAll(i => i.Key == Mall.NoLaneKey && i.Args != null && i.Args.Length > 0 && i.Args[0] is int k && k == g.Index + 1);
            }
            int landed = 0; foreach (var g in mall.Gates) if (g.Landed != "none") landed++;
            ctx.Plan.Facts["gatesLanded"] = landed;
        }

        /// <summary>Did a gate routed to the lane land (cars reach the shops through it when no entrance opens onto the lot)?</summary>
        private static bool LaneReached(Mall mall)
        {
            foreach (var g in mall.Gates) if (g.Cars && (g.Landed == "lane" || g.Landed == "drive")) return true;
            return false;
        }

        /// <summary>The lot planned with its rows along and across the frame; the variant with the most stalls the game will accept.</summary>
        private static Mall.LotTry BestLot(Mall mall, float2[] lot, List<(float2 at, string kind)> entries, SettingsBag parking, uint salt)
        {
            Mall.LotTry best = null; float bestScore = float.MinValue;
            float ang = math.degrees(math.atan2(mall.MainDir.Value.y, mall.MainDir.Value.x));
            foreach (bool ring in new[] { false, true })
                foreach (float rot in new[] { 0f, 90f })
                {
                    var bag = new SettingsBag();
                    foreach (var kv in parking.Values) bag.Values[kv.Key] = kv.Value;
                    bag.Values["setback"] = 0.5;
                    bag.Values["rowAngleMode"] = "fixed";
                    bag.Values["rowAngle"] = (double)(ang + rot);
                    bag.Values["perimeterRoad"] = ring;
                    var t = mall.PlanLot(lot, entries, bag, salt + (uint)rot + (ring ? 7u : 0u), true);
                    if (t == null || NetFit.Violations(t.Sub.Nets, t.Site).Count > 0 || Mall.Warns(t)) continue;
                    // An aisle ending in a turning circle reads as a dead end (the user: "how do the cars even get in there"): each
                    // costs the stalls of a short row.
                    int dead = DeadEnds(t.Sub.Nets);
                    float score = t.Capacity - 12f * dead;
                    if (score > bestScore) { bestScore = score; best = t; }
                }
            return best;
        }

        /// <summary>Net ends no other net shares and that attach to no street or lane: aisles that stop in a turning circle.</summary>
        private static int DeadEnds(List<PlannedNet> nets)
        {
            var ends = new List<(float2 p, bool attached)>();
            foreach (var n in nets)
            {
                if (n == null || n.Role != NetRole.DriveAisle) continue;
                ends.Add((n.A.xz, n.StartAttach != -1));
                ends.Add((n.D.xz, n.EndAttach != -1));
            }
            int dead = 0;
            for (int i = 0; i < ends.Count; i++)
            {
                if (ends[i].attached) continue;
                int same = 0;
                for (int j = 0; j < ends.Count; j++) if (j != i && math.distancesq(ends[i].p, ends[j].p) < 0.04f) same++;
                if (same == 0) dead++;
            }
            return dead;
        }
    }
}
