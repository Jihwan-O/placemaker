// Placemaker neighbourhood polish — look areas (GPL-3.0).
//
// A Zoning type built with "Zone this area" zones the empty cells and leaves a look area behind (saved with the city: a
// headless entity with PolishLookArea, its outline and its changes). A building that grows inside one later is polished
// with the area's look and changes (FlagNewLots), the newest area first. An area built over an older one wholly covers
// it: the older one goes.

using System.Collections.Generic;
using Game.Common;
using Placemaker.Core.Geometry;
using Placemaker.Core.Neighbours;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourPolishSystem
    {
        private static readonly List<(float2[] Ring, string Look, IReadOnlyDictionary<string, float> Changes)> s_newAreas = new List<(float2[], string, IReadOnlyDictionary<string, float>)>();

        /// <summary>An area zoned with a look: buildings that grow in it get the look and these changes.</summary>
        public static void AddLookArea(float2[] ring, string look, IReadOnlyDictionary<string, float> changes)
        {
            if (ring == null || ring.Length < 3 || string.IsNullOrEmpty(look)) return;
            s_newAreas.Add((ring, look, changes));
        }

        private sealed class LookArea
        {
            public float2[] Ring;
            public float2 Min, Max;
            public int Look, Order;
            public PolishLookOverride[] Changes;
        }

        private EntityQuery _lookAreas;
        private List<LookArea> _areaCache;

        /// <summary>Look areas in the city (the status line).</summary>
        public static int LookAreaCount { get; private set; }

        private void CreateAreaQueries()
        {
            _lookAreas = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PolishLookArea>(), ComponentType.ReadOnly<PolishLookAreaPoint>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
        }

        private void AreasUpdate()
        {
            if (s_newAreas.Count == 0) return;
            var areas = LookAreas();
            int order = 0;
            foreach (var a in areas) order = math.max(order, a.Order);
            foreach (var (ring, look, changes) in s_newAreas)
            {
                // older areas this one wholly covers go
                int gone = 0;
                using (var old = _lookAreas.ToEntityArray(Allocator.Temp))
                    foreach (var e in old)
                    {
                        var pts = EntityManager.GetBuffer<PolishLookAreaPoint>(e);
                        bool inside = pts.Length > 0;
                        for (int k = 0; k < pts.Length && inside; k++) inside = Poly.Contains(ring, pts[k].Position);
                        if (!inside) continue;
                        EntityManager.DestroyEntity(e);
                        gone++;
                    }
                Poly.Bounds(ring, out var min, out var max);
                var area = EntityManager.CreateEntity();
                EntityManager.AddComponentData(area, new PolishLookArea { Look = PolishLooks.Hash(look), Order = ++order, Min = min, Max = max });
                var buf = EntityManager.AddBuffer<PolishLookAreaPoint>(area);
                foreach (var p in ring) buf.Add(new PolishLookAreaPoint { Position = p });
                var ch = EntityManager.AddBuffer<PolishLookOverride>(area);
                if (changes != null) foreach (var kv in changes) ch.Add(new PolishLookOverride { Knob = PolishLooks.Hash(kv.Key), Value = kv.Value });
                Mod.Log.Info($"Placemaker neighbourhood polish: look area {order} ({look}, {changes?.Count ?? 0} changes, {Poly.Area(ring):F0} m²): buildings that grow in it get its look{(gone > 0 ? $"; {gone} older areas inside it gone" : "")}");
            }
            s_newAreas.Clear();
            _areaCache = null;
        }

        /// <summary>The look areas, newest first.</summary>
        private List<LookArea> LookAreas()
        {
            if (_areaCache != null) return _areaCache;
            var list = new List<LookArea>();
            using (var all = _lookAreas.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    var a = EntityManager.GetComponentData<PolishLookArea>(e);
                    var pts = EntityManager.GetBuffer<PolishLookAreaPoint>(e);
                    var ring = new float2[pts.Length];
                    for (int k = 0; k < ring.Length; k++) ring[k] = pts[k].Position;
                    var ch = EntityManager.HasBuffer<PolishLookOverride>(e) ? EntityManager.GetBuffer<PolishLookOverride>(e).AsNativeArray().ToArray() : new PolishLookOverride[0];
                    list.Add(new LookArea { Ring = ring, Min = a.Min, Max = a.Max, Look = a.Look, Order = a.Order, Changes = ch });
                }
            list.Sort((x, y) => y.Order.CompareTo(x.Order));
            LookAreaCount = list.Count;
            return _areaCache = list;
        }

        /// <summary>The newest look area at a point, or null.</summary>
        private LookArea LookAreaAt(float2 p)
        {
            foreach (var a in LookAreas())
            {
                if (p.x < a.Min.x || p.y < a.Min.y || p.x > a.Max.x || p.y > a.Max.y) continue;
                if (a.Ring.Length >= 3 && Poly.Contains(a.Ring, p)) return a;
            }
            return null;
        }
    }
}
