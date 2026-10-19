// Placemaker neighbourhood polish — looks: named sets of every polish setting, like Placemaker's styles (GPL-3.0).
//
// A look is a base theme (garden, natural, tidy, yard: what kind of planting and features) and a value for every knob the
// planner reads per house (reach, joining, lawns, paint, planting, features, paths, shops). The built-in looks ship with the
// mod; the player's own looks (New / Save / Rename / Delete in the polish tool) are kept in one file for every city. A
// polished building keeps a look by its id (its hash on the lot) and, when it was polished with changes not saved to the
// look, those changes too; a building whose look was deleted falls back to its base theme's built-in look.

using System;
using System.Collections.Generic;
using System.Linq;
using Placemaker.Core.Tool;

namespace Placemaker.Core.Neighbours
{
    public sealed class PolishLook
    {
        public string Id = "";
        /// <summary>A built-in look: its localisation key; the player's own: its name.</summary>
        public string Name = "";
        public bool Custom;
        /// <summary>garden, natural, tidy or yard.</summary>
        public string Theme = "garden";
        /// <summary>Every knob that differs from <see cref="PolishLooks.Defaults"/>.</summary>
        public Dictionary<string, float> Knobs = new Dictionary<string, float>();

        /// <summary>Every knob's value in this look.</summary>
        public Dictionary<string, float> Values()
        {
            var v = new Dictionary<string, float>(PolishLooks.Defaults);
            foreach (var kv in Knobs) v[kv.Key] = kv.Value;
            return v;
        }
    }

    public static class PolishLooks
    {
        /// <summary>Every knob a look sets, with the planner's own default (YardStitch reads them per house with K()).</summary>
        public static readonly IReadOnlyDictionary<string, float> Defaults = new Dictionary<string, float>
        {
            // reach and joining
            ["sideReach"] = 100f, ["maxFrontGap"] = 100f, ["maxBackGap"] = 150f, ["maxRearGap"] = 60f, ["maxFacingGap"] = 50f,
            ["maxDeepen"] = 60f, ["hug"] = 1f, ["hugReach"] = 80f, ["openReach"] = 20f, ["wrap"] = 1f, ["closeBacks"] = 1f,
            ["alleys"] = 1f, ["alleyChance"] = 45f,
            // the land a garden grows onto: no steeper than this (%), no further above or below the house (m); 0: no limit.
            // Forgiving by default ("they basically just avoid going up a mega cliff"): only cliffs stop a garden.
            ["maxSlope"] = 80f, ["maxRise"] = 0f,
            // lawns and ground paint
            ["lawns"] = 1f, ["paintedGardens"] = 33f, ["paint"] = 1f, ["blendWidth"] = 20f, ["blendFar"] = 40f,
            ["blendStrength"] = 0.6f, ["gardenPaint"] = 0.85f,
            // planting and features
            ["plant"] = 1f, ["bedChance"] = 65f, ["bushesPerGarden"] = 14f, ["features"] = 1f, ["featureChance"] = 55f,
            ["featureMinLand"] = 100f, ["rockChance"] = 12f,
            // paths and shops
            ["pathChance"] = 45f, ["pavedFill"] = 1f,
            // closing gardens: pieces of the house's own fence kind (or one picked) along every open edge
            ["closeGardens"] = 1f, ["fenceKind"] = 0f,
            // lawn near the house, painted ground further out
            ["lawnReach"] = 25f,
            // ground types (0 none, 1 dry grass, 2 lush, 3 dirt, 4 sand) for every job paint does, with widths and strengths
            ["gardenLayer"] = 2f, ["meadowLayer"] = 1f, ["meadowStrength"] = 0.6f, ["blendLayer"] = 2f, ["blendFarLayer"] = 1f,
            ["edgeLayer"] = 3f, ["edgeWidth"] = 2.4f, ["edgeStrength"] = 0.45f, ["roadLayer"] = 0f, ["roadWidth"] = 4f, ["roadStrength"] = 0.5f,
            ["rockLayer"] = 3f, ["rockGround"] = 1f, ["yardLayer"] = 3f, ["patchLayer"] = 3f, ["patchChance"] = 25f,
            // more features in bigger gardens: one for every this much new land
            ["featureSpacing"] = 600f,
        };

        /// <summary>The knobs that say how far a garden reaches: the plan looks this far around its houses.</summary>
        public static readonly string[] ReachKnobs = { "sideReach", "maxFrontGap", "maxBackGap", "maxRearGap", "maxFacingGap", "maxDeepen", "hugReach", "openReach" };

