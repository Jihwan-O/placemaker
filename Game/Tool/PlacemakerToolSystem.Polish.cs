// Placemaker — the Zoning category's looks in the tool (GPL-3.0).
//
// A Zoning type (Core/Layers/Types/Zoning/PolishLookLayer: Garden, Natural, Tidy…) goes through Placemaker's steps like
// every type: the area picks the buildings (as the polish painter's: lots and buildings are not stops, so the area takes the
// houses in it), Design shows the look's rows and styles, Review counts the buildings. Build polishes them with the look and
// the values the rows have (saved on each building: the look and every value its style changed), and the selection stays,
// so it can be built again with another look or style.
// "Zone this area" on: the plan has the zone cells; Build polishes the buildings there now, leaves a look area behind (the
// buildings that grow in it get the look and the changes: NeighbourPolishSystem.Areas) and zones the cells as every build
// does (a loose build: ordinary city zoning, no group).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Layers.Types.Zoning;
using Placemaker.Core.Neighbours;
using Placemaker.Core.Tool;
using Placemaker.Game.Neighbours;
using Unity.Mathematics;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        /// <summary>The design is a Zoning type (a look of the area's buildings).</summary>
        public bool PolishType => _recipe != null && PolishLookLayer.Of(_recipe.Root.Layer) != null;
        private bool _wasPolishType;

        /// <summary>Every frame: a Zoning type picked or left, the area tools' stops change (lots and buildings are not stops
        /// for a look: the area takes the houses), and a picked area is fenced again with them.</summary>
        private void CheckPolishType()
        {
            bool now = PolishType;
            if (now == _wasPolishType) return;
            _wasPolishType = now;
            if (now && _painterRules == null) _painterRules = PainterRulesFrom(PlayerBrushRules);
            _fills?.Invalidate();
            _hoverFace = null;
            _hoverFaceAt = new float2(float.MaxValue);
            if (_mode == SelectionMode.Block) RepickBlocks();
            else if (_mode == SelectionMode.Draw && _draw.Closed) ShapeChanged();
            else if ((_mode == SelectionMode.Paint || _mode == SelectionMode.BlockPaint) && _shape != null) ShapeChanged();
            _countShape = null;
            Touch();
        }

        /// <summary>A setting of the design's root (the type's default when not set).</summary>
        private object PolishSetting(PolishLookLayer layer, string id)
        {
            if (_recipe.Root.Settings.Values.TryGetValue(id, out var v) && v != null) return v;
            return layer.Controls.FirstOrDefault(c => c.Id == id)?.Default;
        }

        private static float AsFloat(object v)
        {
            switch (v)
            {
                case bool b: return b ? 1f : 0f;
                case string str: return float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;
                case IConvertible c: return (float)c.ToDouble(CultureInfo.InvariantCulture);
                default: return 0f;
            }
        }

        /// <summary>The values a zone's Outgrow rows have (its look's for any row not set; slot 0 A, 1 B, 2 C).</summary>
        private Dictionary<string, float> PolishValues(PolishLookLayer layer, int slot = 0)
        {
            var values = (PolishLooks.BuiltIn.FirstOrDefault(l => l.Id == layer.LookId) ?? PolishLooks.BuiltIn[0]).Values();
            var bag = _recipe.Root.Settings;
            foreach (var k in PolishLooks.Knobs)
                if (bag.Values.TryGetValue(PolishLookLayer.KnobId(k.Id, slot), out var v) && v != null) values[k.Id] = AsFloat(v);
            return values;
        }

        /// <summary>The design's zones in use (share above 0): slot, the zone it zones (prefab name).</summary>
        private List<(int Slot, string Zone)> PolishZones(PolishLookLayer layer)
        {
            var list = new List<(int, string)>();
            for (int i = 0; i < 3; i++)
            {
                string sl = PolishLookLayer.SlotNames[i];
                if (AsFloat(PolishSetting(layer, "share" + sl)) <= 0f) continue;
                string asset = PolishSetting(layer, "zoneAsset" + sl) as string;
                string kind = PolishSetting(layer, "zone" + sl) as string ?? "";
                list.Add((i, !string.IsNullOrEmpty(asset) ? asset : Placemaker.Core.Layers.Common.ZoneKinds.PrefabId(kind)));
            }
            if (list.Count == 0) list.Add((0, ""));
            return list;
        }

        /// <summary>Build for a Zoning type: the buildings in the area outgrow their lots, each by its zone's settings (a zone not
        /// in the design: the first zone's). False when the design is not a Zoning type, or zones the area too (then the normal
        /// build zones it).</summary>
        private bool BuildPolish()
        {
            var layer = _recipe == null ? null : PolishLookLayer.Of(_recipe.Root.Layer);
            if (layer == null) return false;
            var polish = World.GetExistingSystemManaged<NeighbourPolishSystem>();
            if (polish == null || _shape == null) return true;
            // Outgrow off: the buildings are left as they are (only the zoning is built)
            bool shape = AsFloat(PolishSetting(layer, "shapeLots")) > 0.5f;
            var lots = shape ? PainterLots(polish) : new List<global::Unity.Entities.Entity>();
            bool zoning = _plan != null && _plan.Layers.Any(l => l.Zoning.Count > 0);
            if (!shape && !zoning) { Flash(new LocText("Placemaker.Polish.Nothing")); return true; }
            if (lots.Count == 0 && !zoning) { Flash(new LocText("Placemaker.Painter.NoLots")); return true; }
            var look = (PolishLooks.BuiltIn.FirstOrDefault(l => l.Id == layer.LookId) ?? PolishLooks.BuiltIn[0]).Values();
            var zones = PolishZones(layer);
            var changesOf = new Dictionary<int, Dictionary<string, float>>();
            foreach (var (slot, _) in zones)
            {
                var values = PolishValues(layer, slot);
                changesOf[slot] = PolishLooks.Diff(values, look);
                NeighbourPolishSystem.NoteReach(values);
            }
            // each building by its zone
            var bySlot = new Dictionary<int, List<global::Unity.Entities.Entity>>();
            foreach (var lot in lots)
            {
                string key = NeighbourPolishSystem.ZoneKey(polish.ZoneOfLot(lot));
                int slot = zones[0].Slot;
                foreach (var (sl, zone) in zones) if (NeighbourPolishSystem.ZoneKey(zone) == key && key != "") { slot = sl; break; }
                if (!bySlot.TryGetValue(slot, out var list)) bySlot[slot] = list = new List<global::Unity.Entities.Entity>();
                list.Add(lot);
            }
            foreach (var kv in bySlot)
            {
                var ch = changesOf[kv.Key];
                NeighbourPolishSystem.Stamp(kv.Value, true, layer.LookId, ch.Count > 0 ? ch : null);
            }
            string perZone = string.Join(", ", bySlot.Select(kv => $"{kv.Value.Count} {PolishLookLayer.SlotNames[kv.Key]}"));
            if (zoning)
            {
                if (shape)
                {
                    // buildings that grow here later: their zone's settings (any other zone: the first zone's)
                    int batch = NeighbourPolishSystem.NewLookBatch();
                    var ring = _shape.Ring2D();
                    foreach (var (slot, zone) in zones)
                        if (!string.IsNullOrEmpty(zone)) NeighbourPolishSystem.AddLookArea(ring, layer.LookId, changesOf[slot].Count > 0 ? changesOf[slot] : null, zone, batch);
                    var first = changesOf[zones[0].Slot];
                    NeighbourPolishSystem.AddLookArea(ring, layer.LookId, first.Count > 0 ? first : null, "", batch);
                }
                int cells = _plan.Layers.Sum(l => l.Zoning.Count);
                Mod.Log.Info($"Placemaker zoning: {cells} zone cells to zone with {layer.LookId}, {lots.Count} buildings outgrown now by zone ({perZone}; {AreaM2:F0} m² picked with {SelectionModes.Id(_mode)})");
                return false;
            }
            LastStamp = (lots.Count, true);
            Mod.Log.Info($"Placemaker zoning: {lots.Count} buildings outgrown with {layer.LookId} by zone ({perZone}; {AreaM2:F0} m² picked with {SelectionModes.Id(_mode)})");
            Flash(new LocText("Placemaker.Polish.Built", lots.Count), 5f);
            SuiteNoteBuilt(0);
            _countShape = null;
            Touch();
            return true;
        }
    }
}
