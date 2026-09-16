using HarmonyLib;
using UnityEngine;

namespace DifficultyXP.Patches
{
    internal static class XpPatches
    {
        private static DifficultyXPConfig Cfg => DifficultyXPPlugin.Cfg;

        /// <summary>
        /// Every XP calculation (per-enemy battle XP in Character.GetExpValue, quest XP in QuestInstance.ExpReward)
        /// reads DifficultySetting.ExperienceMod, which is 1 + ExperienceModifierPerc/100 and 0% on every difficulty.
        /// Return the gold multiplier instead so XP scales exactly like gold.
        /// </summary>
        [HarmonyPatch(typeof(DifficultySetting), nameof(DifficultySetting.ExperienceMod), MethodType.Getter)]
        private static class DifficultySetting_ExperienceMod
        {
            private static void Postfix(DifficultySetting __instance, ref float __result)
            {
                if (!Cfg.Enabled.Value) return;
                float mirrored = DifficultySetting.GetMultiplierBasedOnPercentage(__instance.GoldModifierPerc) * Cfg.ExtraMultiplier.Value;
                float buffPct = 0f;
                if (Cfg.IncludeGoldBuffs.Value)
                {
                    // Same sum the game uses for gold in GameLogic.ApplyGoldModifiers: every owned character's GoldMod stat (campfire 'Prepared' = +20).
                    NetworkingManager nm = NetworkingManager.Instance;
                    if (nm != null && nm.MyPartyCharacters != null)
                    {
                        foreach (Character c in nm.MyPartyCharacters)
                        {
                            if (c != null) buffPct += c["GoldMod"];
                        }
                    }
                    mirrored *= 1f + buffPct / 100f;
                }
                if (Cfg.Verbose.Value)
                {
                    DifficultyXPPlugin.Log.LogInfo("ExperienceMod for '" + __instance.DifficultyName + "': " + __result + " -> " + mirrored + " (gold " + __instance.GoldModifierPerc + "%, buffs +" + buffPct + "%)");
                }
                __result = mirrored;
            }
        }

        /// <summary>
        /// Island events compute their XP reward in EventOption.InitActions without any difficulty factor
        /// (modifyExpAmount = ceil(average battle XP * ratio)). It is recomputed from scratch on every call,
        /// so multiplying afterwards never compounds.
        /// </summary>
        // effect -> { vanilla amount, the amount we last wrote }
        private static readonly System.Collections.Generic.Dictionary<EventActionEffects, float[]> _vanilla = new System.Collections.Generic.Dictionary<EventActionEffects, float[]>();

        [HarmonyPatch(typeof(EventOption), nameof(EventOption.InitActions))]
        private static class EventOption_InitActions
        {
            private static void Postfix(EventOption __instance)
            {
                if (!Cfg.Enabled.Value || !Cfg.ScaleEventXP.Value) return;
                if (__instance == null || __instance.eventActions == null) return;
                GameLogic gl = GameLogic.instance;
                if (gl == null) return;
                DifficultySetting diff = gl.CurrentDifficulty;
                if (diff == null) return;
                float mod = diff.ExperienceMod; // already mirrored by the getter patch
                if (mod == 1f) return;

                foreach (EventAction action in __instance.eventActions)
                {
                    if (action == null) continue;
                    var effects = action.GetEventActionEffects();
                    if (effects == null) continue;
                    foreach (EventActionEffects effect in effects)
                    {
                        if (effect == null || !effect.giveExperience || effect.modifyExpAmount <= 0f) continue;
                        // These are shared asset objects and InitActions runs again on every island. The game recomputes
                        // the amount from scratch only for effects with an XP ratio; for a fixed amount our own previous
                        // result would still be there, so always multiply the remembered vanilla value, never our output.
                        float before = effect.modifyExpAmount;
                        float[] mem;
                        if (_vanilla.TryGetValue(effect, out mem) && Mathf.Approximately(mem[1], before)) before = mem[0];
                        effect.modifyExpAmount = Mathf.Ceil(before * mod);
                        _vanilla[effect] = new[] { before, effect.modifyExpAmount };
                        if (Cfg.Verbose.Value)
                        {
                            DifficultyXPPlugin.Log.LogInfo("Event XP " + before + " -> " + effect.modifyExpAmount + " (x" + mod + ")");
                        }
                    }
                }
            }
        }
    }
}