        public enum KnobKind { Toggle, Slider, Choice }

        /// <summary>One setting of a look as the Zoning types' option rows show it (Core/Layers/Types/Zoning/PolishLookLayer).</summary>
        public sealed class Knob
        {
            public readonly string Id, Group, Unit;
            public readonly KnobKind Kind;
            public readonly ControlTier Tier;
            public readonly double Min, Max, Step;
            public readonly string[] Options;

            public Knob(string id, string group, KnobKind kind, ControlTier tier, double min = 0, double max = 1, double step = 1, string unit = "", string[] options = null)
            {
                Id = id; Group = group; Kind = kind; Tier = tier; Min = min; Max = max; Step = step; Unit = unit; Options = options ?? new string[0];
            }
        }

        /// <summary>Every setting of a look, in the order the rows show them (groups: reach, fences, lawns, ground, planting, paths).
        /// Choices of ground: 0 none, 1 dry grass, 2 lush, 3 dirt, 4 sand; fenceKind: 0 own, 1 low hedge, 2 tall hedge, 3 low fence, 4 tall fence.</summary>
        public static readonly IReadOnlyList<Knob> Knobs = new[]
        {
            new Knob("sideReach", "reach", KnobKind.Slider, ControlTier.Essential, 0, 500, 1, "m"),
            new Knob("maxFrontGap", "reach", KnobKind.Slider, ControlTier.More, 0, 500, 1, "m"),
            new Knob("maxBackGap", "reach", KnobKind.Slider, ControlTier.More, 0, 500, 1, "m"),
            new Knob("maxRearGap", "reach", KnobKind.Slider, ControlTier.More, 0, 500, 1, "m"),
            new Knob("maxFacingGap", "reach", KnobKind.Slider, ControlTier.More, 0, 500, 1, "m"),
            new Knob("maxDeepen", "reach", KnobKind.Slider, ControlTier.More, 0, 500, 1, "m"),
            new Knob("hug", "reach", KnobKind.Toggle, ControlTier.Essential),
            new Knob("hugReach", "reach", KnobKind.Slider, ControlTier.More, 0, 500, 1, "m"),
            new Knob("openReach", "reach", KnobKind.Slider, ControlTier.Essential, 0, 500, 1, "m"),
            new Knob("maxSlope", "reach", KnobKind.Slider, ControlTier.Essential, 0, 150, 5, "%"),
            new Knob("maxRise", "reach", KnobKind.Slider, ControlTier.More, 0, 40, 0.5, "m"),
            new Knob("wrap", "reach", KnobKind.Toggle, ControlTier.More),
            new Knob("closeBacks", "reach", KnobKind.Toggle, ControlTier.More),
            new Knob("closeGardens", "fences", KnobKind.Toggle, ControlTier.Essential),
            new Knob("fenceKind", "fences", KnobKind.Choice, ControlTier.More, options: new[] { "0", "1", "2", "3", "4" }),
            new Knob("lawns", "lawns", KnobKind.Toggle, ControlTier.Essential),
            new Knob("lawnReach", "lawns", KnobKind.Slider, ControlTier.More, 0, 500, 1, "m"),
            new Knob("paintedGardens", "lawns", KnobKind.Slider, ControlTier.Essential, 0, 100, 5, "%"),
            new Knob("gardenPaint", "lawns", KnobKind.Slider, ControlTier.More, 0, 1, 0.05, ""),
            new Knob("paint", "lawns", KnobKind.Toggle, ControlTier.More),
            new Knob("blendWidth", "lawns", KnobKind.Slider, ControlTier.More, 0, 150, 1, "m"),
            new Knob("blendFar", "lawns", KnobKind.Slider, ControlTier.More, 0, 250, 1, "m"),
            new Knob("blendStrength", "lawns", KnobKind.Slider, ControlTier.More, 0, 1, 0.05, ""),
            new Knob("gardenLayer", "ground", KnobKind.Choice, ControlTier.More, options: new[] { "0", "1", "2", "3", "4" }),
            new Knob("meadowLayer", "ground", KnobKind.Choice, ControlTier.More, options: new[] { "0", "1", "2", "3", "4" }),
            new Knob("meadowStrength", "ground", KnobKind.Slider, ControlTier.More, 0, 1, 0.05, ""),
            new Knob("blendLayer", "ground", KnobKind.Choice, ControlTier.More, options: new[] { "0", "1", "2", "3", "4" }),
            new Knob("blendFarLayer", "ground", KnobKind.Choice, ControlTier.More, options: new[] { "0", "1", "2", "3", "4" }),
            new Knob("edgeLayer", "ground", KnobKind.Choice, ControlTier.More, options: new[] { "0", "1", "2", "3", "4" }),
            new Knob("edgeWidth", "ground", KnobKind.Slider, ControlTier.More, 0, 15, 0.5, "m"),
            new Knob("edgeStrength", "ground", KnobKind.Slider, ControlTier.More, 0, 1, 0.05, ""),
            new Knob("roadLayer", "ground", KnobKind.Choice, ControlTier.More, options: new[] { "0", "1", "2", "3", "4" }),
            new Knob("roadWidth", "ground", KnobKind.Slider, ControlTier.More, 0, 20, 0.5, "m"),
            new Knob("roadStrength", "ground", KnobKind.Slider, ControlTier.More, 0, 1, 0.05, ""),
            new Knob("rockLayer", "ground", KnobKind.Choice, ControlTier.More, options: new[] { "0", "1", "2", "3", "4" }),
            new Knob("rockGround", "ground", KnobKind.Choice, ControlTier.More, options: new[] { "0", "1", "2", "3", "4" }),
            new Knob("yardLayer", "ground", KnobKind.Choice, ControlTier.More, options: new[] { "0", "1", "2", "3", "4" }),
            new Knob("patchLayer", "ground", KnobKind.Choice, ControlTier.More, options: new[] { "0", "1", "2", "3", "4" }),
            new Knob("patchChance", "ground", KnobKind.Slider, ControlTier.More, 0, 100, 5, "%"),
            new Knob("plant", "planting", KnobKind.Toggle, ControlTier.Essential),
            new Knob("bedChance", "planting", KnobKind.Slider, ControlTier.More, 0, 100, 5, "%"),
            new Knob("bushesPerGarden", "planting", KnobKind.Slider, ControlTier.More, 0, 80, 1, "count"),
            new Knob("features", "planting", KnobKind.Toggle, ControlTier.Essential),
            new Knob("featureChance", "planting", KnobKind.Slider, ControlTier.More, 0, 100, 5, "%"),
            new Knob("featureSpacing", "planting", KnobKind.Slider, ControlTier.More, 50, 3000, 50, "m2"),
            new Knob("featureMinLand", "planting", KnobKind.Slider, ControlTier.More, 10, 600, 10, "m2"),
            new Knob("rockChance", "planting", KnobKind.Slider, ControlTier.More, 0, 100, 5, "%"),
            new Knob("pathChance", "paths", KnobKind.Slider, ControlTier.More, 0, 100, 5, "%"),
            new Knob("alleys", "paths", KnobKind.Toggle, ControlTier.More),
            new Knob("alleyChance", "paths", KnobKind.Slider, ControlTier.More, 0, 100, 5, "%"),
            new Knob("pavedFill", "paths", KnobKind.Toggle, ControlTier.Essential),
        };

