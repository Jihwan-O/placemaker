// Placemaker "Zoning" category — the looks of an area's buildings as Placemaker types (GPL-3.0).
//
// Each look (Garden, Natural, Tidy, Yard, Meadow, Rustic, Coastal, Manicured; Core/Neighbours/PolishLooks) is a type in the
// Make row's Zoning category: its option rows are every setting the neighbourhood polish reads per house (reach and
// joining, fences, lawns and paint, ground, planting and features, paths and shops; PolishLooks.Knobs), its styles are
// Compact, Standard and Sprawling (and the player's own, as for every type). Planning counts the buildings in the area;
// Build (Game/Tool/PlacemakerToolSystem.Polish) polishes them with the look and the values the rows have, saved on each
// building (NeighbourPolishSystem). Nothing is built.

using System.Linq;
using Placemaker.Core.Geometry;
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

        /// <summary>The type of a layer id, or null.</summary>
        public static PolishLookLayer Of(string layerId) => GeneratorRegistry.Get(layerId) as PolishLookLayer;

        protected override void DefineControls(ControlSet c)
        {
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
        }
    }

    [AreaGenerator(10, Group = "zoning")] public sealed class PolishGardenLayer : PolishLookLayer { public override string LookId => "garden"; }
    [AreaGenerator(11, Group = "zoning")] public sealed class PolishNaturalLayer : PolishLookLayer { public override string LookId => "natural"; }
    [AreaGenerator(12, Group = "zoning")] public sealed class PolishTidyLayer : PolishLookLayer { public override string LookId => "tidy"; }
    [AreaGenerator(13, Group = "zoning")] public sealed class PolishYardLayer : PolishLookLayer { public override string LookId => "yard"; }
    [AreaGenerator(14, Group = "zoning")] public sealed class PolishMeadowLayer : PolishLookLayer { public override string LookId => "meadow"; }
    [AreaGenerator(15, Group = "zoning")] public sealed class PolishRusticLayer : PolishLookLayer { public override string LookId => "rustic"; }
    [AreaGenerator(16, Group = "zoning")] public sealed class PolishCoastalLayer : PolishLookLayer { public override string LookId => "coastal"; }
    [AreaGenerator(17, Group = "zoning")] public sealed class PolishManicuredLayer : PolishLookLayer { public override string LookId => "manicured"; }
}
