// Placemaker — a net edge's ground surface as polygons, from the game's own geometry (GPL-3.0).
//
// Each edge's two halves (EdgeGeometry) and its two ends at the junctions (Start/EndNodeGeometry, cut like
// Game.Zones.CellBlockJobs cuts zone cells with them): junction corners, roundabouts and dead ends' turning circles come out
// exact. A strip along the edge's curve misses all of these (it ends square at the node's centre), so anything built from
// it grew into turning circles and roundabouts. Used by the area tools' walls (AreaWalls) and the gardens' barriers
// (NeighbourPolishSystem.BarriersIn). Read-only; main thread.

using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Net;
using Game.Prefabs;
using Placemaker.Core.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Selection
{
    public static class RoadSurfaces
    {
        /// <summary>
        /// The edge's ground pieces (counter-clockwise rings) added to <paramref name="into"/>; Round marks a piece of a round
        /// end (a turning circle or a roundabout). Ends that are bridges or tunnels are left out. Returns how many were added
        /// (0 when the edge has no geometry yet).
        /// </summary>
        public static int Pieces(EntityManager em, Entity edge, List<(float2[] Ring, bool Round)> into)
        {
            if (!em.HasComponent<EdgeGeometry>(edge)) return 0;
            var g = em.GetComponentData<EdgeGeometry>(edge);
            int n = Strip(g.m_Start.m_Left, g.m_Start.m_Right, false, into) + Strip(g.m_End.m_Left, g.m_End.m_Right, false, into);
            var comp = em.HasComponent<Composition>(edge) ? em.GetComponentData<Composition>(edge) : default;
            if (em.HasComponent<StartNodeGeometry>(edge) && GroundNode(em, comp.m_StartNode))
                n += NodePiece(em.GetComponentData<StartNodeGeometry>(edge).m_Geometry, into);
            if (em.HasComponent<EndNodeGeometry>(edge) && GroundNode(em, comp.m_EndNode))
                n += NodePiece(em.GetComponentData<EndNodeGeometry>(edge).m_Geometry, into);
            return n;
        }

        /// <summary>A junction end that is not a tunnel or a bridge (its own composition says so).</summary>
        private static bool GroundNode(EntityManager em, Entity composition)
        {
            if (composition == Entity.Null || !em.HasComponent<NetCompositionData>(composition)) return true;
            var flags = em.GetComponentData<NetCompositionData>(composition).m_Flags.m_General;
            return (flags & (CompositionFlags.General.Elevated | CompositionFlags.General.Tunnel)) == 0;
        }

        /// <summary>One junction end, cut like CellBlockJobs: round ends (turning circles, roundabouts) in three strips.</summary>
        private static int NodePiece(EdgeNodeGeometry n, List<(float2[] Ring, bool Round)> into)
        {
            if (n.m_MiddleRadius > 0f)
            {
                var mid = MathUtils.Lerp(n.m_Right.m_Left, n.m_Right.m_Right, 0.5f);
                mid.d = n.m_Middle.d;
                return Strip(n.m_Left.m_Left, n.m_Left.m_Right, true, into) + Strip(n.m_Right.m_Left, mid, true, into) + Strip(mid, n.m_Right.m_Right, true, into);
            }
            return Strip(n.m_Left.m_Left, n.m_Middle, false, into) + Strip(n.m_Middle, n.m_Right.m_Right, false, into);
        }

        /// <summary>The surface between two side curves (left forward, right back) as one polygon.</summary>
        private static int Strip(Bezier4x3 left, Bezier4x3 right, bool round, List<(float2[] Ring, bool Round)> into)
        {
            var pts = new List<float2>(20);
            Sample(left, true, pts);
            Sample(right, false, pts);
            var ring = Poly.Clean(pts, 0.02f);
            if (ring.Length < 3 || Poly.Area(ring) < 0.05f) return 0;
            foreach (var p in ring) if (!Poly.IsFinite(p)) return 0;
            into.Add((Poly.EnsureCcw(ring), round));
            return 1;
        }

        private static void Sample(Bezier4x3 c, bool forward, List<float2> into)
        {
            float chord = math.distance(c.a.xz, c.d.xz);
            float hull = math.distance(c.a.xz, c.b.xz) + math.distance(c.b.xz, c.c.xz) + math.distance(c.c.xz, c.d.xz);
            int n = hull - chord < 0.03f ? 1 : math.clamp((int)math.ceil(hull / 3f), 4, 12);
            for (int i = 0; i <= n; i++)
            {
                float t = forward ? (float)i / n : 1f - (float)i / n;
                into.Add(MathUtils.Position(c, t).xz);
            }
        }
    }
}
