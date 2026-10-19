// Placemaker "Zoning" category — the neighbourhood types (GPL-3.0).
//
// Each is a kind of neighbourhood: its zones (up to three, with shares and lot sizes), how much of the land it fills, how
// its lots mix, its road rule, and the look its lots outgrow their borders with (PolishLooks). Its styles are variations of
// that neighbourhood (corner shops, leafy, a high street…), not just more or less reach. Lot sizes are zone cells (8 m):
// width along the road, depth back from it.

using Placemaker.Core.Layers.Common;
using static Placemaker.Core.Layers.Common.ZoneKinds;

namespace Placemaker.Core.Layers.Types.Zoning
{
    /// <summary>A neighbourhood type: id zoning&lt;Name&gt;, name Placemaker.Zoning.Type.&lt;id&gt;.</summary>
    public abstract class ZoningTypeLayer : PolishLookLayer
    {
        /// <summary>The type's short id (suburb, village…).</summary>
        protected abstract string TypeId { get; }
        public override string Id => "zoning" + char.ToUpperInvariant(TypeId[0]) + TypeId.Substring(1);
        public override string NameKey => "Placemaker.Zoning.Type." + TypeId;
        protected static ZoneSlot Z(string kind, double share, int w0, int w1, int d0, int d1) => new ZoneSlot(kind, share, w0, w1, d0, d1);
    }

    /// <summary>Detached houses with gardens.</summary>
    [AreaGenerator(1, Group = "zoning", Loose = true)]
    public sealed class ZoningSuburbLayer : ZoningTypeLayer
    {
        protected override string TypeId => "suburb";
        public override string LookId => "garden";
        protected override ZoneSlot[] Slots => new[] { Z(ResidentialLow, 100, 2, 3, 3, 4), Z(CommercialLow, 0, 2, 3, 2, 3), Z(OfficeLow, 0, 2, 3, 2, 3) };
        protected override void DefinePresets()
        {
            Preset("suburbCornerShops", With(Mix(Z(ResidentialLow, 85, 2, 3, 3, 4), Z(CommercialLow, 15, 2, 3, 2, 3)), new (string, object)[] { ("zonePattern", "even") }));
            Preset("suburbLeafy", With(Mix(Z(ResidentialLow, 100, 3, 4, 4, 5)), new (string, object)[] { ("zoneFill", 85.0) }, WideReach));
            Preset("suburbTight", With(Mix(Z(ResidentialLow, 100, 2, 2, 2, 3)), CompactReach));
        }
    }

    /// <summary>Houses spread out, empty land between them.</summary>
    [AreaGenerator(2, Group = "zoning", Loose = true)]
    public sealed class ZoningVillageLayer : ZoningTypeLayer
    {
        protected override string TypeId => "village";
        public override string LookId => "rustic";
        protected override ZoneSlot[] Slots => new[] { Z(ResidentialLow, 100, 2, 4, 3, 5), Z(CommercialLow, 0, 2, 3, 2, 3), Z(OfficeLow, 0, 2, 3, 2, 3) };
        protected override double DefaultFill => 60;
        protected override string DefaultPattern => "clumps";
        protected override void DefinePresets()
        {
            Preset("villageHamlet", With(Mix(Z(ResidentialLow, 100, 3, 4, 4, 5)), new (string, object)[] { ("zoneFill", 35.0), ("zonePattern", "clumps"), ("zoneGrain", 2.0) }, WideReach));
            Preset("villageGreen", With(Mix(Z(ResidentialLow, 90, 2, 3, 3, 4), Z(CommercialLow, 10, 2, 2, 2, 3)), new (string, object)[] { ("zoneFill", 70.0), ("zonePattern", "clumps"), ("zoneGrain", 4.0) }));
            Preset("villageLanes", With(Mix(Z(ResidentialLow, 100, 3, 5, 4, 6)), new (string, object)[] { ("zoneFill", 45.0), ("zonePattern", "mixed") }, WideReach));
        }
    }

