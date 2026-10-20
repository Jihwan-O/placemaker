// Industrial category: the container yard. Three ways to keep boxes, each laid out like the real thing:
//   terminal  stacked container blocks (the harbour's stacks) in long bands, two high at the street rising to six high at
//             the back, painted empty slots where the yard is not full, concrete throughout;
//   depot     a container depot on gravel: rows of 20 ft boxes doors-to-aisle, a band of two-high stacks, a band of 40 ft
//             boxes laid along the aisle, and a wall of boxes along the back and side fences;
//   storage   self storage: neat back-to-back rows of 20 ft units with doors on the drive aisles, customer parking at the
//             office, a screen of trees, everything square.

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Yards
{
    [AreaGenerator(64, Group = "industrial")]
    public sealed class ContainerYardLayer : IndustrialYardLayer
    {
        public const string LayerId = "containerYard";
        private static readonly string[] StyleIds = { "terminal", "depot", "storage" };

        protected override string Area => "ContainerYard";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/ZoneIndustrialWarehouses.svg";
        protected override string[] Styles => StyleIds;

        protected override void DefinePresets()
        {
            Preset("terminal", ("style", "terminal"), ("ground", "concrete"), ("edge", "blocks"), ("aisle", 12.0), ("fill", 85.0), ("weighbridge", false));
            Preset("depot", ("style", "depot"), ("ground", "gravel"), ("edge", "containers"), ("aisle", 10.0), ("fill", 80.0), ("weighbridge", false));
            Preset("storage", ("style", "storage"), ("ground", "concrete"), ("edge", "trees"), ("aisle", 8.0), ("fill", 100.0), ("weighbridge", false));
        }

        protected override SiteSpec Spec(LayerContext ctx, string style)
        {
            switch (style)
            {
                case "depot":
                    return new SiteSpec { Edge = 4.2f, Front = 24f, BandDepth = 13.8f, MaxBlock = 62f, MinBlock = 14f, BlockGap = 0.5f };
                case "storage":
                    return new SiteSpec { Edge = 5f, Front = 26f, BandDepth = 12.8f, LastBand = 6.6f, MaxBlock = 46f, MinBlock = 10f, BlockGap = 0.4f };
                default:
                    return new SiteSpec { Edge = 3.5f, Front = 26f, BandDepth = 17.2f, MaxBlock = 74f, MinBlock = 14.5f, BlockGap = 0.5f };
            }
        }

        protected override YardFill.FrontSpec Front(LayerContext ctx, string style) => new YardFill.FrontSpec
        {
            Office = IndustrialAssets.ContainerOffice, Offices = style == "storage" ? 1 : 3, Booth = true, Parking = true,
            Toilet = style != "storage", Flag = style == "terminal", ParkedCar = IndustrialAssets.Car,
        };

        protected override void Fill(IndustrialSite s, string style)
        {
            float fill = s.Ctx.F("fill") / 100f;
            int last = LastBand(s);
            foreach (var b in Ordered(s))
            {
                uint salt = s.Salt(0xC0A0u + (uint)b.Index * 7919u);
                switch (style)
                {
                    case "depot":
                    {
                        // Bands in turn: 20 ft rows, two-high stacks, 40 ft lines.
                        int k = b.Band % 3;
                        if (k == 1 && YardFill.Stacks(s, b, 0, fill, salt, false) > 0) { b.Use = "stacks"; break; }
                        if (k == 2 && YardFill.RowsAlong(s, b, IndustrialAssets.Box40, 0.8f, 0.6f, fill, salt) > 0) { b.Use = "box40"; break; }
                        int n = YardFill.RowsAcross(s, b, IndustrialAssets.Box20, 0.35f, fill, salt, 0f, true);
                        if (n > 0) { b.Use = "box20"; s.Containers += n; }
                        break;
                    }
                    case "storage":
                    {
                        // Units with a hand's breadth between them, every unit on the drive aisle.
                        int n = YardFill.RowsAcross(s, b, IndustrialAssets.Box20, 0.5f, fill, salt, 0f, true, 1.2f);
                        if (n > 0) { b.Use = "units"; s.Containers += n; }
                        break;
                    }
                    default:
                    {
                        // A skyline that steps up away from the street; the back band is always the tallest.
                        int tier = b.Band == last && last > 0 ? 2 : math.clamp(b.Band, 0, 2);
                        if (YardFill.Stacks(s, b, tier, fill, salt, true) > 0) b.Use = "stacks";
                        else
                        {
                            int n = YardFill.RowsAlong(s, b, IndustrialAssets.Box40, 0.8f, 0.6f, fill, salt);
                            if (n > 0) { b.Use = "box40"; s.Containers += n; }
                        }
                        break;
                    }
                }
            }
            // Count the 40 ft boxes placed as lines.
            foreach (var o in s.Ctx.Plan.Objects)
                if (o.Asset.Id == IndustrialAssets.Box40.Options[0].Id) s.Containers++;
        }
    }
}
