// Preset browser (contract.ts: selectPreset, savePreset, savePresetAs, renamePreset, deletePreset,
// exportPresets; design.activePreset / design.presetsFile). Category ▸ Type ▸ Preset: picking a
// preset selects that type (keeping the area) and applies the preset as ONE undo step. Saving,
// renaming and deleting presets are file operations (presets.json), not design edits.

using System;
using Placemaker.Core;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Placemaker.Game.Settings;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        private string _presetLayer = "", _presetId = "";
        private string _presetsFile;
        private ActivePresetInfo _activeCache;
        private int _activeDesignStamp = -1, _activePresetsStamp = -1;
        private string _activeKey;

        public PresetStore Presets => _presets;

        /// <summary>Absolute path of presets.json once exportPresets ran (design.presetsFile), else null.</summary>
        public string PresetsFile => _presetsFile;

        /// <summary>design.activePreset: the preset applied to the root step and whether the design still matches it.</summary>
        public ActivePresetInfo ActivePreset
        {
            get
            {
                string key = _presetLayer + "|" + _presetId;
                if (_activeCache != null && _activeDesignStamp == _designVersion && _activePresetsStamp == _presets.Version && _activeKey == key)
                    return _activeCache;
                _activeDesignStamp = _designVersion;
                _activePresetsStamp = _presets.Version;
                _activeKey = key;
                try { _activeCache = _presets.Active(_recipe?.Root, _presetLayer, _presetId); }
                catch (Exception e)
                {
                    Mod.Log.Warn("Placemaker: active preset check failed: " + e.Message);
                    _activeCache = new ActivePresetInfo { Layer = _recipe?.Root?.Layer ?? "" };
                }
                return _activeCache;
            }
        }

        private void SetActivePreset(string layer, string id)
        {
            _presetLayer = layer ?? "";
            _presetId = id ?? "";
            _designVersion++;   // view-only change: no re-plan
            Touch();
        }

        /// <summary>
        /// UI: pick a type's preset. Selects the type's single-layer recipe unless the design already
        /// has that type at its root (the area always stays), then applies the preset — one undo step.
        /// </summary>
        public void SelectPreset(string layerId, string presetId)
        {
            EndValuePreview(true);
            if (string.IsNullOrEmpty(layerId)) return;
            SetPainter(false);
            layerId = LayerAliases.Canonical(layerId);
            presetId = presetId ?? "";
            if (presetId.Length > 0 && !_presets.Exists(layerId, presetId)) { Flash(new LocText("Placemaker.Problem.UnknownPreset")); return; }

            bool switched = false;
            if (_recipe?.Root == null || _recipe.Root.Layer != layerId)
            {
                var recipeId = RecipeFor(layerId);
                if (recipeId == null) { Flash(new LocText("Placemaker.Problem.UnknownRecipe")); return; }
                if (!SwitchRecipe(recipeId)) return;
                switched = true;
            }
            bool changed = switched;
            if (presetId.Length > 0)
            {
                var working = _recipe.Clone();
                if (_presets.Apply(working.Root, presetId, NewSeed)) { _recipe = working; changed = true; }
            }
            bool activeChanged = _presetLayer != layerId || _presetId != presetId;
            if (!changed && !activeChanged) return;
            _presetLayer = layerId;
            _presetId = presetId;
            _focus = RecipePaths.Root;
            DesignChanged();
            Commit("Placemaker.Undo.SelectPreset");
        }

        /// <summary>Id of the type's one-layer recipe (synthesized id = layer id; else a shipped single-type recipe).</summary>
        private string RecipeFor(string layerId)
        {
            var e = _recipes.Find(layerId);
            if (e?.Recipe?.Root != null && e.Recipe.Root.Layer == layerId) return e.Recipe.Id;
            foreach (var x in _recipes.Entries)
            {
                var root = x.Recipe?.Root;
                if (x.Custom || root == null || root.Layer != layerId) continue;
                if (root.Children.Count == 0 && root.RegionOverrides.Count == 0) return x.Recipe.Id;
            }
            foreach (var x in _recipes.Entries)
                if (!x.Custom && x.Recipe?.Root?.Layer == layerId) return x.Recipe.Id;
            return null;
        }

        /// <summary>
        /// Old trigger (applyPreset(path, id)), kept: built-in presets overlay their values (as before);
        /// overridden and custom presets apply the full stored step. On the root it becomes the active preset.
        /// </summary>
        public void ApplyPreset(string path, string presetId)
        {
            EndValuePreview(true);
            var step = RecipePaths.Resolve(_recipe, path);
            if (step == null || string.IsNullOrEmpty(presetId) || !_presets.Exists(step.Layer, presetId)) return;
            bool isRoot = step == _recipe.Root;
            if (isRoot) { _presetLayer = step.Layer; _presetId = presetId; }
            bool user = _presets.Find(step.Layer, presetId) != null;
            bool edited = user
                ? Edit(r => _presets.Apply(RecipePaths.Resolve(r, path), presetId, NewSeed), "Placemaker.Undo.ApplyPreset")
                : Edit(r => _editor.ApplyPreset(r, path, presetId), "Placemaker.Undo.ApplyPreset");
            if (!edited && isRoot) { _history.ReplaceCurrent(Capture()); SetActivePreset(_presetLayer, _presetId); }
        }

        /// <summary>UI: save the step's settings + features over a custom preset, or as an override of a built-in.</summary>
        public void SavePreset(string path, string presetId)
        {
            EndValuePreview(true);
            var step = RecipePaths.Resolve(_recipe, path);
            if (step == null) return;
            bool isRoot = step == _recipe.Root;
            if (string.IsNullOrEmpty(presetId) && isRoot && _presetLayer == step.Layer) presetId = _presetId;
            var saved = _presets.Save(step, presetId);
            if (saved == null) { Flash(new LocText("Placemaker.Problem.PresetSaveFailed")); return; }
            if (!Persist()) return;
            if (isRoot) ActivePresetSaved(step.Layer, saved.Id);
            Flash(new LocText("Placemaker.Hint.PresetSaved"));
        }

        /// <summary>UI: save the step's settings + features as a new custom preset (becomes active on the root).</summary>
        public void SavePresetAs(string path, string name)
        {
            EndValuePreview(true);
            var step = RecipePaths.Resolve(_recipe, path);
            if (step == null) return;
            bool isRoot = step == _recipe.Root;
            string basedOn = isRoot && _presetLayer == step.Layer ? _presetId : "";
            var saved = _presets.SaveAs(step, name, basedOn);
            if (saved == null) { Flash(new LocText("Placemaker.Problem.PresetSaveFailed")); return; }
            if (!Persist()) return;
            if (isRoot) ActivePresetSaved(step.Layer, saved.Id);
            Flash(new LocText("Placemaker.Hint.PresetSaved"));
        }

        public void RenamePreset(string layerId, string presetId, string name)
        {
            if (!_presets.Rename(layerId, presetId, name)) { Flash(new LocText("Placemaker.Problem.CannotRenamePreset")); return; }
            Persist();
            Touch();
        }

        /// <summary>UI: delete a custom preset, or reset an overridden built-in to its default.</summary>
        public void DeletePreset(string layerId, string presetId)
        {
            bool wasOverride = _presets.Find(layerId, presetId)?.IsOverride == true && _presets.BuiltIn(layerId, presetId) != null;
            if (!_presets.Delete(layerId, presetId)) { Flash(new LocText("Placemaker.Problem.CannotDeletePreset")); return; }
            Persist();
            Flash(new LocText(wasOverride ? "Placemaker.Hint.PresetReset" : "Placemaker.Hint.PresetDeleted"));
            // A deleted custom that was active leaves the design as it is, just no longer linked to a preset.
            if (!wasOverride && _presetLayer == LayerAliases.Canonical(layerId) && _presetId == presetId) ActivePresetSaved("", "");
            else Touch();
        }

        /// <summary>UI: (re)write presets.json and publish its absolute path (design.presetsFile).</summary>
        public void ExportPresets()
        {
            var path = PresetFile.Save(_presets);
            if (path == null) { Flash(new LocText("Placemaker.Problem.PresetSaveFailed")); return; }
            _presetsFile = path;
            Flash(new LocText("Placemaker.Hint.PresetsExported"));
            _designVersion++;
            Touch();
        }

        private bool Persist()
        {
            if (PresetFile.Save(_presets) != null) return true;
            Flash(new LocText("Placemaker.Problem.PresetSaveFailed"));
            return false;
        }

        // Saving links the current design to the preset without an undo step: the current history entry is updated.
        private void ActivePresetSaved(string layer, string id)
        {
            _presetLayer = layer ?? "";
            _presetId = id ?? "";
            _history.ReplaceCurrent(Capture());
            SetActivePreset(_presetLayer, _presetId);
        }
    }
}
