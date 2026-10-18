// Placemaker neighbourhood polish — ground surfaces on shared land no house takes (GPL-3.0).
//
// The middle a ring of pie slices leaves (YardStitch StitchPlan.Commons) becomes a small shared green: a lawn and a mulch
// bed, vanilla surfaces (the base game has no decorative ground decals: docs/DECALS.md). They are created the way the game
// creates permanent pieces (ZoneSpawnSystem): a CreationDefinition flagged Permanent with the ring as nodes, Updated and
// Deleted, just before GenerateAreasSystem (Modification1) turns it into a real surface in the same frame. Free-standing,
// saved with the city; one per spot (an existing surface of the same kind with the same centre is left alone).

using System;
using System.Collections.Generic;
using Game;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourAreaSystem : GameSystemBase
    {
        public static readonly List<(string Prefab, float2[] Ring)> Queue = new List<(string, float2[])>();
        public static int TotalMade;

        private PrefabSystem _prefabs;
        private TerrainSystem _terrain;
        private Dictionary<string, Entity> _byName;
        private readonly HashSet<long> _made = new HashSet<long>();

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (Queue.Count == 0) return;
            try
            {
                if (_byName == null) Index();
                var heights = _terrain.GetHeightData(true);
                foreach (var (name, ring) in Queue)
                {
                    if (ring == null || ring.Length < 3 || !_byName.TryGetValue(name, out var prefab)) continue;
                    var c = float2.zero;
                    foreach (var p in ring) c += p;
                    c /= ring.Length;
                    long key = ((long)math.round(c.x) << 32) ^ (uint)(int)math.round(c.y) ^ ((long)prefab.Index << 20);
                    if (!_made.Add(key) || Exists(prefab, c)) continue;
                    var def = EntityManager.CreateEntity();
                    EntityManager.AddComponentData(def, new CreationDefinition { m_Prefab = prefab, m_Flags = CreationFlags.Permanent });
                    var nodes = EntityManager.AddBuffer<Node>(def);
                    foreach (var p in ring) nodes.Add(new Node(new float3(p.x, TerrainUtils.SampleHeight(ref heights, new float3(p.x, 0f, p.y)), p.y), float.MinValue));
                    nodes.Add(nodes[0]);
                    EntityManager.AddComponent<Updated>(def);
                    EntityManager.AddComponent<Deleted>(def);
                    TotalMade++;
                }
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker neighbourhood surfaces failed: " + e.Message); }
            Queue.Clear();
        }

        /// <summary>A surface of this kind already centred here (after a load our in-memory list is empty).</summary>
        private bool Exists(Entity prefab, float2 centre)
        {
            using (var q = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Areas.Surface>(), ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    if (EntityManager.GetComponentData<PrefabRef>(e).m_Prefab != prefab) continue;
                    var nodes = EntityManager.GetBuffer<Node>(e, true);
                    if (nodes.Length < 3) continue;
                    var c = float2.zero;
                    for (int i = 0; i < nodes.Length; i++) c += nodes[i].m_Position.xz;
                    if (math.distance(c / nodes.Length, centre) < 1.5f) return true;
                }
            return false;
        }

        private void Index()
        {
            _byName = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);
            using (var q = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<AreaData>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    string n;
                    try { n = _prefabs.GetPrefab<PrefabBase>(e)?.name; } catch (Exception) { continue; }
                    if (!string.IsNullOrEmpty(n) && !_byName.ContainsKey(n)) _byName[n] = e;
                }
        }
    }
}
