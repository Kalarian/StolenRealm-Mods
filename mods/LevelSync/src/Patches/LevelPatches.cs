using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Burst2Flame;
using HarmonyLib;
using UnityEngine;

namespace LevelSync.Patches
{
    /// <summary>
    /// Character.Level is floor(ExperienceLevel); skill and stat points are derived from Level, so raising
    /// ExperienceLevel is all it takes. Character.Load(saveIndex) postfix syncs a character when it is loaded;
    /// Character.GiveExperience postfix keeps the pool current and (from the next Update) pulls the party up.
    /// The raise mirrors what GiveExperience does after a level change (keep health/mana ratios, flag JustLeveled, save).
    /// Persistence: the game's Character.Save silently does nothing for a character that has not joined the party this
    /// session (unless forced) and while it is still inside Load, so raises made on load are force-saved from the
    /// plugin's Update once the game has finished loading the roster.
    /// </summary>
    internal static class LevelPatches
    {
        private static readonly List<Character> _raisedOnLoad = new List<Character>();

        [HarmonyPatch(typeof(Character), nameof(Character.Load), new[] { typeof(int) })]
        private static class Character_Load
        {
            // Run before SharedFortunes' Load postfix (caps shared fortunes to the character's level) and AutoSalvage's.
            // The attribute must sit on the method: Harmony silently drops a class-level [HarmonyPriority].
            [HarmonyPriority(Priority.High)]
            private static void Postfix(Character __instance, int saveIndex)
            {
                try
                {
                    if (saveIndex < 0) return; // brand-new character: leave the creation screen vanilla; synced on its first real save
                    if (!Eligible(__instance)) return;
                    LevelPool pool = LevelSyncPlugin.Pool;
                    bool grew = pool.Offer(__instance.ExperienceLevel, __instance.CharacterName);
                    if (grew) pool.Save();
                    if (RaiseIfBelow(__instance, pool.Highest, "account highest") && !_raisedOnLoad.Contains(__instance)) _raisedOnLoad.Add(__instance);
                }
                catch (Exception e) { LevelSyncPlugin.Log.LogWarning("Level sync on load failed: " + e); }
            }
        }

        /// <summary>
        /// Rewards hand XP to each party member one after another in the same frame (PostBattleManager, AdventureRewards).
        /// Raising the others immediately would compound: A gets its XP, B is raised to A, then B gets its own XP on top,
        /// A is raised to B... so a party of N would earn about N times the XP. Instead the pool is updated here and the
        /// party is synced from the next Update tick, after everyone has received their own share.
        /// </summary>
        [HarmonyPatch(typeof(Character), nameof(Character.GiveExperience))]
        private static class Character_GiveExperience
        {
            private static void Postfix(Character __instance)
            {
                try
                {
                    if (!Eligible(__instance)) return;
                    LevelPool pool = LevelSyncPlugin.Pool;
                    if (pool.Offer(__instance.ExperienceLevel, __instance.CharacterName)) pool.Save();
                    _syncPending = true;
                    _syncFrom = __instance.CharacterName;
                }
                catch (Exception e) { LevelSyncPlugin.Log.LogWarning("Level sync on XP failed: " + e); }
            }
        }

        private static bool _syncPending;
        private static string _syncFrom;

        /// <summary>Called from the plugin's Update while patched: persists on-load raises once the roster is loaded, and
        /// pulls the local party up to the pool after an XP grant.</summary>
        internal static void Tick()
        {
            try
            {
                if (_raisedOnLoad.Count > 0 && GameLogic.instance != null && GameLogic.instance.FinishedLoadingCharacters)
                {
                    foreach (Character c in _raisedOnLoad.ToArray())
                    {
                        if (!CanSave(c)) continue; // e.g. still loading; try again next tick
                        _raisedOnLoad.Remove(c);
                        if (SaveNow(c)) { if (LevelSyncPlugin.Cfg.Verbose.Value) LevelSyncPlugin.Log.LogInfo(c.CharacterName + ": level saved to file"); }
                        else LevelSyncPlugin.Log.LogWarning(c.CharacterName + ": level raise could not be saved now; it will be redone next launch");
                    }
                }
                if (!_syncPending) return;
                _syncPending = false;
                LevelPool pool = LevelSyncPlugin.Pool;
                if (pool == null) return;
                var party = NetworkingManager.Instance != null ? NetworkingManager.Instance.MyPartyCharacters : null;
                if (party == null) return;
                foreach (Character other in party)
                {
                    if (other == null || !other.Owned || !Eligible(other)) continue;
                    RaiseIfBelow(other, pool.Highest, "party sync after XP for " + _syncFrom, quiet: true);
                }
            }
            catch (Exception e) { LevelSyncPlugin.Log.LogWarning("Tick failed: " + e); }
        }

