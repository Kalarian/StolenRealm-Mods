using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Burst2Flame;
using HarmonyLib;
using UnityEngine;

namespace FortunePreview.Patches
{
    /// <summary>
    /// The Fortune window shows one slot per fortune the character owns (FortuneWindow.UpdateFortuneWindow fills
    /// AvailableFortuneSlots from character.FortuneData). After it has done that, we append one slot per fortune the
    /// character does NOT own, backed by a throw-away FortuneSaveData that is never added to the character, greyed out
    /// and blocked from equipping (SelectedAvailableSlot prefix). Hovering works as normal, so the tooltip shows the
    /// fortune's effect at the character's level plus the Source section: a browsable catalogue of all 83 fortunes.
    /// FortuneUpgrade refuses these slots on its own (their data is not in the character's list).
    /// </summary>
    internal static class FortuneWindowPatches
    {
        private static readonly HashSet<FortuneSlot> _ghosts = new HashSet<FortuneSlot>();
        private static readonly Dictionary<FortuneSlot, Color> _iconColor = new Dictionary<FortuneSlot, Color>();
        private static readonly Dictionary<FortuneSlot, Color> _borderColor = new Dictionary<FortuneSlot, Color>();
        private static readonly FieldInfo _slotsField = AccessTools.Field(typeof(FortuneWindow), "AvailableFortuneSlots");

        /// <summary>Non-null while a greyed-out slot's tooltip is being shown (read by FortuneTooltipPatches).</summary>
        internal static string GhostNote;

        internal static bool IsGhost(FortuneSlot slot) { return slot != null && _ghosts.Contains(slot); }

        internal static void Reset()
        {
            RestoreAll();
            GhostNote = null;
        }

        private static void RestoreAll()
        {
            foreach (FortuneSlot s in _ghosts)
            {
                try
                {
                    if (s == null) continue;
                    Color c;
                    if (s.Icon != null && _iconColor.TryGetValue(s, out c)) s.Icon.color = c;
                    if (s.Border != null && _borderColor.TryGetValue(s, out c)) s.Border.color = c;
                    if (s.Disabled != null) s.Disabled.SetActive(false);
                }
                catch { }
            }
            _ghosts.Clear(); _iconColor.Clear(); _borderColor.Clear();
        }

