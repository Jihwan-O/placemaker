using System.Collections.Generic;
using System.Linq;
using Placemaker.Core.Geometry;
using Placemaker.Core.Neighbours;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class NeighbourStitchTests
    {
        // A 2x6 house (16 x 48 m lot) facing `forward`, with a fence on its right edge, a hedge on its left edge, a back
        // fence and a lawn covering the back half of the lot (as NA_ResidentialLow01 grows them).
        private static StitchHouse House(long id, float2 pos, float2 forward, string right = "FenceResidentialHigh01", string left = "HedgeLow01")
        {
            var h = new StitchHouse { Id = id, Position = pos, Forward = math.normalize(forward), HalfLot = new float2(8f, 24f) };
            h.Lines.Add(new StitchLine { Id = id * 10 + 1, Name = right, A = h.ToWorld(new float2(7.9f, 23.9f)), B = h.ToWorld(new float2(7.9f, -23.9f)) });
            h.Lines.Add(new StitchLine { Id = id * 10 + 2, Name = left, A = h.ToWorld(new float2(-7.3f, 23.9f)), B = h.ToWorld(new float2(-7.3f, -12.8f)) });
            h.Lines.Add(new StitchLine { Id = id * 10 + 3, Name = right, A = h.ToWorld(new float2(7.9f, -23.9f)), B = h.ToWorld(new float2(-7.9f, -23.9f)) });
            h.Lawns.Add(new StitchLawn { Id = id * 10 + 5, Ring = new[] { h.ToWorld(new float2(-7f, -23f)), h.ToWorld(new float2(7.4f, -23f)), h.ToWorld(new float2(7.4f, 0f)), h.ToWorld(new float2(-7f, 0f)) } });
            return h;
        }

        [Fact]
        public void Straight_street_neighbours_keep_one_border_line_and_move_nothing()
        {
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            var b = House(2, new float2(16f, 0f), new float2(0f, 1f));
            var plan = YardStitch.Plan(new[] { a, b }, MiddleLine());
            Assert.Single(plan.Pairs);
            // a's right fence and b's left hedge share the edge: the hedge stays, the fence goes
            Assert.Contains(11L, plan.HideLines);
            Assert.DoesNotContain(22L, plan.HideLines);
            Assert.Equal(22L, plan.Pairs[0].KeptLine);
            Assert.Empty(plan.MoveLines);
            Assert.Empty(plan.Lawns);
        }

        [Fact]
        public void Curve_wedge_gets_one_line_on_the_bisector_back_fences_meet_it_and_lawns_grow_to_it()
        {
            // outside of a curve: houses face the road's centre, backs fan out (front edge on radius 40, back on 88)
            float2 At(float angle) => 64f * new float2(math.cos(angle), math.sin(angle));
            float2 Facing(float angle) => -new float2(math.cos(angle), math.sin(angle));
            float a0 = math.radians(90f), a1 = a0 + 0.45f;   // facing the centre, a's right is counter-clockwise
            var a = House(1, At(a0), Facing(a0));
            var b = House(2, At(a1), Facing(a1));
            Assert.True(math.dot(b.Position - a.Position, a.Right) > 0f);
            var plan = YardStitch.Plan(new[] { a, b }, MiddleLine());
            Assert.Single(plan.Pairs);
            var pair = plan.Pairs[0];
            Assert.True(pair.BackGap > pair.FrontGap + 15f, $"a wedge: back gap {pair.BackGap} vs front {pair.FrontGap}");
            // the hedge (b's left) is kept and laid on the bisector; a's right fence goes
            Assert.Equal(22L, pair.KeptLine);
            Assert.Contains(11L, plan.HideLines);
            var kept = plan.MoveLines[22L];
            Assert.True(math.distance(kept.A, pair.Front) < 0.2f);
            // b's hedge stopped at z -12.8 of its 24 m half depth: it ends part way down the border, on it
            Assert.True(DistanceToSegment(kept.B, pair.Front, pair.Back) < 0.05f);
            // a's back fence now ends at the border's back point
            var back = plan.MoveLines[13L];
            Assert.True(math.distance(back.A, pair.Back) < 0.01f);
            // both lawns reach the shared side, so both grew into their half of the wedge
            Assert.True(plan.Lawns.ContainsKey(15L) && plan.Lawns.ContainsKey(25L), string.Join(" | ", plan.Notes) + " || lawns " + string.Join(",", plan.Lawns.Keys) + " new " + plan.NewLawns.Count + " beds " + plan.Beds.Count);
            Assert.True(plan.LawnAddedM2 > 50f, $"added {plan.LawnAddedM2} m²");
            Assert.True(plan.Lawns.Values.All(r => SurfaceAcceptance.IsAccepted(r)));
            // no lawn crosses the border: a's grown lawn stays on a's side of it
            float sideOfA = Side(pair.Front, pair.Back, a.Position);
            foreach (var p in plan.Lawns[15L]) Assert.True(Side(pair.Front, pair.Back, p) * math.sign(sideOfA) > -0.5f, "a's lawn crosses the border");
        }

        [Fact]
        public void Inside_of_a_curve_wedges_open_to_the_street_and_are_stitched_too()
        {
            // inside of a curve: houses face away from the centre, fronts on radius 82, backs on 34: the gap is widest at the street
            float2 At(float angle) => 58f * new float2(math.cos(angle), math.sin(angle));
            float2 Facing(float angle) => new float2(math.cos(angle), math.sin(angle));
            float a0 = math.radians(90f), a1 = a0 - 0.42f;   // facing outward, a's right is clockwise
            var a = House(1, At(a0), Facing(a0));
            var b = House(2, At(a1), Facing(a1));
            Assert.True(math.dot(b.Position - a.Position, a.Right) > 0f);
            var plan = YardStitch.Plan(new[] { a, b }, MiddleLine());
            Assert.Single(plan.Pairs);
            var pair = plan.Pairs[0];
            Assert.True(pair.FrontGap > pair.BackGap, $"front {pair.FrontGap} back {pair.BackGap}");
            Assert.Equal(22L, pair.KeptLine);
            Assert.Contains(11L, plan.HideLines);
            Assert.True(plan.MoveLines.ContainsKey(22L));
            // the lawns sit at the back, where the lots almost meet: whatever they gain stays on their own side
            foreach (var r in plan.Lawns.Values) Assert.True(SurfaceAcceptance.IsAccepted(r));
        }

        [Fact]
        public void A_shallow_house_beside_a_deep_one_is_its_neighbour()
        {
            // both lots start at the street (z = 24 front edge); b is only 16 m deep, so its centre sits 16 m nearer the street
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            var b = new StitchHouse { Id = 2, Position = new float2(20f, 16f), Forward = new float2(0f, 1f), HalfLot = new float2(8f, 8f) };
            b.Lines.Add(new StitchLine { Id = 21, Name = "HedgeHigh01", A = b.ToWorld(new float2(-7.9f, 7.9f)), B = b.ToWorld(new float2(-7.9f, -7.9f)) });
            var plan = YardStitch.Plan(new[] { a, b }, MiddleLine());
            Assert.Single(plan.Pairs);
            Assert.Equal(21L, plan.Pairs[0].KeptLine);
            // a's fence leaves the shared side: b's garden (made as deep as a's) had no back line, so the freed fence closes it
            Assert.DoesNotContain(11L, plan.HideLines);
            var reused = plan.MoveLines[11L];
            Assert.True(math.abs(b.ToLocal(reused.A).y - b.ToLocal(reused.B).y) < 0.05f && b.ToLocal(reused.A).y < -b.HalfLot.y, $"a's fence at {reused.A} {reused.B}");
        }

        [Fact]
        public void A_lawn_fills_its_plot_up_to_the_shared_fence_and_never_covers_the_neighbours_driveway()
        {
            // a wedge (as on the outside of a curve); b's lawn sits on its far side, and b's driveway runs along the shared side
            float2 At(float angle) => 64f * new float2(math.cos(angle), math.sin(angle));
            float2 Facing(float angle) => -new float2(math.cos(angle), math.sin(angle));
            float a0 = math.radians(90f), a1 = a0 + 0.45f;
            var a = House(1, At(a0), Facing(a0));
            var b = House(2, At(a1), Facing(a1));
            b.Lawns[0].Ring = new[] { b.ToWorld(new float2(2f, -23f)), b.ToWorld(new float2(7.4f, -23f)), b.ToWorld(new float2(7.4f, 0f)), b.ToWorld(new float2(2f, 0f)) };
            var drive = new[] { b.ToWorld(new float2(-8f, 24f)), b.ToWorld(new float2(-4f, 24f)), b.ToWorld(new float2(-4f, -6f)), b.ToWorld(new float2(-8f, -6f)) };
            b.Hard.Add(drive);
            var plan = YardStitch.Plan(new[] { a, b }, MiddleLine());
            Assert.True(plan.Lawns.ContainsKey(15L), "a's lawn grows");
            // b's lawn fills its own plot too, around its driveway
            Assert.True(plan.Lawns.ContainsKey(25L));
            Assert.False(Poly.Contains(plan.Lawns[25L], b.ToWorld(new float2(-6f, -2f))), "b's lawn covers its driveway");
            float sideOfB = Side(plan.Pairs[0].Front, plan.Pairs[0].Back, b.Position);
            foreach (var p in plan.Lawns[25L]) Assert.True(Side(plan.Pairs[0].Front, plan.Pairs[0].Back, p) * math.sign(sideOfB) > -0.5f, "b's lawn crosses the shared fence");
            var grown = plan.Lawns[15L];
            var pair = plan.Pairs[0];
            // it reaches the shared fence half way down the wedge…
            var onBorder = math.lerp(pair.Front, pair.Back, 0.75f);
            var aEdge = a.ToWorld(new float2(8f, -12f));
            Assert.True(Poly.Contains(grown, math.lerp(aEdge, onBorder, 0.9f)), "a's lawn should reach the shared fence");
            // …never past it, and never over b's driveway
            float sideOfA = Side(pair.Front, pair.Back, a.Position);
            foreach (var p in grown) Assert.True(Side(pair.Front, pair.Back, p) * math.sign(sideOfA) > -0.5f, "a's lawn crosses the shared fence");
            Assert.False(Poly.Contains(grown, b.ToWorld(new float2(-6f, -2f))), "a's lawn covers b's driveway");
            Assert.True(SurfaceAcceptance.IsAccepted(grown));
        }

        [Fact]
        public void Extended_gardens_get_beds_along_their_fences_with_bushes_on_them_clear_of_the_house_and_paving()
        {
            float2 At(float angle) => 64f * new float2(math.cos(angle), math.sin(angle));
            float2 Facing(float angle) => -new float2(math.cos(angle), math.sin(angle));
            float c = math.radians(90f);
            var houses = new[] { House(1, At(c + 0.45f), Facing(c + 0.45f)), House(2, At(c), Facing(c)), House(3, At(c - 0.45f), Facing(c - 0.45f)) };
            foreach (var h in houses)
            {
                h.Footprint = new[] { h.ToWorld(new float2(-5f, 2f)), h.ToWorld(new float2(5f, 2f)), h.ToWorld(new float2(5f, 14f)), h.ToWorld(new float2(-5f, 14f)) };
                h.Hard.Add(new[] { h.ToWorld(new float2(-8f, 24f)), h.ToWorld(new float2(-4f, 24f)), h.ToWorld(new float2(-4f, 2f)), h.ToWorld(new float2(-8f, 2f)) });
            }
            var settings = MiddleLine();
            settings.Extra["features"] = 0f;   // beds and their bushes only (features: their own test)
            settings.Extra["rockChance"] = 0f;
            var plan = YardStitch.Plan(houses, settings);
            Assert.NotEmpty(plan.Beds);
            Assert.NotEmpty(plan.Bushes);
            foreach (var (house, ring) in plan.Beds)
            {
                Assert.True(SurfaceAcceptance.IsAccepted(ring));
                var plot = plan.Plots[house];
                foreach (var p in ring) Assert.True(Poly.Contains(plot, p) || DistanceToSegment(p, plot[0], plot[1]) < 0.3f || plot.Select((q, k) => DistanceToSegment(p, q, plot[(k + 1) % plot.Length])).Min() < 0.3f, $"bed of {house} leaves its garden");
                // never on the house or its driveway, never under the lawn
                foreach (var h in houses)
                {
                    Assert.Empty(Poly.Intersection(Ccw(ring), Ccw(h.Footprint)).Where(r => r.Area > 0.05f));
                    Assert.Empty(Poly.Intersection(Ccw(ring), Ccw(h.Hard[0])).Where(r => r.Area > 0.05f));
                }
                foreach (var kv2 in plan.Lawns) { var ov = Poly.Intersection(Ccw(ring), Ccw(kv2.Value)).Sum(r => r.Area); Assert.True(ov < 0.2f, $"bed of {house} overlaps lawn {kv2.Key} by {ov:F2} m², lawns {string.Join(",", plan.Lawns.Keys)}; {string.Join(" | ", plan.Notes)}"); }
            }
            // every bush stands on a bed of its own house, and there are only a few per garden
            foreach (var (house, p, _, kind) in plan.Bushes)
            {
                Assert.Contains(plan.Beds, bed => bed.House == house && Poly.Contains(bed.Ring, p));
                Assert.InRange(kind, 0, YardStitch.BushKinds.Length - 1);
            }
            Assert.True(plan.Bushes.GroupBy(b => b.House).All(g => g.Count() <= 14));
            // the same houses plant the same garden every time
            var again = YardStitch.Plan(houses, settings);
            Assert.Equal(plan.Bushes.Select(b => b.Position), again.Bushes.Select(b => b.Position));
        }

        private static float2[] Ccw(float2[] r) => Poly.IsCcw(r) ? r : r.Reverse().ToArray();

        /// <summary>Gaps shared on the middle line (the stepped split and walkways have their own tests).</summary>
        private static float2[] Strip(float x0, float z0, float x1, float z1) => new[] { new float2(x0, z0), new float2(x1, z0), new float2(x1, z1), new float2(x0, z1) };

        [Fact]
        public void Back_to_back_across_a_road_never_join_and_each_garden_stops_short_of_the_road()
        {
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            var b = House(2, new float2(0f, -60f), new float2(0f, -1f));
            Assert.Contains(YardStitch.Plan(new[] { a, b }, MiddleLine()).Pairs, p => p.Rear);
            var s = MiddleLine().WithBarriers(new List<float2[]> { Strip(-60f, -34f, 60f, -26f) });
            var plan = YardStitch.Plan(new[] { a, b }, s);
            Assert.DoesNotContain(plan.Pairs, p => p.Rear);
            Assert.True(plan.Plots.TryGetValue(1, out var pa), "a reaches out to the road behind it");
            Assert.True(pa.Min(p => p.y) > -26f + 0.5f && pa.Min(p => p.y) < -24.5f, $"a stops just short of the road: {pa.Min(p => p.y)}");
            Assert.True(plan.Plots.TryGetValue(2, out var pb));
            Assert.True(pb.Max(p => p.y) < -34f - 0.5f, $"b stops just short of the road: {pb.Max(p => p.y)}");
        }

        [Fact]
        public void Side_neighbours_across_a_path_never_join()
        {
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            var b = House(2, new float2(30f, 0f), new float2(0f, 1f));
            Assert.Single(YardStitch.Plan(new[] { a, b }, MiddleLine()).Pairs);
            var plan = YardStitch.Plan(new[] { a, b }, MiddleLine().WithBarriers(new List<float2[]> { Strip(12f, -40f, 18f, 40f) }));
            Assert.Empty(plan.Pairs);
            Assert.True(plan.Plots.TryGetValue(1, out var pa) && pa.Max(p => p.x) < 12f - 0.5f, "a's garden stops short of the path");
            Assert.True(plan.Plots.TryGetValue(2, out var pb) && pb.Min(p => p.x) > 18f + 0.5f, "b's garden stops short of the path");
            // a's right fence follows the path
            Assert.True(plan.MoveLines.TryGetValue(11L, out var fence) && math.abs(fence.A.x - 11.2f) < 0.3f && math.abs(fence.B.x - 11.2f) < 0.3f, "a's fence runs along the path");
        }

        [Fact]
        public void A_garden_hugs_a_diagonal_road_near_it_and_never_crosses_it()
        {
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            // a road running away at 45 degrees from near a's front right corner
            var road = new[] { new float2(12f, 24f), new float2(18f, 24f), new float2(58f, -16f), new float2(52f, -16f) };
            var plan = YardStitch.Plan(new[] { a }, MiddleLine().WithBarriers(new List<float2[]> { road }));
            Assert.True(plan.Plots.TryGetValue(1, out var pa), "a reaches out to the road");
            Assert.True(pa.Max(p => p.x) > 14f, $"a fills the wedge towards the road: {pa.Max(p => p.x)}");
            foreach (var p in pa) Assert.False(Poly.Contains(road, p), $"a plot corner {p} lies on the road");
            Assert.False(Overlap(pa, road), "the garden overlaps the road");
        }

        [Fact]
        public void A_lone_garden_balloons_into_open_land_as_far_as_its_look_lets_it_but_never_onto_a_road()
        {
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            a.Options = new Dictionary<string, float>(PolishLooks.Defaults) { ["openReach"] = 60f };
            var plan = YardStitch.Plan(new[] { a }, MiddleLine());
            Assert.True(plan.Plots.TryGetValue(1, out var pa), "the garden grows");
            Assert.True(pa.Min(p => p.x) < -8f - 55f && pa.Max(p => p.x) > 8f + 55f && pa.Min(p => p.y) < -24f - 55f, "60 m into the open land on both sides and at the back");
            Assert.True(pa.Max(p => p.y) <= 24.01f, "never past its street edge");
            // a road 20 m to its right: it stops there
            var plan2 = YardStitch.Plan(new[] { a }, MiddleLine().WithBarriers(new List<float2[]> { Strip(28f, -200f, 36f, 200f) }));
            Assert.True(plan2.Plots.TryGetValue(1, out var pb) && pb.Max(p => p.x) < 28f - 0.5f, "stops short of the road");
        }

        private static bool Overlap(float2[] a, float2[] b) =>
            Poly.Boolean(new[] { (IReadOnlyList<float2>)a }, new[] { (IReadOnlyList<float2>)b }, BoolOp.Intersection, FillRule.NonZero).Sum(r => r.Area) > 0.05f;

        private static StitchSettings MiddleLine()
        {
            var s = new StitchSettings();
            s.Extra["steps"] = 0f;
            s.Extra["alleys"] = 0f;
            return s;
        }

        [Fact]
        public void A_wedge_can_be_split_front_and_back_with_fences_on_the_houses_own_lines_and_a_bush_row_for_the_step()
        {
            float2 At(float angle) => 64f * new float2(math.cos(angle), math.sin(angle));
            float2 Facing(float angle) => -new float2(math.cos(angle), math.sin(angle));
            float a0 = math.radians(90f), a1 = a0 + 0.45f;
            var a = House(1, At(a0), Facing(a0));
            var b = House(2, At(a1), Facing(a1));
            var steps = new StitchSettings();
            steps.Extra["stepChance"] = 100f;
            var plan = YardStitch.Plan(new[] { a, b }, steps);
            Assert.Contains(plan.Notes, n => n.Contains("the gap split"));
            // both side lines stay on their own lot lines (only shortened), so no diagonal fence
            foreach (var (h, id, x) in new[] { (a, 11L, 8f), (b, 22L, -8f) })
            {
                var m = plan.MoveLines[id];
                Assert.True(math.abs(h.ToLocal(m.A).x - x) < 0.05f && math.abs(h.ToLocal(m.B).x - x) < 0.05f, $"line {id} left its lot line: {m.A} {m.B}");
                Assert.True(math.distance(m.A, m.B) > 8f && math.distance(m.A, m.B) < 40f, $"line {id} is {math.distance(m.A, m.B):F1} m");
            }
            // the two gardens do not overlap, and together cover the wedge
            var pa = plan.Plots[1];
            var pb = plan.Plots[2];
            Assert.True(Poly.Intersection(pa, pb).Sum(r => r.Area) < 1f);
            // the step is planted: a bed with bushes close together
            Assert.NotEmpty(plan.Beds);
            Assert.True(plan.Bushes.Count >= 3);
        }


        [Fact]
        public void Two_deep_lots_meet_behind_a_shallow_one_between_them()
        {
            // a straight street: 48 m deep, 16 m deep, 48 m deep, touching, street edges in line (z = 24 in world)
            var l = House(1, new float2(0f, 0f), new float2(0f, 1f));
            var m = new StitchHouse { Id = 2, Position = new float2(16f, 16f), Forward = new float2(0f, 1f), HalfLot = new float2(8f, 8f) };
            m.Lines.Add(new StitchLine { Id = 21, Name = "FenceResidentialHigh01", A = m.ToWorld(new float2(-7.9f, 7.9f)), B = m.ToWorld(new float2(-7.9f, -7.9f)) });
            m.Lines.Add(new StitchLine { Id = 23, Name = "FenceResidentialHigh01", A = m.ToWorld(new float2(7.9f, -7.9f)), B = m.ToWorld(new float2(-7.9f, -7.9f)) });
            var r = House(3, new float2(32f, 0f), new float2(0f, 1f));
            var plan = YardStitch.Plan(new[] { l, m, r }, MiddleLine());
            Assert.Contains(plan.Notes, x => x.Contains("meet behind 2"));
            // the land behind the shallow lot (y 8 down to -24) is the deep neighbours': left half to 1, right half to 3
            Assert.True(Poly.Contains(plan.Plots[1], new float2(12f, -10f)), "left half behind the shallow lot is not 1's");
            Assert.True(Poly.Contains(plan.Plots[3], new float2(20f, -10f)), "right half behind the shallow lot is not 3's");
            // a line down the middle behind it
            Assert.Contains(plan.MoveLines.Values, mv => math.abs(mv.A.x - 16f) < 0.05f && math.abs(mv.B.x - 16f) < 0.05f && math.abs(mv.A.y - mv.B.y) > 20f);
            // no line runs across the land behind it from street to back on the old lot edges (x = 8 or 24 below y = 8)
            foreach (var mv in plan.MoveLines.Values.Concat(new[] { l, m, r }.SelectMany(h => h.Lines).Where(x => !plan.HideLines.Contains(x.Id) && !plan.MoveLines.ContainsKey(x.Id)).Select(x => (x.A, x.B))))
                foreach (float x in new[] { 8f, 24f })
                    if (math.abs(mv.A.x - x) < 1.2f && math.abs(mv.B.x - x) < 1.2f) Assert.True(math.min(mv.A.y, mv.B.y) > 7f, $"a line on x {x} runs on behind the shallow lot: {mv.A} {mv.B}");
        }

        [Fact]
        public void Big_new_garden_land_gets_a_feature_inside_its_own_garden_clear_of_the_house_and_paving()
        {
            float2 At(float angle) => 64f * new float2(math.cos(angle), math.sin(angle));
            float2 Facing(float angle) => -new float2(math.cos(angle), math.sin(angle));
            float c = math.radians(90f);
            var houses = new[] { House(1, At(c + 0.55f), Facing(c + 0.55f)), House(2, At(c), Facing(c)), House(3, At(c - 0.55f), Facing(c - 0.55f)) };
            foreach (var h in houses)
            {
                h.Footprint = new[] { h.ToWorld(new float2(-5f, 2f)), h.ToWorld(new float2(5f, 2f)), h.ToWorld(new float2(5f, 14f)), h.ToWorld(new float2(-5f, 14f)) };
                h.Hard.Add(new[] { h.ToWorld(new float2(-8f, 24f)), h.ToWorld(new float2(-4f, 24f)), h.ToWorld(new float2(-4f, 2f)), h.ToWorld(new float2(-8f, 2f)) });
            }
            var settings = MiddleLine();
            settings.Extra["featureChance"] = 100f;
            var plan = YardStitch.Plan(houses, settings);
            var named = new[] { "rock garden", "sand pit", "wild corner", "bench nook" };
            Assert.Contains(plan.Notes, x => named.Any(x.Contains));
            // every object stands in its own house's garden, never on a house or a driveway
            foreach (var (house, p, _, _) in plan.Bushes)
            {
                Assert.True(Poly.Contains(plan.Plots[house], p), $"an object of {house} outside its garden");
                foreach (var h in houses)
                {
                    Assert.False(Poly.Contains(h.Footprint, p), $"an object on house {h.Id}");
                    Assert.False(Poly.Contains(h.Hard[0], p), $"an object on the driveway of {h.Id}");
                }
            }
            Assert.True(plan.Paths.All(pt => SurfaceAcceptance.IsAccepted(pt.Ring)));
        }

        [Fact]
        public void A_border_line_that_would_run_through_a_house_stops_at_the_house()
        {
            float2 At(float angle) => 64f * new float2(math.cos(angle), math.sin(angle));
            float2 Facing(float angle) => -new float2(math.cos(angle), math.sin(angle));
            float a0 = math.radians(90f), a1 = a0 + 0.45f;
            var a = House(1, At(a0), Facing(a0));
            var b = House(2, At(a1), Facing(a1));
            // b's house is wider than its lot on a's side: it reaches over the border
            b.Footprint = new[] { b.ToWorld(new float2(-14f, 10f)), b.ToWorld(new float2(6f, 10f)), b.ToWorld(new float2(6f, -10f)), b.ToWorld(new float2(-14f, -10f)) };
            var plan = YardStitch.Plan(new[] { a, b }, MiddleLine());
            // the hedge is still kept on the border, but no piece of it lies inside b's house
            Assert.True(plan.MoveLines.ContainsKey(22L));
            var kept = plan.MoveLines[22L];
            float inside = Poly.ClipSegmentToPolygon(b.Footprint, kept.A, kept.B).Sum(x => math.distance(x.a, x.b));
            Assert.True(inside < 0.05f, $"{inside} m of the hedge inside the house");
            Assert.Contains(plan.Notes, n => n.Contains("stop at a house"));
        }

        [Fact]
        public void Back_to_back_houses_share_one_back_line_on_the_middle_line_and_lawns_reach_it()
        {
            // a faces south to its street, b faces north to the other street; 8 m between their back edges
            var a = House(1, new float2(0f, 0f), new float2(0f, -1f), right: "FenceResidentialHigh01");
            var b = House(2, new float2(0f, 56f), new float2(0f, 1f), right: "HedgeHigh01");
            // lawns along the back edges
            a.Lawns[0].Ring = new[] { a.ToWorld(new float2(-7f, -23f)), a.ToWorld(new float2(7f, -23f)), a.ToWorld(new float2(7f, -10f)), a.ToWorld(new float2(-7f, -10f)) };
            b.Lawns[0].Ring = new[] { b.ToWorld(new float2(-7f, -23f)), b.ToWorld(new float2(7f, -23f)), b.ToWorld(new float2(7f, -10f)), b.ToWorld(new float2(-7f, -10f)) };
            var noPlanting = new StitchSettings();
            noPlanting.Extra["plant"] = 0f;   // fences and lawns only (beds along the fences: their own test)
            var plan = YardStitch.Plan(new[] { a, b }, noPlanting);
            Assert.Single(plan.Pairs, p => p.Rear);
            // both back lines go onto the middle line (z = 28); b's is a hedge (its "right" kind), a's a fence: the hedge stays
            Assert.Contains(13L, plan.HideLines);
            var kept = plan.MoveLines[23L];
            Assert.True(math.abs(kept.A.y - 28f) < 0.01f && math.abs(kept.B.y - 28f) < 0.01f, $"kept back line at {kept.A} {kept.B}");
            // both lawns reach the middle line
            Assert.True(plan.Lawns[15L].Max(p => p.y) > 27.5f);
            Assert.True(plan.Lawns[25L].Min(p => p.y) < 28.5f);
            // a's right fence ends at its back corner: it now reaches the middle line too
            Assert.True(math.abs(plan.MoveLines[11L].B.y - 28f) < 0.01f);
        }

        [Fact]
        public void Back_to_back_rows_of_different_widths_meet_on_one_middle_line_with_no_doubled_fence()
        {
            // a city block's middle: three 16 m lots facing south, two 24 m lots facing north behind them, 10 m between backs
            var houses = new List<StitchHouse>();
            for (int i = 0; i < 3; i++) houses.Add(House(i + 1, new float2(i * 16f, 0f), new float2(0f, -1f)));
            for (int i = 0; i < 2; i++)
            {
                var h = new StitchHouse { Id = i + 4, Position = new float2(4f + i * 24f, 58f), Forward = new float2(0f, 1f), HalfLot = new float2(12f, 24f) };
                h.Lines.Add(new StitchLine { Id = (i + 4) * 10 + 3, Name = "HedgeHigh01", A = h.ToWorld(new float2(11.9f, -23.9f)), B = h.ToWorld(new float2(-11.9f, -23.9f)) });
                h.Lines.Add(new StitchLine { Id = (i + 4) * 10 + 1, Name = "FenceResidentialHigh01", A = h.ToWorld(new float2(11.9f, 23.9f)), B = h.ToWorld(new float2(11.9f, -23.9f)) });
                houses.Add(h);
            }
            var plan = YardStitch.Plan(houses);
            Assert.True(plan.Pairs.Count(p => p.Rear) >= 2);
            // every back line still drawn lies on the middle line (y = 29)
            var backs = new List<(float2 A, float2 B)>();
            foreach (var h in houses)
                foreach (var l in h.Lines.Where(l => l.Id % 10 == 3 && !plan.HideLines.Contains(l.Id)))
                {
                    var m = plan.MoveLines.TryGetValue(l.Id, out var mv) ? mv : (l.A, l.B);
                    Assert.True(math.abs(m.A.y - 29f) < 0.05f && math.abs(m.B.y - 29f) < 0.05f, $"back line {l.Id} at {m.A} {m.B}");
                    backs.Add(m);
                }
            // no two of them overlap (one fence or hedge along any stretch of the middle line)
            for (int i = 0; i < backs.Count; i++)
                for (int j = i + 1; j < backs.Count; j++)
                {
                    float o = math.min(math.max(backs[i].A.x, backs[i].B.x), math.max(backs[j].A.x, backs[j].B.x)) - math.max(math.min(backs[i].A.x, backs[i].B.x), math.min(backs[j].A.x, backs[j].B.x));
                    Assert.True(o < 0.6f, $"two back lines overlap by {o:F1} m");
                }
            // and together they cover the shared stretch (the hedges win over the fences)
            Assert.True(backs.Sum(b => math.distance(b.A, b.B)) > 44f, $"back lines cover {backs.Sum(b => math.distance(b.A, b.B)):F1} m");
        }

        [Fact]
        public void Houses_inside_a_small_circle_get_slices_that_stop_at_a_shared_green()
        {
            // eight houses inside a ring road, facing out to it, backs close to the centre (front edges on radius 54, backs on 6)
            var houses = new List<StitchHouse>();
            for (int i = 0; i < 8; i++)
            {
                float ang = i * math.PI * 2f / 8f;
                var dir = new float2(math.cos(ang), math.sin(ang));
                var h = House(i + 1, dir * 30f, dir);
                // back gardens reaching from the house to (past) where the ring line will be
                h.Lawns[0].Ring = new[] { h.ToWorld(new float2(-7f, -12f)), h.ToWorld(new float2(7f, -12f)), h.ToWorld(new float2(7f, 2f)), h.ToWorld(new float2(-7f, 2f)) };
                houses.Add(h);
            }
            var plan = YardStitch.Plan(houses);
            Assert.Equal(8, plan.Pairs.Count(p => !p.Rear));
            // the slices stop at one ring line, half the street's radius out (27 m along each house's axis): no long thin
            // points, a shared green inside
            foreach (var p in plan.Pairs.Where(p => !p.Rear)) Assert.True(math.abs(math.length(p.Back) - 27f / math.cos(math.radians(22.5f))) < 1f, $"shared fence ends at radius {math.length(p.Back)}");
            Assert.Contains(plan.Notes, n => n.Contains("shared green"));
            Assert.Single(plan.Commons);
            // each back fence lies on the ring line, from one shared fence to the next
            for (int i = 1; i <= 8; i++)
            {
                var m = plan.MoveLines[i * 10L + 3];
                var h = houses[i - 1];
                Assert.True(math.abs(math.dot(m.A, h.Forward) - 27f) < 0.05f && math.abs(math.dot(m.B, h.Forward) - 27f) < 0.05f, $"back fence of {i} at {m.A} {m.B}");
                Assert.True(math.distance(m.A, m.B) > 20f, $"back fence of {i} only {math.distance(m.A, m.B):F1} m");
            }
            // lawns stay in their slice and stop at the ring line
            Assert.True(plan.Lawns.Count >= 6, $"{plan.Lawns.Count} lawns grew");
            foreach (var kv in plan.Lawns)
            {
                var h = houses.Single(x => x.Id * 10 + 5 == kv.Key);
                foreach (var p in kv.Value)
                {
                    float ang = math.degrees(math.abs(math.atan2(math.dot(p, h.Right), math.dot(p, h.Forward))));
                    Assert.True(ang < 22.5f + 2f, $"lawn of {h.Id} leaves its slice ({ang:F1}°)");
                    Assert.True(math.dot(p, h.Forward) > 26.5f, $"lawn of {h.Id} crosses the ring line");
                }
            }
            Assert.True(plan.Lawns.Values.All(r => SurfaceAcceptance.IsAccepted(r)));
        }

        [Fact]
        public void A_big_middle_keeps_its_back_fences_joined_up_around_a_shared_green()
        {
            // shallow lots (16 m deep) on a big ring: the borders run on 8 m, the back fences join their ends, the middle is shared
            var houses = new List<StitchHouse>();
            for (int i = 0; i < 10; i++)
            {
                float ang = i * math.PI * 2f / 10f;
                var dir = new float2(math.cos(ang), math.sin(ang));
                var h = new StitchHouse { Id = i + 1, Position = dir * 58f, Forward = dir, HalfLot = new float2(8f, 8f) };
                h.Lines.Add(new StitchLine { Id = (i + 1) * 10 + 1, Name = "HedgeHigh01", A = h.ToWorld(new float2(7.9f, 7.9f)), B = h.ToWorld(new float2(7.9f, -7.9f)) });
                h.Lines.Add(new StitchLine { Id = (i + 1) * 10 + 3, Name = "FenceResidentialHigh01", A = h.ToWorld(new float2(7.9f, -7.9f)), B = h.ToWorld(new float2(-7.9f, -7.9f)) });
                houses.Add(h);
            }
            var plan = YardStitch.Plan(houses);
            Assert.Contains(plan.Notes, n => n.Contains("shared green"));
            // every back fence moves onto the ring line (half the street's radius: 33 m) and reaches both shared fences, so
            // together they close the ring
            for (int i = 1; i <= 10; i++)
            {
                Assert.True(plan.MoveLines.ContainsKey(i * 10L + 3), $"back fence of {i} not joined");
                var m = plan.MoveLines[i * 10L + 3];
                var h = houses[i - 1];
                Assert.True(math.abs(math.dot(m.A, h.Forward) - 33f) < 0.05f && math.abs(math.dot(m.B, h.Forward) - 33f) < 0.05f, $"back fence of {i} at {m.A} {m.B}");
            }
            foreach (var p in plan.Pairs.Where(p => !p.Rear)) Assert.True(plan.MoveLines.Values.Count(m => math.distance(m.A, p.Back) < 0.05f || math.distance(m.B, p.Back) < 0.05f) >= 2, "a shared fence's back end meets no back fence");
            Assert.Single(plan.Commons);
            Assert.True(Poly.Area(plan.Commons[0]) > 3000f, $"shared green {Poly.Area(plan.Commons[0]):F0} m²");
        }

        [Fact]
        public void A_house_behind_another_is_not_its_side_neighbour()
        {
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            var b = House(2, new float2(12f, -40f), new float2(0f, 1f));
            Assert.Empty(YardStitch.Plan(new[] { a, b }).Pairs);
        }

        [Fact]
        public void Houses_across_the_street_are_never_a_pair()
        {
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            var b = House(2, new float2(16f, 70f), new float2(0f, -1f));
            Assert.Empty(YardStitch.Plan(new[] { a, b }).Pairs);
        }

        [Fact]
        public void A_row_of_three_stitches_both_gaps_of_the_middle_house()
        {
            float2 At(float angle) => 64f * new float2(math.cos(angle), math.sin(angle));
            float2 Facing(float angle) => -new float2(math.cos(angle), math.sin(angle));
            float c = math.radians(90f);
            var houses = new[] { House(1, At(c + 0.45f), Facing(c + 0.45f)), House(2, At(c), Facing(c)), House(3, At(c - 0.45f), Facing(c - 0.45f)) };
            var plan = YardStitch.Plan(houses, MiddleLine());
            Assert.Equal(2, plan.Pairs.Count);
            // the middle house's back fence meets the border on both ends
            var back = plan.MoveLines[23L];
            Assert.True(math.distance(back.A, plan.Pairs.Single(p => p.Left == 2).Back) < 0.01f);
            Assert.True(math.distance(back.B, plan.Pairs.Single(p => p.Right == 2).Back) < 0.01f);
        }

        // ───────────── yes meets no (the player's per-lot flag) ─────────────

        /// <summary>A wedge on the outside of a curve: a (polished) on the left, b (unpolished) on the right.</summary>
        private static (StitchHouse A, StitchHouse B) Wedge()
        {
            float2 At(float angle) => 64f * new float2(math.cos(angle), math.sin(angle));
            float2 Facing(float angle) => -new float2(math.cos(angle), math.sin(angle));
            float a0 = math.radians(90f), a1 = a0 + 0.45f;
            var a = House(1, At(a0), Facing(a0));
            var b = House(2, At(a1), Facing(a1));
            b.Polished = false;
            return (a, b);
        }

        private static void AssertUntouched(StitchPlan plan, StitchHouse h)
        {
            foreach (var l in h.Lines)
            {
                Assert.DoesNotContain(l.Id, plan.HideLines);
                Assert.False(plan.MoveLines.ContainsKey(l.Id), $"unpolished {h.Id}'s line {l.Id} moved");
            }
            foreach (var l in h.Lawns) Assert.False(plan.Lawns.ContainsKey(l.Id), $"unpolished {h.Id}'s lawn changed");
            Assert.DoesNotContain(plan.NewLawns, x => x.House == h.Id);
            Assert.DoesNotContain(plan.Beds, x => x.House == h.Id);
            Assert.DoesNotContain(plan.Bushes, x => x.House == h.Id);
            Assert.DoesNotContain(plan.Paths, x => x.House == h.Id);
            Assert.False(plan.Plots.ContainsKey(h.Id));
        }

        /// <summary>How far p lies inside h's lot (negative: outside).</summary>
        private static float InsideLot(StitchHouse h, float2 p)
        {
            var l = h.ToLocal(p);
            return math.min(h.HalfLot.x - math.abs(l.x), h.HalfLot.y - math.abs(l.y));
        }

        [Fact]
        public void A_polished_house_beside_an_unpolished_one_takes_the_whole_gap_and_leans_on_its_fence()
        {
            var (a, b) = Wedge();
            var plan = YardStitch.Plan(new[] { a, b }, MiddleLine());
            var pair = Assert.Single(plan.Pairs);
            Assert.True(pair.Edge);
            AssertUntouched(plan, b);
            // b's left hedge is the border: a's own right fence (its duplicate) goes
            Assert.Contains(11L, plan.HideLines);
            Assert.Equal(22L, pair.KeptLine);
            // a's garden reaches b's left lot edge (not the middle line), and never into b's lot
            var plot = plan.Plots[1];
            Assert.True(plot.Max(p => b.ToLocal(p).x) > -b.HalfLot.x - 0.05f, "a's plot stops short of b's edge");
            Assert.True(plot.All(p => InsideLot(b, p) < 0.05f), "a's plot enters b's lot");
            var lawn = plan.Lawns[15L];
            Assert.True(lawn.Any(p => math.abs(b.ToLocal(p).x + b.HalfLot.x) < 0.6f), "a's lawn does not reach b's edge");
            Assert.True(lawn.All(p => InsideLot(b, p) < 0.05f), "a's lawn enters b's lot");
            // a's back fence runs on to b's edge
            var back = plan.MoveLines[13L];
            Assert.True(new[] { back.A, back.B }.Any(p => math.abs(b.ToLocal(p).x + b.HalfLot.x) < 0.1f), $"a's back fence ends at {back.A} {back.B}");
            Assert.Contains(plan.Notes, n => n.Contains("leans on"));
        }

        [Fact]
        public void When_the_unpolished_lot_has_no_fence_on_that_side_the_polished_ones_line_goes_on_its_edge()
        {
            var (a, b) = Wedge();
            b.Lines.RemoveAll(l => l.Id == 22L);   // b has no left line
            var plan = YardStitch.Plan(new[] { a, b }, MiddleLine());
            AssertUntouched(plan, b);
            Assert.DoesNotContain(11L, plan.HideLines);
            var m = plan.MoveLines[11L];
            foreach (var p in new[] { m.A, m.B })
                Assert.True(math.abs(b.ToLocal(p).x + b.HalfLot.x) < 0.05f, $"a's fence end {p} is not on b's left edge (local x {b.ToLocal(p).x:F2})");
            Assert.True(math.distance(m.A, m.B) > 30f, $"a's fence on the edge is only {math.distance(m.A, m.B):F1} m");
        }

        [Fact]
        public void The_unpolished_house_on_the_left_is_the_edge_too()
        {
            var (a, b) = Wedge();
            a.Polished = false;
            b.Polished = true;
            var plan = YardStitch.Plan(new[] { a, b }, MiddleLine());
            AssertUntouched(plan, a);
            // a's right fence is the border: b's left hedge (its duplicate) goes, b's garden reaches a's right edge
            Assert.Contains(22L, plan.HideLines);
            Assert.Equal(11L, plan.Pairs[0].KeptLine);
            Assert.True(plan.Plots[2].Min(p => a.ToLocal(p).x) < a.HalfLot.x + 0.05f);
            Assert.True(plan.Plots[2].All(p => InsideLot(a, p) < 0.05f));
        }

        [Fact]
        public void Two_unpolished_neighbours_are_left_alone()
        {
            var (a, b) = Wedge();
            a.Polished = false;
            var plan = YardStitch.Plan(new[] { a, b }, MiddleLine());
            Assert.Empty(plan.Pairs);
            Assert.Empty(plan.HideLines);
            Assert.Empty(plan.MoveLines);
            Assert.Empty(plan.Lawns);
            Assert.Empty(plan.Paints);
            AssertUntouched(plan, a);
            AssertUntouched(plan, b);
        }

        [Fact]
        public void Nothing_is_planted_or_painted_on_an_unpolished_lot()
        {
            var (a, b) = Wedge();
            var c = House(3, 64f * new float2(math.cos(math.radians(90f) - 0.45f), math.sin(math.radians(90f) - 0.45f)), -new float2(math.cos(math.radians(90f) - 0.45f), math.sin(math.radians(90f) - 0.45f)));
            var houses = new[] { c, a, b };
            foreach (var h in houses) h.Footprint = new[] { h.ToWorld(new float2(-5f, 2f)), h.ToWorld(new float2(5f, 2f)), h.ToWorld(new float2(5f, 14f)), h.ToWorld(new float2(-5f, 14f)) };
            var plan = YardStitch.Plan(houses, MiddleLine());
            AssertUntouched(plan, b);
            // ground paint never reaches into b's lot (a soft edge may touch it)
            foreach (var paint in plan.Paints)
            {
                var ring = paint.Ring.Select(p => p.xz).ToArray();
                Assert.True(ring.Count(p => InsideLot(b, p) > 1f) == 0, $"paint layer {paint.Layer} reaches into the unpolished lot");
            }
            foreach (var (_, p, _, _) in plan.Bushes) Assert.True(InsideLot(b, p) < 0f);
            foreach (var (_, ring) in plan.Beds) Assert.True(ring.All(p => InsideLot(b, p) < 0.05f));
        }

        [Fact]
        public void A_polished_house_back_to_back_with_an_unpolished_one_reaches_its_back_edge_and_uses_its_back_fence()
        {
            var a = House(1, new float2(0f, 0f), new float2(0f, -1f), right: "FenceResidentialHigh01");
            var b = House(2, new float2(0f, 56f), new float2(0f, 1f), right: "HedgeHigh01");
            b.Polished = false;
            a.Lawns[0].Ring = new[] { a.ToWorld(new float2(-7f, -23f)), a.ToWorld(new float2(7f, -23f)), a.ToWorld(new float2(7f, -10f)), a.ToWorld(new float2(-7f, -10f)) };
            var noPlanting = new StitchSettings();
            noPlanting.Extra["plant"] = 0f;
            var plan = YardStitch.Plan(new[] { a, b }, noPlanting);
            AssertUntouched(plan, b);
            Assert.Single(plan.Pairs, p => p.Rear && p.Edge);
            // a's garden reaches b's back edge (z = 32), not the middle line (28); b's back hedge closes it, so a's back fence goes
            Assert.True(plan.Lawns[15L].Max(p => p.y) > 31.4f, $"a's lawn reaches {plan.Lawns[15L].Max(p => p.y):F1}");
            Assert.True(plan.Lawns[15L].Max(p => p.y) < 32.05f);
            Assert.True(plan.HideLines.Contains(13L) || (plan.MoveLines.TryGetValue(13L, out var m13) && math.distance(m13.A, m13.B) < 2.5f), "a's back fence doubles b's hedge");
        }

        private static float DistanceToSegment(float2 p, float2 a, float2 b)
        {
            var ab = b - a;
            float t = math.saturate(math.dot(p - a, ab) / math.dot(ab, ab));
            return math.distance(p, a + ab * t);
        }

        private static float Side(float2 a, float2 b, float2 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
    }
}
