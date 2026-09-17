using System;
using System.Collections.Generic;
using System.Text;
using Burst2Flame;
using HarmonyLib;
using UnityEngine;

namespace RoguelikeQoL.Patches
{
    /// <summary>
    /// Gear rarity boost for the Roguelike post-battle chooser.
    ///
    /// After every battle each character levels and RoguelikeManager.GetItemChoices(level, recipient, boss) rolls the six
    /// offers (one per equipment slot; the player keeps one). Non-boss: rarities Uncommon..Mythic, up to 21 iterations of
    /// LootTable.GetWorldLoot (independent per-rarity rolls at GlobalSettings.UnassignedItemChancePercentage: U 5, R 1,
    /// L 0.2, M 0.05 percent), keep the best of the first non-empty iteration, else GetGuaranteedWorldLoot (weighted by
    /// the same numbers). Boss: CharacterInfo.GetLootDrop x20, top 6. Both read that one table, so for the duration of
    /// GetItemChoices the table is swapped for a scaled copy: chance x RarityBoost^(rarity-1) (Uncommon unchanged, Rare
    /// xB, Legendary xB^2, Mythic xB^3, capped at 100). Same swap/restore shape as DropRates' WorldLootPatches; DropRates
    /// itself stays out of Roguelike (LeaveRoguelikeUntouched), so the two never stack by default.
    /// </summary>
    internal static class RarityPatches
    {
        private static List<ChanceByQuality> _saved;

        private static RoguelikeQoLConfig Cfg => RoguelikeQoLPlugin.Cfg;

        internal static void Reset()
        {
            Restore();
        }

        internal static float Boost()
        {
            try { return Cfg != null ? Mathf.Clamp(Cfg.RarityBoost.Value, 0.1f, 10f) : 1f; } catch { return 1f; }
        }

        internal static float Scaled(float chance, ItemQuality rarity, float boost)
        {
            int step = (int)rarity - 1; // Uncommon = 0 steps
            if (step <= 0) return chance;
            return Mathf.Min(100f, chance * Mathf.Pow(boost, step));
        }

        private static void SwapIn(float boost)
        {
            if (_saved != null) return; // re-entrancy guard
            GlobalSettingsManager mgr = GlobalSettingsManager.instance;
            if (mgr == null) return;
            GlobalSettings gs = mgr.globalSettings;
            if (gs == null || gs.UnassignedItemChancePercentage == null) return;
            _saved = gs.UnassignedItemChancePercentage;
            var scaled = new List<ChanceByQuality>(_saved.Count);
            foreach (ChanceByQuality c in _saved)
                scaled.Add(new ChanceByQuality { rarity = c.rarity, chance = Scaled(c.chance, c.rarity, boost) });
            gs.UnassignedItemChancePercentage = scaled;
        }

        private static void Restore()
        {
            if (_saved == null) return;
            try
            {
                GlobalSettingsManager mgr = GlobalSettingsManager.instance;
                if (mgr != null && mgr.globalSettings != null) mgr.globalSettings.UnassignedItemChancePercentage = _saved;
            }
            catch { }
            _saved = null;
        }

        [HarmonyPatch(typeof(RoguelikeManager), nameof(RoguelikeManager.GetItemChoices), new[] { typeof(int), typeof(Character), typeof(Burst2Flame.CharacterInfo) })]
        private static class ItemChoices
        {
            private static void Prefix(Burst2Flame.CharacterInfo __2)
            {
                try
                {
                    if (Cfg == null) return;
                    float b = Boost();
                    if (Mathf.Approximately(b, 1f)) return;
                    if (__2 != null && !Cfg.BoostBossRewards.Value) return;
                    SwapIn(b);
                }
                catch (Exception e) { RoguelikeQoLPlugin.Log.LogWarning("GetItemChoices prefix failed: " + e); }
            }

            private static void Finalizer()
            {
                Restore();
            }

            private static void Postfix(int __0, Character __1, Burst2Flame.CharacterInfo __2, List<Item> __result)
            {
                try
                {
                    if (Cfg == null || !Cfg.Verbose.Value || __result == null) return;
                    var sb = new StringBuilder();
                    foreach (Item it in __result)
                    {
                        if (it == null || it.ItemInfo == null) continue;
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append(it.ItemInfo.ItemType).Append(' ').Append(it.ItemInfo.Rarity).Append(" '").Append(it.ItemName).Append('\'');
                    }
                    RoguelikeQoLPlugin.Log.LogInfo("Rarity boost x" + Boost() + (__2 != null ? " (boss)" : "") + ": " + (__1 != null ? __1.CharacterName : "?") + " L" + __0 + " offers: " + sb);
                }
                catch { }
            }
        }
    }
}
