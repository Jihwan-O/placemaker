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
using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Neighbours;
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
        private string _painterTheme = null;   // the look picked ("zone": each building its zone's look); null: the last one
        private Dictionary<string, float> _lookValues = new Dictionary<string, float>(PolishLooks.Defaults);
        private string _lookTheme = "garden";

        private string PainterLook
        {
            get
            {
                if (_painterTheme == null) SelectLook(global::Placemaker.Game.Tool.ToolRegistration.Settings?.PolishLookLast ?? "zone", remember: false);
                return _painterTheme;
            }
        }

        /// <summary>A look picked in the polish tool (its values become the rows'), or "zone".</summary>
        private void SelectLook(string id, bool remember = true)
        {
            var look = id == "zone" ? null : NeighbourPolishSystem.LookById(id);
            if (look == null && id != "zone") id = "zone";
            _painterTheme = id;
            _lookValues = look != null ? look.Values() : new Dictionary<string, float>(PolishLooks.Defaults);
            _lookTheme = look?.Theme ?? "garden";
            NeighbourPolishSystem.NoteReach(_lookValues);
            LiveChanged();
            var s = global::Placemaker.Game.Tool.ToolRegistration.Settings;
            if (remember && s != null && s.PolishLookLast != id) { s.PolishLookLast = id; s.ApplyAndSave(); }
            Touch();
        }

        private PolishLook CurrentLook => NeighbourPolishSystem.LookById(PainterLook);

        // Live polish (the switch beside Polish): the buildings in the selection are polished again a moment after every
        // change of the look or the selection; the selection stays (Polish clears it). Unpolish puts them back as ever.
        private bool _polishLive;
        private int _liveVersion, _liveDone = -1;
        private float _liveAt;
        private AreaShape _liveShape;
        private const float LiveDelay = 0.4f;

        public bool PolishLive => _polishLive;

        public void SetPolishLive(bool on)
        {
            if (on == _polishLive) return;
            _polishLive = on;
            LiveChanged();
            Touch();
        }

        private void LiveChanged()
        {
            _liveVersion++;
            _liveAt = UnityEngine.Time.realtimeSinceStartup;
        }

        /// <summary>Every frame: the selection polished again once the look or the selection has settled for a moment.</summary>
        private void UpdateLivePolish()
        {
            if (!_painter || !_polishLive || _building) return;
            if (!ReferenceEquals(_liveShape, _shape)) { _liveShape = _shape; LiveChanged(); }
            if (_shape == null || _liveDone == _liveVersion || _gesture != Gesture.None || UnityEngine.Time.realtimeSinceStartup - _liveAt < LiveDelay) return;
            _liveDone = _liveVersion;
            var polish = World.GetExistingSystemManaged<NeighbourPolishSystem>();
            if (polish == null) return;
            var lots = PainterLots(polish);
            if (lots.Count == 0) return;
            NeighbourPolishSystem.Stamp(lots, true, PainterLook, LookChanges());
            LastStamp = (lots.Count, true);
            Mod.Log.Info($"Placemaker painter: live polish of {lots.Count} lots ({PainterLook}{(LookChanges() != null ? ", changed" : "")})");
        }

        /// <summary>What the rows changed from the picked look (null: nothing), "theme" its base theme's index.</summary>
        private Dictionary<string, float> LookChanges()
        {
            var look = CurrentLook;
            if (look == null) return null;
            var d = PolishLooks.Diff(_lookValues, look.Values());
            if (_lookTheme != look.Theme) d["theme"] = Array.IndexOf(PolishLooks.Themes, _lookTheme);
            return d.Count > 0 ? d : null;
        }

        /// <summary>For the game runner: a setting of the look as the rows have it; the changes Polish would give.</summary>
        public float PolishKnobValue(string id) => _lookValues.TryGetValue(id, out var v) ? v : float.NaN;
        public Dictionary<string, float> PolishChanges => LookChanges();

        /// <summary>A look row of the polish tool ("theme": the base theme's index).</summary>
        public void SetPolishKnob(string id, double value)
        {
            if (id == "theme") { int t = (int)Math.Round(value); if (t >= 0 && t < PolishLooks.Themes.Length) _lookTheme = PolishLooks.Themes[t]; }
            else if (PolishLooks.Defaults.ContainsKey(id)) _lookValues[id] = (float)value;
            else return;
            NeighbourPolishSystem.NoteReach(_lookValues);
            LiveChanged();
            Touch();
        }

        /// <summary>The look's buttons: "save" (over the player's own look), "saveAs" (a new look named <paramref name="arg"/>),
        /// "rename" (the picked look to <paramref name="arg"/>), "delete" (the picked look), "revert" (back to its saved values).</summary>
        public void PolishLookAction(string what, string arg)
        {
            var look = CurrentLook;
            switch (what)
            {
                case "save":
                    if (look == null || !look.Custom) return;
                    NeighbourPolishSystem.SaveLook(look.Id, look.Name, _lookTheme, _lookValues);
                    SelectLook(look.Id);
                    break;
                case "saveAs":
                    var id = NeighbourPolishSystem.SaveLook(null, string.IsNullOrWhiteSpace(arg) ? "My look" : arg, _lookTheme, _lookValues);
                    SelectLook(id);
                    break;
                case "rename":
                    if (look != null && look.Custom && NeighbourPolishSystem.RenameLook(look.Id, arg)) Touch();
                    break;
                case "delete":
                    if (look != null && look.Custom && NeighbourPolishSystem.DeleteLook(look.Id))
                    {
                        var s = global::Placemaker.Game.Tool.ToolRegistration.Settings;
                        if (s != null) { s.PolishZoneLooks = NeighbourPolishSystem.ZoneLooksJson(); s.ApplyAndSave(); }
                        SelectLook("garden");
                    }
                    break;
                case "revert":
                    if (look != null) SelectLook(look.Id);
                    break;
            }
        }

        /// <summary>The City window: a zone type's look for new buildings and Polish the whole city ("off"; "" = as its kind).</summary>
        public void SetPolishZoneLook(string zone, string look)
        {
            if (!string.IsNullOrEmpty(look) && look != "off" && NeighbourPolishSystem.LookById(look) == null) return;
            NeighbourPolishSystem.SetZoneLook(zone, look);
            var s = global::Placemaker.Game.Tool.ToolRegistration.Settings;
            if (s != null) { s.PolishZoneLooks = NeighbourPolishSystem.ZoneLooksJson(); s.ApplyAndSave(); }
            Touch();
        }

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
        public string PainterTheme => PainterLook;

        /// <summary>The theme the painter's Polish gives: "zone" (by each house's zone) or a theme name.</summary>
        public void SetPainterTheme(string theme)
        {
            if (string.IsNullOrEmpty(theme) || theme == PainterLook) return;
            SelectLook(theme);
        }
        private BrushRules _painterRules;
        private AreaShape _countShape;
        private readonly List<(string Key, string Zone, string Icon, int Count)> _areaZones = new List<(string, string, string, int)>();
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
        /// The panel's Whole city rows: "on" / "newHouses" (value "true" / "false"), a kind's look for new buildings ("homes",
        /// "shops", "offices", "industry": "auto" each zone type its own, a look id, off), "polishAll" (every building with the Style picked
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
                    if (value != "off" && value != NeighbourPolishSystem.KindAuto && NeighbourPolishSystem.LookById(value) == null) return;
                    if (what == "homes") s.PolishThemeHomes = value;
                    else if (what == "shops") s.PolishThemeShops = value;
                    else if (what == "offices") s.PolishThemeOffices = value;
                    else s.PolishThemeIndustry = value;
                    break;
                case "polishAll":
                    if (!s.NeighbourhoodPolish) s.NeighbourhoodPolish = true;
                    s.ApplyAndSave();
                    NeighbourPolishSystem.PolishEverything(0);
                    Mod.Log.Info("Placemaker painter: polish the whole city (each building its zone's look)");
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

        /// <summary>Polish (yes) or Unpolish (no) the lots in the area. The selection stays (press it again as often as wanted).</summary>
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
            // "By zone": each lot gets its zone's look; a look picked: that look, and what the rows changed in it
            NeighbourPolishSystem.Stamp(lots, yes, PainterLook, yes ? LookChanges() : null);
            LastStamp = (lots.Count, yes);
            Mod.Log.Info($"Placemaker painter: {(yes ? "polish" : "unpolish")} {lots.Count} lots ({AreaM2:F0} m² picked with {SelectionModes.Id(_mode)})");
            // the selection stays: Polish again (another look, changed settings) or Unpolish whenever
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
                if (!_painter && !_wasPolishType || _shape == null) return (0, 0);
                if (!ReferenceEquals(_countShape, _shape) || _countFlags != NeighbourPolishSystem.FlagsVersion)
                {
                    _countShape = _shape;
                    _countFlags = NeighbourPolishSystem.FlagsVersion;
                    _countLots = _countYes = 0;
                    _areaZones.Clear();
                    var polish = World.GetExistingSystemManaged<NeighbourPolishSystem>();
                    if (polish != null)
                    {
                        var byKey = new Dictionary<string, (string Zone, int Count)>();
                        foreach (var e in PainterLots(polish))
                        {
                            _countLots++;
                            if (polish.IsYes(e)) _countYes++;
                            string zone = polish.ZoneOfLot(e);
                            string key = NeighbourPolishSystem.ZoneKey(zone);
                            if (key == "") continue;
                            byKey[key] = (byKey.TryGetValue(key, out var z) ? z.Zone : zone, (byKey.TryGetValue(key, out var z2) ? z2.Count : 0) + 1);
                        }
                        var icons = new Dictionary<string, string>();
                        foreach (var zz in polish.Zones()) icons[zz.Id] = zz.Icon;
                        foreach (var kv in byKey.OrderByDescending(kv => kv.Value.Count))
                            _areaZones.Add((kv.Key, kv.Value.Zone, icons.TryGetValue(kv.Value.Zone, out var ic) ? ic : "", kv.Value.Count));
                    }
                }
                return (_countLots, _countYes);
            }
        }

        private PainterView PainterState()
        {
            if (!_painter && !_wasPolishType) return null;
            var (lots, yes) = PainterCount;
            var s = global::Placemaker.Game.Tool.ToolRegistration.Settings;
            var v = new PainterView
            {
                Lots = lots, Polished = yes, FeatureOn = NeighbourPolishSystem.FeatureEnabled, NewHouses = NeighbourPolishSystem.PolishNew, Theme = PainterLook,
                Tool = _painter,
                CityPolished = NeighbourPolishSystem.StatusPolished, CityTotal = NeighbourPolishSystem.StatusTotal,
                Waiting = NeighbourPolishSystem.StatusWaiting, Restoring = NeighbourPolishSystem.StatusRestoring,
            };
            var current = CurrentLook;
            v.LookTheme = _lookTheme;
            v.Live = _polishLive;
            v.Modified = LookChanges() != null;
            v.Values = new Dictionary<string, float>(_lookValues);
            v.LookValues = current != null ? current.Values() : new Dictionary<string, float>(PolishLooks.Defaults);
            v.LookSavedTheme = current?.Theme ?? "garden";
            foreach (var l in NeighbourPolishSystem.Looks) v.Looks.Add((l.Id, l.Name, l.Custom, l.Theme));
            v.AreaZones.AddRange(_areaZones);
            var polish = World.GetExistingSystemManaged<NeighbourPolishSystem>();
            if (polish != null)
                foreach (var (zone, kind, icon) in polish.Zones())
                    v.Zones.Add((zone, kind, NeighbourPolishSystem.ZoneLook(zone, kind), NeighbourPolishSystem.ZoneHasOwnLook(zone), icon, NeighbourPolishSystem.DefaultZoneLook(zone, kind)));
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
