// Placemaker neighbourhood polish — what a shop's or an office's outgrown land holds (GPL-3.0).
//
// A shop's extra land used to be one concrete slab out to its fence ("it's just kind of an empty field, it's weird"). Now,
// as the land behind real shops looks:
//   * a grass strip inside the fence (not along the street) with a row of street trees and bushes between them;
//   * near the shop: paved, a service corner by the building (dumpsters, a skip on a bigger lot, pallets, crates, cardboard,
//     a gas-bottle cage, a shed or a backup generator on a big one) and an overflow car park in marked rows, most bays with
//     a parked car or van (Placemaker's vehicle props; decoration: nobody drives in);
//   * further out (past shopFar of the lot): no concrete, the ground painted green with a few trees and bushes.
// Pieces are the house's own (StitchPlan.Paths and Bushes), made and kept like every garden piece.

using System;
using System.Collections.Generic;
using System.Linq;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Neighbours
{
    public static partial class YardStitch
    {
        // BushKinds of the shop yard (appended after the garden kinds: never renumber)
        private const int Dumpster2 = 29, Dumpster3 = 30, Skip = 31, Pallet3 = 32, CratePile3 = 33, Cardboard1 = 34, Cardboard3 = 35,
            GasCage = 36, ShedBig = 37, Container = 38, Generator = 39, StallLine = 40, FirstCar = 41, CarCount = 10, FirstVan = 51, VanCount = 3,
            FirstStreetTree = 54, StreetTreeCount = 3;

        /// <summary>An object's half size (local x, local z) for the yard's fitting.</summary>
        private static float2 HalfSize(int kind)
        {
            switch (kind)
            {
                case Dumpster2: return new float2(1.36f, 0.63f);
                case Dumpster3: return new float2(1.38f, 1.06f);
                case Skip: return new float2(3.16f, 1.17f);
                case 17: case 18: case Pallet3: return new float2(0.52f, 0.62f);
                case 19: case 20: case CratePile3: return new float2(0.97f, 0.7f);
                case Cardboard1: return new float2(0.71f, 0.61f);
                case Cardboard3: return new float2(1.08f, 0.62f);
                case GasCage: return new float2(0.52f, 0.51f);
                case ShedBig: return new float2(2.19f, 1.3f);
                case Container: return new float2(1.22f, 3.02f);
                case Generator: return new float2(2.37f, 1.26f);
                case StallLine: return new float2(1.6f, 3.1f);
                default: return kind >= FirstCar && kind < FirstVan + VanCount ? new float2(1.12f, 2.7f) : new float2(0.6f, 0.6f);
            }
        }

        /// <summary>The corners of an object's footprint (half size h, grown by m) at c turned by yaw (quaternion.RotateY).</summary>
        private static float2[] Footprint(float2 c, float2 h, float yaw, float m)
        {
            var rx = new float2(math.cos(yaw), -math.sin(yaw)) * (h.x + m);
            var rz = new float2(math.sin(yaw), math.cos(yaw)) * (h.y + m);
            return Ccw(new[] { c - rx - rz, c + rx - rz, c + rx + rz, c - rx + rz });
        }

        /// <summary>The yaw that turns local +z (or local +x) along the world direction d.</summary>
        private static float YawZ(float2 d) => math.atan2(d.x, d.y);
        private static float YawX(float2 d) => math.atan2(-d.y, d.x);

        private static bool InLand(List<Region> land, float2 p)
        {
            foreach (var r in land)
                if (Poly.Contains(r.Outer, p) && !r.Holes.Any(hl => Poly.Contains(hl, p))) return true;
            return false;
        }

        /// <summary>A footprint lies in the land (corners and centre), clear of what is built and of what was placed.</summary>
        private static bool Fits(float2[] fp, List<Region> land, Obstacle built, List<float2[]> taken)
        {
            foreach (var p in fp) if (!InLand(land, p)) return false;
            if (!InLand(land, Poly.Centroid(fp))) return false;
            foreach (var t in taken) if (Overlaps(fp, t)) return false;
            foreach (var r in built.Near(fp)) if (Overlaps(fp, r.Outer)) return false;
            return true;
        }

        private static void ShopYard(IReadOnlyList<StitchHouse> houses, StitchHouse h, float2[] plot, List<Region> spare, StitchPlan plan, StitchSettings s)
        {
            uint seed = Hash((ulong)h.Id * 0x51F15EEDUL ^ 0x5409UL);
            float greenW = K(h, s, "shopGreen", 3f), farD = K(h, s, "shopFar", 32f);
            var built = new Obstacle(houses);
            var lot = Ccw(Lot(h));
            var plotR = new Region(Ccw(plot));

            // the grass strip: the plot's outer band inside its fence, not along the street
            var land = spare;
            var greenLand = new List<Region>();
            if (greenW > 0.2f)
            {
                var inner = Poly.Offset(Ccw(plot), greenW).Select(r => new Region(Ccw(r))).ToList();
                var band = inner.Count > 0 ? Poly.Boolean(new[] { plotR }, inner, BoolOp.Difference) : new List<Region> { plotR };
                var street = Ccw(new[] { h.ToWorld(new float2(-2000f, h.HalfLot.y - greenW - 0.5f)), h.ToWorld(new float2(2000f, h.HalfLot.y - greenW - 0.5f)),
                    h.ToWorld(new float2(2000f, h.HalfLot.y + 2000f)), h.ToWorld(new float2(-2000f, h.HalfLot.y + 2000f)) });
                band = Poly.Boolean(band, new[] { new Region(street) }, BoolOp.Difference);
                greenLand = Poly.Boolean(spare, band, BoolOp.Intersection).Where(r => r.Area >= 6f).ToList();
                if (greenLand.Count > 0) land = Poly.Boolean(spare, greenLand, BoolOp.Difference);
            }
            // near the shop: paved; further out: green ground (only when there is a good piece of it)
            var near = land;
            var far = new List<Region>();
            var reach = Poly.Offset(lot, -farD, OffsetJoin.Round, 4f, 0.5f);
            if (reach.Count > 0)
            {
                var outer = Poly.Boolean(land, reach, BoolOp.Difference);
                if (outer.Sum(r => r.Area) >= K(h, s, "shopFarMin", 150f))
                {
                    far = outer.Where(r => r.Area >= 12f).ToList();
                    near = Poly.Boolean(land, reach, BoolOp.Intersection);
                }
            }
            near = near.Where(r => r.Area >= 10f).ToList();

            int paved = 0, grass = 0;
            foreach (var r in near)
            {
                var ring = Prepared(r.HasHoles ? Poly.CutHoles(r, 0f).First() : r.Outer);
                if (ring != null) { plan.Paths.Add((h.Id, ring, h.MainSurface)); paved++; }
            }
            foreach (var r in greenLand)
            {
                var ring = Prepared(r.HasHoles ? Poly.CutHoles(r, 0f).First() : r.Outer);
                if (ring != null) { plan.Paths.Add((h.Id, ring, "Grass Surface 01")); grass++; }
            }

            // what the lot's look leaves out stays out: planting (trees, bushes), features (the service corner, the car park), paint
            bool planting = K(h, s, "plant", 1f) > 0.5f, features = K(h, s, "features", 1f) > 0.5f, paint = K(h, s, "paint", 1f) > 0.5f;
            var taken = new List<float2[]>();
            int trees = planting ? StreetTrees(h, plot, greenLand, built, taken, plan, seed) : 0;
            int service = features ? ServiceCorner(h, near, built, taken, plan, seed) : 0;
            int stalls = 0, cars = 0;
            if (features)
                foreach (var r in near.OrderByDescending(r => r.Area)) Parking(h, r, built, taken, plan, seed, ref stalls, ref cars);
            int farTrees = GreenEnd(h, far, built, taken, plan, s, seed, planting, paint);
            plan.Notes.Add($"{h.Id}: shop yard: {paved} paved pieces ({near.Sum(r => r.Area):F0} m²), {grass} grass strips with {trees} trees, a service corner of {service}, {stalls} parking bays ({cars} cars), {far.Sum(r => r.Area):F0} m² green further out ({farTrees} trees)");
        }

        /// <summary>Street trees along the grass strip (one kind per shop, every 9 m), bushes between them.</summary>
        private static int StreetTrees(StitchHouse h, float2[] plot, List<Region> greenLand, Obstacle built, List<float2[]> taken, StitchPlan plan, uint seed)
        {
            if (greenLand.Count == 0) return 0;
            int kind = FirstStreetTree + (int)(seed % StreetTreeCount), trees = 0;
            var ring = Ccw(plot);
            for (int k = 0; k < ring.Length; k++)
            {
                var a = ring[k];
                var b = ring[(k + 1) % ring.Length];
                float len = math.distance(a, b);
                if (len < 6f) continue;
                var d = (b - a) / len;
                var n = new float2(-d.y, d.x);   // CCW ring: the inside is on the left
                for (float t = 4.5f; t < len - 3f; t += 4.5f)
                {
                    var p = a + d * t + n * 1.5f;
                    if (!InLand(greenLand, p)) continue;
                    bool treeSpot = ((int)math.round(t / 4.5f) & 1) == 1;
                    var fp = Footprint(p, new float2(0.6f, 0.6f), 0f, treeSpot ? 0.4f : 0.1f);
                    if (built.Near(fp).Any(r => Overlaps(fp, r.Outer)) || taken.Any(x => Overlaps(fp, x))) continue;
                    uint hk = Hash(seed + (uint)k * 7919u + (uint)(t * 10f));
                    if (treeSpot) { plan.Bushes.Add((h.Id, p, (hk & 1023) / 1023f * 2f * math.PI, kind)); trees++; }
                    else if (hk % 100 < 60) plan.Bushes.Add((h.Id, p, (hk & 1023) / 1023f * 2f * math.PI, (int)(hk >> 10) % 2));
                    taken.Add(fp);
                }
            }
            return trees;
        }

        /// <summary>
        /// The service corner: at the roomiest spot of the paved land nearest the building, a row along the nearest edge of the
        /// land (bins, a skip, pallets, crates, cardboard, a gas cage; a shed, a container or a generator on a big lot).
        /// </summary>
        private static int ServiceCorner(StitchHouse h, List<Region> near, Obstacle built, List<float2[]> taken, StitchPlan plan, uint seed)
        {
            if (near.Count == 0) return 0;
            float area = near.Sum(r => r.Area);
            var target = h.Footprint != null && h.Footprint.Length >= 3 ? Poly.Centroid(h.Footprint) : h.Position;
            // the spot: 2.5 m clear of the land's edge, nearest the building
            float2 best = default;
            float bestD = float.MaxValue;
            Region home = null;
            foreach (var r in near)
            {
                Poly.Bounds(r.Outer, out var lo, out var hi);
                for (float x = lo.x + 0.5f; x < hi.x; x += 1f)
                    for (float y = lo.y + 0.5f; y < hi.y; y += 1f)
                    {
                        var p = new float2(x, y);
                        if (!Poly.Contains(r.Outer, p) || r.Holes.Any(hl => Poly.Contains(hl, p)) || DistanceToRing(r.Outer, p) < 2.5f) continue;
                        float dist = math.distance(p, target);
                        if (dist < bestD) { bestD = dist; best = p; home = r; }
                    }
            }
            if (home == null) return 0;
            // the nearest edge of that land: the row runs along it, its back a metre off it
            var ring = home.Outer;
            float edgeBest = float.MaxValue;
            float2 ea = ring[0], eb = ring[1];
            for (int k = 0; k < ring.Length; k++)
            {
                var a = ring[k];
                var b = ring[(k + 1) % ring.Length];
                if (math.distance(a, b) < 3f) continue;
                float dd = DistanceToSegment(best, a, b);
                if (dd < edgeBest) { edgeBest = dd; ea = a; eb = b; }
            }
            var dir = math.normalizesafe(eb - ea, h.Right);
            var nrm = new float2(-dir.y, dir.x);
            if (math.dot(best - ea, nrm) < 0f) nrm = -nrm;
            var foot = ea + dir * math.dot(best - ea, dir);
            var items = new List<int> { Dumpster3, Dumpster2 };
            if (area >= 400f) items.Add(Skip);
            items.AddRange(new[] { 17 + (int)(seed % 2), Pallet3, CratePile3, Cardboard3, Cardboard1, GasCage });
            if (area >= 700f)
                items.Add(h.Zone == "office" ? Generator : (seed >> 8) % 2 == 0 ? ShedBig : Container);
            // the row grows both ways from the spot, on the shorter side first; what does not fit is left out
            var placed = new List<(float2 At, float Yaw, int Kind, float2[] Fp)>();
            var reach = new[] { 0f, 0f };   // how far the row runs each way (+dir, -dir)
            foreach (int kind in items)
            {
                var half = HalfSize(kind);
                // long side along the row (a container stands along it too), its front (local +z, a bin's lid side) to the yard
                bool alongZ = half.y > half.x;
                float hl = alongZ ? half.y : half.x, hd = alongZ ? half.x : half.y;
                float yaw = alongZ ? YawX(nrm) : YawZ(nrm);
                bool done = false;
                foreach (int way in reach[0] <= reach[1] ? new[] { 0, 1 } : new[] { 1, 0 })
                {
                    float side = way == 0 ? 1f : -1f;
                    for (int tries = 0; tries < 3 && !done; tries++)
                    {
                        float t = reach[way] + tries * 0.6f;
                        var at = foot + dir * side * (t + hl) + nrm * (1f + hd);
                        var fp = Footprint(at, half, yaw, 0.15f);
                        if (!Fits(fp, near, built, taken) || placed.Any(x => Overlaps(fp, x.Fp))) continue;
                        placed.Add((at, yaw, kind, fp));
                        reach[way] = t + 2f * hl + 0.5f;
                        done = true;
                    }
                    if (done) break;
                }
            }
            if (placed.Count < 3) return 0;
            foreach (var (at, yaw, kind, _) in placed)
            {
                plan.Bushes.Add((h.Id, at, yaw, kind));
                // kept clear: a little room round the corner
                taken.Add(Footprint(at, HalfSize(kind), yaw, 1.2f));
            }
            return placed.Count;
        }

        /// <summary>An overflow car park in marked rows (3.3 m bays, 6 m deep, 6.5 m aisles) over one piece of paved land; most bays
        /// with a parked car or van. At least four bays, else none.</summary>
        private static void Parking(StitchHouse h, Region land, Obstacle built, List<float2[]> taken, StitchPlan plan, uint seed, ref int stalls, ref int cars)
        {
            if (land.Area < 80f) return;
            const float W = 3.3f, D = 6f, Aisle = 6.5f;
            // rows along the land's longer side (in the house's frame)
            var local = land.Outer.Select(h.ToLocal).ToArray();
            float2 lo = local.Aggregate(new float2(float.MaxValue), math.min), hi = local.Aggregate(new float2(float.MinValue), math.max);
            bool rowsAlongX = hi.x - lo.x >= hi.y - lo.y;
            var rAxis = rowsAlongX ? h.Right : h.Forward;     // along a row
            var dAxis = rowsAlongX ? h.Forward : h.Right;     // across rows (a bay's depth)
            float r0 = rowsAlongX ? lo.x : lo.y, r1 = rowsAlongX ? hi.x : hi.y, d0 = rowsAlongX ? lo.y : lo.x, d1 = rowsAlongX ? hi.y : hi.x;
            var one = new List<Region> { land };
            var bays = new List<(float2 At, float Yaw, bool Car, uint Hash)>();
            for (int row = 0; ; row++)
            {
                // rows 0,1 face one aisle, 2,3 the next (1 and 2 back to back)
                float start = d0 + 0.5f + row * D + ((row + 1) / 2) * Aisle;
                if (start + D > d1) break;
                bool aisleAfter = row % 2 == 0;
                for (float r = r0 + 0.5f; r + W <= r1; r += W)
                {
                    var l = new float2(r + W * 0.5f, start + D * 0.5f);
                    var at = rowsAlongX ? h.ToWorld(l) : h.ToWorld(new float2(l.y, l.x));
                    float yaw = YawZ(dAxis);
                    var fp = Footprint(at, new float2(W * 0.5f, D * 0.5f), yaw, 0f);
                    if (!Fits(fp, one, built, taken)) continue;
                    uint hk = Hash(seed + (uint)row * 104729u + (uint)(int)math.round(r * 10f));
                    bays.Add((at, aisleAfter ? yaw : yaw + math.PI, hk % 100 < 65, hk));
                }
            }
            if (bays.Count < 4) return;
            foreach (var (at, yaw, car, hk) in bays)
            {
                plan.Bushes.Add((h.Id, at, yaw, StallLine));
                taken.Add(Footprint(at, new float2(W * 0.5f, D * 0.5f), yaw, 0f));
                stalls++;
                if (!car) continue;
                int kind = (hk >> 8) % 100 < 15 ? FirstVan + (int)((hk >> 12) % VanCount) : FirstCar + (int)((hk >> 12) % CarCount);
                // nose to the aisle, a little off the bay's back
                var to = new float2(math.sin(yaw), math.cos(yaw));
                plan.Bushes.Add((h.Id, at + to * 0.2f, yaw, kind));
                cars++;
            }
        }

        /// <summary>The land further out: green ground (painted, lush with dry patches), a few trees with bushes round them.</summary>
        private static int GreenEnd(StitchHouse h, List<Region> far, Obstacle built, List<float2[]> taken, StitchPlan plan, StitchSettings s, uint seed, bool planting, bool paint)
        {
            int trees = 0;
            foreach (var r in far)
            {
                if (paint)
                    foreach (var piece in r.HasHoles ? Poly.CutHoles(r, 0f) : new List<float2[]> { r.Outer })
                    {
                        if (Poly.Area(piece) < 12f) continue;
                        Paint(plan, Poly.Simplify(piece, 0.3f), (int)s.Knob("lushLayer", 2f), K(h, s, "gardenPaint", 0.85f), 2f, 1.5f, 6f, seed ^ (uint)Poly.Area(piece));
                    }
                // a few trees at roomy spots, 9 m or more apart
                int want = planting ? (int)math.clamp(r.Area / 150f, 1f, 8f) : 0;
                Poly.Bounds(r.Outer, out var lo, out var hi);
                var spots = new List<float2>();
                float cell = math.max(1f, math.sqrt(r.Area) / 30f);
                for (float x = lo.x + cell * 0.5f; x < hi.x; x += cell)
                    for (float y = lo.y + cell * 0.5f; y < hi.y; y += cell)
                    {
                        var p = new float2(x, y);
                        if (!Poly.Contains(r.Outer, p) || r.Holes.Any(hl => Poly.Contains(hl, p)) || DistanceToRing(r.Outer, p) < 4f) continue;
                        spots.Add(p);
                    }
                int kind = FirstStreetTree + (int)((seed >> 4) % StreetTreeCount);
                foreach (var p in spots.OrderBy(q => Hash(seed ^ (uint)(int)math.round(q.x * 7f) ^ (uint)(int)math.round(q.y * 13f) * 31u)))
                {
                    if (trees >= want) break;
                    if (plan.Bushes.Any(b => b.House == h.Id && b.Kind >= FirstStreetTree && math.distance(b.Position, p) < 9f)) continue;
                    var fp = Footprint(p, new float2(0.6f, 0.6f), 0f, 0.5f);
                    if (built.Near(fp).Any(o => Overlaps(fp, o.Outer)) || taken.Any(x => Overlaps(fp, x))) continue;
                    uint hk = Hash(seed + (uint)trees * 31u);
                    plan.Bushes.Add((h.Id, p, (hk & 1023) / 1023f * 2f * math.PI, kind));
                    for (int k = 0; k < 2; k++)
                    {
                        float ang = ((hk >> (8 + k * 4)) & 15) / 15f * 2f * math.PI;
                        plan.Bushes.Add((h.Id, p + new float2(math.cos(ang), math.sin(ang)) * 2.6f, ang, (int)((hk >> (16 + k)) % 5)));
                    }
                    taken.Add(fp);
                    trees++;
                }
                // a worn patch or two of dry ground
                if (paint && r.Area > 200f)
                {
                    var c = spots.Count > 0 ? spots[(int)(seed % (uint)spots.Count)] : Poly.Centroid(r.Outer);
                    Paint(plan, Blob(c, 3f + (seed >> 6) % 3, seed, 10), 1, 0.55f, 1.8f, 1.2f, 3f, seed ^ 0xD27u);
                }
            }
            return trees;
        }
    }
}
