using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DropRates.Patches
{
    /// <summary>
    /// The town armorers and jewelers (one of each per act) roll every equipment slot from a fixed table of
    /// Uncommon 80 / Rare 20 (Gareth's 70/30), so they never stock a legendary or mythic in any act. This replaces that
    /// table, for those shops only, with Rare / Legendary / Mythic weights that rise with the act. The game already
    /// falls back one rarity at a time when nothing of the rolled rarity exists at your level (mythics need level 5),
    /// so a slot never comes up empty. Island vendors (act -1), potion makers and the Roguelike merchant are untouched.
    /// Uses the same shopkeeper context as MerchantPatches (set in the ShopManager.RefreshItemDictSingle prefix).
    /// The shopkeeper asset data is never modified.
    /// </summary>
    internal static class TownShopPatches
    {
        private static string _lastLogged;

        private static bool IsTownEquipmentShop(Shopkeeper sk)
        {
            if (sk == null || sk.assignedAct < 1) return false;
            string t = sk.shopType ?? "";
            return t == "Armorer" || t == "Jeweler";
        }

        /// <summary>Weights for the act: act 1 = the configured base, each later act shifts the configured step from Rare to Legendary/Mythic.</summary>
        internal static void WeightsFor(int act, DropRatesConfig cfg, out float rare, out float legendary, out float mythic)
        {
            int steps = Mathf.Max(0, act - 1);
            legendary = Mathf.Max(0f, cfg.TownAct1Legendary.Value + cfg.TownLegendaryPerAct.Value * steps);
            mythic = Mathf.Max(0f, cfg.TownAct1Mythic.Value + cfg.TownMythicPerAct.Value * steps);
            rare = Mathf.Max(0f, cfg.TownAct1Rare.Value - (cfg.TownLegendaryPerAct.Value + cfg.TownMythicPerAct.Value) * steps);
        }

        [HarmonyPatch(typeof(ShopItemTypeChanceSet), nameof(ShopItemTypeChanceSet.GetRarityChances), new[] { typeof(int) })]
        private static class ShopItemTypeChanceSet_GetRarityChances
        {
            private static void Postfix(int act, ref RarityChance[] __result)
            {
                DropRatesConfig cfg = DropRatesPlugin.Cfg;
                if (cfg == null || !cfg.TownShopsEnabled.Value || cfg.Bypass) return;
                Shopkeeper sk = LootContext.CurrentShopkeeper;
                if (!IsTownEquipmentShop(sk)) return;
                if (__result == null || __result.Length == 0) return;
                float rare, legendary, mythic;
                WeightsFor(act, cfg, out rare, out legendary, out mythic);
                if (rare + legendary + mythic <= 0f) return; // avoid a zero-weight roll
                var list = new List<RarityChance>(3);
                if (rare > 0f) list.Add(new RarityChance { Rarity = ItemQuality.Rare, Chance = rare });
                if (legendary > 0f) list.Add(new RarityChance { Rarity = ItemQuality.Legendary, Chance = legendary });
                if (mythic > 0f) list.Add(new RarityChance { Rarity = ItemQuality.Mythic, Chance = mythic });
                __result = list.ToArray();
                if (cfg.Verbose.Value)
                {
                    string key = sk.name + "#" + act; // one line per shop visit, not one per slot roll
                    if (key != _lastLogged)
                    {
                        _lastLogged = key;
                        DropRatesPlugin.Log.LogInfo("Town shop weights for '" + sk.name + "' (act " + act + "): Rare=" + rare + " Legendary=" + legendary + " Mythic=" + mythic);
                    }
                }
            }
        }
    }
}
