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
            var plan = YardStitch.Plan(new[] { a, b });
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
            var plan = YardStitch.Plan(new[] { a, b });
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
            Assert.True(plan.Lawns.ContainsKey(15L) && plan.Lawns.ContainsKey(25L));
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
            var plan = YardStitch.Plan(new[] { a, b });
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
            var plan = YardStitch.Plan(new[] { a, b });
            Assert.Single(plan.Pairs);
            Assert.Equal(21L, plan.Pairs[0].KeptLine);
            Assert.Contains(11L, plan.HideLines);
        }

        [Fact]
        public void A_lawn_takes_the_whole_gap_when_the_neighbour_has_none_there_but_never_covers_its_driveway()
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
            var plan = YardStitch.Plan(new[] { a, b });
            Assert.True(plan.Lawns.ContainsKey(15L), "a's lawn grows");
            Assert.False(plan.Lawns.ContainsKey(25L), "b's lawn is not on the shared side");
            var grown = plan.Lawns[15L];
            // it reaches (almost) b's lot edge behind the driveway, past the border…
            var pair = plan.Pairs[0];
            var bEdge = b.ToWorld(new float2(-8f, -15f));
            var mid = math.lerp(pair.Front, pair.Back, (24f + 15f) / 48f);
            Assert.True(Poly.Contains(grown, bEdge + (mid - bEdge) * 0.15f), "a's lawn should reach b's edge where b has nothing");
            // …and never covers b's driveway
            Assert.False(Poly.Contains(grown, b.ToWorld(new float2(-6f, -2f))), "a's lawn covers b's driveway");
            Assert.True(SurfaceAcceptance.IsAccepted(grown));
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
            var plan = YardStitch.Plan(houses);
            Assert.Equal(2, plan.Pairs.Count);
            // the middle house's back fence meets the border on both ends
            var back = plan.MoveLines[23L];
            Assert.True(math.distance(back.A, plan.Pairs.Single(p => p.Left == 2).Back) < 0.01f);
            Assert.True(math.distance(back.B, plan.Pairs.Single(p => p.Right == 2).Back) < 0.01f);
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
