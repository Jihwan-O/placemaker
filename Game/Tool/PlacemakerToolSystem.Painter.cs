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

        /// <summary>
        /// The polish tool's own button (next to Placemaker's): opens this tool as the polish painter, which the panel shows
        /// as its own small window (area tools, themes, Look, Whole city, Polish / Unpolish), or closes it. From Placemaker
        /// it switches over (Placemaker's own type and area stay for when its button is pressed again).
        /// </summary>
        public void TogglePolishTool()
        {
            if (m_ToolSystem == null || _building) return;
            bool active = m_ToolSystem.activeTool == this;
            if (active && _painter) { Close(); return; }
            if (_stage == ToolStage.Manage) GoToStage(ToolStage.Area);
            SetPainter(true);
            if (!active) { _closeAfterGhost = 0; m_ToolSystem.activeTool = this; }
        }

        /// <summary>For the game runner: the theme picked in the painter's Style row.</summary>
        public string PainterTheme => _painterTheme;

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
            // a painted area keeps what was painted and is fenced again with the painter's stops (lots and buildings no
            // longer stop it); lots the paint ran up against count as picked too (NeighbourPolishSystem.LotsInside)
            else if ((_mode == SelectionMode.Paint || _mode == SelectionMode.BlockPaint) && _shape != null) ShapeChanged();
            if (_stage == ToolStage.Review) SetStage(ToolStage.Design);   // Review is the other tool's: back to its Design
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

        /// <summary>The panel's look rows (Polish gardens card): saved in the settings, given by Polish from now on and to new buildings.</summary>
        public void SetPolishOption(string id, double value)
        {
            var s = global::Placemaker.Game.Tool.ToolRegistration.Settings;
            if (s == null) return;
            bool on = value > 0.5;
            switch (id)
            {
                case "painted": s.PolishPaintedGardens = (int)Math.Round(Math.Max(0, Math.Min(100, value))); break;
                case "lawns": s.PolishLawns = on; break;
                case "planting": s.PolishPlanting = on; break;
                case "features": s.PolishFeatures = on; break;
                case "paths": s.PolishPaths = on; break;
                case "rocks": s.PolishRocks = on; break;
                case "groundPaint": s.PolishGroundPaint = on; break;
                case "shopPaving": s.PolishPavedFill = on; break;
                default: return;
            }
            s.ApplyAndSave();
            Touch();
        }

        /// <summary>
        /// The panel's Whole city rows: "on" / "newHouses" (value "true" / "false"), a kind's theme for new buildings ("homes",
        /// "shops", "offices", "industry": garden, natural, tidy, yard, off), "polishAll" (every building with the Style picked
        /// and the look; By kind leaves out the kinds set to Off), "unpolishAll" (every garden back as the game made it). The
        /// city-wide two run in the background, a few buildings a frame; the panel asks first.
        /// </summary>
        public void PolishCity(string what, string value)
        {
            var s = global::Placemaker.Game.Tool.ToolRegistration.Settings;
            if (s == null) return;
            bool on = value == "true";
            switch (what)
            {
                case "on": s.NeighbourhoodPolish = on; break;
                case "newHouses": s.PolishNewHouses = on; break;
                case "homes": case "shops": case "offices": case "industry":
                    if (Array.IndexOf(global::Placemaker.Game.Settings.PlacemakerSettings.PolishThemeValues, value) < 0) return;
                    if (what == "homes") s.PolishThemeHomes = value;
                    else if (what == "shops") s.PolishThemeShops = value;
                    else if (what == "offices") s.PolishThemeOffices = value;
                    else s.PolishThemeIndustry = value;
                    break;
                case "polishAll":
                    if (!s.NeighbourhoodPolish) s.NeighbourhoodPolish = true;
                    s.ApplyAndSave();
                    NeighbourPolishSystem.PolishEverything((byte)Math.Max(0, Array.IndexOf(NeighbourPolishSystem.ThemeNames, _painterTheme)));
                    Mod.Log.Info($"Placemaker painter: polish the whole city ({_painterTheme})");
                    Flash(new LocText("Placemaker.Painter.CityPolishing"));
                    Touch();
                    return;
                case "unpolishAll":
                    NeighbourPolishSystem.RemoveAll();
                    Mod.Log.Info("Placemaker painter: unpolish the whole city");
                    Flash(new LocText("Placemaker.Painter.CityUnpolishing"));
                    Touch();
                    return;
                default: return;
            }
            s.ApplyAndSave();
            Touch();
        }

        /// <summary>Polish (yes) or Unpolish (no) the lots in the area. The selection goes; only the flags stay.</summary>
        public void PolishStamp(bool yes)
        {
            if (!_painter || _building) return;
            var polish = World.GetExistingSystemManaged<NeighbourPolishSystem>();
            if (polish == null) return;
            if (_shape == null) { Flash(new LocText("Placemaker.Reason.NoArea")); return; }
            var lots = PainterLots(polish);
            if (lots.Count == 0) { Flash(new LocText("Placemaker.Painter.NoLots")); return; }
            // polishing is what the player asked for: the polish runs from now on (Whole city: Neighbourhood polish)
            var settings = global::Placemaker.Game.Tool.ToolRegistration.Settings;
            if (yes && settings != null && !settings.NeighbourhoodPolish) { settings.NeighbourhoodPolish = true; settings.ApplyAndSave(); }
            // "By zone": each lot gets its kind's theme from the settings now; a theme button picks one for all of them
            NeighbourPolishSystem.Stamp(lots, yes, (byte)Math.Max(0, Array.IndexOf(NeighbourPolishSystem.ThemeNames, _painterTheme)));
            LastStamp = (lots.Count, yes);
            Mod.Log.Info($"Placemaker painter: {(yes ? "polish" : "unpolish")} {lots.Count} lots ({AreaM2:F0} m² picked with {SelectionModes.Id(_mode)})");
            ClearArea();
            Flash(new LocText(yes ? "Placemaker.Painter.Polished" : "Placemaker.Painter.Unpolished", lots.Count));
        }

        /// <summary>The houses the painter acts on: in the area, or (Paint area) painted over by the brush.</summary>
        private List<Entity> PainterLots(NeighbourPolishSystem polish)
        {
            var lots = polish.LotsInside(_shape.Ring2D());
            if ((_mode == SelectionMode.BlockPaint || _mode == SelectionMode.Paint) && _bpBrushed.Count > 0)
            {
                var have = new HashSet<Entity>(lots);
                foreach (var e in polish.GrowablesNear(_shape.Ring2D(), 80f))
                    if (!have.Contains(e) && BrushedOver(EntityManager.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz)) { lots.Add(e); have.Add(e); }
            }
            return lots;
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
                        foreach (var e in PainterLots(polish))
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
            var s = global::Placemaker.Game.Tool.ToolRegistration.Settings;
            var v = new PainterView
            {
                Lots = lots, Polished = yes, FeatureOn = NeighbourPolishSystem.FeatureEnabled, NewHouses = NeighbourPolishSystem.PolishNew, Theme = _painterTheme,
                CityPolished = NeighbourPolishSystem.StatusPolished, CityTotal = NeighbourPolishSystem.StatusTotal,
                Waiting = NeighbourPolishSystem.StatusWaiting, Restoring = NeighbourPolishSystem.StatusRestoring,
            };
            if (s != null)
            {
                v.Painted = s.PolishPaintedGardens;
                v.Lawns = s.PolishLawns; v.Planting = s.PolishPlanting; v.Features = s.PolishFeatures; v.Paths = s.PolishPaths;
                v.Rocks = s.PolishRocks; v.GroundPaint = s.PolishGroundPaint; v.ShopPaving = s.PolishPavedFill;
                v.Homes = s.PolishThemeHomes; v.Shops = s.PolishThemeShops; v.Offices = s.PolishThemeOffices; v.Industry = s.PolishThemeIndustry;
            }
            return v;
        }
    }
}
