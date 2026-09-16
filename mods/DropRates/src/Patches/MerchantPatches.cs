using System.Linq;
using HarmonyLib;

namespace DropRates.Patches
{
    /// <summary>
    /// The Merchant (island vendor) stocks one equipment piece with rarity weights Legendary 70 / Mythic 30.
    /// We record which shopkeeper is being stocked, then return a replacement weight array from
    /// ShopItemTypeChanceSet.GetRarityChances when it is The Merchant's {Legendary, Mythic} set.
    /// The shopkeeper asset data is never modified.
    /// </summary>
    internal static class MerchantPatches
    {
        private const string MerchantName = "The Merchant";
        private const string RoguelikeMerchantName = "The Merchant (Roguelike)";

        [HarmonyPatch(typeof(ShopManager), nameof(ShopManager.RefreshItemDictSingle), new[] { typeof(int), typeof(Shopkeeper) })]
        private static class ShopManager_RefreshItemDictSingle
        {
            private static void Prefix(Shopkeeper shopkeeper)
            {
                LootContext.CurrentShopkeeper = shopkeeper;
            }

            private static void Finalizer()
            {
                LootContext.CurrentShopkeeper = null;
            }
        }

        [HarmonyPatch(typeof(ShopItemTypeChanceSet), nameof(ShopItemTypeChanceSet.GetRarityChances), new[] { typeof(int) })]
        private static class ShopItemTypeChanceSet_GetRarityChances
        {
            private static bool IsMerchant(Shopkeeper sk)
            {
                if (sk == null) return false;
                if (sk.name == MerchantName) return true;
                return DropRatesPlugin.Cfg.AffectRoguelikeMerchant.Value && sk.name == RoguelikeMerchantName;
            }

            private static void Postfix(ref RarityChance[] __result)
            {
                DropRatesConfig cfg = DropRatesPlugin.Cfg;
                if (!cfg.MerchantEnabled.Value || cfg.Bypass) return;
                Shopkeeper sk = LootContext.CurrentShopkeeper;
                if (!IsMerchant(sk)) return;
                if (__result == null || __result.Length != 2) return;
                bool isLegMyth = __result.Any(r => r.Rarity == ItemQuality.Legendary) && __result.Any(r => r.Rarity == ItemQuality.Mythic);
                if (!isLegMyth) return; // leaves the Roguelike merchant's other chance sets untouched

                float l = cfg.MerchantLegendary.Value;
                float m = cfg.MerchantMythic.Value;
                if (l <= 0f && m <= 0f) return; // avoid a zero-weight roll

                __result = new[]
                {
                    new RarityChance { Rarity = ItemQuality.Legendary, Chance = l },
                    new RarityChance { Rarity = ItemQuality.Mythic, Chance = m },
                };
                if (cfg.Verbose.Value)
                {
                    DropRatesPlugin.Log.LogInfo("Merchant weights applied for '" + sk.name + "': Legendary=" + l + " Mythic=" + m);
                }
            }
        }
    }
}
