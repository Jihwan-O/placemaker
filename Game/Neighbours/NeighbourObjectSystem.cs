// Placemaker neighbourhood polish — bushes planted in stitched gardens, owned by the house (GPL-3.0).
//
// YardStitch plans beds along the new fences and hedges with bushes on them (StitchPlan.Bushes). Each bush is created the
// way the game spawns a growable's pieces (ZoneSpawnSystem): a CreationDefinition flagged Permanent, with the house as
// owner, and an ObjectDefinition, Updated and Deleted, just before GenerateObjectsSystem (Modification1) makes the object
// in the same frame. Owned by the house: it is one of the house's own pieces, bulldozed with it; a rebuild (level-up)
// removes it with the house's other pieces and the next polish pass plants it again. One per spot: a bush of the same
// kind the house already owns within 0.5 m is left alone.

using System;
using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourObjectSystem : GameSystemBase
    {
        public sealed class Request
        {
            public string Name;
            public float2 Position;
            public float Yaw;
            public Entity Owner;
        }

        public static readonly List<Request> Queue = new List<Request>();
        public static int TotalMade;

        private PrefabSystem _prefabs;
        private TerrainSystem _terrain;
        private Dictionary<string, Entity> _byName;
        private static HashSet<string> _pieces;

        /// <summary>A piece the game has (its fence kind's), else a low hedge piece.</summary>
        public static string PieceName(string name) => _pieces == null || _pieces.Contains(name) ? name : "HedgePieceLow01";
        private EntityArchetype _definition;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            _definition = EntityManager.CreateArchetype(ComponentType.ReadWrite<CreationDefinition>(), ComponentType.ReadWrite<ObjectDefinition>(), ComponentType.ReadWrite<Updated>(), ComponentType.ReadWrite<Deleted>());
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (Queue.Count == 0) return;
            try
            {
                if (_byName == null) Index();
                var heights = _terrain.GetHeightData(true);
                foreach (var r in Queue)
                {
                    if (r.Owner == Entity.Null || !EntityManager.Exists(r.Owner) || EntityManager.HasComponent<Deleted>(r.Owner)) continue;
                    if (string.IsNullOrEmpty(r.Name) || !_byName.TryGetValue(r.Name, out var prefab)) continue;
                    if (Owns(r.Owner, prefab, r.Position)) continue;
                    var pos = new float3(r.Position.x, TerrainUtils.SampleHeight(ref heights, new float3(r.Position.x, 0f, r.Position.y)), r.Position.y);
                    var rot = quaternion.RotateY(r.Yaw);
                    var def = EntityManager.CreateEntity(_definition);
                    EntityManager.SetComponentData(def, new CreationDefinition
                    {
                        m_Prefab = prefab, m_Owner = r.Owner, m_Flags = CreationFlags.Permanent,
                        m_RandomSeed = (int)math.hash(new float2(math.round(r.Position.x * 4f), math.round(r.Position.y * 4f))),
                    });
                    EntityManager.SetComponentData(def, new ObjectDefinition
                    {
                        m_ParentMesh = -1, m_Position = pos, m_Rotation = rot, m_LocalPosition = pos, m_LocalRotation = rot,
                        m_Scale = new float3(1f), m_Intensity = 1f, m_Probability = 100, m_PrefabSubIndex = -1,
                        m_Age = 0.425f,   // grown, not a sapling (ToolUtils.GetRandomAge stage 2)
                    });
                    NeighbourPolishSystem.PendingTags.Add(new NeighbourPolishSystem.PendingTag { Owner = r.Owner, Prefab = prefab, At = r.Position, Kind = PolishPieceKind.Garden, Frame = NeighbourPolishSystem.LotFrame });
                    TotalMade++;
                }
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker neighbourhood bushes failed: " + e.Message); }
            Queue.Clear();
        }

        /// <summary>The house already owns a piece of this kind here (after a load, our list of what we made is empty).</summary>
        private bool Owns(Entity owner, Entity prefab, float2 at)
        {
            if (!EntityManager.HasBuffer<global::Game.Objects.SubObject>(owner)) return false;
            var subs = EntityManager.GetBuffer<global::Game.Objects.SubObject>(owner, true);
            for (int i = 0; i < subs.Length; i++)
            {
                var o = subs[i].m_SubObject;
                if (!EntityManager.Exists(o) || EntityManager.HasComponent<Deleted>(o) || !EntityManager.HasComponent<PrefabRef>(o)) continue;
                if (EntityManager.GetComponentData<PrefabRef>(o).m_Prefab != prefab || !EntityManager.HasComponent<Transform>(o)) continue;
                if (math.distance(EntityManager.GetComponentData<Transform>(o).m_Position.xz, at) < 0.5f) return true;
            }
            return false;
        }

        private void Index()
        {
            _byName = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);
            var wanted = new HashSet<string>(Placemaker.Core.Neighbours.YardStitch.BushKinds, StringComparer.OrdinalIgnoreCase);
            _pieces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var q = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<ObjectGeometryData>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    string n;
                    try { n = _prefabs.GetPrefab<PrefabBase>(e)?.name; } catch (Exception) { continue; }
                    if (string.IsNullOrEmpty(n) || _byName.ContainsKey(n)) continue;
                    // fence and hedge pieces (what fences are made of): every kind, for closing gardens
                    bool piece = n.IndexOf("Piece", StringComparison.OrdinalIgnoreCase) >= 0 && (n.IndexOf("Fence", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Hedge", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (piece) _pieces.Add(n);
                    if (wanted.Contains(n) || piece) _byName[n] = e;
                }
            Mod.Log.Info($"Placemaker neighbourhood bushes: {_byName.Count} of {wanted.Count} kinds found");
        }
    }
}