    /// <summary>Rows of town houses.</summary>
    [AreaGenerator(3, Group = "zoning", Loose = true)]
    public sealed class ZoningTownhousesLayer : ZoningTypeLayer
    {
        protected override string TypeId => "townhouses";
        public override string LookId => "tidy";
        protected override ZoneSlot[] Slots => new[] { Z(ResidentialRow, 100, 2, 4, 2, 3), Z(ResidentialMedium, 0, 2, 3, 3, 4), Z(CommercialLow, 0, 2, 2, 2, 3) };
        protected override void DefinePresets()
        {
            Preset("rowsTerraces", With(Mix(Z(ResidentialRow, 100, 3, 6, 2, 3)), CompactReach));
            Preset("rowsMews", With(Mix(Z(ResidentialRow, 70, 2, 4, 2, 3), Z(ResidentialMedium, 30, 2, 3, 3, 4)), new (string, object)[] { ("zonePattern", "clumps"), ("zoneGrain", 3.0) }));
            Preset("rowsCornerShops", With(Mix(Z(ResidentialRow, 85, 2, 4, 2, 3), Z(CommercialLow, 15, 2, 2, 2, 3)), new (string, object)[] { ("zonePattern", "even") }));
        }
    }

    /// <summary>Shops along the street, homes around them; the big roads get the bigger shops.</summary>
    [AreaGenerator(4, Group = "zoning", Loose = true)]
    public sealed class ZoningMainStreetLayer : ZoningTypeLayer
    {
        protected override string TypeId => "mainStreet";
        public override string LookId => "tidy";
        protected override ZoneSlot[] Slots => new[] { Z(CommercialLow, 60, 2, 4, 2, 3), Z(ResidentialMedium, 40, 2, 3, 3, 5), Z(OfficeLow, 0, 2, 3, 3, 4) };
        protected override string DefaultPattern => "even";
        protected override (bool On, string Kind, double MinWidth, double Depth) Road => (true, CommercialHigh, 20, 3);
        protected override void DefinePresets()
        {
            Preset("mainHighStreet", With(Mix(Z(CommercialLow, 60, 2, 3, 2, 3), Z(ResidentialLow, 40, 2, 3, 3, 4)), new (string, object)[] { ("roadRule", false), ("zonePattern", "even") }));
            Preset("mainBoulevard", With(Mix(Z(ResidentialMedium, 70, 2, 4, 3, 5), Z(CommercialLow, 30, 2, 3, 2, 3)), RoadRule(CommercialHigh, 15, 4)));
            Preset("mainMarketTown", With(Mix(Z(CommercialLow, 50, 2, 3, 2, 3), Z(Mixed, 30, 2, 3, 3, 4), Z(ResidentialRow, 20, 2, 4, 2, 3)), new (string, object)[] { ("zonePattern", "clumps"), ("zoneGrain", 2.0), ("roadRule", false) }));
        }
    }

    /// <summary>Flats over shops, offices, homes: a town's middle.</summary>
    [AreaGenerator(5, Group = "zoning", Loose = true)]
    public sealed class ZoningTownCentreLayer : ZoningTypeLayer
    {
        protected override string TypeId => "townCentre";
        public override string LookId => "manicured";
        protected override ZoneSlot[] Slots => new[] { Z(Mixed, 50, 2, 3, 3, 4), Z(ResidentialMedium, 30, 2, 4, 3, 5), Z(CommercialLow, 20, 2, 3, 2, 3) };
        protected override void DefinePresets()
        {
            Preset("centreOldTown", With(Mix(Z(Mixed, 70, 2, 2, 3, 4), Z(CommercialLow, 30, 2, 2, 2, 3)), new (string, object)[] { ("zonePattern", "clumps"), ("zoneGrain", 2.0) }, CompactReach));
            Preset("centreNewTown", With(Mix(Z(ResidentialMedium, 60, 3, 5, 4, 5), Z(CommercialHigh, 20, 3, 4, 3, 4), Z(OfficeLow, 20, 3, 4, 4, 5)), new (string, object)[] { ("zonePattern", "even") }));
            Preset("centreSquare", With(Mix(Z(CommercialLow, 50, 2, 3, 2, 3), Z(Mixed, 50, 2, 3, 3, 4)), RoadRule(CommercialHigh, 15, 3)));
        }
    }

    /// <summary>Towers: flats, offices, big shops.</summary>
    [AreaGenerator(6, Group = "zoning", Loose = true)]
    public sealed class ZoningDowntownLayer : ZoningTypeLayer
    {
        protected override string TypeId => "downtown";
        public override string LookId => "manicured";
        protected override ZoneSlot[] Slots => new[] { Z(ResidentialHigh, 50, 3, 5, 4, 6), Z(CommercialHigh, 25, 3, 5, 4, 6), Z(OfficeHigh, 25, 3, 5, 5, 6) };
        protected override void DefinePresets()
        {
            Preset("downtownBusiness", With(Mix(Z(OfficeHigh, 70, 3, 6, 5, 6), Z(CommercialHigh, 30, 3, 5, 4, 6)), new (string, object)[] { ("zonePattern", "clumps"), ("zoneGrain", 3.0) }, CompactReach));
            Preset("downtownLiving", With(Mix(Z(ResidentialHigh, 70, 3, 5, 4, 6), Z(CommercialHigh, 30, 3, 4, 3, 5)), new (string, object)[] { ("zonePattern", "even") }));
            Preset("downtownShopping", With(Mix(Z(CommercialHigh, 60, 3, 6, 4, 6), Z(ResidentialHigh, 40, 3, 5, 4, 6)), RoadRule(CommercialHigh, 15, 6)));
        }
    }

