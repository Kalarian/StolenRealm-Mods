using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Burst2Flame;
using HarmonyLib;
using UnityEngine;

namespace SharedProgress.Patches
{
    /// <summary>
    /// Campaign progress lives per character (CompletedQuestNodes = quest and town node GUIDs, LastMainQuestLevelCompleted
    /// = the quest level that decides the act, HighestCompletedLevel = max selectable quest level, ShopHighestLevel = shop
    /// stock tier, LastVisitedActIndex = the town you load into). The game already unions the PARTY's lists on the quest
    /// map and credits every party member on completion (QuestManager.CompleteQuest) and on entering a town
    /// (TownManager.OpenTown); this mod widens that to every campaign character on the account:
    ///   Character.Load postfix  -> offer the character's progress to the pool, then merge the pool into it (force-saved
    ///                              from Tick once the roster is loaded, because Save() skips non-party characters).
    ///   CompleteQuest / OpenTown postfix -> mark a sync; Tick (outside battle) offers the party's progress and merges
    ///                              the pool into every owned campaign character, saving the ones that changed.
    /// Only Owned characters (never a friend's), never Roguelike unless configured, never removes anything.
    /// </summary>
    internal static class ProgressPatches
    {
        private static readonly List<Character> _changedOnLoad = new List<Character>();
        private static bool _syncPending;
        private static string _syncReason;

        private static SharedProgressConfig Cfg => SharedProgressPlugin.Cfg;
        private static ProgressPool Pool => SharedProgressPlugin.Pool;

        [HarmonyPatch(typeof(Character), nameof(Character.Load), new[] { typeof(int) })]
        private static class Character_Load
        {
            // On the method: Harmony drops a class-level priority. Run early, like LevelSync.
            [HarmonyPriority(Priority.High)]
            private static void Postfix(Character __instance, int saveIndex)
            {
                try
                {
                    if (saveIndex < 0) return; // brand-new character: creation stays vanilla; it is merged on its first real load/save
                    if (!Eligible(__instance, forWrite: false)) return;
                    if (Pool.Offer(__instance)) Pool.Save();
                    if (!Eligible(__instance, forWrite: true)) return;
                    if (MergeInto(__instance, "account progress") && !_changedOnLoad.Contains(__instance)) _changedOnLoad.Add(__instance);
                }
                catch (Exception e) { SharedProgressPlugin.Log.LogWarning("Progress sync on load failed: " + e); }
            }
        }

        [HarmonyPatch(typeof(QuestManager), nameof(QuestManager.CompleteQuest))]
        private static class QuestManager_CompleteQuest
        {
            private static void Postfix(bool success) { if (success) { _syncPending = true; _syncReason = "quest completed"; } }
        }

        [HarmonyPatch(typeof(TownManager), nameof(TownManager.OpenTown))]
        private static class TownManager_OpenTown
        {
            private static void Postfix() { _syncPending = true; _syncReason = "town entered"; }
        }

        /// <summary>Called from the plugin's Update while patched.</summary>
        internal static void Tick()
        {
            try
            {
                if (_changedOnLoad.Count > 0 && GameLogic.instance != null && GameLogic.instance.FinishedLoadingCharacters)
                {
                    foreach (Character c in _changedOnLoad.ToArray())
                    {
                        if (!CanSave(c)) continue; // still loading; next tick
                        _changedOnLoad.Remove(c);
                        if (SaveNow(c)) { if (Cfg.Verbose.Value) SharedProgressPlugin.Log.LogInfo(c.CharacterName + ": progress saved to file"); }
                        else SharedProgressPlugin.Log.LogWarning(c.CharacterName + ": merged progress could not be saved now; it will be redone next launch");
                    }
                }
                if (!_syncPending) return;
                if (GUIManager.instance != null && GUIManager.instance.CurrentGuiState == GUIState.InBattle) return; // wait until the fight is over
                _syncPending = false;
                if (Pool == null || GameLogic.instance == null || GameLogic.instance.AllMyCharacters == null) return;
                bool grew = false;
                var party = NetworkingManager.Instance != null ? NetworkingManager.Instance.MyPartyCharacters : null;
                if (party != null) foreach (Character p in party) if (p != null && Eligible(p, forWrite: false) && Pool.Offer(p)) grew = true;
                foreach (Character c in GameLogic.instance.AllMyCharacters.ToList())
                {
                    if (c == null || !Eligible(c, forWrite: true)) continue;
                    if (!MergeInto(c, _syncReason)) continue;
                    if (!SaveNow(c)) SharedProgressPlugin.Log.LogWarning(c.CharacterName + ": progress merged but not saved now (" + _syncReason + "); it will be redone next launch");
                }
                if (grew) Pool.Save();
            }
            catch (Exception e) { SharedProgressPlugin.Log.LogWarning("Tick failed: " + e); }
        }

        internal static void Reset() { _syncPending = false; _changedOnLoad.Clear(); }

