using System.Collections.Generic;
using HarmonyLib;

namespace DropRates.Patches
{
    /// <summary>
    /// Gambling (Ulf's Wager) picks each item's rarity by weight from GamblingManager.RarityChances.
    /// Swap in a config-built list for the duration of PopulateGamblingItems, then restore.
    /// </summary>
    internal static class GamblingPatches
    {
        private static GamblingManager _patched;
        private static List<RarityChance> _saved;

        [HarmonyPatch(typeof(GamblingManager), nameof(GamblingManager.PopulateGamblingItems))]
        private static class GamblingManager_PopulateGamblingItems
        {
            private static void Prefix(GamblingManager __instance)
            {
                DropRatesConfig cfg = DropRatesPlugin.Cfg;
                if (!cfg.GamblingEnabled.Value || cfg.Bypass || __instance == null) return;
                if (_patched != null) return; // re-entrancy guard

                _patched = __instance;
                _saved = __instance.RarityChances;
                __instance.RarityChances = new List<RarityChance>
                {
                    new RarityChance { Rarity = ItemQuality.Common, Chance = cfg.GambleCommon.Value },
                    new RarityChance { Rarity = ItemQuality.Uncommon, Chance = cfg.GambleUncommon.Value },
                    new RarityChance { Rarity = ItemQuality.Rare, Chance = cfg.GambleRare.Value },
                    new RarityChance { Rarity = ItemQuality.Legendary, Chance = cfg.GambleLegendary.Value },
                    new RarityChance { Rarity = ItemQuality.Mythic, Chance = cfg.GambleMythic.Value },
                };
                if (cfg.Verbose.Value)
                {
                    DropRatesPlugin.Log.LogInfo("Gambling weights applied: " + cfg.GambleCommon.Value + "/" + cfg.GambleUncommon.Value + "/"
                        + cfg.GambleRare.Value + "/" + cfg.GambleLegendary.Value + "/" + cfg.GambleMythic.Value);
                }
            }

            private static void Finalizer()
            {
                if (_patched != null)
                {
                    _patched.RarityChances = _saved;
                    _patched = null;
                    _saved = null;
                }
            }
        }
    }
}
