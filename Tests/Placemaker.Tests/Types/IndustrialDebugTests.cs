using System.Linq;
using Placemaker.Core;
using Xunit;
using Xunit.Abstractions;
namespace Placemaker.Tests.Types
{
    public class IndustrialDebugTests
    {
        private readonly ITestOutputHelper _out;
        public IndustrialDebugTests(ITestOutputHelper o) { _out = o; }
        [Fact]
        public void Dump()
        {
            var ring = TestKit.Rect(150, 140);
            Placemaker.Core.Layers.Types.Yards.IndustrialYardLayer.DebugBlocks = true;
            var plan = YardTestKit.PlanPreset("junkYard", "sorted", ring, TestKit.RoadAlongBottom(150));
            Placemaker.Core.Layers.Types.Yards.IndustrialYardLayer.DebugBlocks = false;
            foreach (var f in plan.Facts.Where(f => !f.Key.StartsWith("blk") || f.Key.EndsWith("band"))) _out.WriteLine($"F {f.Key}={f.Value:0.0}");
            foreach (var g in plan.Objects.GroupBy(o => o.Asset.Id)) _out.WriteLine($"{g.Key}: {g.Count()}");
        }
    }
}