    /// <summary>Offices in green grounds.</summary>
    [AreaGenerator(7, Group = "zoning", Loose = true)]
    public sealed class ZoningBusinessParkLayer : ZoningTypeLayer
    {
        protected override string TypeId => "businessPark";
        public override string LookId => "manicured";
        protected override ZoneSlot[] Slots => new[] { Z(OfficeLow, 100, 3, 5, 4, 6), Z(CommercialLow, 0, 2, 3, 2, 3), Z(OfficeHigh, 0, 3, 5, 5, 6) };
        protected override double DefaultFill => 80;
        protected override void DefinePresets()
        {
            Preset("businessCampus", With(Mix(Z(OfficeLow, 100, 4, 6, 5, 6)), new (string, object)[] { ("zoneFill", 65.0), ("zonePattern", "even") }, WideReach));
            Preset("businessStrip", With(Mix(Z(OfficeLow, 70, 3, 4, 4, 5), Z(CommercialLow, 30, 2, 3, 2, 3)), new (string, object)[] { ("zonePattern", "even") }, RoadRule(OfficeHigh, 20, 4)));
            Preset("businessTech", With(Mix(Z(OfficeHigh, 50, 3, 5, 5, 6), Z(OfficeLow, 50, 3, 5, 4, 6)), new (string, object)[] { ("zonePattern", "clumps"), ("zoneGrain", 3.0) }));
        }
    }

    /// <summary>Works and yards.</summary>
    [AreaGenerator(8, Group = "zoning", Loose = true)]
    public sealed class ZoningIndustrialLayer : ZoningTypeLayer
    {
        protected override string TypeId => "industrial";
        public override string LookId => "yard";
        protected override ZoneSlot[] Slots => new[] { Z(Industrial, 100, 3, 6, 4, 6), Z(OfficeLow, 0, 3, 4, 4, 5), Z(CommercialLow, 0, 2, 4, 2, 4) };
        protected override void DefinePresets()
        {
            Preset("industryMixedYards", With(Mix(Z(Industrial, 80, 3, 6, 4, 6), Z(OfficeLow, 20, 3, 4, 4, 5)), new (string, object)[] { ("zonePattern", "even") }));
            Preset("industryTradePark", With(Mix(Z(Industrial, 60, 3, 5, 4, 6), Z(CommercialLow, 40, 2, 4, 2, 4)), RoadRule(CommercialLow, 15, 3)));
            Preset("industryBigWorks", With(Mix(Z(Industrial, 100, 5, 8, 6, 6)), CompactReach));
        }
    }

    /// <summary>Houses by the sea, a few shops.</summary>
    [AreaGenerator(9, Group = "zoning", Loose = true)]
    public sealed class ZoningSeasideLayer : ZoningTypeLayer
    {
        protected override string TypeId => "seaside";
        public override string LookId => "coastal";
        protected override ZoneSlot[] Slots => new[] { Z(ResidentialLow, 80, 2, 3, 3, 4), Z(CommercialLow, 20, 2, 3, 2, 3), Z(CommercialHigh, 0, 3, 4, 3, 4) };
        protected override void DefinePresets()
        {
            Preset("seasideBeachHouses", With(Mix(Z(ResidentialLow, 100, 2, 3, 3, 4)), new (string, object)[] { ("zoneFill", 80.0) }, WideReach));
            Preset("seasidePromenade", With(Mix(Z(ResidentialLow, 100, 2, 3, 3, 4)), RoadRule(CommercialLow, 12, 2)));
            Preset("seasideResort", With(Mix(Z(ResidentialMedium, 50, 2, 4, 3, 5), Z(CommercialLow, 30, 2, 3, 2, 3), Z(CommercialHigh, 20, 3, 4, 3, 4)), new (string, object)[] { ("zonePattern", "even") }));
        }
    }
}
