// Placemaker — the neighbourhood-polish painter's tint: every polished lot green while "Polish gardens" is the pick, like
// the game's zoning overlay; nothing for lots left as the game made them. Gone when the painter (or the tool) closes.

using System.Collections.Generic;
using Placemaker.Game.Neighbours;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Overlay
{
    public sealed partial class PlacemakerOverlaySystem
    {
        private static readonly Color PolishedTint = new Color(0.47f, 0.88f, 0.33f, 0.34f);
        private readonly List<float3[]> _polished = new List<float3[]>();
        private int _polishedFlags = -1, _polishedFrame;
        private float2 _polishedAt = new float2(float.MaxValue);
        private long _polishedSig;

        /// <summary>For the game runner: polished lots the tint shows now.</summary>
        public static int PolishedShown { get; private set; }

        private void DrawPolishedLots()
        {
            var polish = World.GetExistingSystemManaged<NeighbourPolishSystem>();
            var cam = Camera.main;
            if (polish == null || cam == null) return;
            // around the ground point the camera looks at (as far as it can see well)
            float3 cp = cam.transform.position;
            float3 fw = cam.transform.forward;
            float2 at = cp.xz + (fw.y < -0.05f ? fw.xz * math.min(cp.y / -fw.y, 3000f) : float2.zero);
            float radius = math.clamp(cp.y * 3f, 500f, 3000f);
            if (_polishedFlags != NeighbourPolishSystem.FlagsVersion || math.distance(at, _polishedAt) > radius * 0.2f || ++_polishedFrame > 180)
            {
                _polishedFlags = NeighbourPolishSystem.FlagsVersion;
                _polishedAt = at;
                _polishedFrame = 0;
                polish.PolishedRingsNear(at, radius * 1.3f, _polished);
                _polishedSig = FilledPreviewSystem.Signature(_polished) ^ _polishedFlags;
                PolishedShown = _polished.Count;
            }
            if (_polished.Count > 0) _fill.SetLayer("polishedLots", _polished, PolishedTint, -94.2f, _polishedSig);
        }
    }
}
