// Placemaker "Zoning" category — the looks of an area's buildings as Placemaker types (GPL-3.0).
//
// Each look (Garden, Natural, Tidy, Yard, Meadow, Rustic, Coastal, Manicured; Core/Neighbours/PolishLooks) is a type in the
// Make row's Zoning category: its option rows are every setting the neighbourhood polish reads per house (reach and
// joining, fences, lawns and paint, ground, planting and features, paths and shops; PolishLooks.Knobs), its styles are
// Compact, Standard and Sprawling (and the player's own, as for every type). Planning counts the buildings in the area;
// Build (Game/Tool/PlacemakerToolSystem.Polish) polishes them with the look and the values the rows have, saved on each
// building (NeighbourPolishSystem).
// Zone this area (phase 2): the zone cells of the blocks in the area (open, visible, off the roads) get zones: up to three
// with shares (a zone type, or an exact zone prefab: any, mods' too), laid out in clumps, speckled, as smooth noise or in
// rows (patch size in cells), and a road rule (cells this deep along roads at least this wide get their own zone). The
// zoning is built as every build's (never over cells already zoned); the houses that grow there get the look (phase 3).

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
        public static readonly string[] Patterns = { "clumps", "speckled", "noise", "rows" };

        /// <summary>The zone a look zones by default (Yard: industry; every other look: low density homes).</summary>
        protected virtual string DefaultZone => LookId == "yard" ? ZoneKinds.Industrial : ZoneKinds.ResidentialLow;

        /// <summary>The type of a layer id, or null.</summary>
        public static PolishLookLayer Of(string layerId) => GeneratorRegistry.Get(layerId) as PolishLookLayer;

        protected override void DefineControls(ControlSet c)
        {
            // zone this area (first: the area's zoning, then how its buildings look): up to three zones with shares, a pattern, a road rule
            c.Toggle("zoneArea", false, ControlTier.Essential, "zone").Tip();
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
                c.Fixed("zone" + slot);
                c.Fixed("zoneAsset" + slot);
                c.Fixed("share" + slot);
            }
            c.Choice("zonePattern", "clumps", ControlTier.Essential, "zone", Patterns).Tip().When("zoneArea=true");
            c.Slider("zoneGrain", 6, 1, 40, 1, "count", ControlTier.More, "zone").Tip().When("zoneArea=true");
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
            ctx.Plan.Stats.Add(new PlanStat(StatBuildings, buildings, "count"));
            ctx.Plan.Facts["polishBuildings"] = buildings;
            if (ctx.B("zoneArea")) PlanZoning(ctx);
        }

        /// <summary>
        /// The open zone cells of the blocks in the area, each given a zone: the road rule first (cells this deep along a road
        /// at least this wide), else by the pattern's value at the cell, split by the zones' shares.
        /// </summary>
        private void PlanZoning(LayerContext ctx)
        {
            var site = ctx.Input.Site;
            var ring = ctx.LayerRegion.Ring;
            var zones = new List<(string Id, float Share)>();
            foreach (var slot in new[] { "A", "B", "C" })
            {
                float share = ctx.F("share" + slot);
                if (share <= 0f) continue;
                string asset = ctx.S("zoneAsset" + slot);
                string id = !string.IsNullOrEmpty(asset) ? asset : ZoneKinds.PrefabId(ctx.S("zone" + slot));
                zones.Add((id, share));
            }
            if (zones.Count == 0) return;
            float total = zones.Sum(z => z.Share);
            string pattern = ctx.S("zonePattern");
            float grain = math.max(1f, ctx.F("zoneGrain")) * ZoneGuard.Cell;
            uint seed = ctx.Input.Seed * 2654435761u + 0x2D0Eu;
            // roads wide enough for the road rule, as sampled centre lines
            bool roadRule = ctx.B("roadRule");
            string roadZone = ZoneKinds.PrefabId(ctx.S("roadZone"));
            float minWidth = ctx.F("roadMinWidth"), depth = math.max(1, ctx.I("roadDepth")) * ZoneGuard.Cell;
            var roads = new List<(float2[] Line, float Half)>();
            if (roadRule)
                foreach (var net in site.NearbyNets)
                {
                    if (net.Category != NetCategory.Road || net.Width < minWidth) continue;
                    var line = new float2[17];
                    for (int k = 0; k <= 16; k++) line[k] = Bezier(net, k / 16f);
                    roads.Add((line, net.Width * 0.5f));
                }
            Poly.Bounds(ring, out var rmin, out var rmax);
            var open = ZoneGuard.CellOpener(site.NearbyNets, rmin - 40f, rmax + 40f);
            var count = new Dictionary<string, int>();
            var done = new HashSet<long>();
            foreach (var g in site.ZoneGrids)
            {
                for (int y = 0; y < g.Size.y; y++)
                    for (int x = 0; x < g.Size.x; x++)
                    {
                        var c = ZoneGuard.CellCentre(g, x, y);
                        if (c.x < rmin.x || c.y < rmin.y || c.x > rmax.x || c.y > rmax.y) continue;
                        if (!Poly.Contains(ring, c) || !open(g, x, y)) continue;
                        long key = ((long)math.round(c.x * 2f) << 32) ^ (uint)(int)math.round(c.y * 2f);
                        if (!done.Add(key)) continue;   // overlapping blocks: one zone per spot
                        string zone = null;
                        if (roadRule)
                            foreach (var (line, half) in roads)
                                if (DistanceToLine(line, c) <= half + depth) { zone = roadZone; break; }
                        if (zone == null)
                        {
                            float v = PatternValue(pattern, c, g, y, grain, seed) * total;
                            foreach (var (id, share) in zones) { zone = id; if (v < share) break; v -= share; }
                        }
                        var sq = ZoneGuard.CellSquare(g, x, y);
                        ctx.Plan.Zoning.Add(new PlannedZoning { A = P(ctx, sq[0]), B = P(ctx, sq[1]), C = P(ctx, sq[2]), D = P(ctx, sq[3]), ZoneId = zone });
                        count[zone] = (count.TryGetValue(zone, out var n) ? n : 0) + 1;
                    }
            }
            int cells = count.Values.Sum();
            ctx.Plan.Stats.Add(new PlanStat(StatCells, cells, "count"));
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

        /// <summary>A value in [0, 1) at a cell: the same for every cell of a clump, random per cell (speckled), smooth
        /// (noise: a value noise of the patch size), or the same along a row of the block (rows).</summary>
        private static float PatternValue(string pattern, float2 c, ZoneGrid g, int row, float grain, uint seed)
        {
            switch (pattern)
            {
                case "speckled": return Rand(Cell(c, ZoneGuard.Cell), seed);
                case "rows":
                {
                    var d = math.normalizesafe(g.Direction);
                    return Rand(new int2((int)math.floor(math.dot(c, d) / grain), (int)math.floor(math.dot(c, new float2(d.y, -d.x)) / 1000f)), seed);
                }
                case "noise":
                {
                    var q = c / grain;
                    var i = (int2)math.floor(q);
                    var f = q - i;
                    f = f * f * (3f - 2f * f);
                    float a = Rand(i, seed), b = Rand(i + new int2(1, 0), seed), cc = Rand(i + new int2(0, 1), seed), dd = Rand(i + new int2(1, 1), seed);
                    float v = math.lerp(math.lerp(a, b, f.x), math.lerp(cc, dd, f.x), f.y);
                    // spread the smooth values back over [0, 1) so the shares hold
                    return math.saturate(0.5f + (v - 0.5f) * 1.8f) * 0.9999f;
                }
                default: return Rand(Cell(c, grain), seed);   // clumps
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
