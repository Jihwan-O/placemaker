// Placemaker neighbourhood polish — writes the lawns NeighbourPolishSystem planned (GPL-3.0).
//
// A surface's nodes may only change before Game.Areas.GeometrySystem (Modification2B) in a frame: that system
// re-triangulates Updated areas, and everything after it (Areas.SearchSystem, SurfaceExpandSystem, the area batches)
// reads the triangles. NeighbourPolishSystem plans in Modification4B (after the houses' fence lanes are rebuilt), so its
// lawn edits wait here for the next frame. A first version wrote them in 4B and crashed the game natively on the
// first pass (triangles pointing past the new ring).

using System.Collections.Generic;
using Game;
using Game.Common;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourLawnSystem : GameSystemBase
    {
        public static readonly List<(Entity Lawn, float3[] Nodes)> Queue = new List<(Entity, float3[])>();

        [Preserve]
        protected override void OnUpdate()
        {
            if (Queue.Count == 0) return;
            foreach (var (lawn, ring) in Queue)
            {
                if (!EntityManager.Exists(lawn) || EntityManager.HasComponent<Deleted>(lawn) || !EntityManager.HasBuffer<global::Game.Areas.Node>(lawn)) continue;
                var nodes = EntityManager.GetBuffer<global::Game.Areas.Node>(lawn);
                nodes.Clear();
                foreach (var p in ring) nodes.Add(new global::Game.Areas.Node(p, float.MinValue));
                EntityManager.AddComponent<Updated>(lawn);
            }
            Queue.Clear();
        }
    }
}
