// Placemaker — the neighbourhood-polish painter: "Polish gardens" in the Make row (GPL-3.0).
//
// Not a type: picked the way a type is picked, it turns the tool into a stamp for the lots' saved yes/no flag
// (Game/Neighbours/NeighbourPolishSystem.Lots). The lots are picked with the usual area tools (Points, Paint bucket,
// Paint area), fenced only by roads, paths, tracks and water (the lots themselves are what is picked); Polish stamps yes,
// Unpolish stamps no, on every lot whose middle lies in the area. The selection goes after the stamp: only the flags stay,
// nothing is a lasting zone. While the painter is the pick, every polished lot is tinted green (PlacemakerOverlaySystem).
// Nothing is planned or built: no plan, no ghost, no Review.

using System;
using System.Collections.Generic;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Tool;
using Placemaker.Game.Neighbours;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        /// <summary>The painter's Make-row entry (Recipes/shelf/neighbourhood.json lists it).</summary>
        public const string PainterId = "polishGardens";

        private bool _painter;
        private string _painterTheme = "zone";

        /// <summary>The theme the painter's Polish gives: "zone" (by each house's zone) or a theme name.</summary>
        public void SetPainterTheme(string theme)
        {
            if (string.IsNullOrEmpty(theme) || Array.IndexOf(NeighbourPolishSystem.ThemeNames, theme) < 0 || theme == _painterTheme) return;
            _painterTheme = theme;
            Touch();
        }
        private BrushRules _painterRules;
        private AreaShape _countShape;
        private int _countFlags = -1;
        private int _countLots, _countYes;

        /// <summary>The painter is the pick: the area is stamped, never built.</summary>
        public bool Painter => _painter;

        /// <summary>For the game runner: the last stamp (how many lots, yes or no).</summary>
        public (int Lots, bool Yes) LastStamp { get; private set; }

        /// <summary>Game runner: a scenario starts on its type, not on the painter a scenario before it left picked.</summary>
        public void SuiteLeavePainter() { if (SetPainter(false)) DesignChanged(); }

        /// <summary>Picks or leaves the painter. True when it changed.</summary>
        private bool SetPainter(bool on)
        {
            if (on == _painter) return false;
            CancelGesture();
            ExitMarkerMode();
            _painter = on;
            if (on && _painterRules == null) _painterRules = PainterRulesFrom(PlayerBrushRules);
            // other stops: the fills and the outline are worked out again
            _fills?.Invalidate();
            _hoverFace = null;
            _hoverFaceAt = new float2(float.MaxValue);
            if (_mode == SelectionMode.Block) RepickBlocks();
            else if (_mode == SelectionMode.Draw && _draw.Closed) ShapeChanged();
            // a painted area keeps the outline it was painted with (the other stops): it goes, and is painted again
            else if ((_mode == SelectionMode.Paint || _mode == SelectionMode.BlockPaint) && _shape != null) ClearArea();
            if (on && _stage == ToolStage.Review) SetStage(ToolStage.Design);
            _countShape = null;
            DesignChanged();
            Touch();
            return true;
        }

        /// <summary>The painter's stops: roads, paths, tracks and water as the player has them; never buildings, lots, zoning or builds.</summary>
        private static BrushRules PainterRulesFrom(BrushRules r)
        {
            var p = (r ?? BrushRules.Defaults()).Clone();
            p.Buildings = false;
            p.Lots = false;
            p.Builds = false;
            p.SetZoningLevel(0);
            p.Anything = false;
            p.DrawClip = false;
            return p.Normalized();
        }

        /// <summary>Polish (yes) or Unpolish (no) the lots in the area. The selection goes; only the flags stay.</summary>
        public void PolishStamp(bool yes)
        {
            if (!_painter || _building) return;
            var polish = World.GetExistingSystemManaged<NeighbourPolishSystem>();
            if (polish == null) return;
            if (_shape == null) { Flash(new LocText("Placemaker.Reason.NoArea")); return; }
            var lots = polish.LotsInside(_shape.Ring2D());
            if (lots.Count == 0) { Flash(new LocText("Placemaker.Painter.NoLots")); return; }
            NeighbourPolishSystem.Stamp(lots, yes, (byte)Math.Max(0, Array.IndexOf(NeighbourPolishSystem.ThemeNames, _painterTheme)));
            LastStamp = (lots.Count, yes);
            Mod.Log.Info($"Placemaker painter: {(yes ? "polish" : "unpolish")} {lots.Count} lots ({AreaM2:F0} m² picked with {SelectionModes.Id(_mode)})");
            ClearArea();
            Flash(new LocText(yes ? "Placemaker.Painter.Polished" : "Placemaker.Painter.Unpolished", lots.Count));
        }

        /// <summary>Lots in the current area and how many of them are polished (for the panel).</summary>
        public (int Lots, int Yes) PainterCount
        {
            get
            {
                if (!_painter || _shape == null) return (0, 0);
                if (!ReferenceEquals(_countShape, _shape) || _countFlags != NeighbourPolishSystem.FlagsVersion)
                {
                    _countShape = _shape;
                    _countFlags = NeighbourPolishSystem.FlagsVersion;
                    _countLots = _countYes = 0;
                    var polish = World.GetExistingSystemManaged<NeighbourPolishSystem>();
                    if (polish != null)
                        foreach (var e in polish.LotsInside(_shape.Ring2D()))
                        {
                            _countLots++;
                            if (polish.IsYes(e)) _countYes++;
                        }
                }
                return (_countLots, _countYes);
            }
        }

        private PainterView PainterState()
        {
            if (!_painter) return null;
            var (lots, yes) = PainterCount;
            return new PainterView
            {
                Lots = lots, Polished = yes, FeatureOn = NeighbourPolishSystem.FeatureEnabled, NewHouses = NeighbourPolishSystem.PolishNew, Theme = _painterTheme,
            };
        }
    }
}
