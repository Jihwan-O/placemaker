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

        /// <summary>The values a Zoning type's rows have (its look's for any row not set).</summary>
        private Dictionary<string, float> PolishValues(PolishLookLayer layer)
        {
            var values = (PolishLooks.BuiltIn.FirstOrDefault(l => l.Id == layer.LookId) ?? PolishLooks.BuiltIn[0]).Values();
            var bag = _recipe.Root.Settings;
            foreach (var k in PolishLooks.Knobs)
            {
                if (!bag.Values.TryGetValue(k.Id, out var v) || v == null) continue;
                switch (v)
                {
                    case bool b: values[k.Id] = b ? 1f : 0f; break;
                    case string str:
                        if (float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) values[k.Id] = f;
                        break;
                    case IConvertible c: values[k.Id] = (float)c.ToDouble(CultureInfo.InvariantCulture); break;
                }
            }
            return values;
        }

        /// <summary>Build for a Zoning type: the buildings in the area polished with the look and the rows' values. False when
        /// the design is not a Zoning type, or zones the area too (then the normal build zones it).</summary>
        private bool BuildPolish()
        {
            var layer = _recipe == null ? null : PolishLookLayer.Of(_recipe.Root.Layer);
            if (layer == null) return false;
            var polish = World.GetExistingSystemManaged<NeighbourPolishSystem>();
            if (polish == null || _shape == null) return true;
            var lots = PainterLots(polish);
            bool zoning = _plan != null && _plan.Layers.Any(l => l.Zoning.Count > 0);
            if (lots.Count == 0 && !zoning) { Flash(new LocText("Placemaker.Painter.NoLots")); return true; }
            var look = (PolishLooks.BuiltIn.FirstOrDefault(l => l.Id == layer.LookId) ?? PolishLooks.BuiltIn[0]).Values();
            var values = PolishValues(layer);
            var changes = PolishLooks.Diff(values, look);
            if (lots.Count > 0) NeighbourPolishSystem.Stamp(lots, true, layer.LookId, changes.Count > 0 ? changes : null);
            NeighbourPolishSystem.NoteReach(values);
            if (zoning)
            {
                NeighbourPolishSystem.AddLookArea(_shape.Ring2D(), layer.LookId, changes.Count > 0 ? changes : null);
                int cells = _plan.Layers.Sum(l => l.Zoning.Count);
                Mod.Log.Info($"Placemaker zoning: {cells} zone cells to zone with {layer.LookId}, {lots.Count} buildings polished now ({changes.Count} settings changed from the look; {AreaM2:F0} m² picked with {SelectionModes.Id(_mode)})");
                return false;
            }
            LastStamp = (lots.Count, true);
            Mod.Log.Info($"Placemaker zoning: {lots.Count} buildings polished with {layer.LookId} ({changes.Count} settings changed from the look; {AreaM2:F0} m² picked with {SelectionModes.Id(_mode)})");
            Flash(new LocText("Placemaker.Polish.Built", lots.Count), 5f);
            SuiteNoteBuilt(0);
            _countShape = null;
            Touch();
            return true;
        }
    }
}