        public static readonly string[] Themes = { "garden", "natural", "tidy", "yard" };

        /// <summary>The looks that ship with the mod (one per theme).</summary>
        public static readonly IReadOnlyList<PolishLook> BuiltIn = new[]
        {
            new PolishLook { Id = "garden", Name = "Placemaker.UI.Painter.Theme.garden", Theme = "garden" },
            new PolishLook { Id = "natural", Name = "Placemaker.UI.Painter.Theme.natural", Theme = "natural", Knobs = { ["featureChance"] = 75f, ["rockChance"] = 35f, ["bedChance"] = 0f } },
            new PolishLook { Id = "tidy", Name = "Placemaker.UI.Painter.Theme.tidy", Theme = "tidy", Knobs = { ["bedChance"] = 90f, ["rockChance"] = 0f } },
            new PolishLook { Id = "yard", Name = "Placemaker.UI.Painter.Theme.yard", Theme = "yard", Knobs = { ["bedChance"] = 0f, ["rockChance"] = 0f, ["gardenLayer"] = 3f, ["meadowLayer"] = 3f, ["blendLayer"] = 3f, ["patchChance"] = 60f } },
            // more ready looks, each a different mix of ground
            new PolishLook { Id = "meadow", Name = "Placemaker.UI.Painter.Theme.meadow", Theme = "natural", Knobs = { ["paintedGardens"] = 70f, ["gardenLayer"] = 1f, ["meadowLayer"] = 1f, ["blendLayer"] = 2f, ["blendFarLayer"] = 1f, ["lawnReach"] = 12f, ["featureChance"] = 75f, ["rockChance"] = 35f, ["bedChance"] = 0f, ["patchChance"] = 30f, ["patchLayer"] = 1f } },
            new PolishLook { Id = "rustic", Name = "Placemaker.UI.Painter.Theme.rustic", Theme = "garden", Knobs = { ["paintedGardens"] = 50f, ["gardenLayer"] = 1f, ["meadowLayer"] = 1f, ["edgeLayer"] = 3f, ["edgeWidth"] = 3.5f, ["edgeStrength"] = 0.6f, ["patchChance"] = 70f, ["patchLayer"] = 3f, ["rockChance"] = 30f, ["roadLayer"] = 3f, ["roadWidth"] = 3f, ["roadStrength"] = 0.45f } },
            new PolishLook { Id = "coastal", Name = "Placemaker.UI.Painter.Theme.coastal", Theme = "natural", Knobs = { ["paintedGardens"] = 60f, ["gardenLayer"] = 4f, ["meadowLayer"] = 1f, ["blendLayer"] = 4f, ["blendFarLayer"] = 1f, ["roadLayer"] = 4f, ["roadWidth"] = 5f, ["roadStrength"] = 0.6f, ["rockLayer"] = 4f, ["rockGround"] = 4f, ["patchLayer"] = 4f, ["patchChance"] = 40f, ["rockChance"] = 30f } },
            new PolishLook { Id = "manicured", Name = "Placemaker.UI.Painter.Theme.manicured", Theme = "tidy", Knobs = { ["paintedGardens"] = 0f, ["lawnReach"] = 500f, ["blendLayer"] = 2f, ["blendFarLayer"] = 0f, ["edgeLayer"] = 0f, ["patchChance"] = 0f, ["rockChance"] = 0f, ["bedChance"] = 95f } },
        };

