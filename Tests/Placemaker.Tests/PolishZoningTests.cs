// The Zoning category's "Zone this area" (Core/Layers/Types/Zoning/PolishLookLayer): the open zone cells of the blocks in the
// area get zones by their shares and pattern, and the road rule gives the cells along wide roads their own zone. On the game
// runner's grid block B0-0 (GridBlockZoningTests.Site: West and South a 4-lane Medium Road, East and North a 2-lane Small Road).

using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Roads;
using Placemaker.Core.Suite;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class PolishZoningTests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        private static ComposedPlan Plan(params (string Id, object Value)[] values)
        {
            var site = GridBlockZoningTests.Site(out var block);
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get("polishGarden"), 1);
            foreach (var (id, v) in values) Editor.SetValue(r, RecipePaths.Root, id, v, false);   // false: the value it already had
            var ring = GridBlockZoningTests.Ring(block, "rect");
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
        }

        private static PlannedZoning[] Cells(ComposedPlan plan) => plan.Layers.SelectMany(l => l.Zoning).ToArray();

        [Fact]
        public void OffZonesNothing()
        {
            Assert.Empty(Cells(Plan(("zoneArea", false))));
        }

        [Fact]
        public void OneZoneZonesEveryOpenCell()
        {
            var cells = Cells(Plan(("zoneArea", true)));
            Assert.True(cells.Length > 100, $"{cells.Length} cells");
            Assert.All(cells, c => Assert.Equal(ZoneKinds.PrefabId(ZoneKinds.ResidentialLow), c.ZoneId));
            // one zone per spot, never two on one cell
            Assert.Equal(cells.Length, cells.Select(c => math.round((c.A + c.C) * 0.5f)).Distinct().Count());
        }

        [Theory]
        [InlineData("speckled")]
        [InlineData("clumps")]
        [InlineData("noise")]
        [InlineData("rows")]
        public void SharesSplitTheCells(string pattern)
        {
            var cells = Cells(Plan(("zoneArea", true), ("shareA", 50.0), ("shareB", 50.0), ("zoneB", ZoneKinds.CommercialLow), ("zonePattern", pattern), ("zoneGrain", 2.0)));
            int homes = cells.Count(c => c.ZoneId == ZoneKinds.PrefabId(ZoneKinds.ResidentialLow));
            int shops = cells.Count(c => c.ZoneId == ZoneKinds.PrefabId(ZoneKinds.CommercialLow));
            Assert.Equal(cells.Length, homes + shops);
            float share = homes / (float)cells.Length;
            Assert.True(share > 0.2f && share < 0.8f, $"{pattern}: {homes} homes, {shops} shops");
        }

        [Fact]
        public void SpeckledMixesNeighboursClumpsKeepThemTogether()
        {
            float Changes(string pattern)
            {
                var cells = Cells(Plan(("zoneArea", true), ("shareA", 50.0), ("shareB", 50.0), ("zonePattern", pattern), ("zoneGrain", 6.0)));
                int differ = 0, pairs = 0;
                foreach (var a in cells)
                {
                    var pa = (a.A + a.C) * 0.5f;
                    foreach (var b in cells)
                    {
                        var d = math.distance(pa, (b.A + b.C) * 0.5f);
                        if (d < 7.5f || d > 8.5f) continue;
                        pairs++;
                        if (a.ZoneId != b.ZoneId) differ++;
                    }
                }
                return differ / (float)math.max(1, pairs);
            }
            float speckled = Changes("speckled"), clumps = Changes("clumps");
            Assert.True(speckled > clumps * 1.5f, $"neighbours differing: speckled {speckled:P0}, clumps {clumps:P0}");
        }

        [Fact]
        public void RoadRuleZonesAlongTheWideRoads()
        {
            float medium = RoadRoles.Find(SuiteLayout.MediumRoad).Width, small = RoadRoles.Find(SuiteLayout.SmallRoad).Width;
            Assert.True(medium > small);
            var plan = Plan(("zoneArea", true), ("roadRule", true), ("roadZone", ZoneKinds.OfficeLow), ("roadMinWidth", (double)((medium + small) * 0.5f)), ("roadDepth", 2.0));
            var cells = Cells(plan);
            var office = cells.Where(c => c.ZoneId == ZoneKinds.PrefabId(ZoneKinds.OfficeLow)).ToArray();
            var homes = cells.Where(c => c.ZoneId == ZoneKinds.PrefabId(ZoneKinds.ResidentialLow)).ToArray();
            Assert.NotEmpty(office);
            Assert.NotEmpty(homes);
            Assert.Equal(cells.Length, office.Length + homes.Length);
        }
    }
}
