using System.Collections.Generic;
using HarmonyLib;

namespace DropRates.Patches
{
    /// <summary>
    /// Scales the percent of non-guaranteed loot-table entries, but only while an enemy's loot is
    /// being rolled (personal loot + enemy-group tables). Island events and gathering nodes also call
    /// LootTable.GetLoot and are deliberately left alone. Guaranteed ("must get one") tables pick by
    /// weight and ignore the modifier in vanilla, so they are skipped too.
    /// </summary>
    internal static class PersonalLootPatches
    {
        /// <summary>Does the table hold gear of at least `min` rarity? Potions are ranked Legendary/Mythic in the game's data
        /// (Greater/Super potions), so with EquipmentOnly they no longer make a potion table count as a named-gear table.</summary>
        private static bool TableHasRarity(LootTable table, ItemQuality min)
        {
            if (table.itemLootChances == null) return false;
            bool equipmentOnly = DropRatesPlugin.Cfg.PersonalLootEquipmentOnly.Value;
            foreach (ItemLootChance e in table.itemLootChances)
            {
                if (e == null || e.ItemInfo == null || e.ItemInfo.Rarity < min) continue;
                if (equipmentOnly && !IsEquipment(e.ItemInfo.ItemType)) continue;
                return true;
            }
            return false;
        }

        private static bool IsEquipment(ItemType t)
        {
            return t == ItemType.Weapon || t == ItemType.Shield || t == ItemType.Head || t == ItemType.Armor || t == ItemType.Ring || t == ItemType.Amulet;
        }

        [HarmonyPatch(typeof(LootTable), nameof(LootTable.GetLoot),
            new[] { typeof(int), typeof(float), typeof(LootRestrictions), typeof(bool), typeof(System.Random), typeof(string) })]
        private static class LootTable_GetLoot
        {
            // Parameter names must match the original signature: chanceModifier, overrideMustGetOne.
            private static void Prefix(LootTable __instance, ref float chanceModifier, bool overrideMustGetOne)
            {
                if (!LootContext.EnemyActive || LootContext.UseVanilla || DropRatesPlugin.Cfg.Bypass) return;
                if (__instance.mustGetOne || overrideMustGetOne) return;
                float mult = DropRatesPlugin.Cfg.PersonalLootMultiplier.Value;
                if (mult == 1f) return;
                if (!TableHasRarity(__instance, DropRatesPlugin.Cfg.PersonalLootMinRarity.Value)) return;
                chanceModifier *= mult;
                if (DropRatesPlugin.Cfg.Verbose.Value)
                {
                    DropRatesPlugin.Log.LogInfo("GetLoot (enemy table, " + (__instance.itemLootChances != null ? __instance.itemLootChances.Count : 0)
                        + " entries) chanceModifier -> " + chanceModifier);
                }
            }
        }
    }
}