        [HarmonyPatch(typeof(FortuneWindow), nameof(FortuneWindow.UpdateFortuneWindow))]
        private static class FortuneWindow_UpdateFortuneWindow
        {
            private static void Postfix(FortuneWindow __instance, Character character)
            {
                try
                {
                    RestoreAll();
                    FortunePreviewConfig cfg = FortunePreviewPlugin.Cfg;
                    if (cfg == null || !cfg.ShowUnowned.Value || __instance == null || character == null || character.FortuneData == null) return;
                    var slots = _slotsField != null ? _slotsField.GetValue(__instance) as List<FortuneSlot> : null;
                    if (slots == null) { FortunePreviewPlugin.Log.LogWarning("Fortune window: slot list not found"); return; }

                    var owned = new HashSet<string>(character.FortuneData.Where(f => f != null && f.Guid != null).Select(f => f.Guid), StringComparer.OrdinalIgnoreCase);
                    ActionStatusInfo[] all = Game.Instance != null ? Game.Instance.Fortunes : null;
                    if (all == null) return;
                    IEnumerable<ActionStatusInfo> missing = all.Where(f => f != null && !owned.Contains(f.Guid.ToString()));
                    if (cfg.SortUnownedByRarity.Value) missing = missing.OrderByDescending(f => (int)f.Rarity).ThenBy(f => Localize(f.Name));
                    else missing = missing.OrderBy(f => Localize(f.Name));

                    int idx = slots.Count(s => s != null && s.gameObject.activeSelf); // the owned ones the game just filled
                    float level = cfg.UnownedAtCharacterLevel.Value ? Mathf.Max(1, character.Level) : 1f;
                    float alpha = Mathf.Clamp(cfg.UnownedAlpha.Value, 10, 80) / 100f;
                    int added = 0;
                    foreach (ActionStatusInfo f in missing)
                    {
                        while (slots.Count <= idx)
                        {
                            FortuneSlot made = UnityEngine.Object.Instantiate(__instance.FortuneSlotPrefab, __instance.AvailableSlotHolder);
                            slots.Add(made);
                            made.OnClick.AddListener(__instance.SelectedAvailableSlot);
                        }
                        FortuneSlot slot = slots[idx++];
                        slot.FortuneSaveData = new FortuneSaveData { Guid = f.Guid.ToString(), Level = level, EquippedSlotIndex = -1, IsNew = false };
                        slot.gameObject.SetActive(true);
                        _ghosts.Add(slot);
                        if (slot.Icon != null) { _iconColor[slot] = slot.Icon.color; Color c = slot.Icon.color; slot.Icon.color = new Color(c.r * 0.6f, c.g * 0.6f, c.b * 0.6f, alpha); }
                        if (slot.Border != null) { _borderColor[slot] = slot.Border.color; Color b = slot.Border.color; slot.Border.color = new Color(b.r, b.g, b.b, b.a * 0.5f); }
                        if (cfg.UseDisabledOverlay.Value && slot.Disabled != null) slot.Disabled.SetActive(true);
                        if (slot.LevelText != null) slot.LevelText.text = "<color=#8A8F98>?</color>";
                        added++;
                    }
                    // Game build 25240684 (2026-09) added a search bar: ApplySearchFilter hides every slot at index >=
                    // numAvailableSlotsInUse and builds its word list from those slots, so count ours in (fields absent on older builds).
                    try
                    {
                        FieldInfo inUse = AccessTools.Field(typeof(FortuneWindow), "numAvailableSlotsInUse");
                        if (inUse != null) inUse.SetValue(__instance, idx);
                        FieldInfo vocab = AccessTools.Field(typeof(FortuneWindow), "searchVocabulary");
                        if (vocab != null) vocab.SetValue(__instance, null);
                    }
                    catch (Exception e) { FortunePreviewPlugin.Log.LogWarning("Fortune window search hookup: " + e.Message); }
                    if (cfg.Verbose.Value) FortunePreviewPlugin.Log.LogInfo("Fortune window: " + owned.Count + " owned, " + added + " unowned shown greyed out (effects at L" + level + ")");
                }
                catch (Exception e) { FortunePreviewPlugin.Log.LogWarning("Fortune window catalogue failed: " + e); }
            }
        }

        // Clicking a greyed-out slot must not equip it (the game would write our throw-away data into an equipped slot).
        [HarmonyPatch(typeof(FortuneWindow), nameof(FortuneWindow.SelectedAvailableSlot))]
        private static class FortuneWindow_SelectedAvailableSlot
        {
            private static bool Prefix(FortuneSlot fortuneSlot)
            {
                if (!IsGhost(fortuneSlot)) return true;
                try
                {
                    string name = "this fortune";
                    ActionStatusInfo info = fortuneSlot.FortuneSaveData != null ? FortuneWindow.GetFortuneByGuid(fortuneSlot.FortuneSaveData.Guid) : null;
                    if (info != null) name = Localize(info.Name);
                    if (ConfirmWindow.Instance != null) ConfirmWindow.Instance.ShowPopupMessage("Not owned", "You have not found " + name + " yet. Hover it to see where it comes from.");
                }
                catch { }
                return false;
            }
        }

        // While a greyed-out slot shows its tooltip, FortuneTooltipPatches prepends a "not owned" line.
        [HarmonyPatch(typeof(FortuneSlot), nameof(FortuneSlot.ShowTooltip))]
        private static class FortuneSlot_ShowTooltip
        {
            private static void Prefix(FortuneSlot __instance)
            {
                GhostNote = null;
                if (!IsGhost(__instance)) return;
                FortunePreviewConfig cfg = FortunePreviewPlugin.Cfg;
                float level = __instance.FortuneSaveData != null ? __instance.FortuneSaveData.Level : 1f;
                GhostNote = "<color=#E06C6C>Not owned</color> <color=#9AA5B1>(effects shown at level " + ((int)level) + ")</color>";
            }
            private static void Postfix() { GhostNote = null; }
        }

        private static string Localize(string key)
        {
            try { return OptionsManager.Localize(key) ?? key; } catch { return key; }
        }
    }
}
