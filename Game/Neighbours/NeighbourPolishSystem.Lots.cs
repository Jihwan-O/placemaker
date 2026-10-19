// Placemaker neighbourhood polish — the per-lot flag, the painter's stamps and putting a lot back (GPL-3.0).
//
// Every growable lot carries a saved yes/no flag (PolishedLot). Only yes lots are edited; a yes lot next to a no lot takes
// the whole gap up to the no lot's edge (YardStitch). The flag comes from:
//   * "Polish new houses": a lot that spawns gets yes (off: no). A building the game replaces (rebuilt after it burnt down,
//     collapsed or was bulldozed) gets the flag of the one that stood there (by position); a level-up keeps the entity.
//   * the painter (Placemaker's "Polish gardens" entry): the lots inside the picked area are stamped yes or no.
//   * "Remove all polish": every lot to no.
// Unpolish puts a lot back: its fence and hedge lines where the game put them, its own lawns as the game made them (also the
// grass a painted garden tucked under the house), and every piece the polish gave it (beds, paths, paving, owned lawns,
// bushes, rocks, a shared green) deleted at PostTool (NeighbourDeleteSystem). Its polished neighbours are planned again.

using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Placemaker.Core.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourPolishSystem
    {
        // ───────────── the painter and the settings (main thread) ─────────────

        private static readonly List<(Entity Lot, bool Yes, byte Theme, byte Parts, byte Painted)> s_stamps = new List<(Entity, bool, byte, byte, byte)>();
        private static bool s_removeAll, s_polishAll, s_retheme;
        private static byte s_polishAllTheme;

        /// <summary>Bumped whenever a lot's flag changes (the painter's overlay redraws).</summary>
        public static int FlagsVersion { get; private set; }
        /// <summary>For the game runner: totals since the game started.</summary>
        public static int TotalStampedYes, TotalStampedNo, TotalRestored, TotalCarried, TotalFlaggedNew, TotalTagged;

        /// <summary>The painter: these lots become polished (yes) or are put back (no); theme 0: each by its kind's setting.
        /// Applied from the next frame on, a few thousand a frame.</summary>
        public static void Stamp(IEnumerable<Entity> lots, bool yes, byte theme = 0)
        {
            foreach (var e in lots) s_stamps.Add((e, yes, theme, LookParts, LookPainted));
        }

        /// <summary>Setting "Remove all polish": every lot to no, everything put back (a few hundred lots a frame).</summary>
        public static void RemoveAll() => s_removeAll = true;

        /// <summary>The panel's "Polish the whole city": every growable lot standing to yes with this theme (0: each by its
        /// kind, the kinds set to Off left out) and the look now; stamped 2000 a frame, planned a patch of town a frame.</summary>
        public static void PolishEverything(byte theme = 0)
        {
            s_polishAll = true;
            s_polishAllTheme = theme;
        }

        /// <summary>Setting "Redo polished buildings": polished lots get their kind's theme again (not the ones the painter
        /// gave a theme) and are all planned again with the current settings, a patch of town per frame.</summary>
        public static void RedoAll()
        {
            s_retheme = true;
            Redo();
        }

        // ───────────── the status line in the options ─────────────

        /// <summary>For the options page: polished lots, growable lots, lots waiting to be fitted, lots being put back.</summary>
        public static int StatusPolished, StatusTotal, StatusWaiting, StatusRestoring;
        /// <summary>Bumped when the status line would read differently (the options page redraws it).</summary>
        public static int StatusVersion;
        public static bool StatusInCity;
        private int _statusFrame = -1000;
        private readonly Dictionary<Entity, bool> _growablePrefab = new Dictionary<Entity, bool>();

        private void UpdateStatus()
        {
            if (LotFrame - _statusFrame < 60) return;
            _statusFrame = LotFrame;
            int yes = 0, total = 0;
            using (var flags = _flaggedLots.ToComponentDataArray<PolishedLot>(Allocator.Temp))
                foreach (var f in flags) if (f.Yes) yes++;
            using (var refs = _growables.ToComponentDataArray<PrefabRef>(Allocator.Temp))
                foreach (var r in refs)
                {
                    if (!_growablePrefab.TryGetValue(r.m_Prefab, out var g))
                        _growablePrefab[r.m_Prefab] = g = EntityManager.HasComponent<SpawnableBuildingData>(r.m_Prefab) && EntityManager.HasComponent<BuildingData>(r.m_Prefab);
                    if (g) total++;
                }
            int waiting = FeatureEnabled ? _dirty.Count + s_stamps.Count : s_stamps.Count;
            int restoring = _restoreQueue.Count;
            if (yes != StatusPolished || total != StatusTotal || waiting != StatusWaiting || restoring != StatusRestoring || !StatusInCity)
            {
                StatusPolished = yes;
                StatusTotal = total;
                StatusWaiting = waiting;
                StatusRestoring = restoring;
                StatusInCity = true;
                StatusVersion++;
            }
        }

        /// <summary>Pieces the surface and object systems just asked the game to make: tagged ours when they appear.</summary>
        internal struct PendingTag
        {
            public Entity Owner, Prefab;
            public float2 At;
            public PolishPieceKind Kind;
            public int Frame;
        }
        internal static readonly List<PendingTag> PendingTags = new List<PendingTag>();
        internal static int LotFrame;

        private EntityQuery _newLots, _flaggedLots, _newPieces, _growables;
        // flags of buildings that went (bulldozed, replaced): given to the next one built on that spot
        private readonly List<(float2 At, bool Yes)> _goneFlags = new List<(float2, bool)>();
        private readonly List<Entity> _restoreQueue = new List<Entity>();
        private int _removeAllCount;
        // putting a lot back writes its lines and lawns and deletes its pieces: a few hundred a frame never stutter
        private const int RestoresPerFrame = 150;
        // a stamp: a flag (yes) or a lot put back (no, ten times the work)
        private const int StampBudget = 2000, StampCostNo = 10;
        private int _stampYes, _stampNo, _stampRestored;
        private bool _sweepThemes = true;

        private void CreateLotQueries()
        {
            _newLots = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.ReadOnly<Created>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<PolishedLot>() },
            });
            _flaggedLots = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PolishedLot>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            _newPieces = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<PrefabRef>() },
                Any = new[] { ComponentType.ReadOnly<global::Game.Areas.Area>(), ComponentType.ReadOnly<global::Game.Objects.Object>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<PolishPiece>() },
            });
            _growables = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<global::Game.Objects.Transform>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
        }

        private void LotsUpdate()
        {
            LotFrame++;
            RememberGoneFlags();
            FlagNewLots();
            TagNewPieces();
            if (_sweepThemes) SweepThemes();
            if (s_retheme) Retheme();
            if (s_polishAll) StartPolishAll();
            ApplyStamps();
            if (s_removeAll) StartRemoveAll();
            DrainRestores();
            UpdateStatus();
        }

        /// <summary>
        /// Lots polished before themes were kept on the lot (theme 0, "by its zone") get their kind's theme now, once per city:
        /// a later change of the settings never changes them by itself.
        /// </summary>
        private void SweepThemes()
        {
            _sweepThemes = false;
            int n = 0;
            using (var all = _flaggedLots.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    var f = EntityManager.GetComponentData<PolishedLot>(e);
                    if (!f.Yes || f.Theme != 0) continue;
                    f.Theme = ThemeByte(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab);
                    f.Picked = false;
                    EntityManager.SetComponentData(e, f);
                    n++;
                }
            if (n > 0) Mod.Log.Info($"Placemaker neighbourhood polish: {n} polished lots keep the theme their kind has now.");
        }

        /// <summary>"Redo polished buildings": every polished lot whose theme came from its kind gets its kind's theme now.</summary>
        private void Retheme()
        {
            s_retheme = false;
            int n = 0;
            using (var all = _flaggedLots.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    var f = EntityManager.GetComponentData<PolishedLot>(e);
                    if (!f.Yes || f.Picked) continue;
                    byte t = ThemeByte(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab);
                    if (t == f.Theme) continue;
                    f.Theme = t;
                    EntityManager.SetComponentData(e, f);
                    n++;
                }
            FlagsVersion++;
            Mod.Log.Info($"Placemaker neighbourhood polish: redo, {n} polished lots take their kind's new theme.");
        }

        /// <summary>"Polish every building": every growable lot not yet polished (kinds set to Off left out) is stamped yes.</summary>
        private void StartPolishAll()
        {
            s_polishAll = false;
            int n = 0, off = 0;
            using (var all = _growables.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    if (!IsGrowable(e)) continue;
                    if (s_polishAllTheme == 0 && ZoneTheme(ZoneKind(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab)) == "off") { off++; continue; }
                    s_stamps.Add((e, true, s_polishAllTheme, LookParts, LookPainted));
                    n++;
                }
            Mod.Log.Info($"Placemaker neighbourhood polish: polish the whole city, {n} lots to polish{(s_polishAllTheme > 0 ? " (" + ThemeNames[s_polishAllTheme] + ")" : "")} ({off} left out: their kind is Off).");
        }

        /// <summary>A growable building (a house, shop, office or works the game grew on zoned land).</summary>
        private bool IsGrowable(Entity building)
        {
            if (!EntityManager.HasComponent<PrefabRef>(building)) return false;
            var prefab = EntityManager.GetComponentData<PrefabRef>(building).m_Prefab;
            return EntityManager.HasComponent<SpawnableBuildingData>(prefab) && EntityManager.HasComponent<BuildingData>(prefab);
        }

        /// <summary>The lot's flag: yes (polished) or no (as the game made it; also a lot without a flag yet).</summary>
        public bool IsYes(Entity lot) => EntityManager.HasComponent<PolishedLot>(lot) && EntityManager.GetComponentData<PolishedLot>(lot).Yes;

        /// <summary>The lot's theme (0: by its zone).</summary>
        public byte ThemeOfLot(Entity lot) => EntityManager.HasComponent<PolishedLot>(lot) ? EntityManager.GetComponentData<PolishedLot>(lot).Theme : (byte)0;

        /// <summary>The lot's flag; a yes lot always gets a theme (0: its kind's setting now; <paramref name="picked"/>: the
        /// painter chose it).</summary>
        /// <summary>The lot's flag; a yes lot always gets a theme (0: its kind's setting now; <paramref name="picked"/>: the
        /// painter chose it) and the look it is polished with.</summary>
        private void SetFlag(Entity lot, bool yes, byte theme = 0, bool picked = false, byte parts = PartsAll, byte painted = 33)
        {
            if (yes && theme == 0) { theme = ThemeByte(EntityManager.GetComponentData<PrefabRef>(lot).m_Prefab); picked = false; }
            var now = yes ? new PolishedLot { Yes = true, Theme = theme, Picked = picked, Own = true, Parts = parts, Painted = painted } : default;
            if (EntityManager.HasComponent<PolishedLot>(lot))
            {
                var was = EntityManager.GetComponentData<PolishedLot>(lot);
                if (!yes) { if (!was.Yes) return; was.Yes = false; now = was; }   // no: what it had is kept (shown if polished again)
                else if (was.Yes && was.Theme == theme && was.Picked == picked && was.Own && was.Parts == parts && was.Painted == painted) return;
                EntityManager.SetComponentData(lot, now);
            }
            else EntityManager.AddComponentData(lot, now);
            FlagsVersion++;
        }

        private void RememberGoneFlags()
        {
            if (_goneHouses.IsEmptyIgnoreFilter) return;
            using (var gone = _goneHouses.ToEntityArray(Allocator.Temp))
                foreach (var g in gone)
                {
                    if (!EntityManager.HasComponent<PolishedLot>(g)) continue;
                    _goneFlags.Add((EntityManager.GetComponentData<global::Game.Objects.Transform>(g).m_Position.xz, EntityManager.GetComponentData<PolishedLot>(g).Yes));
                    FlagsVersion++;
                }
            if (_goneFlags.Count > 4096) _goneFlags.RemoveRange(0, _goneFlags.Count - 4096);
        }

        /// <summary>Lots the game just made: the flag of the building that stood there, else "Polish new houses".</summary>
        private void FlagNewLots()
        {
            if (_newLots.IsEmptyIgnoreFilter) return;
            using (var lots = _newLots.ToEntityArray(Allocator.Temp))
                foreach (var e in lots)
                {
                    if (!IsGrowable(e)) continue;
                    var t = EntityManager.GetComponentData<global::Game.Objects.Transform>(e);
                    var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                    var half = (float2)EntityManager.GetComponentData<BuildingData>(prefab).m_LotSize * 4f + 2f;
                    var fwd = math.normalizesafe(math.mul(t.m_Rotation, new float3(0f, 0f, 1f)).xz, new float2(0f, 1f));
                    var right = new float2(fwd.y, -fwd.x);
                    // a new lot: polished when the polish is on and new houses are, unless its kind's theme is Off
                    bool yes = FeatureEnabled && PolishNew && ZoneTheme(ZoneKind(prefab)) != "off";
                    for (int k = _goneFlags.Count - 1; k >= 0; k--)
                    {
                        var d = _goneFlags[k].At - t.m_Position.xz;
                        if (math.abs(math.dot(d, right)) > half.x || math.abs(math.dot(d, fwd)) > half.y) continue;
                        yes = _goneFlags[k].Yes;
                        _goneFlags.RemoveAt(k);
                        TotalCarried++;
                        break;
                    }
                    EntityManager.AddComponentData(e, yes ? new PolishedLot { Yes = true, Theme = ThemeByte(prefab), Own = true, Parts = LookParts, Painted = LookPainted } : new PolishedLot());
                    TotalFlaggedNew++;
                    FlagsVersion++;
                }
        }

        /// <summary>The surfaces and objects our systems made this frame (Modification1) get the PolishPiece tag (saved).</summary>
        private void TagNewPieces()
        {
            if (PendingTags.Count == 0) return;
            if (!_newPieces.IsEmptyIgnoreFilter)
                using (var made = _newPieces.ToEntityArray(Allocator.Temp))
                    foreach (var e in made)
                    {
                        var owner = EntityManager.GetComponentData<Owner>(e).m_Owner;
                        var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                        float2 at;
                        if (EntityManager.HasBuffer<global::Game.Areas.Node>(e))
                        {
                            var nodes = EntityManager.GetBuffer<global::Game.Areas.Node>(e, true);
                            if (nodes.Length == 0) continue;
                            var c = float2.zero;
                            for (int i = 0; i < nodes.Length; i++) c += nodes[i].m_Position.xz;
                            at = c / nodes.Length;
                        }
                        else if (EntityManager.HasComponent<global::Game.Objects.Transform>(e)) at = EntityManager.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz;
                        else continue;
                        // the nearest request of this owner and kind (an area's centre moves a little with its node count)
                        int best = -1;
                        float bestD = 1.5f;
                        for (int k = 0; k < PendingTags.Count; k++)
                        {
                            var p = PendingTags[k];
                            if (p.Owner != owner || p.Prefab != prefab) continue;
                            float d = math.distance(p.At, at);
                            if (d < bestD) { bestD = d; best = k; }
                        }
                        if (best < 0) continue;
                        EntityManager.AddComponentData(e, new PolishPiece { Kind = PendingTags[best].Kind });
                        PendingTags.RemoveAt(best);
                        TotalTagged++;
                    }
            // a request the game did not make (a piece already stood there, the owner went): forgotten after a few frames
            PendingTags.RemoveAll(p => LotFrame - p.Frame > 8);
        }

        private void ApplyStamps()
        {
            if (s_stamps.Count == 0) return;
            // a budget a frame: a whole city stamped at once (Polish every building) is spread over a few frames
            int take = 0, cost = 0;
            while (take < s_stamps.Count && cost < StampBudget) cost += s_stamps[take++].Yes ? 1 : StampCostNo;
            var stamps = s_stamps.GetRange(0, take);
            s_stamps.RemoveRange(0, take);
            int yes = 0, no = 0, restored = 0;
            foreach (var (lot, y, theme, parts, painted) in stamps)
            {
                if (!EntityManager.Exists(lot) || EntityManager.HasComponent<Deleted>(lot) || !IsGrowable(lot)) continue;
                SetFlag(lot, y, theme, theme != 0, parts, painted);
                _described.Remove(lot);
                if (y)
                {
                    // its plan edits the polished neighbours near it too (they are in the same pass)
                    _dirty.Add(lot);
                    yes++;
                }
                else
                {
                    if (Restore(lot)) restored++;
                    _dirty.Remove(lot);
                    NeighboursDirty(lot, EntityManager.GetComponentData<global::Game.Objects.Transform>(lot).m_Position.xz);
                    no++;
                }
            }
            TotalStampedYes += yes;
            TotalStampedNo += no;
            _gridStale = true;
            _stampYes += yes;
            _stampNo += no;
            _stampRestored += restored;
            if (s_stamps.Count > 0) return;
            Mod.Log.Info($"Placemaker neighbourhood polish: {_stampYes} lots polished, {_stampNo} unpolished ({_stampRestored} of them put back){(FeatureEnabled ? "" : " (the polish is switched off: gardens change once it is on)")}.");
            _stampYes = _stampNo = _stampRestored = 0;
        }

        /// <summary>The polished lots near a lot that changed (unpolished, rebuilt): planned again.</summary>
        private void NeighboursDirty(Entity lot, float2 at)
        {
            EnsureGrid();
            for (int dx = -2; dx <= 2; dx++)
                for (int dz = -2; dz <= 2; dz++)
                    if (_grid.TryGetValue(Key(at + new float2(dx, dz) * Cell), out var near))
                        foreach (int j in near)
                        {
                            var h = _gridHouses[j];
                            if (h.E != lot && math.distance(h.T.m_Position.xz, at) < GoneRadius && EntityManager.Exists(h.E) && IsYes(h.E)) _dirty.Add(h.E);
                        }
        }

        private void StartRemoveAll()
        {
            s_removeAll = false;
            int was = 0;
            using (var all = _growables.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    if (!IsGrowable(e)) continue;
                    if (IsYes(e)) was++;
                    if (EntityManager.HasComponent<PolishedLot>(e)) SetFlag(e, false);
                    _restoreQueue.Add(e);
                }
            _dirty.Clear();
            _described.Clear();
            _removeAllCount = 0;
            Mod.Log.Info($"Placemaker neighbourhood polish: remove all polish: {was} polished lots set to no, {_restoreQueue.Count} lots to put back.");
        }

        private void DrainRestores()
        {
            if (_restoreQueue.Count == 0) return;
            int n = math.min(RestoresPerFrame, _restoreQueue.Count);
            for (int i = 0; i < n; i++)
            {
                var e = _restoreQueue[i];
                if (EntityManager.Exists(e) && !EntityManager.HasComponent<Deleted>(e) && Restore(e)) _removeAllCount++;
            }
            _restoreQueue.RemoveRange(0, n);
            if (_restoreQueue.Count == 0) Mod.Log.Info($"Placemaker neighbourhood polish: remove all polish done, {_removeAllCount} lots put back.");
        }

        /// <summary>
        /// The lot as the game made it: its fence and hedge lines where LaneSystem put them, its own lawns with their own
        /// nodes, every piece the polish gave it deleted (at PostTool). True when anything had to change.
        /// </summary>
        private bool Restore(Entity house)
        {
            bool changed = false;
            var writes = new List<(Entity Lane, Bezier4x3 Curve)>();
            MovedLanes(house, writes);
            // (written after the walk: no structural change while the buffer is read)
            foreach (var (lane, curve) in writes)
            {
                _written.Remove(lane);
                var cur = EntityManager.GetComponentData<Curve>(lane);
                cur.m_Bezier = curve;
                cur.m_Length = MathUtils.Length(curve);
                EntityManager.SetComponentData(lane, cur);
                EntityManager.AddComponent<Updated>(lane);
                changed = true;
            }
            if (EntityManager.HasBuffer<global::Game.Areas.SubArea>(house))
            {
                var areas = EntityManager.GetBuffer<global::Game.Areas.SubArea>(house, true);
                for (int i = 0; i < areas.Length; i++)
                {
                    var a = areas[i].m_Area;
                    if (!EntityManager.Exists(a) || EntityManager.HasComponent<Deleted>(a)) continue;
                    if (EntityManager.HasComponent<PolishPiece>(a)) { NeighbourDeleteSystem.Queue.Add(a); changed = true; }
                    else if (EntityManager.HasBuffer<PolishLawnOriginal>(a)) { NeighbourLawnSystem.Queue.Add(new NeighbourLawnSystem.Write { Lawn = a, Restore = true }); changed = true; }
                }
            }
            if (EntityManager.HasBuffer<global::Game.Objects.SubObject>(house))
            {
                var objects = EntityManager.GetBuffer<global::Game.Objects.SubObject>(house, true);
                for (int i = 0; i < objects.Length; i++)
                {
                    var o = objects[i].m_SubObject;
                    if (EntityManager.Exists(o) && !EntityManager.HasComponent<Deleted>(o) && EntityManager.HasComponent<PolishPiece>(o)) { NeighbourDeleteSystem.Queue.Add(o); changed = true; }
                }
            }
            _described.Remove(house);
            if (changed) TotalRestored++;
            return changed;
        }

        /// <summary>The house's fence and hedge lanes that are not where the game put them, with the curve it gave them.</summary>
        private void MovedLanes(Entity house, List<(Entity Lane, Bezier4x3 Original)> into)
        {
            if (!EntityManager.HasBuffer<global::Game.Net.SubLane>(house) || !EntityManager.HasComponent<global::Game.Objects.Transform>(house) || !EntityManager.HasComponent<PrefabRef>(house)) return;
            var t = EntityManager.GetComponentData<global::Game.Objects.Transform>(house);
            var prefab = EntityManager.GetComponentData<PrefabRef>(house).m_Prefab;
            if (!EntityManager.HasBuffer<global::Game.Prefabs.SubLane>(prefab)) return;
            var prefabLanes = EntityManager.GetBuffer<global::Game.Prefabs.SubLane>(prefab, true);
            var lanes = EntityManager.GetBuffer<global::Game.Net.SubLane>(house, true);
            for (int i = 0; i < lanes.Length; i++)
            {
                var lane = lanes[i].m_SubLane;
                if (!EntityManager.Exists(lane) || EntityManager.HasComponent<Deleted>(lane) || !EntityManager.HasComponent<global::Game.Net.UtilityLane>(lane) || !EntityManager.HasComponent<Lane>(lane)) continue;
                if (!IsFence(EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab)) continue;
                int k = 65535 - EntityManager.GetComponentData<Lane>(lane).m_MiddleNode.GetLaneIndex();
                if (k < 0 || k >= prefabLanes.Length) continue;
                var c = prefabLanes[k].m_Curve;
                var original = new Bezier4x3(global::Game.Objects.ObjectUtils.LocalToWorld(t, c.a), global::Game.Objects.ObjectUtils.LocalToWorld(t, c.b),
                    global::Game.Objects.ObjectUtils.LocalToWorld(t, c.c), global::Game.Objects.ObjectUtils.LocalToWorld(t, c.d));
                if (!IsOriginal(EntityManager.GetComponentData<Curve>(lane).m_Bezier, original)) into.Add((lane, original));
            }
        }

        /// <summary>For the game runner: a lot's flag and what the polish changed on it (lines not where the game put them,
        /// own lawns reshaped, pieces it was given).</summary>
        public (bool Yes, int Lines, int Lawns, int Pieces) Inspect(Entity house)
        {
            var moved = new List<(Entity, Bezier4x3)>();
            MovedLanes(house, moved);
            int lawns = 0, pieces = 0;
            if (EntityManager.HasBuffer<global::Game.Areas.SubArea>(house))
                foreach (var sa in EntityManager.GetBuffer<global::Game.Areas.SubArea>(house, true))
                {
                    if (!EntityManager.Exists(sa.m_Area) || EntityManager.HasComponent<Deleted>(sa.m_Area)) continue;
                    if (EntityManager.HasComponent<PolishPiece>(sa.m_Area)) pieces++;
                    else if (EntityManager.HasBuffer<PolishLawnOriginal>(sa.m_Area)) lawns++;
                }
            if (EntityManager.HasBuffer<global::Game.Objects.SubObject>(house))
                foreach (var so in EntityManager.GetBuffer<global::Game.Objects.SubObject>(house, true))
                    if (EntityManager.Exists(so.m_SubObject) && !EntityManager.HasComponent<Deleted>(so.m_SubObject) && EntityManager.HasComponent<PolishPiece>(so.m_SubObject)) pieces++;
            return (IsYes(house), moved.Count, lawns, pieces);
        }

        /// <summary>The house's own pieces the polish made (tagged PolishPiece) of one kind: entity, prefab name, centre.</summary>
        private List<(Entity Piece, string Name, float2 At)> OwnPieces(Entity house, PolishPieceKind kind)
        {
            var list = new List<(Entity, string, float2)>();
            if (!EntityManager.Exists(house)) return list;
            if (EntityManager.HasBuffer<global::Game.Areas.SubArea>(house))
                foreach (var sa in EntityManager.GetBuffer<global::Game.Areas.SubArea>(house, true))
                {
                    var a = sa.m_Area;
                    if (!EntityManager.Exists(a) || EntityManager.HasComponent<Deleted>(a) || !EntityManager.HasComponent<PolishPiece>(a) || !EntityManager.HasBuffer<global::Game.Areas.Node>(a)) continue;
                    if (EntityManager.GetComponentData<PolishPiece>(a).Kind != kind) continue;
                    var nodes = EntityManager.GetBuffer<global::Game.Areas.Node>(a, true);
                    if (nodes.Length == 0) continue;
                    var c = float2.zero;
                    for (int i = 0; i < nodes.Length; i++) c += nodes[i].m_Position.xz;
                    list.Add((a, NameOf(EntityManager.GetComponentData<PrefabRef>(a).m_Prefab), c / nodes.Length));
                }
            if (EntityManager.HasBuffer<global::Game.Objects.SubObject>(house))
                foreach (var so in EntityManager.GetBuffer<global::Game.Objects.SubObject>(house, true))
                {
                    var o = so.m_SubObject;
                    if (!EntityManager.Exists(o) || EntityManager.HasComponent<Deleted>(o) || !EntityManager.HasComponent<PolishPiece>(o) || !EntityManager.HasComponent<global::Game.Objects.Transform>(o)) continue;
                    if (EntityManager.GetComponentData<PolishPiece>(o).Kind != kind) continue;
                    list.Add((o, NameOf(EntityManager.GetComponentData<PrefabRef>(o).m_Prefab), EntityManager.GetComponentData<global::Game.Objects.Transform>(o).m_Position.xz));
                }
            return list;
        }

        /// <summary>Another city: nothing in memory may name its entities.</summary>
        private void ForgetCity()
        {
            _written.Clear();
            _lawnOriginal.Clear();
            _described.Clear();
            _dirty.Clear();
            _inFlight.Clear();
            _painted.Clear();
            _goneFlags.Clear();
            _restoreQueue.Clear();
            _sweepThemes = true;
            StatusInCity = false;
            StatusVersion++;
            _houseOf.Clear();
            _lawnPrefabOf.Clear();
            _grid = null;
            _gridHouses = null;
            _gridIndex = null;
            _gridStale = true;
            s_stamps.Clear();
            s_removeAll = false;
            s_polishAll = false;
            s_retheme = false;
            PendingTags.Clear();
            NeighbourLawnSystem.Queue.Clear();
            NeighbourAreaSystem.Queue.Clear();
            NeighbourObjectSystem.Queue.Clear();
            NeighbourDeleteSystem.Queue.Clear();
            NeighbourPaintSystem.Queue.Clear();
            FlagsVersion++;
        }

        // ───────────── what the painter reads ─────────────

        /// <summary>The growable lots whose middle lies inside the outline (also ones still being built).</summary>
        public List<Entity> LotsInside(IReadOnlyList<float2> outline)
        {
            var list = new List<Entity>();
            if (outline == null || outline.Count < 3) return list;
            var lo = new float2(float.MaxValue);
            var hi = new float2(float.MinValue);
            foreach (var p in outline) { lo = math.min(lo, p); hi = math.max(hi, p); }
            using (var all = _growables.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    var t = EntityManager.GetComponentData<global::Game.Objects.Transform>(e);
                    var at = t.m_Position.xz;
                    if (math.any(at < lo - 60f) || math.any(at > hi + 60f) || !IsGrowable(e)) continue;
                    if (Poly.Contains(outline, at) || PaintedAgainst(e, t, outline)) list.Add(e);
                }
            return list;
        }

        /// <summary>Growable lots whose middle lies within <paramref name="margin"/> of the outline's bounds.</summary>
        public List<Entity> GrowablesNear(IReadOnlyList<float2> outline, float margin)
        {
            var list = new List<Entity>();
            if (outline == null || outline.Count < 3) return list;
            var lo = new float2(float.MaxValue);
            var hi = new float2(float.MinValue);
            foreach (var p in outline) { lo = math.min(lo, p); hi = math.max(hi, p); }
            lo -= margin;
            hi += margin;
            using (var all = _growables.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    var at = EntityManager.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz;
                    if (math.all(at >= lo) && math.all(at <= hi) && IsGrowable(e)) list.Add(e);
                }
            return list;
        }

        /// <summary>
        /// The area ran up against the lot: painted with stops at lots (Paint area picked before the painter, or the player's
        /// stops), the paint stays out of the lot but follows its edge. At least 6 m of the lot's edge lies on or in the area.
        /// </summary>
        private bool PaintedAgainst(Entity lot, global::Game.Objects.Transform t, IReadOnlyList<float2> outline)
        {
            var prefab = EntityManager.GetComponentData<PrefabRef>(lot).m_Prefab;
            if (!EntityManager.HasComponent<BuildingData>(prefab)) return false;
            var half = (float2)EntityManager.GetComponentData<BuildingData>(prefab).m_LotSize * 4f;
            var fwd = math.normalizesafe(math.mul(t.m_Rotation, new float3(0f, 0f, 1f)).xz, new float2(0f, 1f));
            var right = new float2(fwd.y, -fwd.x);
            var c = t.m_Position.xz;
            var corners = new[] { c - right * half.x - fwd * half.y, c + right * half.x - fwd * half.y, c + right * half.x + fwd * half.y, c - right * half.x + fwd * half.y };
            float touched = 0f;
            for (int k = 0; k < 4; k++)
            {
                var a = corners[k];
                var b = corners[(k + 1) % 4];
                int steps = math.max(1, (int)(math.distance(a, b) / 1f));
                for (int i = 0; i < steps; i++)
                {
                    var p = math.lerp(a, b, (i + 0.5f) / steps);
                    if (Poly.Contains(outline, p) || DistanceToOutline(outline, p) < 1.5f) touched += math.distance(a, b) / steps;
                }
            }
            return touched >= 6f;
        }

        private static float DistanceToOutline(IReadOnlyList<float2> ring, float2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var e = ring[(i + 1) % ring.Count] - a;
                float u = math.saturate(math.dot(p - a, e) / math.max(1e-6f, math.dot(e, e)));
                best = math.min(best, math.distance(p, a + e * u));
            }
            return best;
        }

        /// <summary>The polished lots within <paramref name="radius"/> of a point, as rings on the ground (the painter's tint).</summary>
        public void PolishedRingsNear(float2 centre, float radius, List<float3[]> into, int max = 6000)
        {
            into.Clear();
            using (var all = _flaggedLots.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    if (into.Count >= max) break;
                    if (!EntityManager.GetComponentData<PolishedLot>(e).Yes) continue;
                    var t = EntityManager.GetComponentData<global::Game.Objects.Transform>(e);
                    if (math.distance(t.m_Position.xz, centre) > radius) continue;
                    var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!EntityManager.HasComponent<BuildingData>(prefab)) continue;
                    var half = (float2)EntityManager.GetComponentData<BuildingData>(prefab).m_LotSize * 4f - 0.4f;
                    into.Add(new[]
                    {
                        global::Game.Objects.ObjectUtils.LocalToWorld(t, new float3(-half.x, 0f, -half.y)), global::Game.Objects.ObjectUtils.LocalToWorld(t, new float3(half.x, 0f, -half.y)),
                        global::Game.Objects.ObjectUtils.LocalToWorld(t, new float3(half.x, 0f, half.y)), global::Game.Objects.ObjectUtils.LocalToWorld(t, new float3(-half.x, 0f, half.y)),
                    });
                }
        }
    }
}
