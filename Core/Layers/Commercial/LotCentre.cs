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
            /// <summary>A square in the middle of the back row, shops either side of it facing the same lane: "plaza", "park", "garden", or null.</summary>
            public string Square;
        }

        public const string LotFact = "lotCentre";
        /// <summary>(shops4) No layout fits: the area is too shallow behind its entrances, or too narrow (an error: Build waits).</summary>
        public const string NoRoomKey = K + "Issue.NoRoom";
        /// <summary>(shops4) The site is too steep for the shops' lanes on the ground as it is (an error: Build waits).</summary>
        public const string TooSteepKey = K + "Issue.TooSteep";
        /// <summary>(shops4) A footpath entrance behind the row of shops: no walk reaches the parking from there.</summary>
        public const string FootpathBehindKey = K + "Issue.FootpathBehind";

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
            // On a steep site the lanes found no line across the slope gentle enough (the ground is not reshaped): say that instead.
            mall.Region.PrincipalAxes(out _, out _, out _, out float minor);
            float relief = Relief(ctx, mall.Region.Ring);
            if (relief / math.max(20f, minor) > 0.08f) ctx.Plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Error, Key = TooSteepKey, Args = new object[] { (int)math.round(100f * relief / math.max(20f, minor)) } });
            else ctx.Plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Error, Key = NoRoomKey, Args = new object[] { (int)math.ceil(need), (int)math.floor(have) } });
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
            // The frames ranked by a cheap estimate (no lot planned); the best two are planned and scored (shops and parking in
            // balance, every entrance connected), then the winner planned again for real. (Planning every frame fully ran past the
            // game's 12 s planning limit on a 300 x 280 m site.)
            var order = new List<(float est, int k)>();
            for (int k = 0; k < frames.Count; k++) { ctx.Check(); if (CompactIn(ctx, mall, frames[k], o, true, out float e)) order.Add((e, k)); }
            order.Sort((x, y) => y.est.CompareTo(x.est));
            var chosen = new HashSet<int>();
            for (int i = 0; i < order.Count && i < 2; i++) chosen.Add(order[i].k);
            for (int k = 0; k < frames.Count; k++)
            {
                ctx.Check();
                if (!chosen.Contains(k)) { tries.Add(-1f); continue; }
                float score = 0f; int lostMask = 0;
                if (CompactIn(ctx, mall, frames[k], o))
                {
                    // Every entrance the player placed must lead somewhere: a layout that leaves one unconnected loses to one that does not.
                    int lost = 0; foreach (var g in gates) if ((g.Cars || g.Kind == ShopEntrances.Footpath) && g.Landed == "none") { lost++; lostMask += 1 << g.Index; }
                    // Shops and parking in balance (the user: "maybe 50 % shops, 50 % parking lot"): all the ground they take, less half
                    // the difference between them (a stall with its share of aisle is about 26 m²).
                    float shops = mall.ShopArea(), lot = 26f * mall.Stalls;
                    score = math.max(1f, shops + lot - 0.5f * math.abs(shops - lot) - 100000f * lost);
                    // The best-ranked frame that connects every entrance is kept as it is (no second planning).
                    if (lost == 0 && k == order[0].k)
                    {
                        ctx.Plan.Facts["compactFrames"] = frames.Count; ctx.Plan.Facts["compactFrame"] = k;
                        return true;
                    }
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
        private static bool CompactIn(LayerContext ctx, Mall mall, StreetShops.Run r, LotLayout o) => CompactIn(ctx, mall, r, o, false, out _);

        /// <summary><paramref name="estimate"/>: only the best line's score (cheap: no lot planned), to rank the frames.</summary>
        private static bool CompactIn(LayerContext ctx, Mall mall, StreetShops.Run r, LotLayout o, bool estimate, out float est)
        {
            est = 0f;
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
                // Every line that holds a row, scored: shop ground and lot in balance, less the land left over behind the row (a
                // round site's rim holds a short row; deeper in, its chord is long); the best few are composed.
                var cand = new List<(float score, float tl, float a, float b, float2 gate)>();
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
                    float shops = (b - a) * d;
                    float lot = SliceArea(mall, r, sMin, sMax, tMin - 5f, tl - h);
                    float waste = SliceArea(mall, r, sMin, sMax, tl + back + 2f, tMax + 5f);
                    cand.Add((shops + lot - 0.5f * math.abs(shops - lot) - waste, tl, a, b, gate));
                }
                if (cand.Count == 0) continue;
                // The U (shops down the sides too) first, on the deepest line that holds a row: no land left behind it.
                var deep = cand[0];
                foreach (var c in cand) if (c.tl > deep.tl && float.IsNaN(c.gate.x)) deep = c;
                cand.Sort((x, y) => y.score.CompareTo(x.score));
                if (estimate) { est = cand[0].score; return true; }
                // (only where no double row fits: on a deeper site the double rows hold more shops)
                float island = 2f * (zo + math.min(cells, 3) * Mall.Cell) + 2f * h, lotMin = Mall.LotDepth(1) + 4f;
                bool rowsFit = (deep.tl - h - tMin) - lotMin >= lotMin + island;
                // (and on bigger sites only: on a city block its corners and street margins waste more than its side rows add)
                if (o.Square == null && !rowsFit && mall.Region.Area >= 22000f && ComposeU(ctx, mall, r, o, deep.tl, deep.a, deep.b, cells, h, asset, sMin, sMax, tMin)) return true;
                for (int c = 0; c < cand.Count && c < 3; c++)
                    if (Compose(ctx, mall, r, o, cand[c].tl, cand[c].a, cand[c].b, cells, h, asset, sMin, sMax, tMin)) return true;
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
            float island = 2f * (zo + math.min(cells, 3) * Mall.Cell) + 2f * h;   // the middle rows: shops three cells deep (more rows fit)
            // between double rows a band of lot one double aisle deep (the user: "maybe double the amount of shops")
            float lotMin = Mall.LotDepth(1) + 4f;
            float front = (tl - h) - tMin;
            int kMax = math.clamp((int)math.floor((front - lotMin) / (lotMin + island)), 0, 8);
            // too shallow for a double row: the U (shops along the sides too) when the site is wide enough
            for (int k = kMax; k >= 0; k--)
                if (ComposeRows(ctx, mall, r, o, tl, a, b, cells, h, asset, sMin, sMax, tMin, k)) return true;
            return false;
        }

        /// <summary>
        /// The U: shops along the back and down both sides, all facing the lot (the user: "maybe double the amount of shops"). One
        /// lane: up the left side, across the back, down the right side; its two ends stop a lot band short of the front, where the
        /// lot runs the whole width and the lane's ends meet its aisles. False when the sides have no room for a row and a lot between.
        /// </summary>
        private static bool ComposeU(LayerContext ctx, Mall mall, StreetShops.Run r, LotLayout o, float tl, float a, float b, int cells, float h, AssetRef asset,
            float sMin, float sMax, float tMin)
        {
            // The side rows are smaller shops (three cells deep, else two) so a city block holds a lot between them.
            for (int sc = math.min(cells, 3); sc >= 2; sc--)
                if (ComposeU(ctx, mall, r, o, tl, a, b, cells, sc, h, asset, sMin, sMax, tMin)) return true;
            return false;
        }

        private static bool ComposeU(LayerContext ctx, Mall mall, StreetShops.Run r, LotLayout o, float tl, float a, float b, int cells, int sideCells, float h, AssetRef asset,
            float sMin, float sMax, float tMin)
        {
            float d = cells * Mall.Cell, zo = Mall.ZoneOffset(h), ds = sideCells * Mall.Cell;
            float side = zo + ds + 1f;                // a side row's depth from its lane's centre line to its back
            float ty = tMin + Mall.LotDepth(1) + 4f + h;   // the legs' front ends: one band of lot across the whole front
            if (tl - ty < 3f * Mall.Cell + 2f * h + 4f) return false;
            // each side row as far out as it stays inside the site and clear of the side street's first zone rows
            bool SideFits(float sLane, float dir)
            {
                for (float tt = ty; tt <= tl; tt += 2f)
                {
                    var p = r.At(sLane - dir * side, tt);
                    if (!mall.Region.Contains(p) || mall.KerbMargin(p, r.In) > 0f) return false;
                }
                return true;
            }
            float a2 = float.NaN, b2 = float.NaN;
            for (float shift = 0f; shift <= 30f; shift += 2f) if (SideFits(a + side + shift, +1f)) { a2 = a + side + shift; break; }
            for (float shift = 0f; shift <= 30f; shift += 2f) if (SideFits(b - side - shift, -1f)) { b2 = b - side - shift; break; }
            // a court of lot 60 m wide at least between the side rows (narrower, the corners and margins waste more than the side rows add)
            if (float.IsNaN(a2) || float.IsNaN(b2) || b2 - a2 - 2f * h < 60f) return false;
            if (Steep(ctx, r.At(a2, ty), r.At(a2, tl)) || Steep(ctx, r.At(b2, ty), r.At(b2, tl)) || Steep(ctx, r.At(a2, tl), r.At(b2, tl))) return false;
            // an entrance beside a side row would be cut off by it: the plain row instead
            foreach (var g in mall.Gates)
            {
                float gs = math.dot(g.At - r.A, r.Dir), gt = math.dot(g.At - r.A, r.In);
                if (gt > ty - h - 2f && (gs < a2 - h || gs > b2 + h)) return false;
            }

            var planMark = PlanMark.Of(ctx.Plan); var mallMark = mall.Mark();
            bool Fail(int step) { planMark.Restore(ctx.Plan); mall.Rewind(mallMark); mall.RowHolds.Clear(); foreach (var g in mall.Gates) g.Landed = "none"; ctx.Plan.Facts["uFail"] = step; return false; }
            var lane = mall.Connect(Mall.End.Free(r.At(a2, ty)), new[] { r.At(a2, tl), r.At(b2, tl) }, Mall.End.Free(r.At(b2, ty)), NetRole.Alley, asset, h);
            if (lane == null) return Fail(1);
            for (int i = 0; i + 1 < lane.Points.Count; i++)
            {
                var f = Mall.Frame(lane.Points[i], lane.Points[i + 1]);
                float depth = i == 1 ? d : ds;
                for (float trim = h + 0.5f; trim <= h + 16f; trim += 2f)
                {
                    if (f.To - 2f * trim < 8f) break;
                    if (mall.HoldRow(Mall.Rect(f, trim, f.To - trim, h, zo + depth + 1f)) != null) break;
                }
            }
            // The lot: a band across the front, and the court between the legs up to the back lane.
            float2[] lot = null;
            try
            {
                var front = Mall.Rect(r, sMin - 5f, sMax + 5f, tMin - 5f, ty - h - 0.3f);
                var court = Mall.Rect(r, a2 + h + 0.3f, b2 - h - 0.3f, ty - h - 1f, tl - h - 0.3f);
                var union = Poly.Boolean(new[] { (IReadOnlyList<float2>)front }, new[] { (IReadOnlyList<float2>)court }, BoolOp.Union);
                if (union.Count == 1 && union[0].Holes.Count == 0) lot = Mall.Largest(Poly.Intersection(mall.Region.Ring, Poly.EnsureCcw(union[0].Outer)), 300f);
            }
            catch (Exception) { lot = null; }
            if (lot == null) return Fail(3);
            var entries = new List<(float2 at, string kind)>
            {
                // the lane's ends (straight down from them into the front band) and the back corners of the court
                (r.At(a2, ty - h - 0.3f), "alley"), (r.At(b2, ty - h - 0.3f), "alley"),
                (r.At(a2 + h + 0.3f, tl - h - 0.5f), "alley"), (r.At(b2 - h - 0.3f, tl - h - 0.5f), "alley"),
            };
            if (b2 - a2 > 160f) entries.Add((r.At(0.5f * (a2 + b2), tl - h - 0.3f), "alley"));
            var rest = new List<ShopEntrances.Gate>();
            bool cars = false;
            foreach (var g in mall.Gates)
            {
                if ((g.Cars || g.Kind == ShopEntrances.Footpath) && Poly.DistanceToBoundary(lot, g.At) < 1.5f && math.dot(g.At - r.A, r.In) < ty - h - 4f)
                { entries.Add((g.At, g.Kind)); g.Landed = "lot"; if (g.Cars) cars = true; }
                else rest.Add(g);
            }
            var all = mall.Gates;
            mall.Gates = rest;
            try { mall.RouteGates(); }
            finally { mall.Gates = all; }
            mall.Grading = false;
            lot = Minus(lot, mall.Taken);
            var parking = Mall.Parking(ctx);
            if (Relief(ctx, lot) < FlatRelief && StreetsLevel(ctx, mall)) { parking.Values["grading"] = false; ctx.Plan.Facts["flatGround"] = 1; }
            var t = BestLot(mall, lot, entries, parking, Hashing.Mix(ctx.Seed, 0xC1Cu));
            bool anyCars = false; foreach (var g in mall.Gates) if (g.Cars) anyCars = true;
            if (t == null || (anyCars && !cars && !LaneReached(mall))) return Fail(4);
            mall.CommitLot(t, true);
            WalksToTheLot(ctx, mall, t.Ring);
            ctx.Plan.Facts["gates"] = mall.Gates.Count;
            mall.ReleaseRows();
            mall.LaneRows(lane, cells, null, +1, 0f, 0f, o.MaxPiece, 2, i => i == 1);
            mall.LaneRows(lane, sideCells, null, +1, 0f, 0f, 3, 2, i => i != 1);
            if (mall.Shops.Count == 0) return Fail(5);
            ctx.Plan.Facts[StripMallLayer.RowsFact] = 3;
            ctx.Plan.Facts["uRow"] = 1;
            ctx.Plan.Facts["compactDepth"] = math.round(tl - tMin);
            return true;
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
            int ic = math.min(cells, 3); float di = ic * Mall.Cell;
            float span = 2f * (zo + di), island = span + 2f * h;
            // The ground is not reshaped, so the lanes lie on it as it is: none steeper than a road should be (across the slope instead).
            if (Steep(ctx, r.At(a, tl), r.At(b, tl))) return Fail(20);
            var lane = mall.Connect(Mall.End.Free(r.At(a, tl)), null, Mall.End.Free(r.At(b, tl)), NetRole.Alley, asset, h);
            if (lane == null) return Fail(1);
            // The square (an extra) takes the middle of the row: its land is kept before the row is held either side of it.
            float2[] square = null;
            if (o.Square != null && b - a >= 6f * Mall.Cell + 40f)
            {
                float w = math.clamp(0.3f * (b - a), 32f, 64f), mid = 0.5f * (a + b);
                square = mall.Hold(Mall.Rect(r, mid - 0.5f * w, mid + 0.5f * w, tl + h + 0.3f, tl + zo + d + 1f));
                if (mall.HoldRow(Mall.Rect(r, a + h + 0.5f, mid - 0.5f * w - 0.3f, tl + h, tl + zo + d + 1f)) == null) return Fail(2);
                if (mall.HoldRow(Mall.Rect(r, mid + 0.5f * w + 0.3f, b - h - 0.5f, tl + h, tl + zo + d + 1f)) == null) return Fail(2);
            }
            else if (mall.HoldRow(Mall.Rect(r, a + h + 0.5f, b - h - 0.5f, tl + h, tl + zo + d + 1f)) == null) return Fail(2);

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
                    if (ok) foreach (float t in new[] { y1 - h - 6f, y1, y1 + zo + di, y2, y2 + h + 6f }) if (!mall.Region.Contains(r.At(s, t)) || mall.KerbMargin(r.At(s, t), r.In) > 0f) { ok = false; break; }
                    if (ok) { if (float.IsNaN(ca)) ca = s; }
                    else if (!float.IsNaN(ca)) { if (float.IsNaN(ia) || s - ca > ib - ia) { ia = ca; ib = s - 1f; } ca = float.NaN; }
                }
                if (float.IsNaN(ia)) return Fail(10);
                // a planted end and room to walk round it, not a lane along the street
                ia += 8f; ib -= 8f;
                if (ib - ia < 6f * Mall.Cell + 2f * h) return Fail(11);
                if (Steep(ctx, r.At(ia, y1), r.At(ib, y1)) || Steep(ctx, r.At(ia, y2), r.At(ib, y2)) || Steep(ctx, r.At(ia, y1), r.At(ia, y2))) return Fail(21);
                var loop = mall.Loop(r.At(ia, y1), new[] { r.At(ib, y1), r.At(ib, y2), r.At(ia, y2) }, NetRole.Alley, asset, h);
                if (loop == null) return Fail(12);
                if (mall.HoldRow(Mall.Rect(r, ia + h + 0.5f, ib - h - 0.5f, y1 + h, y1 + zo + di)) == null) return Fail(13);
                if (mall.HoldRow(Mall.Rect(r, ia + h + 0.5f, ib - h - 0.5f, y2 - zo - di, y2 - h)) == null) return Fail(14);
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
                mall.CommitLot(t, true);   // (non-convex lots are kept as convex parts: a T kept whole blocked the side rows)
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
            if (square != null)
            {
                mall.Release(square);
                uint salt = Hashing.Mix(ctx.Seed, 0x5A0u);
                string layer = o.Square == "plaza" ? "plaza" : "park", preset = o.Square == "plaza" ? "cafe" : o.Square == "garden" ? "garden" : "pocket";
                // two ways in from the lane (a quarter in from each side): its walks run through it, never to a dead end
                float w2 = 0.5f * (b - a), mid2 = 0.5f * (a + b), sw = 0f;
                foreach (var v in square) sw = math.max(sw, math.abs(math.dot(v - r.A, r.Dir) - mid2));
                var doors = new List<(Mall.Lane, float2)> { (lane, r.At(mid2 - 0.5f * sw, tl)), (lane, r.At(mid2 + 0.5f * sw, tl)) };
                if (mall.FillOpen(square, layer, preset, doors, null, salt)) ctx.Plan.Facts["square"] = 1;
                else mall.Green(square, salt + 1u);
            }
            foreach (var (loop, _, _, _, _) in loops) rows += mall.LaneRows(loop, ic, null, +1, 0f, 0f, o.MaxPiece, 2, i => i % 2 == 0) > 0 ? 2 : 0;
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

        /// <summary>A lane on the ground as it is from a to b climbs more than 8 % over any 8 m.</summary>
        private static bool Steep(LayerContext ctx, float2 a, float2 b)
        {
            float len = math.distance(a, b);
            for (float u = 0f; u + 8f <= len; u += 4f)
            {
                var p = math.lerp(a, b, u / len); var q = math.lerp(a, b, (u + 8f) / len);
                if (math.abs(ctx.H(q) - ctx.H(p)) / 8f > 0.08f) return true;
            }
            return false;
        }

        /// <summary>Area (m²) of the site between t0 and t1 in frame <paramref name="r"/>.</summary>
        private static float SliceArea(Mall mall, StreetShops.Run r, float s0, float s1, float t0, float t1)
        {
            if (t1 <= t0) return 0f;
            try { float a = 0f; foreach (var p in Poly.Intersection(mall.Region.Ring, Mall.Rect(r, s0 - 5f, s1 + 5f, t0, t1))) if (p?.Outer != null) a += Poly.Area(p.Outer); return a; }
            catch (Exception) { return 0f; }
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
                // Clear of every other net (a walk ending on a lot's drive: the game clipped the ground under the two, a 4.6 m pit in the
                // game runner's circle): 4 m from any of them but where it leaves the sidewalk.
                foreach (var net in ctx.Plan.Nets)
                {
                    if (blocked) break;
                    for (float u = 0.25f; u <= 1.001f; u += 0.25f)
                        if (math.sqrt(SegmentIndex.DistSq(math.lerp(g.At, q, u), net.A.xz, net.D.xz)) < 4f) { blocked = true; break; }
                }
                if (blocked || mall.PathChain(new List<float2> { g.At, q }, -g.In, null) == null) continue;
                g.Landed = "walk";
                ctx.Plan.Issues.RemoveAll(i => i.Key == Mall.NoLaneKey && i.Args != null && i.Args.Length > 0 && i.Args[0] is int k && k == g.Index + 1);
            }
            // A footpath still without a way: behind the row of shops (no walk cuts through a shop). Say so plainly.
            foreach (var g in mall.Gates)
            {
                if (g.Kind != ShopEntrances.Footpath || g.Landed != "none") continue;
                ctx.Plan.Issues.RemoveAll(i => i.Key == Mall.NoLaneKey && i.Args != null && i.Args.Length > 0 && i.Args[0] is int k && k == g.Index + 1);
                ctx.Plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = FootpathBehindKey, Args = new object[] { g.Index + 1 }, At = ctx.P3(g.At) });
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
            foreach (bool ring in new[] { true, false })
                foreach (float rot in new[] { 0f, 90f })
                {
                    if (best != null) break;   // the lot's own drive round it, rows along the frame first; the others only when that fails
                    var bag = new SettingsBag();
                    foreach (var kv in parking.Values) bag.Values[kv.Key] = kv.Value;
                    bag.Values["setback"] = 0.5;
                    bag.Values["rowAngleMode"] = "fixed";
                    bag.Values["rowAngle"] = (double)(ang + rot);
                    bag.Values["perimeterRoad"] = ring;
                    var t = mall.PlanLot(lot, entries, bag, salt + (uint)rot + (ring ? 7u : 0u), true);
                    if (t == null || NetFit.Violations(t.Sub.Nets, t.Site).Count > 0 || Mall.Warns(t)) continue;
                    t.Sub.Issues.RemoveAll(i => i.Key != null && i.Key.EndsWith("PerimeterRoadDropped"));   // our choice to try, not the player's
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