        /// <summary>A look id's number on a lot (FNV-1a, never 0: 0 is "no look", a lot polished before looks).</summary>
        public static int Hash(string id)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in id ?? "") { h ^= c; h *= 16777619; }
                return h == 0 ? 1 : (int)h;
            }
        }

        /// <summary>The player's own looks from their file (JSON: {"looks":[{id,name,theme,knobs:{…}}]}).</summary>
        public static List<PolishLook> Read(string json)
        {
            var list = new List<PolishLook>();
            if (string.IsNullOrWhiteSpace(json)) return list;
            JsonValue doc;
            try { doc = JsonValue.Parse(json); }
            catch (Exception) { return list; }
            foreach (var o in doc["looks"].Items)
            {
                var id = o["id"].AsString();
                if (string.IsNullOrEmpty(id) || BuiltIn.Any(b => b.Id == id) || list.Any(l => l.Id == id)) continue;
                var look = new PolishLook { Id = id, Name = o["name"].AsString(id), Custom = true, Theme = Array.IndexOf(Themes, o["theme"].AsString()) >= 0 ? o["theme"].AsString() : "garden" };
                foreach (var kv in o["knobs"].Properties)
                    if (Defaults.ContainsKey(kv.Key) && kv.Value.Kind == JsonKind.Number) look.Knobs[kv.Key] = (float)kv.Value.AsNumber();
                list.Add(look);
            }
            return list;
        }

        public static string Write(IEnumerable<PolishLook> looks)
        {
            var arr = JsonValue.Arr();
            foreach (var l in looks.Where(l => l.Custom))
            {
                var knobs = JsonValue.Obj();
                foreach (var kv in l.Knobs.OrderBy(k => k.Key)) knobs.Set(kv.Key, kv.Value);
                arr.Add(JsonValue.Obj().Set("id", l.Id).Set("name", l.Name).Set("theme", l.Theme).Set("knobs", knobs));
            }
            return JsonValue.Obj().Set("looks", arr).Write(true);
        }

        /// <summary>Only the knobs that differ from the defaults (a look keeps what makes it itself).</summary>
        public static Dictionary<string, float> Diff(IReadOnlyDictionary<string, float> values, IReadOnlyDictionary<string, float> from)
        {
            var d = new Dictionary<string, float>();
            foreach (var kv in values)
                if (!from.TryGetValue(kv.Key, out var f) || Math.Abs(f - kv.Value) > 1e-4f) d[kv.Key] = kv.Value;
            return d;
        }
    }
}
