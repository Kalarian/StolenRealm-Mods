using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Burst2Flame;
using HarmonyLib;
using UnityEngine;

namespace SharedFortunes.Patches
{
    /// <summary>
    /// Character.Load(saveIndex) copies CharacterSaveFile.FortuneSaveData into Character.FortuneData and applies the
    /// equipped ones. Our postfix first pools that character's fortunes, then merges the pool back in: missing fortunes
    /// are added unequipped (IsNew), lower levels are raised (optionally capped to the character's level). If anything
    /// changed we re-run LoadFortunes (re-applies equipped ones at the new level) and ask for a save.
    /// Character.AddFortune / LevelUpAllMyFortunesToMyLevel keep the pool current while playing.
    /// A Character.Save prefix re-merges when the character's level has grown since the last merge (a brand-new
    /// character is merged at level 1 and then raised by LevelSync moments later).
    /// Persistence: the game's Character.Save silently does nothing for a character that has not joined the party this
    /// session (unless forced) and while it is still inside Load, so merges made on load are force-saved from the
    /// plugin's Update once the game has finished loading the roster.
    /// </summary>
    internal static class FortunePatches
    {
        private static readonly Dictionary<Character, int> _mergedAtLevel = new Dictionary<Character, int>();
        private static readonly List<Character> _changedOnLoad = new List<Character>();

