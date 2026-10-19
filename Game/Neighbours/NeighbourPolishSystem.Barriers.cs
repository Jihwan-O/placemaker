// Placemaker neighbourhood polish — what gardens never cross and like to meet (GPL-3.0).
//
// For each pass the planner gets the barriers around its patch of town (StitchSettings.Barriers): the ground surfaces of
// roads, paths and tracks (each edge's curve as a strip of its full width, sidewalks included; elevated and tunnelled
// pieces left out) and the lots of buildings that are not houses (service buildings, Placemaker builds' buildings…). Two
// houses never join across one, no garden reaches onto one, and a garden side nobody else has reaches out to one near it
// (YardStitch.Hug). Read from the game's own search trees, so a patch costs the same in a city of any size.

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
        private List<float2[]> BarriersIn(float2 min, float2 max)
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
                        if (strip != null) list.Add(strip);
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
                        if (lot != null && lot.Length >= 3) list.Add(lot);
                    }
                }
            }
            finally { results.Dispose(); }
            return list;
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
