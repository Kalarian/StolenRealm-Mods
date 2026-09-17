using System;
using Burst2Flame;
using HarmonyLib;
using UnityEngine;

namespace RoguelikeQoL.Patches
{
    /// <summary>
    /// Meta currency (the Roguelike currency spent on permanent power-ups) multiplier.
    ///
    /// Every grant goes through GlobalSaveData.ModifyRoguelikeCurrency(amount): the per-battle amount
    /// (GameLogic: CurrencyPerLevelNodes x CurrencyDifficultyMultiplierNodes, ceil'd, sent to every client with the battle
    /// result and applied locally in PostBattleManager) and the 1200 run-completion bonus (Root.SendRoguelikeComplete).
    /// Spending never uses it (AvailableRoguelikeCurrency subtracts what was spent), so scaling positive amounts here is
    /// the whole feature. The level-up screen prints the battle amount it was handed
    /// (RoguelikeManager.AddToSkillSelectionQueue currencyEarned -> CurrencyText), so that number is scaled the same way.
    /// Each player's own game applies the grant, so every co-op member needs the mod for their own currency.
    /// </summary>
    internal static class CurrencyPatches
    {
        private static RoguelikeQoLConfig Cfg => RoguelikeQoLPlugin.Cfg;

        internal static void Reset() { }

        private static float Mult()
        {
            try { return Cfg != null ? Mathf.Max(0f, Cfg.CurrencyMultiplier.Value) : 1f; } catch { return 1f; }
        }

        [HarmonyPatch(typeof(GlobalSaveData), nameof(GlobalSaveData.ModifyRoguelikeCurrency), new[] { typeof(float) })]
        private static class ModifyCurrency
        {
            private static void Prefix(ref float amount)
            {
                try
                {
                    float m = Mult();
                    if (amount <= 0f || Mathf.Approximately(m, 1f)) return;
                    float before = amount;
                    amount = Mathf.Ceil(amount * m);
                    if (Cfg != null && Cfg.Verbose.Value) RoguelikeQoLPlugin.Log.LogInfo("Roguelike currency " + before + " -> " + amount + " (x" + m + ")");
                }
                catch (Exception e) { RoguelikeQoLPlugin.Log.LogWarning("ModifyRoguelikeCurrency prefix failed: " + e); }
            }
        }

        // ---------- gold earned

        /// <summary>GameLogic.ApplyGoldModifiers(originalGold) applies the party's GoldMod stat and is the one helper under every
        /// earned-gold path: battle rewards (PostBattleManager, applied on every client per character), event gold and gold piles
        /// (EventWindow), campaign quest rewards (AdventureRewards). Selling, trading and buy-backs never call it.</summary>
        [HarmonyPatch(typeof(GameLogic), nameof(GameLogic.ApplyGoldModifiers), new[] { typeof(float) })]
        private static class GoldEarned
        {
            private static void Postfix(float originalGold, ref float __result)
            {
                try
                {
                    if (Cfg == null) return;
                    float m = Mathf.Max(0f, Cfg.GoldMultiplier.Value);
                    if (originalGold <= 0f || Mathf.Approximately(m, 1f)) return;
                    if (Game.Instance == null || !Game.Instance.RoguelikeModeActive) return;
                    if (PostBattleManager.IsNotNullAndIsActive) return; // battle gold was already scaled at its source (GetTotalGoldValue)
                    float before = __result;
                    __result = Mathf.Ceil(__result * m);
                    if (Cfg.Verbose.Value) RoguelikeQoLPlugin.Log.LogInfo("Roguelike event gold " + before + " -> " + __result + " (x" + m + ")");
                }
                catch (Exception e) { RoguelikeQoLPlugin.Log.LogWarning("ApplyGoldModifiers postfix failed: " + e); }
            }
        }

        /// <summary>Battle gold is computed once on the host (GameLogic.GetTotalGoldValue, already x5 in Roguelike) and sent to every
        /// client with the battle result, where the post-battle window prints it and hands it to each character through
        /// ApplyGoldModifiers. Scaling it here keeps the printed number and the grant equal for everyone in the lobby.</summary>
        [HarmonyPatch(typeof(GameLogic), nameof(GameLogic.GetTotalGoldValue), new[] { typeof(float) })]
        private static class BattleGold
        {
            private static void Postfix(ref float __result)
            {
                try
                {
                    if (Cfg == null) return;
                    float m = Mathf.Max(0f, Cfg.GoldMultiplier.Value);
                    if (__result <= 0f || Mathf.Approximately(m, 1f)) return;
                    if (Game.Instance == null || !Game.Instance.RoguelikeModeActive) return;
                    float before = __result;
                    __result = Mathf.Ceil(__result * m);
                    if (Cfg.Verbose.Value) RoguelikeQoLPlugin.Log.LogInfo("Roguelike battle gold " + before + " -> " + __result + " (x" + m + ")");
                }
                catch (Exception e) { RoguelikeQoLPlugin.Log.LogWarning("GetTotalGoldValue postfix failed: " + e); }
            }
        }

        [HarmonyPatch(typeof(RoguelikeManager), nameof(RoguelikeManager.AddToSkillSelectionQueue), new[] { typeof(Character), typeof(bool), typeof(int), typeof(int), typeof(bool) })]
        private static class SkillQueueDisplay
        {
            private static void Prefix(ref int currencyEarned)
            {
                try
                {
                    float m = Mult();
                    if (currencyEarned <= 0 || Mathf.Approximately(m, 1f)) return;
                    currencyEarned = Mathf.CeilToInt(currencyEarned * m);
                }
                catch { }
            }
        }
    }
}