        // ---------- the merge ----------

        /// <summary>Give the character everything in the pool it does not have yet. Never lowers or removes.</summary>
        private static bool MergeInto(Character c, string reason)
        {
            ProgressPool pool = Pool;
            if (pool == null || c == null) return false;
            int addedNodes = 0, addedMains = 0;
            int oldMain = c.LastMainQuestLevelCompleted, oldAct = c.LastVisitedActIndex, oldShop = c.ShopHighestLevel, oldHighest = c.HighestCompletedLevel;
            bool changed = false;
            if (Cfg.ShareQuestMap.Value)
            {
                if (c.CompletedQuestNodes != null)
                {
                    var have = new HashSet<string>(c.CompletedQuestNodes.Where(x => x != null), StringComparer.OrdinalIgnoreCase);
                    foreach (string n in pool.Nodes) if (have.Add(n)) { c.CompletedQuestNodes.Add(n); addedNodes++; }
                }
                if (c.CompletedMainQuests != null)
                {
                    foreach (string m in pool.MainQuests)
                    {
                        Guid g; if (!Guid.TryParse(m, out g)) continue;
                        bool v; if (c.CompletedMainQuests.TryGetValue(g, out v) && v) continue;
                        c.CompletedMainQuests[g] = true; addedMains++;
                    }
                }
                int mainCap = int.MaxValue;
                try { var mains = GlobalSettingsManager.instance.miscSettings.MainQuests; if (mains != null && mains.Count > 0) mainCap = mains.Last().questLevel; } catch { }
                int wantMain = Math.Min(pool.LastMainQuestLevelCompleted, mainCap);
                if (wantMain > c.LastMainQuestLevelCompleted) { c.LastMainQuestLevelCompleted = wantMain; changed = true; }
                if (pool.CurrentMainQuestIndex > c.CurrentMainQuestIndex) { c.CurrentMainQuestIndex = pool.CurrentMainQuestIndex; changed = true; }
                if (addedNodes > 0 || addedMains > 0) changed = true;
            }
            if (Cfg.ShareActAndShops.Value)
            {
                int actCap = 3;
                try { var towns = GlobalSettingsManager.instance.miscSettings.TownGuids; if (towns != null && towns.Count > 0) actCap = towns.Count - 1; } catch { }
                int wantAct = Math.Min(pool.LastVisitedActIndex, actCap);
                if (wantAct > c.LastVisitedActIndex) { c.LastVisitedActIndex = wantAct; changed = true; }
                if (pool.HighestCompletedLevel > c.HighestCompletedLevel) { c.HighestCompletedLevel = pool.HighestCompletedLevel; changed = true; }
                if (pool.ShopHighestLevel > c.ShopHighestLevel) { c.ShopHighestLevel = pool.ShopHighestLevel; changed = true; }
            }
            if (changed)
            {
                SharedProgressPlugin.Log.LogInfo(c.CharacterName + ": +" + addedNodes + " quest nodes" + (addedMains > 0 ? ", +" + addedMains + " main quests" : "")
                    + (c.LastMainQuestLevelCompleted != oldMain ? ", main quest L" + oldMain + " -> L" + c.LastMainQuestLevelCompleted : "")
                    + (c.LastVisitedActIndex != oldAct ? ", act " + (oldAct + 1) + " -> " + (c.LastVisitedActIndex + 1) : "")
                    + (c.HighestCompletedLevel != oldHighest ? ", highest quest L" + oldHighest + " -> L" + c.HighestCompletedLevel : "")
                    + (c.ShopHighestLevel != oldShop ? ", shop L" + oldShop + " -> L" + c.ShopHighestLevel : "")
                    + " (" + reason + (pool.From.Length > 0 ? ", from " + pool.From : "") + ")");
            }
            else if (Cfg.Verbose.Value) SharedProgressPlugin.Log.LogInfo(c.CharacterName + ": already has the account's progress");
            return changed;
        }

        // ---------- eligibility and saving (same rules as LevelSync) ----------

        private static bool Eligible(Character c, bool forWrite)
        {
            SharedProgressConfig cfg = Cfg;
            if (cfg == null || !cfg.Enabled.Value || c == null || c.IsAI) return false;
            if (!c.Owned) return false; // never a friend's character
            if (!cfg.IncludeRoguelikeCharacters.Value)
            {
                if (c.IsRoguelikeCharacter) return false;
                if (c.CharacterSaveFile != null && c.CharacterSaveFile.IsRoguelikeCharacter) return false;
                try { if (Game.Instance != null && Game.Instance.RoguelikeModeActive) return false; } catch { }
            }
            if (forWrite && c.HardcoreDeath) return false; // a dead hardcore character still counts as a source, but is not changed
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
            catch (Exception e) { SharedProgressPlugin.Log.LogWarning("Save of " + c.CharacterName + " threw: " + e.Message); return false; }
        }
    }
}
