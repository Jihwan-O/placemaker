// Industrial category: how a block, the front yard and the edge are filled. Every filler lays its props on an exact grid
// (equal pitch, centred in the block, one kind per row or bay) and fills a block from the spine outward, so a part-full yard
// reads as a yard that is filling up, not as holes. Variety comes from choosing per row / per bay, never per prop.

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Types.Scrapyard;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Yards
{
    public static class YardFill
    {
        /// <summary>How many of n slots to use for a fill share (at least one).</summary>
        private static int Take(int n, float fill) => n <= 0 ? 0 : math.clamp((int)math.round(n * math.saturate(fill)), 1, n);

        /// <summary>Slot positions along U, centred in the block, ordered from the spine outward.</summary>
        private static List<float> Slots(YardBlock b, float pitch, float size, float endMargin)
        {
            var list = new List<float>();
            float usable = b.Length - 2f * endMargin;
            int n = (int)math.floor((usable - size) / pitch) + 1;
            if (usable < size || n <= 0) return list;
            float start = b.UMid - (n - 1) * pitch * 0.5f;
            for (int i = 0; i < n; i++) list.Add(start + i * pitch);
            list.Sort((x, y) => math.abs(x).CompareTo(math.abs(y)));
            return list;
        }

        // ───────────── rows ─────────────

        /// <summary>
        /// Props side by side across the block (long side across, nose to the aisle): two rows back to back when the block has
        /// aisles on both sides and room, else one row facing its aisle. <paramref name="perRow"/> picks the asset per row (one
        /// kind per row) or per slot when it returns a different pick for each.
        /// </summary>
        public static int RowsAcross(IndustrialSite s, YardBlock b, YardItem item, float gap, float fill, uint salt,
            float yawJitter = 0f, bool perSlot = false, float endMargin = 0.5f)
        {
            int placed = 0;
            var rows = new List<(float v, float2 face)>();
            bool two = b.Depth >= 2f * item.Z + 0.4f;
            if (two && b.AisleFront && b.AisleBack)
            {
                rows.Add((b.V0 + item.Z * 0.5f + 0.1f, -s.V));
                rows.Add((b.V1 - item.Z * 0.5f - 0.1f, s.V));
            }
            else if (two)
            {
                // One aisle: both rows face it (the second row stands behind the first).
                var face = b.AisleFront ? -s.V : s.V;
                rows.Add((b.V0 + item.Z * 0.5f + 0.1f, face));
                rows.Add((b.V1 - item.Z * 0.5f - 0.1f, face));
            }
            else rows.Add((b.AisleFront ? b.V0 + item.Z * 0.5f + 0.1f : b.V1 - item.Z * 0.5f - 0.1f, b.AisleFront ? -s.V : s.V));
            var slots = Slots(b, item.X + gap, item.X, endMargin);
            int use = Take(slots.Count, fill);
            for (int r = 0; r < rows.Count; r++)
            {
                uint rowPick = Hashing.Mix(salt, (uint)(b.Index * 7 + r));
                for (int i = 0; i < use; i++)
                {
                    uint v = Hashing.Mix(rowPick, (uint)i);
                    var asset = item.Pick(perSlot ? v : rowPick);
                    var face = rows[r].face;
                    if (yawJitter > 0f)
                    {
                        float a = (Hashing.Unit(Hashing.Mix(v, 9u)) - 0.5f) * 2f * yawJitter;
                        face = new float2(face.x * math.cos(a) - face.y * math.sin(a), face.x * math.sin(a) + face.y * math.cos(a));
                    }
                    if (s.Put(item, asset, s.W(slots[i], rows[r].v), face, v, 0.02f)) placed++;
                }
            }
            return placed;
        }

        /// <summary>Props end to end along the block in lines across its depth (long side along the aisle).</summary>
        public static int RowsAlong(IndustrialSite s, YardBlock b, YardItem item, float gapAlong, float gapAcross, float fill, uint salt,
            int maxLines = 99, bool perSlot = false)
        {
            int placed = 0;
            int lines = math.min(maxLines, (int)math.floor((b.Depth - 0.4f + gapAcross) / (item.X + gapAcross)));
            if (lines <= 0) return 0;
            float pitchV = item.X + gapAcross;
            // Lines start at the aisle side.
            bool fromFront = b.AisleFront || !b.AisleBack;
            var slots = Slots(b, item.Z + gapAlong, item.Z, 0.4f);
            int use = Take(slots.Count, fill);
            for (int l = 0; l < lines; l++)
            {
                float v = fromFront ? b.V0 + 0.2f + item.X * 0.5f + l * pitchV : b.V1 - 0.2f - item.X * 0.5f - l * pitchV;
                uint linePick = Hashing.Mix(salt, (uint)(b.Index * 13 + l));
                var dir = (l & 1) == 0 ? s.U : -s.U;
                for (int i = 0; i < use; i++)
                {
                    uint v2 = Hashing.Mix(linePick, (uint)i);
                    if (s.Put(item, item.Pick(perSlot ? v2 : linePick), s.W(slots[i], v), dir, v2, 0.02f)) placed++;
                }
            }
            return placed;
        }

        // ───────────── container stacks ─────────────

        /// <summary>
        /// Stacked container blocks along the block (local X along U), sizes chosen to fill its length, all of one height
        /// tier; the slots the fill leaves free get the painted slot grid. Concrete pads under the stacks.
        /// </summary>
        public static int Stacks(IndustrialSite s, YardBlock b, int tier, float fill, uint salt, bool paint)
        {
            int made = 0;
            const float gap = 2.2f;
            float z = IndustrialAssets.Stacks.Z(0, tier);
            if (b.Depth < IndustrialAssets.Stacks.LowZ + 0.4f) return 0;
            // Largest first, then fill what is left with smaller ones.
            var sizes = new List<int>();
            float left = b.Length - 0.3f;
            foreach (int size in new[] { 2, 1, 0 })
                while (left >= IndustrialAssets.Stacks.X(size)) { sizes.Add(size); left -= IndustrialAssets.Stacks.X(size) + gap; }
            if (sizes.Count == 0) return 0;
            // Big ones in the middle of the run (symmetric skyline).
            sizes.Sort((x, y) => y.CompareTo(x));
            var order = new List<int>();
            for (int i = 0; i < sizes.Count; i++) { if ((i & 1) == 0) order.Add(sizes[i]); else order.Insert(0, sizes[i]); }
            float total = -gap;
            foreach (var sz in order) total += IndustrialAssets.Stacks.X(sz) + gap;
            float u = b.UMid - total * 0.5f;
            int use = Take(order.Count, fill);
            // Fill from the spine side.
            var idx = new List<int>();
            for (int i = 0; i < order.Count; i++) idx.Add(i);
            float uu = u;
            var centres = new List<float>();
            foreach (var sz in order) { centres.Add(uu + IndustrialAssets.Stacks.X(sz) * 0.5f); uu += IndustrialAssets.Stacks.X(sz) + gap; }
            idx.Sort((x, y) => math.abs(centres[x]).CompareTo(math.abs(centres[y])));
            var face = b.AisleFront ? -s.V : s.V;
            for (int k = 0; k < idx.Count; k++)
            {
                int i = idx[k];
                int sz = order[i];
                float zz = IndustrialAssets.Stacks.Z(sz, tier);
                if (zz > b.Depth - 0.3f) zz = IndustrialAssets.Stacks.LowZ;
                float vc = b.AisleFront ? b.V0 + zz * 0.5f + 0.15f : b.V1 - zz * 0.5f - 0.15f;
                var c = s.W(centres[i], vc);
                int useTier = zz < IndustrialAssets.Stacks.TallZ - 0.1f ? 0 : tier;
                var item = new YardItem(AssetCategories.ScrapContainer, IndustrialAssets.Stacks.X(sz), zz, IndustrialAssets.Stacks.PivotZ(sz, useTier));
                if (k < use)
                {
                    var asset = IndustrialAssets.Stacks.Ref(sz, useTier);
                    s.Pad(new OrientedRect { Center = c, Axis = s.U, HalfLength = item.X * 0.5f + 0.6f, HalfWidth = zz * 0.5f + 0.6f }, SurfaceRole.Paving, AssetCategories.SurfacePaving);
                    if (s.Put(item, asset, c, face, Hashing.Mix(salt, (uint)(b.Index * 31 + i)), 0.02f))
                    {
                        made++;
                        s.Containers += (int)math.round(item.X / 2.5f) * (tier == 0 ? 2 : tier == 1 ? 3 : 6);
                        s.Anchors.Add((c, item.X * 0.5f));
                    }
                }
                else if (paint)
                {
                    // An empty, painted slot: the yard is not full yet.
                    var grid = IndustrialAssets.SlotGrid;
                    for (float g = -item.X * 0.5f + grid.X * 0.5f; g <= item.X * 0.5f - grid.X * 0.5f + 0.01f; g += grid.X + 0.1f)
                        s.Put(grid, grid.Pick(0), c + s.U * g, s.V, Hashing.Mix(salt, (uint)i), 0f, false, ObjectRole.Decal);
                }
            }
            return made;
        }

        // ───────────── bays ─────────────

        /// <summary>
        /// Material bays along the back of the block: concrete-block walls (back and sides, open to the aisle), each bay holding
        /// one material. <paramref name="fillBay"/>(bay index, centre, face toward the aisle, inner width, inner depth) fills it.
        /// </summary>
        public static int Bays(IndustrialSite s, YardBlock b, float bayWidth, float fill, uint salt,
            System.Action<int, float2, float2, float, float> fillBay)
        {
            // The bays open to the block's aisle (the front one when it has both).
            bool openFront = b.AisleFront;
            float depth = math.min(b.Depth - 0.4f, 14f);
            const float wall = 0.65f;
            int n = (int)math.floor((b.Length - 1f - wall) / (bayWidth + wall));
            if (n <= 0 || depth < 5f) return 0;
            int use = Take(n, fill);
            float total = n * (bayWidth + wall) + wall;
            float u0 = b.UMid - total * 0.5f;
            // Nothing behind the block (the back fence): the bays lean on it, with a paved apron in front of their mouths.
            bool againstBack = openFront ? !b.AisleBack : !b.AisleFront;
            float vBack = openFront ? (againstBack ? b.V1 - 0.1f : b.V0 + depth) : (againstBack ? b.V0 + 0.1f : b.V1 - depth);
            float sign = openFront ? -1f : 1f;   // from the back wall toward the opening, in V
            var face = openFront ? -s.V : s.V;
            // Fill from the spine side: pick the bays nearest it.
            var order = new List<int>();
            for (int i = 0; i < n; i++) order.Add(i);
            order.Sort((x, y) => math.abs(u0 + wall + (x + 0.5f) * (bayWidth + wall)).CompareTo(math.abs(u0 + wall + (y + 0.5f) * (bayWidth + wall))));
            var used = new HashSet<int>(order.GetRange(0, use));
            int lo = n, hi = -1;
            foreach (var i in used) { lo = math.min(lo, i); hi = math.max(hi, i); }
            // Back wall over the used bays; side walls between them, 3/4 deep.
            float uA = u0 + lo * (bayWidth + wall), uB = u0 + (hi + 1) * (bayWidth + wall) + wall;
            float vw = vBack + (openFront ? -wall * 0.5f : wall * 0.5f);
            s.BlockWall(s.W(uA, vw), s.W(uB, vw), Hashing.Mix(salt, 0xBAC0u));
            for (int i = lo; i <= hi + 1; i++)
            {
                float uw = u0 + i * (bayWidth + wall) + wall * 0.5f;
                float vEnd = vBack + sign * depth * 0.75f;
                s.BlockWall(s.W(uw, vw + sign * wall * 0.5f), s.W(uw, vEnd), Hashing.Mix(salt, 0xBAC1u + (uint)i));
            }
            if (againstBack && b.Depth - depth > 3f)
            {
                float mouth = vBack + sign * depth;
                s.Pad(s.Rect(uA, uB, math.min(mouth, openFront ? b.V0 + 0.1f : b.V1 - 0.1f), math.max(mouth, openFront ? b.V0 + 0.1f : b.V1 - 0.1f)),
                    SurfaceRole.Paving, AssetCategories.SurfacePaving);
            }
            int made = 0;
            foreach (var i in used)
            {
                float uc = u0 + wall + (i + 0.5f) * (bayWidth + wall) - wall * 0.5f;
                float innerDepth = depth - wall;
                var c = s.W(uc, vBack + sign * (wall + innerDepth * 0.5f));
                s.Pad(new OrientedRect { Center = c, Axis = s.U, HalfLength = bayWidth * 0.5f, HalfWidth = innerDepth * 0.5f }, SurfaceRole.Paving, AssetCategories.SurfacePaving);
                fillBay?.Invoke(i, c, face, bayWidth - 0.4f, innerDepth - 0.4f);
                s.Anchors.Add((c, bayWidth * 0.5f));
                made++;
            }
            s.Bays += made;
            return made;
        }

        /// <summary>A bay (or any rectangle) filled with a grid of small goods of one kind, from the back wall forward.</summary>
        public static int Grid(IndustrialSite s, float2 c, float2 face, float width, float depth, YardItem item, float gap, float fill, uint salt,
            bool perSlot = false)
        {
            var side = new float2(face.y, -face.x);
            int nx = (int)math.floor((width + gap) / (item.X + gap));
            int nz = (int)math.floor((depth + gap) / (item.Z + gap));
            if (nx <= 0 || nz <= 0) return 0;
            int rows = math.max(1, (int)math.ceil(nz * math.saturate(fill)));
            int placed = 0;
            uint pick = Hashing.Mix(salt, 0x6A1Du);
            for (int r = 0; r < rows; r++)
            {
                // r = 0 is against the back wall.
                float z = -depth * 0.5f + item.Z * 0.5f + r * (item.Z + gap);
                for (int k = 0; k < nx; k++)
                {
                    float x = (k - (nx - 1) * 0.5f) * (item.X + gap);
                    uint v = Hashing.Mix(salt, (uint)(r * 97 + k));
                    if (s.Put(item, item.Pick(perSlot ? v : pick), c - face * z + side * x, face, v, 0.02f)) placed++;
                }
            }
            return placed;
        }

        /// <summary>One big heap centred in a bay or block, its long side across the bay when it fits better.</summary>
        public static bool Heap(IndustrialSite s, float2 c, float2 face, float width, float depth, YardItem item, uint salt)
        {
            float2 dir = face;
            if (item.Z > depth && item.X <= depth && item.Z <= width) dir = new float2(face.y, -face.x);
            else if (item.Z > depth || item.X > width)
            {
                // Too big for the bay: the next smaller metal heap.
                if (item.Category != IndustrialAssets.MetalHeap.Category) return false;
                item = IndustrialAssets.MetalHeap;
                if (item.Z > depth || item.X > width) return false;
            }
            if (!s.Put(item, item.Pick(salt), c, dir, salt, -0.2f)) return false;
            s.Heaps++;
            return true;
        }

        // ───────────── heaps, carports, plant ─────────────

        /// <summary>Big heaps of one material side by side along the block, evenly spaced.</summary>
        public static int HeapRow(IndustrialSite s, YardBlock b, YardItem item, float gap, float fill, uint salt)
        {
            // Long side across the block when it fits, else along it.
            bool across = item.Z <= b.Depth - 0.6f;
            float along = across ? item.X : item.Z, deep = across ? item.Z : item.X;
            if (deep > b.Depth - 0.4f) return 0;
            var slots = Slots(b, along + gap, along, 0.8f);
            int use = Take(slots.Count, fill);
            int made = 0;
            float v = b.AisleFront ? b.V1 - deep * 0.5f - 0.3f : b.V0 + deep * 0.5f + 0.3f;
            for (int i = 0; i < use; i++)
            {
                uint p = Hashing.Mix(salt, (uint)(b.Index * 11 + i));
                var dir = across ? (b.AisleFront ? -s.V : s.V) : s.U;
                if (s.Put(item, item.Pick(p), s.W(slots[i], v), dir, p, -0.1f)) { made++; s.Heaps++; s.Anchors.Add((s.W(slots[i], v), along * 0.5f)); }
            }
            return made;
        }

        /// <summary>Carports facing the aisle with a car under each (dismantling bays), drums between them.</summary>
        public static int Carports(IndustrialSite s, YardBlock b, float fill, uint salt, YardItem car)
        {
            var port = IndustrialAssets.Carport;
            if (b.Depth < port.Z + 0.4f) return 0;
            const float gap = 1.8f;
            var slots = Slots(b, port.X + gap, port.X, 0.6f);
            int use = Take(slots.Count, fill);
            var face = b.AisleFront ? -s.V : s.V;
            float v = b.AisleFront ? b.V0 + port.Z * 0.5f + 0.2f : b.V1 - port.Z * 0.5f - 0.2f;
            int made = 0;
            for (int i = 0; i < use; i++)
            {
                uint p = Hashing.Mix(salt, (uint)(b.Index * 17 + i));
                var c = s.W(slots[i], v);
                s.Pad(new OrientedRect { Center = c, Axis = s.U, HalfLength = port.X * 0.5f + 0.3f, HalfWidth = port.Z * 0.5f + 0.3f }, SurfaceRole.Paving, AssetCategories.SurfacePaving);
                // The carport is a roof: the car stands under it (placed first, the roof does not check the car).
                if (!s.Put(car, car.Pick(p), c, face, p, 0.02f)) continue;
                s.Put(port, port.Pick(salt), c, face, p, 0f, false);
                s.Wrecks++;
                made++;
                // Drums or a gas pallet in the gap toward the outer end.
                if (i + 1 < use || i == 0)
                {
                    float gu = slots[i] + math.sign(slots[i] == 0f ? 1f : slots[i]) * (port.X * 0.5f + gap * 0.5f);
                    var item = (i & 1) == 0 ? IndustrialAssets.Drums : IndustrialAssets.Gas;
                    s.Put(item, item.Pick(p), s.W(gu, v + (b.AisleFront ? 1.5f : -1.5f)), face, Hashing.Mix(p, 3u), 0.02f);
                }
            }
            return made;
        }

        /// <summary>
        /// A small process plant, symmetric about the block's middle: the plant block in the centre, a tank each side, the
        /// chimney behind, a conveyor out to the aisle, all on one concrete pad.
        /// </summary>
        public static bool Plant(IndustrialSite s, YardBlock b, uint salt, bool tall)
        {
            var core = b.Depth >= IndustrialAssets.PlantSquare.Z + 1f && b.Length >= IndustrialAssets.PlantSquare.X + 26f ? IndustrialAssets.PlantSquare
                : IndustrialAssets.PlantSmall;
            var tank = IndustrialAssets.TankSmall;
            if (b.Length < core.X + 4f || b.Depth < core.Z + 1f) return false;
            var face = b.AisleFront ? -s.V : s.V;
            float vc = b.AisleFront ? b.V0 + core.Z * 0.5f + 0.8f : b.V1 - core.Z * 0.5f - 0.8f;
            float pad = math.min(b.Length - 0.6f, core.X + 2f * (tank.X + 3f) + 2f);
            s.Pad(s.Rect(b.UMid - pad * 0.5f, b.UMid + pad * 0.5f, b.V0 + 0.2f, b.V1 - 0.2f), SurfaceRole.Paving, AssetCategories.SurfacePaving);
            var c = s.W(b.UMid, vc);
            if (!s.Put(core, core.Pick(salt), c, face, salt, 0.05f)) return false;
            s.Anchors.Add((c, core.X * 0.5f));
            // Tanks either side (when there is room) and the chimney between plant and back wall.
            foreach (float side in new[] { -1f, 1f })
            {
                float tu = b.UMid + side * (core.X * 0.5f + 2.5f + tank.X * 0.5f);
                if (math.abs(tu - b.UMid) + tank.X * 0.5f > b.Length * 0.5f - 0.3f) continue;
                float tv = b.AisleFront ? b.V0 + tank.Z * 0.5f + 0.8f : b.V1 - tank.Z * 0.5f - 0.8f;
                if (tank.Z > b.Depth - 1f) continue;
                s.Put(tank, tank.Pick(Hashing.Mix(salt, 1u)), s.W(tu, tv), face, Hashing.Mix(salt, (uint)(2 + side)), 0.1f);
            }
            var stack = tall ? IndustrialAssets.StackLarge : IndustrialAssets.StackMedium;
            float behind = (b.AisleFront ? 1f : -1f) * (core.Z * 0.5f + stack.Z * 0.5f + 1.2f);
            var sc = s.W(b.UMid + core.X * 0.3f, vc + behind);
            if (s.Region.ContainsDisc(sc, 2f) && math.abs(vc + behind - b.VMid) < b.Depth * 0.5f) s.Put(stack, stack.Pick(salt), sc, face, Hashing.Mix(salt, 7u), 0.05f);
            else s.Put(IndustrialAssets.StackSmall, IndustrialAssets.StackSmall.Pick(salt), s.W(b.UMid + core.X * 0.5f + 1.6f, vc), face, Hashing.Mix(salt, 8u), 0.05f);
            s.Machines++;
            return true;
        }

        private static readonly YardItem BigMountain = new YardItem(AssetCategories.ScrapHeap, 16.6f, 24.3f, "PileOreMedium01|PileOreSmall01");

        /// <summary>
        /// The yard's centrepiece: one big tipped heap of metal in the middle of the block on a concrete pad, a loader parked
        /// at its foot facing it, and a line of open skips along the aisle side waiting to be filled.
        /// </summary>
        public static bool Mountain(IndustrialSite s, YardBlock b, uint salt, bool skips = true)
        {
            var face = b.AisleFront ? -s.V : s.V;
            var heap = BigMountain;
            bool across = heap.Z <= b.Depth - 4f && heap.X <= b.Length - 14f;
            bool along = !across && heap.Z <= b.Length - 14f && heap.X <= b.Depth - 4f;
            if (!across && !along) { heap = IndustrialAssets.MetalMountain; across = heap.Z <= b.Depth - 3f; along = !across && heap.X <= b.Depth - 3f && heap.Z <= b.Length - 8f; }
            if (!across && !along) return false;
            float deep = across ? heap.Z : heap.X, wide = across ? heap.X : heap.Z;
            float vc = b.AisleFront ? b.V1 - deep * 0.5f - 0.6f : b.V0 + deep * 0.5f + 0.6f;
            var c = s.W(b.UMid, vc);
            s.Pad(s.Rect(b.UMid - wide * 0.5f - 6f, b.UMid + wide * 0.5f + 6f, b.V0 + 0.2f, b.V1 - 0.2f), SurfaceRole.Paving, AssetCategories.SurfacePaving);
            if (!s.Put(heap, heap.Pick(salt), c, across ? face : s.U, salt, -0.2f)) return false;
            s.Heaps++;
            // The heap stands on a real mound: the mountain rises out of the yard, not off a flat floor.
            s.Mounds.Add(new Mound { Center = c, Radius = wide * 0.5f + 5f, Height = 2.2f, Flat = 0.55f });
            s.Anchors.Add((c, wide * 0.5f));
            // The loader at the heap's foot, on the spine side, nose to the heap.
            var loader = IndustrialAssets.Loader;
            float side = b.UMid >= 0f ? -1f : 1f;
            var lp = s.W(b.UMid + side * (wide * 0.5f + loader.X * 0.5f + 1.2f), vc);
            if (s.Put(loader, loader.Pick(salt), lp, -s.U * side, Hashing.Mix(salt, 2u), 0.05f) ||
                s.Put(loader, loader.Pick(salt), lp + face * 2f, -face, Hashing.Mix(salt, 3u), 0.05f)) s.Machines++;
            if (skips)
            {
                // Skips in a line between the heap and the aisle, long side along the aisle.
                var skip = IndustrialAssets.OpenSkip;
                float vs = b.AisleFront ? b.V0 + skip.Z * 0.5f + 0.4f : b.V1 - skip.Z * 0.5f - 0.4f;
                if (math.abs(vs - vc) > deep * 0.5f + skip.Z * 0.5f + 0.5f)
                {
                    int n = (int)math.floor((wide + 4f) / (skip.X + 0.8f));
                    for (int i = 0; i < n; i++)
                    {
                        float u = b.UMid + (i - (n - 1) * 0.5f) * (skip.X + 0.8f);
                        s.Put(skip, skip.Pick(salt), s.W(u, vs), face, Hashing.Mix(salt, 0x5C1u + (uint)i), 0.05f);
                    }
                }
            }
            return true;
        }

        // ───────────── front yard ─────────────

        /// <summary>What the front yard holds.</summary>
        public struct FrontSpec
        {
            public YardItem Office;       // the office kind (container office, mobile home, booth…)
            public int Offices;           // how many in the row
            public bool Booth;            // a gate booth by the barrier
            public bool Weighbridge;      // a weighbridge slab on the spine
            public bool Parking;          // staff/customer parking on the other side
            public bool Toilet, Flag;
            public YardItem ParkedCar;
        }

        /// <summary>
        /// The front yard: boom barrier and booth at the gate, the office row along the spine on the office side with a paved
        /// forecourt, toilet and bins at its end, the parking row on the other side, the weighbridge on the spine.
        /// </summary>
        public static void FrontYard(IndustrialSite s, FrontSpec f, uint salt)
        {
            var ctx = s.Ctx;
            float a = s.Spec.Aisle, front = s.Spec.Front;
            int os = s.OfficeSide, ps = -os;
            float2 toSpine(int side) => -s.U * side;
            // Spine apron: the gate mouth is paved a little wider than the spine.
            if (s.HasGate)
            {
                s.Pad(s.Rect(-a * 0.5f - 1.5f, a * 0.5f + 1.5f, -0.5f, 4f), SurfaceRole.Asphalt, AssetCategories.SurfaceAsphalt);
                // Boom barrier on the inbound lane's kerb, booth beside it.
                var boom = IndustrialAssets.Boom;
                s.Put(boom, boom.Pick(salt), s.W(os * (a * 0.5f + 0.5f), 5f), s.U * -os, Hashing.Mix(salt, 1u), 0.05f);
                if (f.Booth)
                {
                    var booth = IndustrialAssets.Booth;
                    // Long side along the spine, window to the lane.
                    var c = s.W(os * (a * 0.5f + 1.2f + booth.Z * 0.5f), 6.5f + booth.X * 0.5f);
                    if (s.Put(booth, booth.Pick(salt), c, toSpine(os), Hashing.Mix(salt, 2u), 0.1f)) s.Offices++;
                }
            }
            if (f.Weighbridge)
            {
                float v0 = math.min(front - 1f, 7f), v1 = math.min(front - 0.5f, v0 + 18f);
                if (v1 - v0 > 8f) s.Pad(s.Rect(-1.8f, 1.8f, v0, v1), SurfaceRole.Paving, AssetCategories.SurfacePaving);
            }
            // The office row: side by side along the spine behind a paved forecourt, each end-on to it (like portacabins).
            float rowStart = f.Booth ? 6.5f + IndustrialAssets.Booth.X + 1.5f : 4f;
            var office = f.Office;
            float back = a * 0.5f + 3.2f;   // forecourt between spine and offices
            float uRow = os * (back + office.Z * 0.5f);
            float pitch = office.X + 1.2f;
            float v = rowStart;
            if (f.Offices > 0 && office.Options != null)
            {
                int fit = math.min(f.Offices, (int)math.floor((front - 0.5f - rowStart + 1.2f) / pitch));
                if (fit > 0)
                    s.Pad(s.Rect(os > 0 ? a * 0.5f : -back - office.Z - 0.6f, os > 0 ? back + office.Z + 0.6f : -a * 0.5f, rowStart - 1f, rowStart + fit * pitch - 0.2f),
                        SurfaceRole.Paving, AssetCategories.SurfacePaving);
                for (int i = 0; i < fit; i++)
                {
                    var c = s.W(uRow, v + office.X * 0.5f);
                    // Long side across: local Z along U, the end toward the spine.
                    if (s.Put(office, office.Pick(Hashing.Mix(salt, 0x0FFu)), c, -s.U * os, Hashing.Mix(salt, 0x100u + (uint)i), 0.1f)) s.Offices++;
                    v += pitch;
                }
            }
            // Toilet, bins and a flag at the end of the row.
            if (f.Toilet && v + 1.5f <= front)
            {
                s.Put(IndustrialAssets.Toilet, IndustrialAssets.Toilet.Pick(salt), s.W(uRow, v + 0.8f), toSpine(os), Hashing.Mix(salt, 0x7011u), 0.1f);
                s.Put(IndustrialAssets.Toilet, IndustrialAssets.Toilet.Pick(salt), s.W(uRow + os * 1.6f, v + 0.8f), toSpine(os), Hashing.Mix(salt, 0x7012u), 0.1f);
            }
            s.Put(IndustrialAssets.Bin, IndustrialAssets.Bin.Pick(salt), s.W(os * (a * 0.5f + 1.2f), rowStart + 0.5f), toSpine(os), Hashing.Mix(salt, 0xB1Bu), 0.1f);
            if (f.Flag) s.Put(IndustrialAssets.Flag, IndustrialAssets.Flag.Pick(salt), s.W(os * (a * 0.5f + 1.2f), math.max(2.5f, rowStart - 2f)), s.V, Hashing.Mix(salt, 0xF1Au), 0.1f);

            // Parking: one row of stalls along the spine on the other side, cars nose in.
            if (f.Parking)
            {
                var car = f.ParkedCar;
                float stall = 2.9f, depth = 5.6f;
                float pv0 = 4.5f, pv1 = front - 0.8f;
                int n = (int)math.floor((pv1 - pv0) / stall);
                if (n >= 2)
                {
                    float u0 = ps * (a * 0.5f + 0.4f), u1 = ps * (a * 0.5f + 0.4f + depth);
                    s.Pad(s.Rect(math.min(u0, u1), math.max(u0, u1), pv0 - 0.3f, pv0 + n * stall + 0.3f), SurfaceRole.Asphalt, AssetCategories.SurfaceAsphalt);
                    for (int i = 0; i < n; i++)
                    {
                        uint p = Hashing.Mix(salt, 0xCA70u + (uint)i);
                        // Two in three stalls taken, in a fixed rhythm (never random holes).
                        if (i % 3 == 2) continue;
                        var c = s.W(ps * (a * 0.5f + 0.4f + depth * 0.5f), pv0 + (i + 0.5f) * stall);
                        if (s.Put(car, car.Pick(p), c, s.U * ps, p, 0.02f)) s.Parked++;
                    }
                    // Wheel stops: a block at the head of the row.
                    s.BlockWall(s.W(ps * (a * 0.5f + 0.4f + depth + 0.4f), pv0), s.W(ps * (a * 0.5f + 0.4f + depth + 0.4f), pv0 + n * stall), Hashing.Mix(salt, 0xCA7Bu));
                }
            }
        }

        // ───────────── edge ─────────────

        /// <summary>
        /// The tidy band inside the fence: "trees" (an even screen), "blocks" (a wall of concrete blocks), "containers" (a wall
        /// of 20 ft boxes along the back and sides, never along the street), "plain" (gravel only). Gaps at the gates.
        /// </summary>
        public static void Edge(IndustrialSite s, string mode, string[] treeIds, uint salt)
        {
            var region = s.Region;
            float e = s.Spec.Edge;
            if (mode == "plain" || e < 1.5f) return;
            if (mode == "trees")
            {
                YardKit.Screen(s.Ctx, s.Occ, s.Gates, math.min(e * 0.5f + 0.3f, 3.2f), 8f, treeIds, salt);
                return;
            }
            var item = mode == "containers" ? IndustrialAssets.Box20 : IndustrialAssets.Block;
            int n = region.Ring.Length;
            for (int i = 0; i < n; i++)
            {
                var a = region.Ring[i]; var b = region.Ring[(i + 1) % n];
                float len = math.distance(a, b);
                if (len < 4f) continue;
                var t = (b - a) / len;
                var inward = region.InwardNormal(i);
                // Containers stand along the back and the sides only: never facing the street.
                if (mode == "containers" && math.dot(inward, s.V) > 0.7f) continue;
                float off = mode == "containers" ? item.X * 0.5f + 0.6f : 0.9f;
                float along = mode == "containers" ? item.Z : item.X;
                float pitch = along + (mode == "containers" ? 0.35f : 0.05f);
                // Keep clear of the corners (the next edge's row) and of the gates.
                // (The next edge's row stands this deep inside the fence.)
                float corner = mode == "containers" ? item.X + 1.4f : 1.8f;
                float m0 = corner, m1 = len - corner;
                int k = (int)math.floor((m1 - m0 - along) / pitch) + 1;
                if (k <= 0) continue;
                float start = (m0 + m1) * 0.5f - (k - 1) * pitch * 0.5f;
                uint pick = Hashing.Mix(salt, (uint)i);
                for (int j = 0; j < k; j++)
                {
                    var p = a + t * (start + j * pitch) + inward * off;
                    bool nearGate = false;
                    foreach (var g in s.Gates) if (math.distance(g.Position, p) < s.Spec.Aisle * 0.5f + 3f + along * 0.5f) { nearGate = true; break; }
                    if (nearGate) continue;
                    uint v = Hashing.Mix(pick, (uint)j);
                    // Containers lie along the fence (local Z along the edge); blocks too (their long side is local X).
                    var dir = mode == "containers" ? t : inward;
                    if (s.Put(item, item.Pick(mode == "containers" ? v : pick), p, dir, v, 0.01f) && mode == "containers") s.Containers++;
                }
            }
        }

        /// <summary>Floodlight masts where the aisles meet the edge band, at the gate and at the end of the spine.</summary>
        public static int Lights(IndustrialSite s, uint salt)
        {
            var mast = IndustrialAssets.Flood;
            var spots = new List<float2>(s.AisleEnds);
            spots.Add(s.W(s.OfficeSide * (s.Spec.Aisle * 0.5f + 0.8f), 2.2f));
            spots.Add(s.W(-s.OfficeSide * (s.Spec.Aisle * 0.5f + 0.8f), s.SpineEnd + s.Spec.Aisle * 0.5f + 0.8f));
            int n = 0;
            for (int i = 0; i < spots.Count; i++)
                if (s.Region.ContainsDisc(spots[i], 0.8f) && s.Put(mast, mast.Pick(salt), spots[i], s.V, Hashing.Mix(salt, (uint)i), 0.05f)) n++;
            s.Lights += n;
            return n;
        }
    }
}
