// Placemaker neighbourhood polish — automatic yard stitching on the game's own houses (GPL-3.0).
//
// Every house the game grows is edited after it is placed, never replaced: its own fence/hedge lines and its own lawn are
// reshaped so neighbours join up (Core/Neighbours/YardStitch decides what; docs/research/custom-assets-on-the-fly.md).
// How the game's parts behave (decompiled 1.6.2f1):
//   * fences and hedges are utility lanes (UtilityTypes.Fence) owned by the building, made by LaneSystem (Modification4)
//     from the prefab's SubLane list whenever the building gets Updated (construction end, level-up, move): the same lane
//     entity is reused and its Curve reset, or recreated (Created) after construction. LaneSystem matches lanes by path
//     nodes, never by curve, so a rewritten Curve survives until the next rebuild — and is saved with the game.
//   * a lane collapsed to a point draws nothing and never hides another fence (Net.OverrideSystem tests parallel lines).
//   * a lawn is a surface area owned by the building; its nodes can be rewritten (+Updated) and are saved; construction end
//     and level-up recreate it from the prefab.
// So this runs in Modification4B (after LaneSystem's barrier, before OverrideSystem, culling and rendering: no flicker),
// picks up fence lanes the game created or reset, and re-stitches their houses and neighbours from the prefab's ORIGINAL
// geometry. Our own writes are recognised (the curve we wrote) and never trigger another pass.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Colossal.Mathematics;
using Game;
using Game.Areas;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Placemaker.Core.Neighbours;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourPolishSystem : GameSystemBase
    {
        /// <summary>Setting "Stitch neighbouring houses".</summary>
        public static bool FeatureEnabled;
        /// <summary>Game runner: which parts are edited (both by default).</summary>
        public static bool EditLines = true, EditLawns = true;
        /// <summary>The stitching rules (the game runner can change them live and ask for a redo).</summary>
        public static StitchSettings Settings = new StitchSettings();
        /// <summary>The planner (the game runner's lab op "hotload" swaps in a newer YardStitch without a relaunch).</summary>
        public static Func<IReadOnlyList<StitchHouse>, StitchSettings, StitchPlan> Planner = YardStitch.Plan;
        private static bool s_redo;

        /// <summary>Every house again with the current rules (lines and lawns no longer planned go back to the game's own).</summary>
        public static void Redo() => s_redo = true;

        // a pass costs ~0.6 ms per house asked (its neighbourhood included): 16 keeps a pass near 10 ms, every 4th frame
        private const int HousesPerPass = 16;
        // a plot depends on houses up to ~2 lots away (and a ring's shared green on the whole ring): the pass plans with
        // everything within ClusterRadius but edits only houses within CoreRadius of a dirty one, so a house at the edge of
        // a pass (its neighbours not in it) is never planned half-blind and reset
        private const float ClusterRadius = 110f;
        private const float CoreRadius = 40f;
        private const float Cell = 64f;

        private EntityQuery _changedFences;
        private EntityQuery _houses;
        private PrefabSystem _prefabs;
        private TerrainSystem _terrain;
        private readonly Dictionary<Entity, Bezier4x3> _written = new Dictionary<Entity, Bezier4x3>();
        private readonly Dictionary<Entity, float2[]> _lawnOriginal = new Dictionary<Entity, float2[]>();
        private readonly HashSet<Entity> _lawnGrown = new HashSet<Entity>();
        private readonly Dictionary<Entity, bool> _grassPrefab = new Dictionary<Entity, bool>();
        private readonly Dictionary<Entity, string> _names = new Dictionary<Entity, string>();
        private readonly HashSet<Entity> _dirty = new HashSet<Entity>();
        private readonly HashSet<long> _painted = new HashSet<long>();
        private bool _wasEnabled;
        private int _frame, _errors;
        // totals since the last log line
        private int _pairs, _moved, _hidden, _restored, _lawns, _passes;
        private float _lawnM2;
        private double _ms;

        /// <summary>For the game runner: totals since the game started.</summary>
        public static int TotalPairs, TotalMoved, TotalHidden, TotalLawns, TotalPasses;
        public static float TotalLawnM2;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            _changedFences = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Lane>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<global::Game.Net.UtilityLane>(), ComponentType.ReadOnly<Owner>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            _houses = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<global::Game.Net.SubLane>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (!FeatureEnabled) { _wasEnabled = false; return; }
            try
            {
                if (!_wasEnabled || s_redo)
                {
                    s_redo = false;
                    // switched on (or a city loaded with it on): every house once, a few per pass
                    _wasEnabled = true;
                    using (var all = _houses.ToEntityArray(Allocator.Temp)) foreach (var e in all) _dirty.Add(e);
                    Mod.Log.Info($"Placemaker neighbourhood polish: on, {_dirty.Count} buildings to look at.");
                }
                CollectChanged();
                if (_dirty.Count > 0 && (++_frame & 3) == 0) Pass();
                if (_passes > 0 && (_frame & 255) == 0) Report();
            }
            catch (Exception e)
            {
                if (_errors++ < 5) Mod.Log.Error(e, "Placemaker neighbourhood polish failed");
                if (_errors >= 5) { FeatureEnabled = false; Mod.Log.Warn("Placemaker neighbourhood polish switched off for this session after repeated errors."); }
            }
        }

        /// <summary>Fence lanes the game created or reset (not the ones we wrote): their houses need stitching.</summary>
        private void CollectChanged()
        {
            if (_changedFences.IsEmptyIgnoreFilter) return;
            using (var lanes = _changedFences.ToEntityArray(Allocator.Temp))
                foreach (var lane in lanes)
                {
                    var owner = EntityManager.GetComponentData<Owner>(lane).m_Owner;
                    if (owner == Entity.Null || !EntityManager.HasComponent<Building>(owner)) continue;
                    if (_written.TryGetValue(lane, out var w) && Same(EntityManager.GetComponentData<Curve>(lane).m_Bezier, w)) continue;
                    if (!IsFence(EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab)) continue;
                    _written.Remove(lane);
                    _dirty.Add(owner);
                }
        }

        // ───────────── one pass: a batch of dirty houses with their neighbourhoods ─────────────

        private struct House
        {
            public Entity E;
            public global::Game.Objects.Transform T;
            public Entity Prefab;
            public int2 Lot;
        }

        /// <summary>Game runner (lab op "polishDump"): every finished house in a box as the planner sees it, for offline replays.</summary>
        public List<StitchHouse> DescribeIn(float2 min, float2 max)
        {
            var list = new List<StitchHouse>();
            using (var all = _houses.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    if (EntityManager.HasComponent<global::Game.Objects.UnderConstruction>(e)) continue;
                    var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!EntityManager.HasComponent<SpawnableBuildingData>(prefab) || !EntityManager.HasComponent<BuildingData>(prefab)) continue;
                    var t = EntityManager.GetComponentData<global::Game.Objects.Transform>(e);
                    if (math.any(t.m_Position.xz < min) || math.any(t.m_Position.xz > max)) continue;
                    list.Add(Describe(new House { E = e, T = t, Prefab = prefab, Lot = EntityManager.GetComponentData<BuildingData>(prefab).m_LotSize }, null, null));
                }
            return list;
        }

        private void Pass()
        {
            var sw = Stopwatch.StartNew();
            _passes++;
            // every finished growable, in a coarse grid
            var houses = new List<House>();
            var grid = new Dictionary<long, List<int>>();
            using (var all = _houses.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    if (EntityManager.HasComponent<global::Game.Objects.UnderConstruction>(e)) continue;
                    var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!EntityManager.HasComponent<SpawnableBuildingData>(prefab) || !EntityManager.HasComponent<BuildingData>(prefab)) continue;
                    var t = EntityManager.GetComponentData<global::Game.Objects.Transform>(e);
                    houses.Add(new House { E = e, T = t, Prefab = prefab, Lot = EntityManager.GetComponentData<BuildingData>(prefab).m_LotSize });
                    long key = Key(t.m_Position.xz);
                    if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                    list.Add(houses.Count - 1);
                }
            var index = new Dictionary<Entity, int>();
            for (int i = 0; i < houses.Count; i++) index[houses[i].E] = i;

            // this pass: up to HousesPerPass dirty houses and everything within ClusterRadius of them
            var cluster = new HashSet<int>();
            var core = new HashSet<int>();
            var done = new List<Entity>();
            foreach (var e in _dirty)
            {
                if (done.Count >= HousesPerPass) break;
                done.Add(e);
                if (!index.TryGetValue(e, out int i)) continue;   // gone, or still being built: its lanes come back as Created later
                var p = houses[i].T.m_Position.xz;
                for (int dx = -2; dx <= 2; dx++)
                    for (int dz = -2; dz <= 2; dz++)
                        if (grid.TryGetValue(Key(p + new float2(dx, dz) * Cell), out var near))
                            foreach (int j in near)
                            {
                                float d = math.distance(houses[j].T.m_Position.xz, p);
                                if (d <= ClusterRadius) cluster.Add(j);
                                if (d <= CoreRadius) core.Add(j);
                            }
            }
            foreach (var e in done) _dirty.Remove(e);
            if (cluster.Count == 0) return;

            var stitch = new List<StitchHouse>();
            var lanesOf = new Dictionary<long, (Entity Lane, float3 A, float3 B)>();
            var lawnsOf = new Dictionary<long, Entity>();
            _houseOf.Clear();
            _lawnPrefabOf.Clear();
            var coreIds = new HashSet<long>();
            foreach (int i in cluster)
            {
                stitch.Add(Describe(houses[i], core.Contains(i) ? lanesOf : null, core.Contains(i) ? lawnsOf : null));
                if (core.Contains(i)) coreIds.Add(Id(houses[i].E));
            }
            var plan = Planner(stitch, Settings);
            if (TotalPasses < 6)
                foreach (var note in plan.Notes) Mod.Log.Info("Placemaker neighbourhood polish: " + note);
            if (TotalPasses < 6)
                Mod.Log.Info($"Placemaker neighbourhood polish pass {TotalPasses + 1}: {done.Count} houses asked, {cluster.Count} in their neighbourhoods, {plan.Pairs.Count} pairs, "
                    + $"{plan.MoveLines.Count} lines to move, {plan.HideLines.Count} to hide, {plan.Lawns.Count} lawns{(EditLines ? "" : " (lines off)")}{(EditLawns ? "" : " (lawns off)")}");

            var heights = _terrain.GetHeightData(true);
            float3 Ground(float2 p) => new float3(p.x, TerrainUtils.SampleHeight(ref heights, new float3(p.x, 0f, p.y)), p.y);
            foreach (var kv in lanesOf)
            {
                if (!EditLines) break;
                var (lane, a, b) = kv.Value;
                Bezier4x3 want;
                if (plan.HideLines.Contains(kv.Key))
                {
                    // a 2 cm stub: drawn as nothing (lanes under 0.1 m are skipped) and never a zero tangent
                    var mid = (a + b) * 0.5f;
                    var dir = math.normalizesafe(b - a, new float3(1f, 0f, 0f)) * 0.01f;
                    want = NetUtils.StraightCurve(mid - dir, mid + dir);
                    _hidden++;
                }
                else if (plan.MoveLines.TryGetValue(kv.Key, out var m)) { want = NetUtils.StraightCurve(Ground(m.A), Ground(m.B)); _moved++; }
                else if (_written.ContainsKey(lane)) { want = NetUtils.StraightCurve(a, b); _restored++; }   // no longer stitched (a neighbour left)
                else continue;
                var cur = EntityManager.GetComponentData<Curve>(lane);
                if (Same(cur.m_Bezier, want)) { _written[lane] = want; continue; }
                cur.m_Bezier = want;
                cur.m_Length = MathUtils.Length(want);
                EntityManager.SetComponentData(lane, cur);
                EntityManager.AddComponent<Updated>(lane);
                if (plan.HideLines.Contains(kv.Key) || plan.MoveLines.ContainsKey(kv.Key)) _written[lane] = want; else _written.Remove(lane);
            }
            foreach (var kv in lawnsOf)
            {
                if (!EditLawns) break;
                var lawn = kv.Value;
                float2[] ring;
                if (plan.Lawns.TryGetValue(kv.Key, out var grown)) { ring = grown; _lawnGrown.Add(lawn); _lawns++; }
                else if (_lawnGrown.Remove(lawn)) ring = _lawnOriginal[lawn];
                else continue;
                // written next frame before Areas.GeometrySystem (Modification2B), which re-triangulates Updated areas:
                // nodes changed here, after it ran, would leave triangles pointing past the new ring (native crash)
                var nodes = new float3[ring.Length];
                for (int i = 0; i < ring.Length; i++) nodes[i] = Ground(ring[i]);
                NeighbourLawnSystem.Queue.Add((lawn, nodes));
            }
            // ground paint: once per stitched pair (painting again would change nothing, but costs a raster)
            foreach (var paint in plan.Paints)
            {
                long key = (long)math.round(paint.Ring[0].x * 4f) * 73856093L ^ (long)math.round(paint.Ring[0].z * 4f) * 19349663L ^ paint.Layer;
                if (_painted.Add(key)) NeighbourPaintSystem.Queue.Add(paint);
            }
            // land no lawn of its house reaches: a lawn the house owns (of the house's own kind, else its neighbours' kind)
            if (EditLawns && plan.NewLawns.Count > 0)
            {
                var usual = System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.Select(System.Linq.Enumerable.OrderByDescending(System.Linq.Enumerable.GroupBy(_lawnPrefabOf.Values, p => p), g => System.Linq.Enumerable.Count(g)), g => g.Key));
                foreach (var (house, ring) in plan.NewLawns)
                {
                    if (!coreIds.Contains(house) || !_houseOf.TryGetValue(house, out var owner)) continue;
                    var prefab = _lawnPrefabOf.TryGetValue(house, out var own) ? own : usual;
                    NeighbourAreaSystem.Queue.Add(new NeighbourAreaSystem.Request { Prefab = prefab, Name = "Grass Surface 01", Ring = ring, Owner = owner });
                    _newLawns++;
                }
            }
            // shared land in the middle of a ring: a lawn with a mulch bed in its centre
            foreach (var common in plan.Commons)
            {
                var inset = Placemaker.Core.Geometry.Poly.Offset(common, 0.5f);
                if (inset.Count == 0) continue;
                NeighbourAreaSystem.Queue.Add(new NeighbourAreaSystem.Request { Name = Settings.Extra.ContainsKey("noCommonLawn") ? "" : "Grass Surface 01", Ring = inset[0] });
                var c = float2.zero;
                foreach (var p in common) c += p;
                c /= common.Length;
                float r = math.clamp(math.sqrt(Placemaker.Core.Geometry.Poly.Area(common)) / 6f, 2f, 5f);
                var bed = new float2[10];
                for (int i = 0; i < bed.Length; i++) { float ang = i * math.PI * 2f / bed.Length; bed[i] = c + new float2(math.cos(ang), math.sin(ang)) * r * (i % 2 == 0 ? 1f : 0.82f); }
                NeighbourAreaSystem.Queue.Add(new NeighbourAreaSystem.Request { Name = "Forestry Surface 01", Ring = bed });
            }
            _pairs += plan.Pairs.Count;
            _lawnM2 += plan.LawnAddedM2;
            TotalPairs += plan.Pairs.Count; TotalMoved = TotalMoved + plan.MoveLines.Count; TotalHidden += plan.HideLines.Count; TotalLawns += plan.Lawns.Count; TotalLawnM2 += plan.LawnAddedM2; TotalPasses++;
            _ms += sw.Elapsed.TotalMilliseconds;
            Prune();
        }

        /// <summary>A house as YardStitch sees it: its lot, its fence lines at their ORIGINAL (prefab) places, its lawns as first seen.</summary>
        private StitchHouse Describe(House h, Dictionary<long, (Entity, float3, float3)> lanesOf, Dictionary<long, Entity> lawnsOf)
        {
            var fwd = math.mul(h.T.m_Rotation, new float3(0f, 0f, 1f));
            var sh = new StitchHouse
            {
                Id = Id(h.E), Position = h.T.m_Position.xz, Forward = math.normalizesafe(fwd.xz, new float2(0f, 1f)), HalfLot = (float2)h.Lot * 4f,
            };
            // the house itself: its mesh's ground outline (no moved fence may cross it)
            if (ModelBounds(h.Prefab, out var bnd))
            {
                sh.Footprint = new[]
                {
                    global::Game.Objects.ObjectUtils.LocalToWorld(h.T, new float3(bnd.min.x, 0f, bnd.min.z)).xz, global::Game.Objects.ObjectUtils.LocalToWorld(h.T, new float3(bnd.max.x, 0f, bnd.min.z)).xz,
                    global::Game.Objects.ObjectUtils.LocalToWorld(h.T, new float3(bnd.max.x, 0f, bnd.max.z)).xz, global::Game.Objects.ObjectUtils.LocalToWorld(h.T, new float3(bnd.min.x, 0f, bnd.max.z)).xz,
                };
            }
            bool hasPrefabLanes = EntityManager.HasBuffer<global::Game.Prefabs.SubLane>(h.Prefab);
            var prefabLanes = hasPrefabLanes ? EntityManager.GetBuffer<global::Game.Prefabs.SubLane>(h.Prefab, true) : default;
            foreach (var sl in EntityManager.GetBuffer<global::Game.Net.SubLane>(h.E, true))
            {
                var lane = sl.m_SubLane;
                if (!EntityManager.Exists(lane) || !EntityManager.HasComponent<global::Game.Net.UtilityLane>(lane) || !EntityManager.HasComponent<Lane>(lane)) continue;
                var lanePrefab = EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;
                if (!IsFence(lanePrefab)) continue;
                // the original curve: the prefab's sub-lane this lane was made from (LaneSystem: middle node = 65535 - index)
                float3 a, b;
                int k = 65535 - EntityManager.GetComponentData<Lane>(lane).m_MiddleNode.GetLaneIndex();
                if (hasPrefabLanes && k >= 0 && k < prefabLanes.Length)
                {
                    var c = prefabLanes[k].m_Curve;
                    a = global::Game.Objects.ObjectUtils.LocalToWorld(h.T, c.a);
                    b = global::Game.Objects.ObjectUtils.LocalToWorld(h.T, c.d);
                }
                else if (!_written.ContainsKey(lane))
                {
                    var c = EntityManager.GetComponentData<Curve>(lane).m_Bezier;
                    a = c.a; b = c.d;
                }
                else continue;   // ours, and its original is unknown: leave it
                long id = Id(lane);
                if (lanesOf != null) lanesOf[id] = (lane, a, b);
                sh.Lines.Add(new StitchLine { Id = id, Name = NameOf(lanePrefab), A = a.xz, B = b.xz });
            }
            _houseOf[sh.Id] = h.E;
            if (EntityManager.HasBuffer<global::Game.Areas.SubArea>(h.E))
                foreach (var sa in EntityManager.GetBuffer<global::Game.Areas.SubArea>(h.E, true))
                {
                    var area = sa.m_Area;
                    if (!EntityManager.Exists(area) || !EntityManager.HasComponent<global::Game.Areas.Surface>(area) || !EntityManager.HasBuffer<global::Game.Areas.Node>(area)) continue;
                    if (!IsGrass(EntityManager.GetComponentData<PrefabRef>(area).m_Prefab))
                    {
                        // driveway, paving, sand: grown lawns go around it
                        var hard = EntityManager.GetBuffer<global::Game.Areas.Node>(area, true);
                        if (hard.Length >= 3)
                        {
                            var r = new float2[hard.Length];
                            for (int i = 0; i < r.Length; i++) r[i] = hard[i].m_Position.xz;
                            sh.Hard.Add(r);
                        }
                        continue;
                    }
                    if (!_lawnOriginal.TryGetValue(area, out var ring))
                    {
                        var nodes = EntityManager.GetBuffer<global::Game.Areas.Node>(area, true);
                        if (nodes.Length < 3) continue;
                        ring = new float2[nodes.Length];
                        for (int i = 0; i < ring.Length; i++) ring[i] = nodes[i].m_Position.xz;
                        _lawnOriginal[area] = ring;
                    }
                    long id = Id(area);
                    if (!_lawnPrefabOf.ContainsKey(sh.Id)) _lawnPrefabOf[sh.Id] = EntityManager.GetComponentData<PrefabRef>(area).m_Prefab;
                    if (lawnsOf != null) lawnsOf[id] = area;
                    sh.Lawns.Add(new StitchLawn { Id = id, Ring = ring });
                }
            return sh;
        }

        // ───────────── helpers ─────────────

        private readonly Dictionary<Entity, Bounds3?> _modelBounds = new Dictionary<Entity, Bounds3?>();
        private readonly Dictionary<long, Entity> _houseOf = new Dictionary<long, Entity>();
        private readonly Dictionary<long, Entity> _lawnPrefabOf = new Dictionary<long, Entity>();
        private int _newLawns;

        /// <summary>The house model's own bounds (its meshes), not the collision box (which covers the whole lot).</summary>
        private bool ModelBounds(Entity prefab, out Bounds3 bounds)
        {
            if (!_modelBounds.TryGetValue(prefab, out var cached))
            {
                cached = null;
                try
                {
                    if (_prefabs.TryGetPrefab<ObjectGeometryPrefab>(prefab, out var p) && p != null && p.m_Meshes != null)
                        foreach (var mi in p.m_Meshes)
                        {
                            if (!(mi?.m_Mesh is RenderPrefab rp)) continue;
                            var b = new Bounds3(rp.bounds.min + mi.m_Position, rp.bounds.max + mi.m_Position);
                            cached = cached.HasValue ? new Bounds3(math.min(cached.Value.min, b.min), math.max(cached.Value.max, b.max)) : b;
                        }
                }
                catch (Exception) { cached = null; }
                _modelBounds[prefab] = cached;
            }
            bounds = cached ?? default;
            return cached.HasValue;
        }

        private bool IsFence(Entity lanePrefab) =>
            EntityManager.HasComponent<UtilityLaneData>(lanePrefab) && (EntityManager.GetComponentData<UtilityLaneData>(lanePrefab).m_UtilityTypes & UtilityTypes.Fence) != 0;

        private bool IsGrass(Entity prefab)
        {
            if (_grassPrefab.TryGetValue(prefab, out bool g)) return g;
            g = NameOf(prefab).IndexOf("grass", StringComparison.OrdinalIgnoreCase) >= 0 || NameOf(prefab).IndexOf("lawn", StringComparison.OrdinalIgnoreCase) >= 0;
            _grassPrefab[prefab] = g;
            return g;
        }

        private string NameOf(Entity prefab)
        {
            if (_names.TryGetValue(prefab, out var n)) return n;
            try { n = _prefabs.GetPrefab<PrefabBase>(prefab)?.name ?? ""; } catch (Exception) { n = ""; }
            _names[prefab] = n;
            return n;
        }

        private static bool Same(Bezier4x3 x, Bezier4x3 y) =>
            math.all(math.abs(x.a - y.a) < 0.01f) && math.all(math.abs(x.b - y.b) < 0.01f) && math.all(math.abs(x.c - y.c) < 0.01f) && math.all(math.abs(x.d - y.d) < 0.01f);

        private static long Id(Entity e) => ((long)e.Index << 32) | (uint)e.Version;

        private static long Key(float2 p) => ((long)(int)math.floor(p.x / Cell) << 32) ^ (uint)(int)math.floor(p.y / Cell);

        private void Prune()
        {
            if (_written.Count + _lawnOriginal.Count < 4096) return;
            var gone = new List<Entity>();
            foreach (var e in _written.Keys) if (!EntityManager.Exists(e)) gone.Add(e);
            foreach (var e in gone) _written.Remove(e);
            gone.Clear();
            foreach (var e in _lawnOriginal.Keys) if (!EntityManager.Exists(e)) gone.Add(e);
            foreach (var e in gone) { _lawnOriginal.Remove(e); _lawnGrown.Remove(e); }
        }

        private void Report()
        {
            Mod.Log.Info($"Placemaker neighbourhood polish: {_passes} passes, {_pairs} neighbour pairs, {_moved} fence/hedge lines moved onto shared borders, "
                + $"{_hidden} doubled lines hidden, {_restored} restored, {_lawns} lawns grown (+{_lawnM2:F0} m²), {_ms:F1} ms in total; {_dirty.Count} houses waiting.");
            _passes = _pairs = _moved = _hidden = _restored = _lawns = 0;
            _lawnM2 = 0f;
            _ms = 0;
        }
    }
}