        internal static void Reset() { _syncPending = false; _raisedOnLoad.Clear(); }

        /// <summary>A brand-new character is skipped at Load(-1) so the creation screen stays vanilla; its first save
        /// outside that screen (the game saves right after creation and regularly afterwards) syncs it.</summary>
        [HarmonyPatch(typeof(Character), nameof(Character.Save))]
        private static class Character_Save
        {
            private static void Prefix(Character __instance)
            {
                try
                {
                    if (!Eligible(__instance) || !__instance.IsLoaded) return; // Load() itself saves mid-way, before the file's level is read
                    if (GUIManager.instance != null && GUIManager.instance.CurrentGuiState == GUIState.CreatingCharacter) return;
                    LevelPool pool = LevelSyncPlugin.Pool;
                    if (pool.Offer(__instance.ExperienceLevel, __instance.CharacterName)) pool.Save();
                    RaiseIfBelow(__instance, pool.Highest, "account highest", quiet: true);
                }
                catch (Exception e) { LevelSyncPlugin.Log.LogWarning("Level sync on save failed: " + e); }
            }
        }

        private static bool Eligible(Character c)
        {
            LevelSyncConfig cfg = LevelSyncPlugin.Cfg;
            if (cfg == null || !cfg.Enabled.Value || c == null || c.IsAI) return false;
            if (!c.Owned) return false; // never let a friend's character feed or take from your pool
            if (!cfg.IncludeRoguelikeCharacters.Value)
            {
                // The live flag is set during creation BEFORE the first save copies it into the file; check both, plus the mode.
                if (c.IsRoguelikeCharacter) return false;
                if (c.CharacterSaveFile != null && c.CharacterSaveFile.IsRoguelikeCharacter) return false;
                try { if (Game.Instance != null && Game.Instance.RoguelikeModeActive) return false; } catch { }
            }
            if (c.HardcoreDeath) return false; // a dead hardcore character cannot be played; nothing to raise
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
            catch (Exception e) { LevelSyncPlugin.Log.LogWarning("Save of " + c.CharacterName + " threw: " + e.Message); return false; }
        }

        private static bool RaiseIfBelow(Character c, float target, string why, bool quiet = false)
        {
            LevelSyncConfig cfg = LevelSyncPlugin.Cfg;
            target = Mathf.Min(target, cfg.MaxLevel.Value);
            float before = c.ExperienceLevel;
            if (target <= before + 0.0001f)
            {
                if (cfg.Verbose.Value && !quiet) LevelSyncPlugin.Log.LogInfo(c.CharacterName + ": L" + Fmt(before) + " already at or above " + Fmt(target));
                return false;
            }
            int levelBefore = c.Level;
            float hr = c.HealthRatio;
            float mr = c.ManaRatio;
            c.ExperienceLevel = target;          // also asks the game to save (a no-op for a non-party character; see Tick)
            if (c.Level > levelBefore) c.JustLeveled = true;
            try { c.MaxVariableAttributeChange("Health", hr); c.MaxVariableAttributeChange("Mana", mr); } catch { }
            c.QueueCharacterSave();
            LevelSyncPlugin.Log.LogInfo(c.CharacterName + ": L" + Fmt(before) + " -> L" + Fmt(target) + " (" + why + ")");
            return true;
        }

        private static string Fmt(float v) { return v.ToString("0.##", CultureInfo.InvariantCulture); }
    }
}
