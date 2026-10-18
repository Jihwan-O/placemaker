// Game runner — the neighbourhood-polish painter and the per-lot flag (GPL-3.0).
//   {"do":"polish","stamp":"yes"}   the area picked with an "area" step is stamped through the panel: "Polish gardens" picked in
//                                   the Make row (Design), then Polish (the main button) or Unpolish ("stamp":"no"); waits until
//                                   the polish is idle ("wait": seconds, default 120) and records every lot in the land
//   {"do":"lab","op":"polishFlags"} every growable lot in the land: its flag and what the polish changed on it
//   {"do":"lab","op":"saveLoad"}    the city saved to a scratch save and loaded back (the flags and the edits must survive)
//   {"do":"lab","op":"removeAllPolish"}  the settings button "Remove all polish", then until the polish is idle

using System;
using System.Collections;
using System.Linq;
using Game;
using Game.Common;
using Game.SceneFlow;
using Game.Tools;
using Newtonsoft.Json.Linq;
using Placemaker.Core.Tool;
using Placemaker.Game.Neighbours;
using Placemaker.Game.Tool;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private IEnumerator StepPolish(ScenarioState st, JObject step, JObject rec)
        {
            bool yes = !string.Equals((string)step["stamp"] ?? "yes", "no", StringComparison.OrdinalIgnoreCase);
            if (!_tool.SuiteHasSelection || _tool.Shape == null) { rec["error"] = "polish: pick the lots with an area step first"; yield break; }
            // the painter is picked in the Make row, which the panel shows in Design
            if (!_tool.Painter)
            {
                yield return EnsureDesign(st, rec, "The type browser");
                yield return UiAct(st, rec, "Polish gardens", "recipe-" + PlacemakerToolSystem.PainterId, "selectRecipe", new object[] { PlacemakerToolSystem.PainterId },
                    () => _tool.Painter, () => _tool.SelectRecipe(PlacemakerToolSystem.PainterId));
                if (!_tool.Painter) { rec["error"] = "polish: the painter was not picked"; yield break; }
                yield return Frames(4);
            }
            var (lots, polished) = _tool.PainterCount;
            rec["areaLots"] = lots;
            rec["areaPolishedBefore"] = polished;
            var panel = Bound(rec, "state")?["painter"];
            if (panel != null) rec["panel"] = panel;
            int stamps0 = NeighbourPolishSystem.TotalStampedYes + NeighbourPolishSystem.TotalStampedNo;
            if (yes)
                yield return UiAct(st, rec, "Polish", "primary", "primary", null, () => !_tool.SuiteHasSelection, () => _tool.Primary());
            else
                yield return UiAct(st, rec, "Unpolish", "polish-no", "polishStamp", new object[] { false }, () => !_tool.SuiteHasSelection, () => _tool.PolishStamp(false));
            if (_tool.SuiteHasSelection) { rec["error"] = "polish: the area is still there after the stamp" + (_tool.FlashText != null ? ": " + _tool.FlashText.Key : ""); yield break; }
            yield return Until(() => NeighbourPolishSystem.TotalStampedYes + NeighbourPolishSystem.TotalStampedNo > stamps0, 5f, "stamp applied");
            rec["stamped"] = _tool.LastStamp.Lots;
            rec["yes"] = yes;
            yield return PolishIdle(rec, (float?)step["wait"] ?? 120f);
            rec["lots"] = PolishLots(st);
            rec["lotsLine"] = _lastLotsLine;
            Mod.Log.Info($"Placemaker suite: {(yes ? "polish" : "unpolish")} stamped on {_tool.LastStamp.Lots} lots; {_lastLotsLine}");
            Event("build");
        }

        /// <summary>Until the polish has nothing waiting (stamps planned, lots put back), then a few frames for the writes.</summary>
        private IEnumerator PolishIdle(JObject rec, float limit)
        {
            float t0 = Time.realtimeSinceStartup;
            yield return Frames(6);
            while ((NeighbourPolishSystem.Busy || NeighbourPolishSystem.Waiting > 0) && Time.realtimeSinceStartup - t0 < limit) yield return null;
            yield return Frames(20);
            rec["polishSeconds"] = Math.Round(Time.realtimeSinceStartup - t0, 1);
            if (NeighbourPolishSystem.Busy) rec["polishStillBusy"] = true;
        }

        /// <summary>Every growable lot in the land, west to east then south to north: flag and what the polish changed.</summary>
        private JArray PolishLots(ScenarioState st, JObject rec = null)
        {
            var sys = _world.GetExistingSystemManaged<NeighbourPolishSystem>();
            var arr = new JArray();
            if (sys == null) return arr;
            var b = st.S.Block;
            var em = _world.EntityManager;
            int yesN = 0, noN = 0, edited = 0, editedNo = 0;
            var line = new System.Text.StringBuilder();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Buildings.Building>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.ReadOnly<global::Game.Prefabs.PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
            {
                var inLand = all.Where(e =>
                {
                    var p = em.GetComponentData<global::Game.Objects.Transform>(e).m_Position;
                    var prefab = em.GetComponentData<global::Game.Prefabs.PrefabRef>(e).m_Prefab;
                    return p.x >= b.X0 && p.x <= b.X1 && p.z >= b.Z0 && p.z <= b.Z1 && em.HasComponent<global::Game.Prefabs.SpawnableBuildingData>(prefab);
                }).OrderBy(e => math.round(em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.x)).ThenBy(e => em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.z).ToList();
                foreach (var e in inLand)
                {
                    var (y, lines, lawns, pieces) = sys.Inspect(e);
                    var p = em.GetComponentData<global::Game.Objects.Transform>(e).m_Position;
                    bool changed = lines + lawns + pieces > 0;
                    if (y) yesN++; else noN++;
                    if (changed) { edited++; if (!y) editedNo++; }
                    line.Append(y ? (changed ? 'Y' : 'y') : (changed ? '!' : 'n'));
                    arr.Add(new JObject
                    {
                        ["id"] = e.Index, ["x"] = Math.Round(p.x, 1), ["z"] = Math.Round(p.z, 1), ["yes"] = y,
                        ["flagged"] = em.HasComponent<PolishedLot>(e), ["linesMoved"] = lines, ["lawnsChanged"] = lawns, ["pieces"] = pieces,
                        ["building"] = em.HasComponent<global::Game.Objects.UnderConstruction>(e),
                    });
                }
            }
            if (rec == null) rec = new JObject();
            _lastLotsLine = $"{yesN} yes, {noN} no, {edited} edited ({editedNo} of them unpolished); lots (y/Y yes untouched/edited, n/! no untouched/EDITED): {line}";
            return arr;
        }

        private string _lastLotsLine = "";

        private IEnumerator LabPolishFlags(ScenarioState st, JObject rec)
        {
            yield return Frames(10);
            rec["lots"] = PolishLots(st);
            rec["lotsLine"] = _lastLotsLine;
            rec["tinted"] = global::Placemaker.Game.Overlay.PlacemakerOverlaySystem.PolishedShown;
            rec["stampedYes"] = NeighbourPolishSystem.TotalStampedYes;
            rec["stampedNo"] = NeighbourPolishSystem.TotalStampedNo;
            rec["restored"] = NeighbourPolishSystem.TotalRestored;
            rec["carried"] = NeighbourPolishSystem.TotalCarried;
            rec["flaggedNew"] = NeighbourPolishSystem.TotalFlaggedNew;
            rec["tagged"] = NeighbourPolishSystem.TotalTagged;
            rec["edgePairs"] = NeighbourPolishSystem.TotalEdgePairs;
            Mod.Log.Info("Placemaker lab: polish flags " + _lastLotsLine + $"; tint shows {rec["tinted"]} lots; {rec["edgePairs"]} pairs against an unpolished lot so far");
        }

        private IEnumerator LabRemoveAllPolish(ScenarioState st, JObject rec)
        {
            int restored0 = NeighbourPolishSystem.TotalRestored;
            NeighbourPolishSystem.RemoveAll();
            yield return PolishIdle(rec, 120f);
            rec["restored"] = NeighbourPolishSystem.TotalRestored - restored0;
            rec["lots"] = PolishLots(st);
            rec["lotsLine"] = _lastLotsLine;
            Mod.Log.Info($"Placemaker lab: remove all polish: {rec["restored"]} lots put back; {_lastLotsLine}");
        }

        /// <summary>The city saved to a scratch save in the player's saves and loaded back (the save is deleted after).</summary>
        private IEnumerator LabSaveLoad(ScenarioState st, JObject rec)
        {
            const string name = "PlacemakerSuitePolish";
            var before = PolishLots(st);
            rec["beforeLine"] = _lastLotsLine;
            var menu = _world.GetExistingSystemManaged<global::Game.UI.Menu.MenuUISystem>();
            if (menu == null || GameManager.instance == null) { rec["error"] = "lab: saveLoad needs the game's menu system"; yield break; }
            System.Threading.Tasks.Task<bool> save = null;
            try { save = GameManager.instance.Save(name, menu.GetSaveInfo(false), Colossal.IO.AssetDatabase.AssetDatabase.user, (global::Game.UI.ScreenCaptureHelper.AsyncRequest)null); }
            catch (Exception e) { rec["error"] = "lab: save failed: " + e.Message; yield break; }
            yield return Until(() => save.IsCompleted, 120f, "save");
            if (!save.IsCompleted || save.IsFaulted) { rec["error"] = "lab: save failed: " + save.Exception?.GetBaseException().Message; yield break; }
            yield return Frames(10);
            var meta = ScratchSave(name);
            if (meta == null) { rec["error"] = "lab: the save was not found after saving"; yield break; }
            float t0 = Time.realtimeSinceStartup;
            Say("loading the scratch save back");
            var load = GameManager.instance.Load(GameMode.Game, Colossal.Serialization.Entities.Purpose.LoadGame, meta);
            yield return Until(() => load.IsCompleted, 600f, "load task");
            yield return Until(() => GameManager.instance.gameMode == GameMode.Game && !GameManager.instance.isGameLoading, 300f, "game mode after load");
            rec["loadSeconds"] = Math.Round(Time.realtimeSinceStartup - t0, 1);
            rec["loaded"] = load.IsCompleted && !load.IsFaulted && load.Result;
            yield return Frames(60);
            _world.GetOrCreateSystemManaged<global::Game.Simulation.SimulationSystem>().selectedSpeed = 0f;
            Event("load");
            yield return OpenTool();
            yield return Frames(30);
            rec["lots"] = PolishLots(st);
            rec["afterLine"] = _lastLotsLine;
            Mod.Log.Info($"Placemaker lab: save and load: before {rec["beforeLine"]}; after {_lastLotsLine}");
            try
            {
                var again = ScratchSave(name);
                if (again != null) SaveHelpers.DeleteSaveGame(again);
            }
            catch (Exception e) { rec["deleteNote"] = "the scratch save stays: " + e.Message; }
        }

        private static global::Game.Assets.SaveGameMetadata ScratchSave(string name)
        {
            foreach (var m in Colossal.IO.AssetDatabase.AssetDatabase.global.GetAssets<global::Game.Assets.SaveGameMetadata>())
            {
                string n;
                try { n = m.name; } catch (Exception) { continue; }
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return m;
            }
            return null;
        }
    }
}
