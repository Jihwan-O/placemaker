// Placemaker "Zoning" category — the looks of an area's buildings as Placemaker types (GPL-3.0).
//
// Each look (Garden, Natural, Tidy, Yard, Meadow, Rustic, Coastal, Manicured; Core/Neighbours/PolishLooks) is a type in the
// Make row's Zoning category: its option rows are every setting the neighbourhood polish reads per house (reach and
// joining, fences, lawns and paint, ground, planting and features, paths and shops; PolishLooks.Knobs), its styles are
// Compact, Standard and Sprawling (and the player's own, as for every type). Planning counts the buildings in the area;
// Build (Game/Tool/PlacemakerToolSystem.Polish) polishes them with the look and the values the rows have, saved on each
// building (NeighbourPolishSystem).
// Two tools in one, each with its switch: Zoning (zoneArea) and Shape (shapeLots, how the lots grow: the look's settings).
// Zoning: the blocks in the area are zoned as lots that start at their road: along each block, lot after lot, each a zone
// picked by the shares (a zone type, or an exact zone prefab: any, mods' too), its width and depth picked in that zone's
// ranges (cells; a single column left before a gap or the block's end joins the lot before it). A cell is only zoned
// when every cell between it and the road is (a zone behind another zone can never grow anything). Lots are mixed at
// random, in clumps (neighbouring lots alike) or spread evenly; the road rule zones the blocks of roads at least this wide
// with their own zone, this deep. The zoning is built as every build's (never over cells already zoned); with Shape on,
// the houses that grow there get the look.

