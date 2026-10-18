// Game runner lab (exploration, not a Placemaker feature): steps that try what a mod could do beyond Placemaker.
//   {"do":"lab","op":"materials"}  lists the vanilla building surfaces a generated shape could borrow
//   {"do":"lab","op":"mesh","shape":"wedge","size":[20,24],"height":10,"at":[0,0],"yaw":0,"material":"..."}
//       generates a mesh in code, makes it a new game object (SuiteLabShapeSystem) and places it like a vanilla prop.
// Every result is in the step's record and in Placemaker.log ("Placemaker lab: ...").

using System;
using System.Collections;
using System.Linq;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Tools;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private int _labShapes;

        private IEnumerator StepLab(ScenarioState st, JObject step, JObject rec)
        {
            switch (((string)step["op"] ?? "").ToLowerInvariant())
            {
                case "street": yield return LabStreet(st, step, rec); yield break;
                case "yards": yield return LabYards(st, step, rec); yield break;
                case "yardscheck": LabYardsCheck(rec); yield break;
                case "anatomy": LabAnatomy(st, rec); yield break;
                case "polishparts":
                {
                    // which parts neighbourhood polish edits: lines, lawns, both, none (isolates a crash to one kind of edit)
                    string parts = ((string)step["parts"] ?? "both").ToLowerInvariant();
                    Placemaker.Game.Neighbours.NeighbourPolishSystem.EditLines = parts == "lines" || parts == "both";
                    Placemaker.Game.Neighbours.NeighbourPolishSystem.EditLawns = parts == "lawns" || parts == "both";
                    rec["parts"] = parts;
                    Mod.Log.Info("Placemaker lab: neighbourhood polish edits " + parts);
                    yield break;
                }
                case "hotload":
                {
                    // a newer YardStitch (tools/hot/Build-Hot.ps1) becomes the neighbourhood-polish planner in this running game
                    string path = (string)step["path"];
                    try
                    {
                        var asm = System.Reflection.Assembly.LoadFile(path);
                        var type = asm.GetType("Placemaker.Core.Neighbours.YardStitch", true);
                        var method = type.GetMethod("Plan", new[] { typeof(System.Collections.Generic.IReadOnlyList<Placemaker.Core.Neighbours.StitchHouse>), typeof(Placemaker.Core.Neighbours.StitchSettings) });
                        if (method == null) { rec["error"] = "lab: hot planner has no Plan(houses, settings)"; yield break; }
                        Placemaker.Game.Neighbours.NeighbourPolishSystem.Planner = (Func<System.Collections.Generic.IReadOnlyList<Placemaker.Core.Neighbours.StitchHouse>, Placemaker.Core.Neighbours.StitchSettings, Placemaker.Core.Neighbours.StitchPlan>)
                            Delegate.CreateDelegate(typeof(Func<System.Collections.Generic.IReadOnlyList<Placemaker.Core.Neighbours.StitchHouse>, Placemaker.Core.Neighbours.StitchSettings, Placemaker.Core.Neighbours.StitchPlan>), method);
                        Placemaker.Game.Neighbours.NeighbourPolishSystem.Redo();
                        rec["loaded"] = asm.GetName().Name;
                        Mod.Log.Info("Placemaker lab: hot planner " + asm.GetName().Name + " loaded from " + path + ", every house again");
                    }
                    catch (Exception e) { rec["error"] = "lab: hotload failed: " + e.GetType().Name + ": " + e.Message; yield break; }
                    yield return Frames(20);
                    yield break;
                }
                case "polishsettings":
                {
                    // stitching rules changed in the running game (no rebuild, no relaunch), then every house again
                    var set = Placemaker.Game.Neighbours.NeighbourPolishSystem.Settings;
                    var applied = new JObject();
                    if (step["settings"] is JObject values)
                        foreach (var p in values.Properties())
                        {
                            var field = set.GetType().GetField(p.Name);
                            if (field == null || field.Name == "Extra") set.Extra[p.Name] = (float)p.Value;   // a knob for a hot planner
                            else field.SetValue(set, p.Value.ToObject(field.FieldType));
                            applied[p.Name] = p.Value;
                        }
                    rec["settings"] = applied;
                    Placemaker.Game.Neighbours.NeighbourPolishSystem.Redo();
                    Mod.Log.Info("Placemaker lab: stitching rules " + applied.ToString(Newtonsoft.Json.Formatting.None) + ", every house again");
                    yield return Frames(20);
                    yield break;
                }
                case "touchhouses":
                {
                    // the game rebuilds every house's fence lines in the land (as after a move or level-up): the polish must put
                    // its edits back in the same frame
                    var b = st.S.Block;
                    var em = _world.EntityManager;
                    int n = 0;
                    // inside the runner's tool update (ToolUpdate): the Modification systems of the same frame see the Updated
                    yield return ApplyWithFixtureTool(t =>
                    {
                        using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Buildings.Building>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.Exclude<global::Game.Common.Deleted>(), ComponentType.Exclude<global::Game.Tools.Temp>()))
                        using (var all = q.ToEntityArray(Unity.Collections.Allocator.Temp))
                            foreach (var e in all)
                            {
                                var p = em.GetComponentData<global::Game.Objects.Transform>(e).m_Position;
                                if (p.x < b.X0 || p.x > b.X1 || p.z < b.Z0 || p.z > b.Z1) continue;
                                em.AddComponent<global::Game.Common.Updated>(e);
                                n++;
                            }
                        return n;
                    }, "touch houses");
                    rec["touched"] = n;
                    Mod.Log.Info($"Placemaker lab: {n} houses updated (their fence lines rebuilt by the game)");
                    yield return Frames(30);
                    yield break;
                }
                case "polishstats":
                {
                    // neighbourhood polish totals since the game started (Game/Neighbours/NeighbourPolishSystem)
                    yield return Frames(30);
                    rec["enabled"] = Placemaker.Game.Neighbours.NeighbourPolishSystem.FeatureEnabled;
                    rec["passes"] = Placemaker.Game.Neighbours.NeighbourPolishSystem.TotalPasses;
                    rec["pairs"] = Placemaker.Game.Neighbours.NeighbourPolishSystem.TotalPairs;
                    rec["linesMoved"] = Placemaker.Game.Neighbours.NeighbourPolishSystem.TotalMoved;
                    rec["linesHidden"] = Placemaker.Game.Neighbours.NeighbourPolishSystem.TotalHidden;
                    rec["lawnsGrown"] = Placemaker.Game.Neighbours.NeighbourPolishSystem.TotalLawns;
                    rec["lawnM2"] = Math.Round(Placemaker.Game.Neighbours.NeighbourPolishSystem.TotalLawnM2);
                    rec["paintStamps"] = Placemaker.Game.Neighbours.NeighbourPaintSystem.TotalStamps;
                    rec["surfacesMade"] = Placemaker.Game.Neighbours.NeighbourAreaSystem.TotalMade;
                    Mod.Log.Info("Placemaker lab: polish stats " + new JObject { ["passes"] = rec["passes"], ["pairs"] = rec["pairs"], ["moved"] = rec["linesMoved"], ["hidden"] = rec["linesHidden"], ["lawns"] = rec["lawnsGrown"], ["m2"] = rec["lawnM2"], ["paintStamps"] = rec["paintStamps"], ["surfaces"] = rec["surfacesMade"] }.ToString(Newtonsoft.Json.Formatting.None));
                    yield break;
                }
            }
            var shapes = _world.GetExistingSystemManaged<SuiteLabShapeSystem>();
            if (shapes == null) { rec["error"] = "lab: the shape system is not registered"; yield break; }
            yield return WaitFor(rec, "lab database", () => shapes.DatabaseReady || shapes.DatabaseState.StartsWith("failed"), 90f);
            rec["database"] = shapes.DatabaseState;
            if (!shapes.DatabaseReady) { rec["error"] = "lab: shape database " + shapes.DatabaseState; yield break; }
            string op = ((string)step["op"] ?? "mesh").ToLowerInvariant();
            if (op == "materials") { LabMaterials(shapes, rec); yield break; }
            yield return LabMesh(st, step, rec, shapes);
        }

        private void LabMaterials(SuiteLabShapeSystem shapes, JObject rec)
        {
            var all = shapes.Candidates();
            var arr = new JArray();
            foreach (var c in all.Take(60))
                arr.Add(new JObject { ["surface"] = c.Label, ["template"] = c.Template, ["vt"] = c.Vt, ["octahedral"] = c.Octo, ["suitable"] = SuiteLabShapeSystem.Suitable(c), ["keywords"] = string.Join(" ", c.Keywords) });
            rec["materials"] = arr;
            rec["materialCount"] = all.Count;
            rec["suitableCount"] = all.Count(SuiteLabShapeSystem.Suitable);
            var templates = all.GroupBy(c => c.Template).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} x{g.Count()}");
            Mod.Log.Info($"Placemaker lab: {all.Count} building surfaces, {all.Count(SuiteLabShapeSystem.Suitable)} suitable; templates: {string.Join(", ", templates)}");
            foreach (var c in all.Take(25))
                Mod.Log.Info($"Placemaker lab: surface {c.Label} template {c.Template} VT {c.Vt} octahedral {c.Octo} suitable {SuiteLabShapeSystem.Suitable(c)} keywords [{string.Join(" ", c.Keywords)}]");
        }

        private IEnumerator LabMesh(ScenarioState st, JObject step, JObject rec, SuiteLabShapeSystem shapes)
        {
            var b = st.S.Block;
            string shape = (string)step["shape"] ?? "box";
            var size = step["size"] is JArray sz && sz.Count >= 2 ? new float2((float)sz[0], (float)sz[1]) : new float2(16f, 20f);
            float height = (float?)step["height"] ?? 12f;
            float yaw = (float?)step["yaw"] ?? 0f;
            var at = Pt(step["at"], b, false);
            var ring = SuiteLabMesh.Footprint(shape, size, step["points"] as JArray);
            var req = new SuiteLabShapeSystem.Request
            {
                Name = $"Placemaker Lab {shape} {++_labShapes}", Ring = ring, Height = height, Material = (string)step["material"],
            };
            shapes.Enqueue(req);
            yield return WaitFor(rec, "lab shape made", () => req.Done, 30f);
            foreach (var kv in req.Info) rec[kv.Key] = JToken.FromObject(kv.Value);
            rec["prefabName"] = req.Name;
            if (!req.Done || req.Error != null || req.Prefab == Entity.Null) { rec["error"] = "lab: shape not made: " + (req.Error ?? "timed out"); yield break; }
            var em = _world.EntityManager;
            yield return WaitFor(rec, "lab prefab ready", () => UnityEngine.Time.frameCount >= req.ReadyFrame && em.HasComponent<ObjectGeometryData>(req.Prefab), 15f);
            if (!em.HasComponent<ObjectGeometryData>(req.Prefab)) { rec["error"] = "lab: the new prefab never got its geometry data (not initialised)"; yield break; }
            var geom = em.GetComponentData<ObjectGeometryData>(req.Prefab);
            rec["geometrySize"] = new JArray(Math.Round(geom.m_Size.x, 2), Math.Round(geom.m_Size.y, 2), Math.Round(geom.m_Size.z, 2));
            rec["geometryFlags"] = geom.m_Flags.ToString();

            var rot = quaternion.RotateY(math.radians(yaw));
            var pos = new float3(at.x, _fixture.H(at.x, at.y), at.y);
            yield return ApplyWithFixtureTool(t =>
            {
                var def = t.NewDefinition();
                em.AddComponentData(def, new CreationDefinition { m_Prefab = req.Prefab, m_RandomSeed = 4242 + _labShapes });
                em.AddComponent<Updated>(def);
                em.AddComponentData(def, new ObjectDefinition
                {
                    m_Position = pos, m_LocalPosition = pos, m_Rotation = rot, m_LocalRotation = rot, m_Scale = new float3(1f),
                    m_Intensity = 1f, m_Age = 0.5f, m_Probability = 100, m_PrefabSubIndex = -1, m_ParentMesh = -1, m_Elevation = 0f,
                });
                return 1;
            }, "lab shape " + req.Name);
            yield return Frames(30);

            // the placed object, and whether the renderer took it (a MeshBatch entry = it is in a draw batch)
            Entity placed = Entity.Null;
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                    if (em.GetComponentData<PrefabRef>(e).m_Prefab == req.Prefab) { placed = e; break; }
            rec["placed"] = placed != Entity.Null;
            if (placed == Entity.Null) { rec["error"] = "lab: the definition applied but no object of the new prefab exists"; yield break; }
            int batches = em.HasBuffer<global::Game.Rendering.MeshBatch>(placed) ? em.GetBuffer<global::Game.Rendering.MeshBatch>(placed, true).Length : -1;
            rec["meshBatches"] = batches;
            rec["components"] = new JArray(em.GetComponentTypes(placed, Allocator.Temp).ToArray().Select(c => c.GetManagedType()?.Name ?? c.ToString()).OrderBy(x => x));

            // the footprint in the world, so the camera's "area"/"build" target frames the shape
            var world = ring.Select(p => { var r3 = math.mul(rot, new float3(p.x, 0f, p.y)); return new float2(pos.x + r3.x, pos.z + r3.z); }).ToArray();
            st.Ring = st.Ring == null ? world : st.Ring.Concat(world).ToArray();
            Event("build");
            Mod.Log.Info($"Placemaker lab: placed '{req.Name}' at ({pos.x:F0}, {pos.z:F0}), {batches} mesh batch(es), geometry {geom.m_Size}");
        }
    }
}