        [HarmonyPatch(typeof(Character), nameof(Character.Load), new[] { typeof(int) })]
        private static class Character_Load
        {
            private static void Postfix(Character __instance, int saveIndex)
            {
                try
                {
                    if (MergeIntoCharacter(__instance) && saveIndex >= 0 && !_changedOnLoad.Contains(__instance)) _changedOnLoad.Add(__instance);
                }
                catch (Exception e) { SharedFortunesPlugin.Log.LogWarning("Fortune merge failed: " + e); }
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Save))]
        private static class Character_Save
        {
            private static void Prefix(Character __instance)
            {
                try
                {
                    if (!Eligible(__instance) || !__instance.IsLoaded) return;
                    if (GUIManager.instance != null && GUIManager.instance.CurrentGuiState == GUIState.CreatingCharacter) return;
                    int last;
                    if (_mergedAtLevel.TryGetValue(__instance, out last) && __instance.Level > last) MergeIntoCharacter(__instance);
                }
                catch (Exception e) { SharedFortunesPlugin.Log.LogWarning("Fortune re-merge on save failed: " + e); }
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.AddFortune))]
        private static class Character_AddFortune
        {
            private static void Postfix(Character __instance, string guid, float level)
            {
                try
                {
                    if (!Eligible(__instance)) return;
                    if (SharedFortunesPlugin.Pool.Offer(guid, level))
                    {
                        SharedFortunesPlugin.Pool.Save();
                        if (SharedFortunesPlugin.Cfg.Verbose.Value)
                            SharedFortunesPlugin.Log.LogInfo("Pool += " + guid + " L" + level.ToString("0", CultureInfo.InvariantCulture) + " (from " + __instance.CharacterName + ")");
                    }
                }
                catch (Exception e) { SharedFortunesPlugin.Log.LogWarning("Pool update failed: " + e); }
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.LevelUpAllMyFortunesToMyLevel))]
        private static class Character_LevelUpAllMyFortunesToMyLevel
        {
            private static void Postfix(Character __instance)
            {
                try
                {
                    if (!Eligible(__instance) || __instance.FortuneData == null) return;
                    bool changed = false;
                    foreach (FortuneSaveData f in __instance.FortuneData) changed |= SharedFortunesPlugin.Pool.Offer(f.Guid, f.Level);
                    if (changed) SharedFortunesPlugin.Pool.Save();
                }
                catch (Exception e) { SharedFortunesPlugin.Log.LogWarning("Pool update failed: " + e); }
            }
        }

        /// <summary>Called from the plugin's Update while patched: persists on-load merges once the roster is loaded.</summary>
        internal static void Tick()
        {
            if (_changedOnLoad.Count == 0) return;
            try
            {
                if (GameLogic.instance == null || !GameLogic.instance.FinishedLoadingCharacters) return;
                foreach (Character c in _changedOnLoad.ToArray())
                {
                    if (!CanSave(c)) continue;
                    _changedOnLoad.Remove(c);
                    if (SaveNow(c)) { if (SharedFortunesPlugin.Cfg.Verbose.Value) SharedFortunesPlugin.Log.LogInfo(c.CharacterName + ": fortunes saved to file"); }
                    else SharedFortunesPlugin.Log.LogWarning(c.CharacterName + ": fortune merge could not be saved now; it will be redone next launch");
                }
            }
            catch (Exception e) { SharedFortunesPlugin.Log.LogWarning("Tick failed: " + e); }
        }

        internal static void Reset() { _changedOnLoad.Clear(); _mergedAtLevel.Clear(); }

        private static bool Eligible(Character c)
        {
            SharedFortunesConfig cfg = SharedFortunesPlugin.Cfg;
            if (cfg == null || !cfg.Enabled.Value || c == null || c.IsAI) return false;
            if (!c.Owned) return false; // never pool from, or push into, a co-op partner's character
            if (!cfg.IncludeRoguelikeCharacters.Value)
            {
                // The live flag is set during creation BEFORE the first save copies it into the file; check both, plus the mode.
                if (c.IsRoguelikeCharacter) return false;
                if (c.CharacterSaveFile != null && c.CharacterSaveFile.IsRoguelikeCharacter) return false;
                try { if (Game.Instance != null && Game.Instance.RoguelikeModeActive) return false; } catch { }
            }
            return true;
        }

        /// <summary>Can Character.Save(force: true) actually write this character right now? (Mirrors the guards in Character.Save.)</summary>
        private static bool CanSave(Character c)
        {
            try
            {
                if (c == null || c.SavingDisabled || c.IsLocalOnlyCharacter) return false;
                if (GameLogic.instance == null || !GameLogic.instance.AllMyCharacters.Contains(c)) return false;
                bool loading;
                if (Character.IsLoadingMap.TryGetValue(c.SaveIndex, out loading) && loading) return false;
                return true;
            }
            catch { return false; }
        }

        private static bool SaveNow(Character c)
        {
            if (!CanSave(c)) return false;
            try
            {
                string path = c.CurrentFilename;
                bool existed = File.Exists(path);
                DateTime before = existed ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                long lenBefore = existed ? new FileInfo(path).Length : -1;
                c.Save(force: true);
                if (!File.Exists(path)) return false;
                return File.GetLastWriteTimeUtc(path) != before || new FileInfo(path).Length != lenBefore;
            }
            catch (Exception e) { SharedFortunesPlugin.Log.LogWarning("Save of " + c.CharacterName + " threw: " + e.Message); return false; }
        }

        /// <summary>Returns true when the character's fortune list changed.</summary>
        private static bool MergeIntoCharacter(Character c)
        {
            if (!Eligible(c) || c.FortuneData == null) return false;
            SharedFortunesConfig cfg = SharedFortunesPlugin.Cfg;
            FortunePool pool = SharedFortunesPlugin.Pool;

            // 1. This character's own fortunes go into the pool (never lowers anything).
            bool poolChanged = false;
            foreach (FortuneSaveData f in c.FortuneData)
                if (f != null) poolChanged |= pool.Offer(f.Guid, f.Level);

            // 2. Pool into the character: add missing, raise lower.
            int level = Mathf.Max(1, c.Level);
            float cap = cfg.CapToCharacterLevel.Value ? level : 30f;
            _mergedAtLevel[c] = level;
            var byGuid = new Dictionary<string, FortuneSaveData>(StringComparer.OrdinalIgnoreCase);
            foreach (FortuneSaveData f in c.FortuneData) if (f != null && f.Guid != null) byGuid[f.Guid] = f;

            int added = 0, raised = 0;
            foreach (KeyValuePair<string, float> kv in pool.Entries)
            {
                float target = Mathf.Min(kv.Value, cap);
                FortuneSaveData mine;
                if (byGuid.TryGetValue(kv.Key, out mine))
                {
                    if (mine.Level < target)
                    {
                        if (cfg.Verbose.Value) SharedFortunesPlugin.Log.LogInfo(c.CharacterName + ": " + kv.Key + " L" + mine.Level + " -> L" + target);
                        mine.Level = target;
                        raised++;
                    }
                }
                else
                {
                    c.FortuneData.Add(new FortuneSaveData { Guid = kv.Key, Level = target, EquippedSlotIndex = -1, IsNew = true });
                    if (cfg.Verbose.Value) SharedFortunesPlugin.Log.LogInfo(c.CharacterName + ": + " + kv.Key + " L" + target);
                    added++;
                }
            }

            if (poolChanged) pool.Save();
            if (added > 0 || raised > 0)
            {
                c.LoadFortunes();          // re-apply equipped fortunes at their new level
                c.QueueCharacterSave();    // a no-op for a non-party character; Tick force-saves those after loading
                SharedFortunesPlugin.Log.LogInfo(c.CharacterName + " (L" + level + "): " + added + " fortunes added, " + raised + " raised from the shared pool");
                return true;
            }
            if (cfg.Verbose.Value)
                SharedFortunesPlugin.Log.LogInfo(c.CharacterName + ": already has everything in the pool (" + c.FortuneData.Count + " fortunes)");
            return false;
        }
    }
}
