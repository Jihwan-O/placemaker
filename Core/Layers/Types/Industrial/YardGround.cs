// Industrial category, the ground (the user, 2026-10-19: "not just surface drawn ... surface painted elegantly, excellently.
// Dirt pile, dirt puddle, mud puddle, garbage, clean, dirty ... mound of dirt that's painted, certain sections of concrete,
// certain sections of dead trees"). A yard's ground tells where the work happens, so every mark has a reason:
//
//   * the terrain itself is painted (no flat surface polygon over the yard): worked earth or gravel over the yard, fading to
//     dry grass along the fence;
//   * mud where the wheels are: dark strips along both sides of every aisle, a mud patch at every aisle crossing and at the
//     gate apron;
//   * oil-soaked dark ground round the heaps, the mountain, the bays and the machines; the ground under a row of wrecks is
//     worn to dirt; concrete only where something heavy stands (stacks, bays, the plant, the office forecourt);
//   * spoil mounds: real terraformed mounds of dirt in the leftover corners, painted, with a dead tree on top, dead bushes
//     round it and a fallen log at the foot;
//   * decals placed on purpose (asset packs only, the clean game has none): puddles at the aisle crossings and along the
//     mud, an oil stain under every machine and parked lorry, litter round the skips, cracks on the concrete.
//
// Terrain paint is one 3.5 m texel per cell of the city's splatmap, so every mark here is at least 4 m across.

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Types.Scrapyard;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Yards
{
    public static class YardGround
    {
        // Vanilla terrain materials (TerrainMaterials): 1 dry grass, 2 forest floor (the darkest: mud, oil), 3 dirt, 4 sand.
        public const int DryGrass = 1, Mud = 2, Dirt = 3, Gravel = 4;

        /// <summary>The painted yard: base, mud lanes and crossings, oily rings, worn ground under rows, grass at the fence.</summary>
        public static void Paint(IndustrialSite s, string ground, uint salt)
        {
            var ctx = s.Ctx;
            float a = s.Spec.Aisle;
            // Dry grass in the band along the fence (the yard's edge, where nobody drives).
            if (s.Spec.Edge >= 2f) TerrainPaint.Field(ctx, s.Region.Ring, DryGrass, 0.85f, 1.5f, 1.5f, null, 0f, 0f, 0f, Hashing.Mix(salt, 1u));
            if (ground != "concrete")
            {
                // The yard: worked earth or gravel, full strength a few metres inside the edge band.
                int baseLayer = ground == "dirt" ? Dirt : Gravel;
                TerrainPaint.Field(ctx, s.Inner.Ring, baseLayer, 1f, 4f, 2f, null, 0f, 0f, 0f, Hashing.Mix(salt, 2u));
                if (ground == "gravel")
                    // Gravel yards wear to dirt in the middle of the traffic.
                    TerrainPaint.Field(ctx, s.Inner.InsetVertices(math.min(12f, s.Spec.Edge + 6f)), Dirt, 0.45f, 8f, 4f, null, 0f, 0f, 0f, Hashing.Mix(salt, 3u));
            }
            // Mud along both sides of every aisle: a 4 m strip each side, darker at the crossings.
            int k = 0;
            foreach (var (p, q) in s.Aisles)
            {
                float len = math.distance(p, q);
                if (len < 4f) continue;
                var dir = (q - p) / len;
                var n = new float2(-dir.y, dir.x);
                foreach (float side in new[] { -1f, 1f })
                {
                    var c = (p + q) * 0.5f + n * side * (a * 0.5f + 1.6f);
                    var strip = LayerRegion.Rect(c, dir, len * 0.5f, 2.2f);
                    TerrainPaint.Field(ctx, Clip(s, strip), Mud, ground == "concrete" ? 0.35f : 0.7f, 1.6f, 1.2f, null, 0f, 0f, 0f, Hashing.Mix(salt, 10u + (uint)k++));
                }
            }
            foreach (var c in Crossings(s))
                TerrainPaint.Field(ctx, Clip(s, LeisureKit.Blob(c, a * 0.9f + 2f, Hashing.Mix(salt, 0x60u + (uint)k++), 14, 0.25f)), Mud, 0.9f, 3f, 2f, null, 0f, 0f, 0f, Hashing.Mix(salt, 0x61u + (uint)k));
            if (s.HasGate)
                TerrainPaint.Field(ctx, Clip(s, LeisureKit.Blob(s.W(0f, 3f), a * 0.8f + 2f, Hashing.Mix(salt, 0x6A7Eu), 14, 0.2f)), Mud, 0.8f, 2.5f, 1.5f, null, 0f, 0f, 0f, Hashing.Mix(salt, 0x6A7Fu));
            // Oil-soaked rings round what is worked hard.
            int m = 0;
            foreach (var (p, r) in s.Anchors)
            {
                if (m >= 40) break;
                TerrainPaint.Field(ctx, Clip(s, LeisureKit.Blob(p, r + 4f, Hashing.Mix(salt, 0x0110u + (uint)m), 16, 0.3f)), Mud, 0.85f, 3f, 2.5f, null, 0f, 0f, 0f, Hashing.Mix(salt, 0x0111u + (uint)m));
                m++;
            }
            // Wrecks and goods stand on ground worn to dirt (one patch per block, the block's own rectangle).
            foreach (var b in s.Blocks)
            {
                if (b.Use != "wrecks" && b.Use != "goods" && b.Use != "heaps" && b.Use != "skips") continue;
                var r = s.Rect(b.U0 - 1f, b.U1 + 1f, b.V0 - 0.5f, b.V1 + 0.5f);
                TerrainPaint.Field(ctx, Clip(s, r.Ring), b.Use == "heaps" ? Mud : Dirt, 0.75f, 2f, 1.5f, null, 0f, 0f, 0f, Hashing.Mix(salt, 0x0B0u + (uint)b.Index));
            }
        }

        private static IReadOnlyList<float2> Clip(IndustrialSite s, float2[] ring)
        {
            try
            {
                var pieces = Poly.Intersection(ring, s.Region.Ring);
                Region best = null;
                foreach (var r in pieces) if (r?.Outer != null && (best == null || r.Area > best.Area)) best = r;
                return best?.Outer ?? ring;
            }
            catch (System.Exception) { return ring; }
        }

        /// <summary>Where two aisles meet (centre points).</summary>
        public static List<float2> Crossings(IndustrialSite s)
        {
            var list = new List<float2>();
            for (int i = 0; i < s.Aisles.Count; i++)
                for (int j = i + 1; j < s.Aisles.Count; j++)
                {
                    var (a0, a1) = s.Aisles[i]; var (b0, b1) = s.Aisles[j];
                    if (Poly.SegmentIntersection(a0, a1, b0, b1, out var x, out _, out _)) list.Add(x);
                    else
                    {
                        // T-junction: one aisle ends on the other.
                        foreach (var e in new[] { b0, b1 })
                            if (math.sqrt(SegmentIndex.DistSq(e, a0, a1)) < s.Spec.Aisle * 0.6f) { list.Add(SegmentIndex.ClosestPoint(e, a0, a1)); break; }
                    }
                }
            return list;
        }

        // ───────────── spoil mounds ─────────────

        private static readonly YardItem DeadBush = new YardItem(AssetCategories.Bush, 1.8f, 1.8f, "FlowerBushWildDead02");
        private static readonly YardItem FallenLog = new YardItem(AssetCategories.Rock, 0.7f, 10.4f, "Log02", "Log05", "Log08");
        private static readonly AssetRef DeadTree = new AssetRef("");

        /// <summary>
        /// Spoil mounds in the leftover corners (where no block fits and nothing stands): a terraformed mound of dirt, painted
        /// dirt with dry grass round its foot, a dead tree on top, dead bushes on its flanks and a fallen log at the foot.
        /// Returns the mounds for the sculptor.
        /// </summary>
        public static List<Mound> Spoil(IndustrialSite s, int max, uint salt)
        {
            var mounds = new List<Mound>();
            if (max <= 0) return mounds;
            var ctx = s.Ctx;
            var picked = new List<float2>();
            float front = s.Spec.Front;
            var cands = YardKit.Shuffled(Scatter.Candidates(s.Inner, 4f, Hashing.Mix(salt, 1u), 900, ctx.Cancel), Hashing.Mix(salt, 2u));
            var spots = new List<(float2 p, float clear)>();
            foreach (var p in cands)
            {
                var f = s.F(p);
                if (f.y < front + s.Spec.Aisle && math.abs(f.x) < s.Spec.FrontZone + 4f) continue;   // never in the front yard
                float clear = math.min(s.Occ.Clearance(p, 14f), s.Inner.BoundaryDistance(p, 14f) + s.Spec.Edge * 0.6f);
                if (clear >= 6.5f) spots.Add((p, clear));
            }
            spots.Sort((x, y) => y.clear.CompareTo(x.clear));
            int n = 0;
            foreach (var (p, clear) in spots)
            {
                if (n >= max) break;
                bool near = false;
                foreach (var q in picked) if (math.distance(p, q) < 30f) { near = true; break; }
                if (near) continue;
                float r = math.min(clear - 1f, 11f);
                picked.Add(p);
                uint v = Hashing.Mix(salt, 0x5B01u + (uint)n);
                float h = math.clamp(r * 0.28f, 1.5f, 3.2f);
                mounds.Add(new Mound { Center = p, Radius = r, Height = h, Flat = 0.25f });
                s.Occ.Take(p, r, YardKit.KindStuff);
                // Paint: dirt on the mound, dry grass round its foot.
                TerrainPaint.Field(ctx, LeisureKit.Blob(p, r + 2.5f, Hashing.Mix(v, 1u), 16, 0.25f), DryGrass, 0.9f, 2.5f, 2f, null, 0f, 0f, 0f, Hashing.Mix(v, 2u));
                TerrainPaint.Field(ctx, LeisureKit.Blob(p, r * 0.85f, Hashing.Mix(v, 3u), 16, 0.3f), Dirt, 1f, 2.5f, 2f, null, 0f, 0f, 0f, Hashing.Mix(v, 4u));
                // The dead tree on top (tree age 4 = dead), dead bushes round the flanks, a fallen log at the foot.
                ctx.Object(p + new float2(Hashing.Unit(v) - 0.5f, Hashing.Unit(Hashing.Mix(v, 5u)) - 0.5f), Hashing.Unit(Hashing.Mix(v, 6u)) * 6.283f,
                    ObjectRole.Tree, DeadTree, v, PlannedAge.Dead);
                int bushes = 3 + (int)(Hashing.Unit(Hashing.Mix(v, 7u)) * 3f);
                for (int i = 0; i < bushes; i++)
                {
                    float ang = (i + Hashing.Unit(Hashing.Mix(v, 8u + (uint)i)) * 0.5f) / bushes * 6.283f;
                    var bp = p + new float2(math.cos(ang), math.sin(ang)) * r * 0.6f;
                    ctx.Object(bp, ang, ObjectRole.Bush, DeadBush.Pick(v), Hashing.Mix(v, 20u + (uint)i), -1, DeadBush.Category);
                }
                if (r >= 7f)
                {
                    var lp = p + new float2(math.cos(Hashing.Unit(v) * 6.283f), math.sin(Hashing.Unit(v) * 6.283f)) * (r * 0.95f);
                    var tan = math.normalizesafe(lp - p);
                    tan = new float2(-tan.y, tan.x);
                    if (s.Region.ContainsDisc(lp, 5.5f))
                        ctx.Object(lp, math.atan2(tan.x, tan.y), ObjectRole.Furniture, FallenLog.Pick(v), Hashing.Mix(v, 30u), -1, FallenLog.Category);
                }
                n++;
            }
            return mounds;
        }

        // ───────────── decals ─────────────

        /// <summary>
        /// Wear decals with a reason (asset packs only): puddles at the aisle crossings and every 18 m along the mud, an oil
        /// stain under each machine and lorry, litter round the skips, cracks on the concrete pads.
        /// </summary>
        public static int Decals(IndustrialSite s, uint salt)
        {
            var ctx = s.Ctx;
            int n = 0;
            float a = s.Spec.Aisle;
            void Put(float2 p, string cat, uint v)
            {
                if (!s.Region.ContainsDisc(p, 1f) || !s.ReservedOnly.Free(p, 0.6f, 0f, 0f)) return;
                ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 1u)) * 6.283f, ObjectRole.Decal, AssetMix.One(""), v, -1, cat);
                n++;
            }
            int k = 0;
            foreach (var c in Crossings(s))
                for (int i = 0; i < 2; i++)
                {
                    uint v = Hashing.Mix(salt, 0x7001u + (uint)k++);
                    var off = new float2(Hashing.Unit(v) - 0.5f, Hashing.Unit(Hashing.Mix(v, 2u)) - 0.5f) * a * 0.8f;
                    Put(c + off, AssetCategories.DecalPuddles, v);
                }
            foreach (var (p, q) in s.Aisles)
            {
                float len = math.distance(p, q);
                if (len < 20f) continue;
                var dir = (q - p) / len; var nrm = new float2(-dir.y, dir.x);
                int j = 0;
                for (float t = 12f; t < len - 8f; t += 18f, j++)
                {
                    uint v = Hashing.Mix(salt, 0x7100u + (uint)k++);
                    // On the aisle's edge, alternating sides: where the water stands at the kerb of a yard road.
                    Put(p + dir * t + nrm * ((j & 1) == 0 ? 1f : -1f) * (a * 0.5f - 0.6f), AssetCategories.DecalPuddles, v);
                }
            }
            foreach (var o in new List<PlannedObject>(ctx.Plan.Objects))
            {
                if (o.Role != ObjectRole.Furniture) continue;
                uint v = Hashing.Mix(salt, 0x7200u + (uint)k++);
                if (o.Category == AssetCategories.ScrapMachine || o.Category == AssetCategories.LogisticsTruck || (o.Category == AssetCategories.LandfillSkip && (k & 3) == 0))
                    Put(o.Position.xz, o.Category == AssetCategories.LandfillSkip ? AssetCategories.DecalLitter : AssetCategories.DecalDirt, v);
            }
            foreach (var b in s.Blocks)
                if (b.Use == "stacks" || b.Use == "bays" || b.Use == "plant")
                    for (int i = 0; i < 3; i++)
                    {
                        uint v = Hashing.Mix(salt, 0x7300u + (uint)(b.Index * 7 + i));
                        Put(s.W(math.lerp(b.U0, b.U1, Hashing.Unit(v)), b.AisleFront ? b.V0 + 0.8f : b.V1 - 0.8f), AssetCategories.DecalCracks, v);
                    }
            return n;
        }
    }
}
