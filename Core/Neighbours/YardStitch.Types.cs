// Placemaker neighbourhood polish — the data YardStitch plans with (kept apart from the algorithm so the game runner can
// load a newer YardStitch into a running game: tools/hot, lab op "hotload").

using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Neighbours
{
    public sealed class StitchLine
    {
        public long Id;
        public string Name;
        public float2 A, B;
    }

    public sealed class StitchLawn
    {
        public long Id;
        public float2[] Ring;
    }

    public sealed class StitchHouse
    {
        public long Id;
        public float2 Position;
        /// <summary>Unit direction to the house's street (local +z).</summary>
        public float2 Forward;
        /// <summary>Half the lot: (width, depth) / 2 in metres.</summary>
        public float2 HalfLot;
        public List<StitchLine> Lines = new List<StitchLine>();
        public List<StitchLawn> Lawns = new List<StitchLawn>();
        /// <summary>The house's other surfaces (driveway, paving, sand): grown lawns never cover them.</summary>
        public List<float2[]> Hard = new List<float2[]>();
        /// <summary>The house itself (its mesh's ground outline): no moved fence or hedge may cross it.</summary>
        public float2[] Footprint;
        /// <summary>The house's other built pieces (shed, pool, greenhouse, deck): no moved fence or hedge crosses them.</summary>
        public List<float2[]> Props = new List<float2[]>();
        /// <summary>False: only there so its neighbours are planned right (the game layer edits it in another pass); its
        /// lawn, beds and paths are not worked out.</summary>
        public bool Edit = true;
        /// <summary>The lot's saved flag (the player's painter, "Polish new houses"). False: the lot stays exactly as the game
        /// made it (none of its lines, lawns or pieces change, nothing is planted on it); a polished neighbour takes the whole
        /// gap up to its lot edge and leans on its fence there. Two unpolished neighbours: nothing happens between them.</summary>
        public bool Polished = true;
        /// <summary>A shop, office or works (not a home): the name of the paved surface covering most of its ground (its car
        /// park). Its extra land is filled with the same surface, not lawn. Null for homes (and buildings with no paving).</summary>
        public string MainSurface;
        /// <summary>What the building is: "home", "shop", "office" or "industry" (null: unknown, treated as a home).</summary>
        public string Zone;
        /// <summary>How its garden is dressed: "garden" (lawns, beds, a sand pit or a bench), "natural" (painted ground, rock
        /// outcrops, logs, no lawn), "tidy" (lawns, hedges, flower beds, no rocks) or "yard" (worn ground, rubble, pallets).
        /// Null: by what it is (industry a yard, everything else a garden).</summary>
        public string Theme;
        /// <summary>What this house was polished with (the painter's options, saved on its lot): planner knobs by name
        /// ("paintedGardens", "lawns", "plant", "features", "pathChance", "alleyChance", "rockChance", "paint", "pavedFill")
        /// before the settings. Null: the settings.</summary>
        public Dictionary<string, float> Options;

        public float2 Right => new float2(Forward.y, -Forward.x);
        public float2 ToLocal(float2 w) { var d = w - Position; return new float2(math.dot(d, Right), math.dot(d, Forward)); }
        public float2 ToWorld(float2 l) => Position + Right * l.x + Forward * l.y;
        public float2 FrontRight => ToWorld(new float2(HalfLot.x, HalfLot.y));
        public float2 FrontLeft => ToWorld(new float2(-HalfLot.x, HalfLot.y));
        public float2 BackRight => ToWorld(new float2(HalfLot.x, -HalfLot.y));
        public float2 BackLeft => ToWorld(new float2(-HalfLot.x, -HalfLot.y));
    }

    public sealed class StitchSettings
    {
        /// <summary>Largest gap between two neighbours' front corners that still makes them a pair (m); the inside of a curve opens to the street.</summary>
        public float MaxFrontGap = 100f;
        /// <summary>Largest gap between their back corners (wedges on curves widen to the back) (m).</summary>
        public float MaxBackGap = 150f;
        /// <summary>How far two side neighbours' street edges may be apart along the street's normal (m).</summary>
        public float MaxFrontOffset = 10f;
        /// <summary>How far from a lot edge a line still counts as lying on it (m).</summary>
        public float EdgeTolerance = 1.3f;
        /// <summary>Below this gap (m) the lots touch: only the duplicate line goes, nothing moves.</summary>
        public float TouchingGap = 0.6f;
        /// <summary>A lawn reaching this close to the shared edge grows to the border (m).</summary>
        public float LawnContact = 1.6f;
        /// <summary>Knobs a hot-loaded YardStitch reads before they get a field of their own (lab op "polishSettings").</summary>
        public Dictionary<string, float> Extra = new Dictionary<string, float>();
        public float Knob(string name, float fallback) => Extra != null && Extra.TryGetValue(name, out var v) ? v : fallback;
        /// <summary>What gardens never join across nor reach onto, and grow out to meet: the ground surfaces of roads, paths and
        /// tracks and the lots of buildings that are not houses, as rings. Set per plan by the game (WithBarriers).</summary>
        public List<float2[]> Barriers;
        /// <summary>What each barrier is: 0 a road, path or track, 1 a building's lot (null: all roads).</summary>
        public List<int> BarrierKinds;
        /// <summary>A copy of these settings for one plan, with the barriers around its houses.</summary>
        public StitchSettings WithBarriers(List<float2[]> barriers, List<int> kinds = null)
        {
            var c = (StitchSettings)MemberwiseClone();
            c.Barriers = barriers;
            c.BarrierKinds = kinds;
            return c;
        }
    }

    public sealed class StitchPair
    {
        public long Left, Right;
        public float2 Front, Back;
        public float FrontGap, BackGap;
        public long KeptLine = -1;
        /// <summary>Back to back (across the middle of a block) rather than side by side.</summary>
        public bool Rear;
        /// <summary>Its line edits were undone: a moved line would have crossed a house.</summary>
        public bool Reverted;
        /// <summary>The land between the two lots (side pairs): painted as meadow where no lawn covers it.</summary>
        public float2[] Gap;
        /// <summary>Pie slice: where this border runs on to (the point it meets the next border behind the houses), or null.</summary>
        public float2? Tip;
        /// <summary>One of the two is unpolished: the border is that lot's own edge, the polished one takes the whole gap.</summary>
        public bool Edge;
    }

    public sealed class StitchPlan
    {
        public readonly List<StitchPair> Pairs = new List<StitchPair>();
        public readonly HashSet<long> HideLines = new HashSet<long>();
        public readonly Dictionary<long, (float2 A, float2 B)> MoveLines = new Dictionary<long, (float2, float2)>();
        public readonly Dictionary<long, float2[]> Lawns = new Dictionary<long, float2[]>();
        public float LawnAddedM2;
        /// <summary>Why pairs formed or not, and what was undone (the game runner logs them).</summary>
        public readonly List<string> Notes = new List<string>();
        /// <summary>Ground paint that blends the stitched yards into the land around them (terrain materials, permanent).</summary>
        public readonly List<Placemaker.Core.PlannedPaint> Paints = new List<Placemaker.Core.PlannedPaint>();
        /// <summary>Shared land no house takes (the middle inside a ring of slices): ground detail goes here.</summary>
        public readonly List<float2[]> Commons = new List<float2[]>();
        /// <summary>Each stitched house's plot (its share of the land; fences and lawn follow its edges), by house id.</summary>
        public readonly Dictionary<long, float2[]> Plots = new Dictionary<long, float2[]>();
        /// <summary>Lawns to add, owned by a house: for a house with none (gravel yards), and for the part of a plot its own
        /// lawn cannot reach (a driveway splits it). The game layer creates them as the house's own surfaces.</summary>
        public readonly List<(long House, float2[] Ring)> NewLawns = new List<(long, float2[])>();
        /// <summary>Planted beds (forest-floor ground) along new fences and hedges, owned by the house whose garden they are in.</summary>
        public readonly List<(long House, float2[] Ring)> Beds = new List<(long, float2[])>();
        /// <summary>Bushes on those beds, and rocks by the street, owned by the house: Kind indexes <see cref="YardStitch.BushKinds"/>.</summary>
        public readonly List<(long House, float2 Position, float Yaw, int Kind)> Bushes = new List<(long, float2, float, int)>();
        /// <summary>Fence or hedge pieces (objects of the house's own fence kind, "…PieceHigh01") closing a garden's open
        /// edges, owned by the house: where it stands, which way it runs, the piece.</summary>
        public readonly List<(long House, float2 Position, float Yaw, string Name)> FencePieces = new List<(long, float2, float, string)>();
        /// <summary>Walkways and alleys (a vanilla path surface by name), owned by a house.</summary>
        public readonly List<(long House, float2[] Ring, string Surface)> Paths = new List<(long, float2[], string)>();
    }
}
