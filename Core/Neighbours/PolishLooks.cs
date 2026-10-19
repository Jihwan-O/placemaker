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
            // lawns and ground paint
            ["lawns"] = 1f, ["paintedGardens"] = 33f, ["paint"] = 1f, ["blendWidth"] = 20f, ["blendFar"] = 40f,
            ["blendStrength"] = 0.6f, ["gardenPaint"] = 0.85f,
            // planting and features
            ["plant"] = 1f, ["bedChance"] = 65f, ["bushesPerGarden"] = 14f, ["features"] = 1f, ["featureChance"] = 55f,
            ["featureMinLand"] = 100f, ["rockChance"] = 12f,
            // paths and shops
            ["pathChance"] = 45f, ["pavedFill"] = 1f,
        };

        /// <summary>The knobs that say how far a garden reaches: the plan looks this far around its houses.</summary>
        public static readonly string[] ReachKnobs = { "sideReach", "maxFrontGap", "maxBackGap", "maxRearGap", "maxFacingGap", "maxDeepen", "hugReach", "openReach" };

        public static readonly string[] Themes = { "garden", "natural", "tidy", "yard" };

        /// <summary>The looks that ship with the mod (one per theme).</summary>
        public static readonly IReadOnlyList<PolishLook> BuiltIn = new[]
        {
            new PolishLook { Id = "garden", Name = "Placemaker.UI.Painter.Theme.garden", Theme = "garden" },
            new PolishLook { Id = "natural", Name = "Placemaker.UI.Painter.Theme.natural", Theme = "natural", Knobs = { ["featureChance"] = 75f, ["rockChance"] = 35f, ["bedChance"] = 0f } },
            new PolishLook { Id = "tidy", Name = "Placemaker.UI.Painter.Theme.tidy", Theme = "tidy", Knobs = { ["bedChance"] = 90f, ["rockChance"] = 0f } },
            new PolishLook { Id = "yard", Name = "Placemaker.UI.Painter.Theme.yard", Theme = "yard", Knobs = { ["bedChance"] = 0f, ["rockChance"] = 0f } },
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
