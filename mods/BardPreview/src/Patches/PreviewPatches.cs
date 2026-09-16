using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BardPreview.Patches
{
    /// <summary>
    /// The Bard skill tree ships complete in the game files but is hidden behind an unreleased DLC (DlcType.BardPack):
    /// SkillTreeManager.Initialize only activates the Chaos/Bard tabs when !SteamManager.IsSkillTypeHidden(type), and
    /// ChooseTree / Character.Skills / Character.SkillsFromPoints require SteamManager.MeetsDLCRequirements(type). All of
    /// those funnel through the private SteamManager.IsFreeAccess(DlcType): a DLC listed in FreeAccessDlcs is neither
    /// hidden nor unowned, which is how the developers' own BardSkillTest grants itself the tree. The prefix below answers
    /// IsFreeAccess with true for the DLC that locks a configured tree, but ONLY while SteamManager.GetDlcInfo(dlc).Hidden
    /// is true (the game's own "unreleased" flag). A released, purchasable pack is never unlocked here.
    ///
    /// The second patch works around a latent game bug: Initialize collects tabs with GetComponentsInChildren (active
    /// only) and deactivates a hidden DLC tab, so a tab hidden once can never come back within the session. Re-activating
    /// every tab before the game's loop runs lets the mod be switched on after the skill tree was already opened.
    /// </summary>
    internal static class PreviewPatches
    {
        private static readonly HashSet<DlcType> _loggedGranted = new HashSet<DlcType>();
        private static readonly HashSet<DlcType> _loggedReleased = new HashSet<DlcType>();
        private static HashSet<SkillType> _trees;
        private static string _treesRaw;
        private static bool _refreshPending;

        private static BardPreviewConfig Cfg => BardPreviewPlugin.Cfg;

        internal static void Reset()
        {
            _loggedGranted.Clear(); _loggedReleased.Clear(); _trees = null; _treesRaw = null; _refreshPending = false;
        }

        /// <summary>After a config reload or a switchboard change: recompute every own character's skill list once it is safe.</summary>
        internal static void Refresh() { _refreshPending = true; }

        /// <summary>From the plugin's Update while patched: apply a pending refresh outside battle.</summary>
        internal static void Tick()
        {
            if (!_refreshPending) return;
            try
            {
                if (GUIManager.instance != null && GUIManager.instance.CurrentGuiState == GUIState.InBattle) return; // never swap a skill set mid-fight
                _refreshPending = false;
                DirtySkillCaches("config reload");
            }
            catch (Exception e) { _refreshPending = false; BardPreviewPlugin.Log.LogWarning("Refresh failed: " + e); }
        }

        /// <summary>Character.Skills / SkillsFromPoints / SkillTriggers are memoised; dirty them so the DLC check runs again
        /// (the same three the game's BardSkillTest dirties after learning a skill). Called for the mod's own characters only.</summary>
        internal static void DirtySkillCaches(string why)
        {
            var seen = new HashSet<Character>();
            int n = 0;
            try { if (GameLogic.instance != null && GameLogic.instance.AllMyCharacters != null) foreach (Character c in GameLogic.instance.AllMyCharacters) if (c != null && seen.Add(c)) n += Dirty(c) ? 1 : 0; } catch { }
            try { if (NetworkingManager.Instance != null && NetworkingManager.Instance.MyPartyCharacters != null) foreach (Character c in NetworkingManager.Instance.MyPartyCharacters) if (c != null && c.Owned && seen.Add(c)) n += Dirty(c) ? 1 : 0; } catch { }
            if (Cfg != null && Cfg.Verbose.Value) BardPreviewPlugin.Log.LogInfo("Skill caches refreshed for " + n + " character(s) (" + why + ")");
        }

        private static bool Dirty(Character c)
        {
            try
            {
                if (c.SkillsFromPointsCalculation != null) c.SkillsFromPointsCalculation.SetDirty(0);
                if (c.SkillsCalculation != null) c.SkillsCalculation.SetDirty(0);
                if (c.SkillTriggersCalculation != null) c.SkillTriggersCalculation.SetDirty(0);
                return true;
            }
            catch { return false; }
        }

        private static HashSet<SkillType> Trees()
        {
            string raw = Cfg != null ? Cfg.Trees.Value : "Bard";
            if (_trees != null && raw == _treesRaw) return _trees;
            var set = new HashSet<SkillType>();
            foreach (string part in (raw ?? "").Split(','))
            {
                string name = part.Trim();
                if (name.Length == 0) continue;
                try { set.Add((SkillType)Enum.Parse(typeof(SkillType), name, true)); }
                catch { BardPreviewPlugin.Log.LogWarning("Trees: '" + name + "' is not a skill tree name; ignored"); }
            }
            _trees = set; _treesRaw = raw;
            return set;
        }

        /// <summary>True when the mod should answer "free access" for this DLC: it locks a configured tree AND the game still
        /// flags it Hidden (unreleased). Released packs always fall through to the game's own purchase check.</summary>
        internal static bool WantsFreeAccess(SteamManager sm, DlcType dlc)
        {
            if (sm == null || dlc == DlcType.None || Cfg == null || !Cfg.Enabled.Value) return false;
            HashSet<SkillType> trees = Trees();
            if (trees.Count == 0) return false;
            bool locksConfiguredTree = false;
            DlcLockedSkillType[] locks = sm.DlcLockedSkillTypes;
            if (locks != null)
                foreach (DlcLockedSkillType l in locks)
                    if (l.DlcType == dlc && trees.Contains(l.SkillType)) { locksConfiguredTree = true; break; }
            if (!locksConfiguredTree) return false;
            DLCInfo info = sm.GetDlcInfo(dlc);
            if (!info.Hidden)
            {
                if (_loggedReleased.Add(dlc)) BardPreviewPlugin.Log.LogInfo(dlc + " is released (not hidden any more): the preview does nothing for it, the game's own purchase check applies");
                return false;
            }
            if (_loggedGranted.Add(dlc)) BardPreviewPlugin.Log.LogInfo(dlc + " is hidden (unreleased) - granted free access for this session, like the game's own test harness does");
            return true;
        }

        [HarmonyPatch(typeof(SteamManager), "IsFreeAccess")]
        private static class FreeAccess
        {
            private static bool Prefix(SteamManager __instance, DlcType dlcType, ref bool __result)
            {
                try
                {
                    if (WantsFreeAccess(__instance, dlcType)) { __result = true; return false; }
                }
                catch (Exception e) { BardPreviewPlugin.Log.LogWarning("IsFreeAccess hook failed: " + e); }
                return true;
            }
        }

        [HarmonyPatch(typeof(SkillTreeManager), "Initialize")]
        private static class TabRevive
        {
            private static void Prefix(SkillTreeManager __instance)
            {
                try
                {
                    if (__instance == null || __instance.tabHolder == null) return;
                    int revived = 0;
                    foreach (SkillTreeTab tab in __instance.tabHolder.GetComponentsInChildren<SkillTreeTab>(true))
                        if (tab != null && !tab.gameObject.activeSelf) { tab.gameObject.SetActive(true); revived++; }
                    if (revived > 0 && Cfg != null && Cfg.Verbose.Value) BardPreviewPlugin.Log.LogInfo("Skill tree: re-activated " + revived + " hidden tab(s) before the game rebuilt the tab list");
                }
                catch (Exception e) { BardPreviewPlugin.Log.LogWarning("Initialize hook failed: " + e); }
            }
        }
    }
}
