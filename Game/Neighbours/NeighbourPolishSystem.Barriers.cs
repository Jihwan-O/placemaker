// Placemaker neighbourhood polish — what gardens never cross and like to meet (GPL-3.0).
//
// For each pass the planner gets the barriers around its patch of town (StitchSettings.Barriers): the ground surfaces of
// roads, paths and tracks (each edge's curve as a strip of its full width, sidewalks included; elevated and tunnelled
// pieces left out), the lots of buildings that are not houses (service buildings, Placemaker builds' buildings…), lot
// areas and every Placemaker build's outline (as the area brush's "Lots" and "My builds": a park in the middle of a block
// stays the park's), and water (sampled every 4 m, merged). Two houses never join across one, no garden reaches onto one,
// and a garden side nobody else has reaches out to one near it (YardStitch.Hug). Read from the game's own search trees, so
// a patch costs the same in a city of any size.

using System.Collections.Generic;
using Colossal.Collections;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Placemaker.Game.Selection;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourPolishSystem
    {
        private global::Game.Net.SearchSystem _netSearch;
        private global::Game.Objects.SearchSystem _objectSearch;
        private const int MaxBarriers = 800;

        private struct BoundsIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds2 Bounds;
            public NativeList<Entity> Results;
            public bool Intersect(QuadTreeBoundsXZ bounds) => MathUtils.Intersect(bounds.m_Bounds.xz, Bounds);
            public void Iterate(QuadTreeBoundsXZ bounds, Entity e)
            {
                if (MathUtils.Intersect(bounds.m_Bounds.xz, Bounds)) Results.Add(e);
            }
        }

        /// <summary>Roads, paths and tracks on the ground and the lots of buildings that are not houses, in a box.</summary>
        private List<float2[]> BarriersIn(float2 min, float2 max) => BarriersIn(min, max, null);

        private BlockObstacles _obstacles;
        private const float WaterStep = 4f;

        /// <summary>The barriers in a box and what each is (0 a road, path or track, 1 a building's lot, a lot area or a
        /// Placemaker build, 2 water).</summary>
        private List<float2[]> BarriersIn(float2 min, float2 max, List<int> kinds)
        {
            var list = new List<float2[]>();
            _netSearch ??= World.GetExistingSystemManaged<global::Game.Net.SearchSystem>();
            _objectSearch ??= World.GetExistingSystemManaged<global::Game.Objects.SearchSystem>();
            var results = new NativeList<Entity>(64, Allocator.Temp);
            try
            {
                if (_netSearch != null)
                {
                    var tree = _netSearch.GetNetSearchTree(true, out var deps);
                    deps.Complete();
                    var it = new BoundsIterator { Bounds = new Bounds2(min, max), Results = results };
                    tree.Iterate(ref it);
                    var seen = new HashSet<Entity>();
                    for (int i = 0; i < results.Length && list.Count < MaxBarriers; i++)
                    {
                        var e = results[i];
                        if (!seen.Add(e) || !EntityManager.HasComponent<Edge>(e) || !EntityManager.HasComponent<Curve>(e) || !EntityManager.HasComponent<Composition>(e)) continue;
                        if (EntityManager.HasComponent<Temp>(e) || EntityManager.HasComponent<Deleted>(e)) continue;
                        var comp = EntityManager.GetComponentData<Composition>(e).m_Edge;
                        if (comp == Entity.Null || !EntityManager.HasComponent<NetCompositionData>(comp)) continue;
                        var data = EntityManager.GetComponentData<NetCompositionData>(comp);
                        if ((data.m_Flags.m_General & (CompositionFlags.General.Elevated | CompositionFlags.General.Tunnel)) != 0 || data.m_Width < 1f) continue;
                        // walked, driven or ridden on: roads, paths, tracks (not pipes or power lines)
                        var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                        bool ground = EntityManager.HasComponent<Road>(e) || EntityManager.HasComponent<PathwayData>(prefab)
                            || EntityManager.HasComponent<TrainTrack>(e) || EntityManager.HasComponent<TramTrack>(e);
                        if (!ground) continue;
                        var strip = Strip(EntityManager.GetComponentData<Curve>(e), data.m_Width * 0.5f);
                        if (strip != null) { list.Add(strip); kinds?.Add(0); }
                    }
                }
                results.Clear();
                if (_objectSearch != null)
                {
                    var tree = _objectSearch.GetStaticSearchTree(true, out var deps);
                    deps.Complete();
                    var it = new BoundsIterator { Bounds = new Bounds2(min, max), Results = results };
                    tree.Iterate(ref it);
                    var seen = new HashSet<Entity>();
                    for (int i = 0; i < results.Length && list.Count < MaxBarriers; i++)
                    {
                        var e = results[i];
                        if (!seen.Add(e) || !BuildingFootprints.IsBuilding(EntityManager, e)) continue;
                        if (EntityManager.HasComponent<Temp>(e) || EntityManager.HasComponent<Deleted>(e) || IsGrowable(e)) continue;   // houses are the plan's own
                        BuildingFootprints.Of(EntityManager, e, out var lot, out _);
                        if (lot != null && lot.Length >= 3) { list.Add(lot); kinds?.Add(1); }
                    }
                }
            }
            finally { results.Dispose(); }
            // lot areas and Placemaker builds (their saved outlines)
            try
            {
                _obstacles ??= new BlockObstacles(EntityManager, World);
                foreach (var o in _obstacles.LotsAndBuildsIn(min, max))
                {
                    if (list.Count >= MaxBarriers) break;
                    if (o.Ring == null || o.Ring.Length < 3) continue;
                    list.Add(o.Ring);
                    kinds?.Add(1);
                }
                // water: wet cells merged into a few outlines
                var wet = _obstacles.Water(min, max, WaterStep);
                if (wet != null)
                {
                    var rects = new List<IReadOnlyList<float2>>();
                    int nx = (int)math.ceil((max.x - min.x) / WaterStep), nz = (int)math.ceil((max.y - min.y) / WaterStep);
                    for (int j = 0; j < nz; j++)
                    {
                        int run = -1;
                        for (int i = 0; i <= nx; i++)
                        {
                            bool w = i < nx && wet(min + new float2((i + 0.5f) * WaterStep, (j + 0.5f) * WaterStep));
                            if (w && run < 0) run = i;
                            if (!w && run >= 0)
                            {
                                float x0 = min.x + run * WaterStep, x1 = min.x + i * WaterStep, z0 = min.y + j * WaterStep, z1 = z0 + WaterStep;
                                rects.Add(new[] { new float2(x0, z0), new float2(x1, z0), new float2(x1, z1), new float2(x0, z1) });
                                run = -1;
                            }
                        }
                    }
                    if (rects.Count > 0)
                        foreach (var r in Placemaker.Core.Geometry.Poly.UnionAll(rects))
                        {
                            if (list.Count >= MaxBarriers) break;
                            if (r.Outer == null || r.Outer.Length < 3 || r.Area < 8f) continue;
                            list.Add(r.Outer);
                            kinds?.Add(2);
                        }
                }
            }
            catch (System.Exception e) { if (_errors++ < 5) Mod.Log.Warn("Placemaker neighbourhood polish: could not read lots, builds or water: " + e.Message); }
            return list;
        }

        // ───────────── something new built near gardens ─────────────

        private EntityQuery _newNearby;

        private void CreateNearbyQuery()
        {
            // a road, path, lot area, surface or building that is not a house, made this frame
            _newNearby = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Created>() },
                Any = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<global::Game.Areas.Lot>(), ComponentType.ReadOnly<global::Game.Areas.Surface>(), ComponentType.ReadOnly<global::Game.Buildings.Building>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<PolishPiece>() },
            });
        }

        /// <summary>
        /// Something built near polished gardens (a road, a path, a Placemaker build, a park, a service building): the gardens
        /// within reach of it are planned again, so they give that land back (they never grow into a build).
        /// </summary>
        private void CollectNewNearby()
        {
            if (_newNearby.IsEmptyIgnoreFilter) return;
            var boxes = new List<(float2 Min, float2 Max)>();
            using (var all = _newNearby.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    if (boxes.Count > 2000) break;
                    if (EntityManager.HasComponent<global::Game.Buildings.Building>(e))
                    {
                        if (IsGrowable(e) || !EntityManager.HasComponent<global::Game.Objects.Transform>(e)) continue;   // new houses: their own pass
                        var p = EntityManager.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz;
                        boxes.Add((p - 40f, p + 40f));
                        continue;
                    }
                    if (EntityManager.HasComponent<Owner>(e))
                    {
                        var owner = EntityManager.GetComponentData<Owner>(e).m_Owner;
                        if (owner != Entity.Null && EntityManager.HasComponent<global::Game.Buildings.Building>(owner) && IsGrowable(owner)) continue;   // a house's own lawn or lot
                    }
                    if (EntityManager.HasComponent<Curve>(e))
                    {
                        var b = MathUtils.Bounds(EntityManager.GetComponentData<Curve>(e).m_Bezier);
                        boxes.Add((b.min.xz, b.max.xz));
                    }
                    else if (EntityManager.HasBuffer<global::Game.Areas.Node>(e))
                    {
                        var nodes = EntityManager.GetBuffer<global::Game.Areas.Node>(e, true);
                        if (nodes.Length == 0) continue;
                        float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
                        for (int k = 0; k < nodes.Length; k++) { lo = math.min(lo, nodes[k].m_Position.xz); hi = math.max(hi, nodes[k].m_Position.xz); }
                        boxes.Add((lo, hi));
                    }
                }
            if (boxes.Count == 0) return;
            EnsureGrid();
            float reach = ReachInUse + 30f;
            int marked = 0;
            foreach (var (bmin, bmax) in boxes)
            {
                var lo = bmin - reach;
                var hi = bmax + reach;
                for (float x = math.floor(lo.x / Cell) * Cell; x <= hi.x; x += Cell)
                    for (float z = math.floor(lo.y / Cell) * Cell; z <= hi.y; z += Cell)
                        if (_grid.TryGetValue(Key(new float2(x + 1f, z + 1f)), out var near))
                            foreach (int j in near)
                            {
                                var h = _gridHouses[j];
                                var p = h.T.m_Position.xz;
                                if (p.x < lo.x || p.y < lo.y || p.x > hi.x || p.y > hi.y || !EntityManager.Exists(h.E) || !IsYes(h.E)) continue;
                                if (_dirty.Add(h.E)) marked++;
                            }
            }
            if (marked > 0) Mod.Log.Info($"Placemaker neighbourhood polish: something new built nearby: {marked} gardens planned again");
        }

        /// <summary>A net edge's ground as a strip along its curve (its full width; ends square at its nodes).</summary>
        private static float2[] Strip(Curve curve, float half)
        {
            var b = curve.m_Bezier;
            float len = math.max(curve.m_Length, 1f);
            int n = math.clamp((int)(len / 8f), 2, 24);
            var leftSide = new List<float2>(n + 1);
            var rightSide = new List<float2>(n + 1);
            for (int k = 0; k <= n; k++)
            {
                float t = (float)k / n;
                var p = MathUtils.Position(b, t).xz;
                var d = math.normalizesafe(MathUtils.Tangent(b, t).xz, new float2(1f, 0f));
                var nrm = new float2(-d.y, d.x);
                leftSide.Add(p + nrm * half);
                rightSide.Add(p - nrm * half);
            }
            rightSide.Reverse();
            leftSide.AddRange(rightSide);
            return leftSide.ToArray();
        }
    }
}
