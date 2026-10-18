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
            // Every frame is planned and scored (shop ground plus stalls), then the best planned again for real.
            for (int k = 0; k < frames.Count; k++)
            {
                ctx.Check();
                float score = 0f;
                if (CompactIn(ctx, mall, frames[k], o))
                {
                    // Every entrance the player placed must lead somewhere: a layout that leaves one unconnected loses to one that does not.
                    int lost = 0; foreach (var g in gates) if ((g.Cars || g.Kind == ShopEntrances.Footpath) && g.Landed == "none") lost++;
                    score = math.max(1f, mall.ShopArea() + 26f * mall.Stalls - 100000f * lost);
                }
                ctx.Plan.Facts["compactTry" + k] = math.round(score);
                planMark.Restore(ctx.Plan); mall.Rewind(mallMark); mall.ResetDesign(); mall.RowHolds.Clear(); mall.Gates = gates;
                foreach (var g in gates) g.Landed = "none";
                if (score > bestScore) { bestScore = score; bestK = k; }
            }
            if (bestK < 0) return false;
            return CompactIn(ctx, mall, frames[bestK], o);
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

        private static bool Compose(LayerContext ctx, Mall mall, StreetShops.Run r, LotLayout o, float tl, float a, float b, int cells, float h, AssetRef asset,
            float sMin, float sMax, float tMin)
        {
            var planMark = PlanMark.Of(ctx.Plan); var mallMark = mall.Mark();
            bool Fail(int step) { planMark.Restore(ctx.Plan); mall.Rewind(mallMark); mall.RowHolds.Clear(); ctx.Plan.Facts["compactFail"] = step; return false; }
            float d = cells * Mall.Cell, zo = Mall.ZoneOffset(h);
            var lane = mall.Connect(Mall.End.Free(r.At(a, tl)), null, Mall.End.Free(r.At(b, tl)), NetRole.Alley, asset, h);
            if (lane == null) return Fail(1);
            if (mall.HoldRow(Mall.Rect(r, a + h + 0.5f, b - h - 0.5f, tl + h, tl + zo + d + 1f)) == null) return Fail(2);
            // The lot: the site in front of the lane.
            float2[] lot;
            try { lot = Mall.Largest(Poly.Intersection(mall.Region.Ring, Mall.Rect(r, sMin - 5f, sMax + 5f, tMin - 5f, tl - h - 0.3f)), 300f); }
            catch (Exception) { lot = null; }
            if (lot == null) return Fail(3);
            // Car entrances and footpaths on the lot's own edge come in through the lot (the parking planner's connectors, as the
            // parking type); the others are routed to the lane.
            var entries = new List<(float2 at, string kind)>();
            var rest = new List<ShopEntrances.Gate>();
            foreach (var g in mall.Gates)
            {
                if ((g.Cars || g.Kind == ShopEntrances.Footpath) && Poly.DistanceToBoundary(lot, g.At) < 1.5f && math.dot(g.At - r.A, r.In) < tl - h - 4f)
                { entries.Add((g.At, g.Kind)); g.Landed = "lot"; }
                else rest.Add(g);
            }
            bool cars = false;
            foreach (var (_, kind) in entries) if (ShopEntrances.ForCars(kind)) cars = true;
            // Ways onto the lane from the lot (its aisles reach the shops' lane): one per 40 m of lane.
            float len = b - a;
            // Its ends first (the lane's end nodes become corners of the lot's aisles: no dead end), then one per 80 m between on long rows (each costs a cell of shops).
            entries.Add((r.At(a + 0.5f, tl - h - 0.3f), "alley"));
            entries.Add((r.At(b - 0.5f, tl - h - 0.3f), "alley"));
            int n = len > 160f ? (int)math.floor(len / 80f) : 1;
            for (int k = 1; k < n; k++) entries.Add((r.At(a + len * k / n, tl - h - 0.3f), "alley"));
            var all = mall.Gates;
            mall.Gates = rest;
            try { mall.RouteGates(); }
            finally { mall.Gates = all; }
            int landed = 0; foreach (var g in all) if (g.Landed != "none") landed++;
            ctx.Plan.Facts["gates"] = all.Count;
            ctx.Plan.Facts["gatesLanded"] = landed;
            // The drives and driveways routed above took their corridors: the lot keeps off them (it overlapped them, and its level
            // ground replaced a driveway's ramp at the kerb). The largest part is the lot; the entrances on the others stay on it.
            if (mall.Taken.Count > 0)
            {
                var clip = new List<IReadOnlyList<float2>>();
                foreach (var tk in mall.Taken) clip.Add(Poly.EnsureCcw(tk));
                try
                {
                    float2[] cut = null; float ca = 0f;
                    foreach (var p in Poly.Boolean(new[] { (IReadOnlyList<float2>)lot }, clip, BoolOp.Difference))
                        if (p?.Outer != null && p.Holes.Count == 0 && Poly.Area(p.Outer) > ca) { ca = Poly.Area(p.Outer); cut = Poly.EnsureCcw(Poly.Clean(p.Outer, 0.05f)); }
                    if (cut != null && cut.Length >= 3) lot = cut;
                }
                catch (Exception) { }
            }
            // The ground: the user wants it painted, not reshaped. Land that varies less than a metre under the lot is left exactly as it
            // is (no lot tiers, no sculpt round the lanes and plots); only a real slope gets the lot levelled.
            var parking = Mall.Parking(ctx);
            if (Relief(ctx, lot) < FlatRelief && StreetsLevel(ctx, mall)) { parking.Values["grading"] = false; mall.Grading = false; ctx.Plan.Facts["flatGround"] = 1; }
            var t = BestLot(mall, lot, entries, parking, Hashing.Mix(ctx.Seed, 0xC0Cu));
            // No car entrance yet (the preview before the player places one: Build waits for it) is fine; placed ones must reach.
            bool anyCars = false; foreach (var g in mall.Gates) if (g.Cars) anyCars = true;
            if (t == null || (anyCars && !cars && !LaneReached(mall))) return Fail(4);
            mall.CommitLot(t);
            mall.ReleaseRows();
            mall.LaneRows(lane, cells, null, +1, 0f, 0f, o.MaxPiece, 2);
            if (mall.Shops.Count == 0) return Fail(5);
            ctx.Plan.Facts[StripMallLayer.RowsFact] = 1;
            ctx.Plan.Facts["compactDepth"] = math.round(tl - tMin);
            return true;
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