using System.Collections.Generic;
using System.Linq;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.AddOns;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Neighbours;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Zoning
{
    public abstract class PolishLookLayer : LayerBase
    {
        public const string Prefix = "polish";
        public const string StatBuildings = "Placemaker.Polish.Stat.Buildings";

        /// <summary>The look this type is (PolishLooks.BuiltIn id).</summary>
        public abstract string LookId { get; }

        protected override string Area => "Polish";
        public override string Id => Prefix + char.ToUpperInvariant(LookId[0]) + LookId.Substring(1);
        public override string NameKey => "Placemaker.UI.Painter.Theme." + LookId;
        public override string Icon => "Media/Game/Icons/Zones.svg";
        public override LayerKind Kind => LayerKind.Fill;
        public override float MinArea => 20f;

        public const string StatCells = "Placemaker.Polish.Stat.Cells";
        public const string StatZone = "Placemaker.Polish.Stat.Zone.", StatExact = "Placemaker.Polish.Stat.ZoneExact";
        public static readonly string[] Patterns = { "mixed", "clumps", "even" };
        public const int MaxDepth = 6, MaxWidth = 12;

        /// <summary>The Zoning tool's settings (the panel's Zoning section); every other one is Shape's.</summary>
        public static readonly string[] ZoningIds =
        {
            "zoneArea", "zoneA", "zoneB", "zoneC", "zoneAssetA", "zoneAssetB", "zoneAssetC", "shareA", "shareB", "shareC",
            "widthMinA", "widthMaxA", "depthMinA", "depthMaxA", "widthMinB", "widthMaxB", "depthMinB", "depthMaxB",
            "widthMinC", "widthMaxC", "depthMinC", "depthMaxC", "zonePattern", "zoneGrain", "roadRule", "roadZone", "roadMinWidth", "roadDepth",
        };

        /// <summary>A lot's usual size by zone type (cells): width along the road, depth from it.</summary>
        public static (int W0, int W1, int D0, int D1) LotSize(string kind)
        {
            switch (kind)
            {
                case ZoneKinds.ResidentialLow: case ZoneKinds.LowRent: return (2, 3, 2, 4);
                case ZoneKinds.ResidentialRow: return (2, 2, 3, 4);
                case ZoneKinds.CommercialLow: return (2, 4, 2, 4);
                case ZoneKinds.Industrial: return (3, 6, 4, 6);
                default: return (2, 4, 3, 6);
            }
        }

        /// <summary>The zone a look zones by default (Yard: industry; every other look: low density homes).</summary>
        protected virtual string DefaultZone => LookId == "yard" ? ZoneKinds.Industrial : ZoneKinds.ResidentialLow;

        /// <summary>The type of a layer id, or null.</summary>
        public static PolishLookLayer Of(string layerId) => GeneratorRegistry.Get(layerId) as PolishLookLayer;

        protected override void DefineControls(ControlSet c)
        {
            // zone this area (first: the area's zoning, then how its buildings look): up to three zones with shares, a pattern, a road rule
            c.Toggle("zoneArea", true, ControlTier.Essential, "zone").Tip();
            foreach (var (slot, def, share) in new[] { ("A", DefaultZone, 100.0), ("B", ZoneKinds.CommercialLow, 0.0), ("C", ZoneKinds.OfficeLow, 0.0) })
            {
                var z = EdgeZoningAddOn.ZoneKindControl("zone" + slot, "zone", def);
                z.LabelKey = c.Key("zone" + slot);
                z.TooltipKey = z.LabelKey + ".Tooltip";
                z.Tier = slot == "A" ? ControlTier.Essential : ControlTier.More;
                z.VisibleWhen = "zoneArea=true";
                c.List.Add(z);
                c.Asset("zoneAsset" + slot, AssetCategories.Zone, ControlTier.More, "zone").Tip().When("zoneArea=true");
                c.Slider("share" + slot, share, 0, 100, 5, "%", slot == "A" ? ControlTier.Essential : ControlTier.More, "zone").Tip().When("zoneArea=true");
                var (w0, w1, d0, d1) = LotSize(def);
                c.Slider("widthMin" + slot, w0, 1, MaxWidth, 1, "count", ControlTier.More, "zone").Tip().When("zoneArea=true");
                c.Slider("widthMax" + slot, w1, 1, MaxWidth, 1, "count", ControlTier.More, "zone").Tip().When("zoneArea=true");
                c.Slider("depthMin" + slot, d0, 1, MaxDepth, 1, "count", ControlTier.More, "zone").Tip().When("zoneArea=true");
                c.Slider("depthMax" + slot, d1, 1, MaxDepth, 1, "count", ControlTier.More, "zone").Tip().When("zoneArea=true");
                foreach (var id in new[] { "zone", "zoneAsset", "share", "widthMin", "widthMax", "depthMin", "depthMax" }) c.Fixed(id + slot);
            }
            c.Choice("zonePattern", "mixed", ControlTier.Essential, "zone", Patterns).Tip().When("zoneArea=true");
            c.Slider("zoneGrain", 3, 1, 12, 1, "count", ControlTier.More, "zone").Tip().When("zonePattern=clumps");
            c.Toggle("roadRule", false, ControlTier.More, "zone").Tip().When("zoneArea=true");
            var rz = EdgeZoningAddOn.ZoneKindControl("roadZone", "zone", ZoneKinds.CommercialLow);
            rz.LabelKey = c.Key("roadZone");
            rz.TooltipKey = rz.LabelKey + ".Tooltip";
            rz.Tier = ControlTier.More;
            rz.VisibleWhen = "roadRule=true";
            c.List.Add(rz);
            c.Slider("roadMinWidth", 20, 6, 60, 1, "m", ControlTier.More, "zone").Tip().When("roadRule=true");
            c.Slider("roadDepth", 2, 1, 6, 1, "count", ControlTier.More, "zone").Tip().When("roadRule=true");
            foreach (var id in new[] { "zoneArea", "zonePattern", "zoneGrain", "roadRule", "roadZone", "roadMinWidth", "roadDepth" }) c.Fixed(id);
            // Shape: the lots grow by the look (its settings below); off: the area is only zoned
            c.Toggle("shapeLots", true, ControlTier.Essential, "shape").Tip();
            c.Fixed("shapeLots");
            var look = (PolishLooks.BuiltIn.FirstOrDefault(l => l.Id == LookId) ?? PolishLooks.BuiltIn[0]).Values();
            foreach (var k in PolishLooks.Knobs)
            {
                float def = look.TryGetValue(k.Id, out var v) ? v : 0f;
                ControlSchema cs;
                switch (k.Kind)
                {
                    case PolishLooks.KnobKind.Toggle: cs = c.Toggle(k.Id, def > 0.5f, k.Tier, k.Group); break;
                    case PolishLooks.KnobKind.Choice: cs = c.Choice(k.Id, ((int)math.round(def)).ToString(), k.Tier, k.Group, k.Options); break;
                    default: cs = c.Slider(k.Id, def, k.Min, k.Max, k.Step, k.Unit, k.Tier, k.Group); break;
                }
                cs.Tip();
                c.Fixed(k.Id);
            }
        }

        protected override void DefinePresets()
        {
            Preset("standard");
            // gardens that keep to themselves: small reach, no open land, lawn everywhere
            Preset("compact", ("sideReach", 30.0), ("maxFrontGap", 40.0), ("maxBackGap", 60.0), ("maxRearGap", 20.0), ("maxFacingGap", 16.0),
                ("maxDeepen", 16.0), ("hugReach", 16.0), ("openReach", 0.0), ("lawnReach", 1000.0));
            // gardens that take every bit of land around them
            Preset("sprawling", ("sideReach", 250.0), ("maxFrontGap", 200.0), ("maxBackGap", 300.0), ("maxRearGap", 150.0), ("maxFacingGap", 120.0),
                ("maxDeepen", 150.0), ("hugReach", 150.0), ("openReach", 80.0));
        }

        protected override void PlanCore(LayerContext ctx)
        {
            // the buildings standing in the area (their lot's middle): what Build polishes
            int buildings = 0;
            foreach (var o in ctx.Input.Site.Obstacles)
            {
                if (o.Kind != ObstacleKind.Building) continue;
                var mid = (o.A + o.B + o.C + o.D) * 0.25f;
                if (Poly.Contains(ctx.LayerRegion.Ring, mid)) buildings++;
            }
            if (ctx.B("shapeLots"))
            {
                ctx.Plan.Stats.Add(new PlanStat(StatBuildings, buildings, "count"));
                ctx.Plan.Facts["polishBuildings"] = buildings;
            }
            if (ctx.B("zoneArea")) PlanZoning(ctx);
        }

        private sealed class LotZone
        {
            public string Id, Stat;
            public float Share;
            public int W0, W1, D0, D1;
        }

        /// <summary>
        /// The blocks in the area zoned as lots that start at their road (see the file's header): per block, how deep each
        /// column can be zoned (open, inside the area, back from the road with no gap), then lot after lot along the block.
        /// </summary>
        private void PlanZoning(LayerContext ctx)
        {
            var site = ctx.Input.Site;
            var ring = ctx.LayerRegion.Ring;
            var zones = new List<LotZone>();
            foreach (var slot in new[] { "A", "B", "C" })
            {
                float share = ctx.F("share" + slot);
                if (share <= 0f) continue;
                string asset = ctx.S("zoneAsset" + slot);
                string kind = ctx.S("zone" + slot);
                int w0 = math.clamp(ctx.I("widthMin" + slot), 1, MaxWidth), d0 = math.clamp(ctx.I("depthMin" + slot), 1, MaxDepth);
                zones.Add(new LotZone
                {
                    Id = !string.IsNullOrEmpty(asset) ? asset : ZoneKinds.PrefabId(kind),
                    Stat = !string.IsNullOrEmpty(asset) ? StatExact : StatZone + kind,   // its line in Review
                    Share = share,
                    W0 = w0, W1 = math.clamp(ctx.I("widthMax" + slot), w0, MaxWidth),
                    D0 = d0, D1 = math.clamp(ctx.I("depthMax" + slot), d0, MaxDepth),
                });
            }
            if (zones.Count == 0) return;
            float total = zones.Sum(z => z.Share);
            string pattern = ctx.S("zonePattern");
            float clump = math.max(1, ctx.I("zoneGrain")) * 3f * ZoneGuard.Cell;   // about this many lots of 3 cells alike
            uint seed = ctx.Input.Seed * 2654435761u + 0x2D0Eu;
            // the road rule: roads at least this wide, as sampled centre lines; their blocks get its zone, this deep
            LotZone roadZone = null;
            var roads = new List<(float2[] Line, float Half)>();
            if (ctx.B("roadRule"))
            {
                string kind = ctx.S("roadZone");
                int depth = math.clamp(ctx.I("roadDepth"), 1, MaxDepth);
                roadZone = new LotZone { Id = ZoneKinds.PrefabId(kind), Stat = StatZone + kind, W0 = MaxWidth, W1 = MaxWidth, D0 = depth, D1 = depth };
                float minWidth = ctx.F("roadMinWidth");
                foreach (var net in site.NearbyNets)
                {
                    if (net.Category != NetCategory.Road || net.Width < minWidth) continue;
                    var line = new float2[17];
                    for (int k = 0; k <= 16; k++) line[k] = Bezier(net, k / 16f);
                    roads.Add((line, net.Width * 0.5f));
                }
            }
            Poly.Bounds(ring, out var rmin, out var rmax);
            var open = ZoneGuard.CellOpener(site.NearbyNets, rmin - 40f, rmax + 40f);
            var count = new Dictionary<string, (int Cells, string Stat)>();
            var done = new HashSet<long>();
            int lotIndex = 0;
            foreach (var g in site.ZoneGrids)
            {
                int sx = g.Size.x, sy = g.Size.y;
                if (sx <= 0 || sy <= 0) continue;
                // how deep each column can be zoned: from the road back, while open and inside the area
                var reach = new int[sx];
                bool any = false;
                for (int x = 0; x < sx; x++)
                {
                    int y = 0;
                    for (; y < sy; y++)
                    {
                        var c = ZoneGuard.CellCentre(g, x, y);
                        if (c.x < rmin.x || c.y < rmin.y || c.x > rmax.x || c.y > rmax.y || !Poly.Contains(ring, c) || !open(g, x, y)) break;
                    }
                    reach[x] = y;
                    any |= y > 0;
                }
                if (!any) continue;
                // a block along a wide road: the road rule's zone
                bool wide = false;
                if (roadZone != null)
                {
                    var mid = ZoneGuard.CellCentre(g, sx / 2, 0);
                    foreach (var (line, half) in roads)
                        if (DistanceToLine(line, mid) <= half + ZoneGuard.Cell) { wide = true; break; }
                }
                int x0 = 0;
                while (x0 < sx)
                {
                    if (reach[x0] == 0) { x0++; continue; }
                    var front = ZoneGuard.CellCentre(g, x0, 0);
                    uint lotSeed = math.hash(new uint3((uint)(int)math.round(front.x * 4f), (uint)(int)math.round(front.y * 4f), seed));
                    LotZone z;
                    if (wide) z = roadZone;
                    else
                    {
                        float v = PatternValue(pattern, front, lotIndex, clump, seed, lotSeed) * total;
                        z = zones[zones.Count - 1];
                        foreach (var cand in zones) { if (v < cand.Share) { z = cand; break; } v -= cand.Share; }
                    }
                    int w = z.W0 + (int)(((lotSeed >> 4) & 0xFFFF) % (uint)(z.W1 - z.W0 + 1));
                    int d = z.D0 + (int)(((lotSeed >> 20) & 0xFFF) % (uint)(z.D1 - z.D0 + 1));
                    // the lot runs to its width, the block's end or a column that cannot be zoned; a single column left
                    // before the next gap or the end joins it
                    int x1 = x0;
                    while (x1 < sx && x1 - x0 < w && reach[x1] > 0) x1++;
                    if (x1 < sx && reach[x1] > 0 && (x1 + 1 >= sx || reach[x1 + 1] == 0)) x1++;
                    for (int x = x0; x < x1; x++)
                        for (int y = 0; y < math.min(d, reach[x]); y++)
                        {
                            var c = ZoneGuard.CellCentre(g, x, y);
                            long key = ((long)math.round(c.x * 2f) << 32) ^ (uint)(int)math.round(c.y * 2f);
                            if (!done.Add(key)) continue;   // overlapping blocks: one zone per spot
                            var sq = ZoneGuard.CellSquare(g, x, y);
                            ctx.Plan.Zoning.Add(new PlannedZoning { A = P(ctx, sq[0]), B = P(ctx, sq[1]), C = P(ctx, sq[2]), D = P(ctx, sq[3]), ZoneId = z.Id });
                            count[z.Id] = (count.TryGetValue(z.Id, out var n) ? n.Cells + 1 : 1, z.Stat);
                        }
                    x0 = x1;
                    lotIndex++;
                }
            }
            int cells = count.Values.Sum(v => v.Cells);
            ctx.Plan.Stats.Add(new PlanStat(StatCells, cells, "count"));
            if (count.Count > 1)
                foreach (var kv in count.OrderByDescending(kv => kv.Value.Cells))
                    ctx.Plan.Stats.Add(new PlanStat(kv.Value.Stat, kv.Value.Cells, "count"));
            ctx.Plan.Facts["polishZoneCells"] = cells;
        }

        private static float3 P(LayerContext ctx, float2 p) => new float3(p.x, ctx.Input.Site.HeightAt != null ? ctx.Input.Site.HeightAt(p) : 0f, p.y);

        private static float2 Bezier(NearbyNet n, float t)
        {
            float u = 1f - t;
            var p = u * u * u * n.A + 3f * u * u * t * n.B + 3f * u * t * t * n.C + t * t * t * n.D;
            return p.xz;
        }

        private static float DistanceToLine(float2[] line, float2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                var a = line[i];
                var e = line[i + 1] - a;
                float t = math.saturate(math.dot(p - a, e) / math.max(1e-6f, math.dot(e, e)));
                best = math.min(best, math.distance(p, a + e * t));
            }
            return best;
        }

        /// <summary>A lot's value in [0, 1) that picks its zone by the shares: random per lot (mixed), the same for lots near
        /// each other (clumps), or spread evenly along the lots in turn (even: a golden-ratio sequence).</summary>
        private static float PatternValue(string pattern, float2 front, int lotIndex, float clump, uint seed, uint lotSeed)
        {
            switch (pattern)
            {
                case "clumps": return Rand(Cell(front, clump), seed);
                case "even": return math.frac(lotIndex * 0.6180339887f + (seed & 0xFFFF) / 65536f) * 0.9999f;
                default: return (lotSeed & 0xFFFFFF) / (float)0x1000000;   // mixed
            }
        }

        private static int2 Cell(float2 p, float size) => (int2)math.floor(p / size);

        private static float Rand(int2 cell, uint seed)
        {
            uint h = math.hash(new uint3((uint)cell.x, (uint)cell.y, seed));
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    [AreaGenerator(10, Group = "zoning", Loose = true)] public sealed class PolishGardenLayer : PolishLookLayer { public override string LookId => "garden"; }
    [AreaGenerator(11, Group = "zoning", Loose = true)] public sealed class PolishNaturalLayer : PolishLookLayer { public override string LookId => "natural"; }
    [AreaGenerator(12, Group = "zoning", Loose = true)] public sealed class PolishTidyLayer : PolishLookLayer { public override string LookId => "tidy"; }
    [AreaGenerator(13, Group = "zoning", Loose = true)] public sealed class PolishYardLayer : PolishLookLayer { public override string LookId => "yard"; }
    [AreaGenerator(14, Group = "zoning", Loose = true)] public sealed class PolishMeadowLayer : PolishLookLayer { public override string LookId => "meadow"; }
    [AreaGenerator(15, Group = "zoning", Loose = true)] public sealed class PolishRusticLayer : PolishLookLayer { public override string LookId => "rustic"; }
    [AreaGenerator(16, Group = "zoning", Loose = true)] public sealed class PolishCoastalLayer : PolishLookLayer { public override string LookId => "coastal"; }
    [AreaGenerator(17, Group = "zoning", Loose = true)] public sealed class PolishManicuredLayer : PolishLookLayer { public override string LookId => "manicured"; }
}
