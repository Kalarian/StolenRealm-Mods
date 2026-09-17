using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace DropRates.Patches
{
    /// <summary>
    /// World-pool drops. Two mechanisms:
    ///  - rarity cap: rewrite lootRestrictions.possibleRarities from the config cap for the dying enemy;
    ///  - per-rarity multiplier: temporarily swap globalSettings.UnassignedItemChancePercentage for a
    ///    scaled copy while the vanilla method runs, then restore the original reference (Finalizer).
    /// The live asset is never left modified, so this is idempotent across calls and scene loads.
    /// </summary>
    internal static class WorldLootPatches
    {
        private static List<ChanceByQuality> _saved;

        private static DropRatesConfig Cfg => DropRatesPlugin.Cfg;

        /// <param name="gameModifier">The modifier vanilla will multiply each chance by (difficulty, quest modifiers, affixes); caps are applied to chance*gameModifier.</param>
        private static void SwapIn(float gameModifier)
        {
            if (_saved != null) return; // re-entrancy guard
            if (!Cfg.AnyWorldMultNotOne()) return;
            GlobalSettingsManager mgr = GlobalSettingsManager.instance;
            if (mgr == null) return;
            GlobalSettings gs = mgr.globalSettings;
            if (gs == null || gs.UnassignedItemChancePercentage == null) return;

            _saved = gs.UnassignedItemChancePercentage;
            List<ChanceByQuality> scaled = new List<ChanceByQuality>(_saved.Count);
            foreach (ChanceByQuality c in _saved)
            {
                float chance = c.chance * Cfg.WorldMult(c.rarity);
                float cap = Cfg.MaxChance(c.rarity);
                if (cap < 100f && gameModifier > 0f && chance * gameModifier > cap)
                {
                    chance = cap / gameModifier; // so that vanilla's chance * modifier lands exactly on the cap
                }
                scaled.Add(new ChanceByQuality { rarity = c.rarity, chance = chance });
            }
            gs.UnassignedItemChancePercentage = scaled;
        }

        private static void SwapBack()
        {
            if (_saved == null) return;
            GlobalSettingsManager mgr = GlobalSettingsManager.instance;
            if (mgr != null && mgr.globalSettings != null)
            {
                mgr.globalSettings.UnassignedItemChancePercentage = _saved;
            }
            _saved = null;
        }

        private static string DescribeChances()
        {
            GlobalSettingsManager mgr = GlobalSettingsManager.instance;
            if (mgr == null || mgr.globalSettings == null || mgr.globalSettings.UnassignedItemChancePercentage == null) return "?";
            return string.Join(", ", mgr.globalSettings.UnassignedItemChancePercentage.Select(c => c.rarity + "=" + c.chance).ToArray());
        }

        [HarmonyPatch(typeof(LootTable), nameof(LootTable.GetWorldLoot), new[] { typeof(float), typeof(int), typeof(LootRestrictions) })]
        private static class LootTable_GetWorldLoot
        {
            private static void Prefix(float modifier, int level, LootRestrictions lootRestrictions)
            {
                if (Cfg.Bypass) return; // Roguelike: leave everything vanilla
                if (LootContext.UseVanilla)
                {
                    if (Cfg.Verbose.Value)
                    {
                        DropRatesPlugin.Log.LogInfo("GetWorldLoot enemy=" + LootContext.EnemyType + " (summon, vanilla rolls) modifier=" + modifier + " level=" + level);
                    }
                    return;
                }
                // Rarity cap for the enemy currently dropping loot.
                if (LootContext.EnemyActive && lootRestrictions != null && lootRestrictions.limitItemRarities && lootRestrictions.possibleRarities != null)
                {
                    ItemQuality cap = Cfg.CapFor(LootContext.EnemyType);
                    List<ItemQuality> list = lootRestrictions.possibleRarities; // freshly allocated per call by GetLootDrop
                    list.Clear();
                    for (int q = (int)ItemQuality.Common; q <= (int)cap; q++)
                    {
                        list.Add((ItemQuality)q);
                    }
                }

                SwapIn(modifier);

                if (Cfg.Verbose.Value)
                {
                    string rarities = lootRestrictions != null && lootRestrictions.possibleRarities != null
                        ? string.Join(",", lootRestrictions.possibleRarities.Select(r => r.ToString()).ToArray())
                        : "all";
                    DropRatesPlugin.Log.LogInfo("GetWorldLoot enemy=" + (LootContext.EnemyActive ? LootContext.EnemyType.ToString() : "none")
                        + " modifier=" + modifier + " level=" + level + " rarities=[" + rarities + "] chances=[" + DescribeChances() + "]");
                }
            }

            private static void Postfix(List<ItemInfo> __result)
            {
                if (Cfg.Verbose.Value && __result != null && !Cfg.Bypass) // Roguelike's chooser calls this up to 21x per slot; the mod is inert there
                {
                    DropRatesPlugin.Log.LogInfo("  -> " + (__result.Count == 0 ? "nothing" : string.Join(", ", __result.Select(i => i.ItemName + "(" + i.Rarity + ")").ToArray())));
                }
            }

            private static void Finalizer()
            {
                SwapBack();
            }
        }

        [HarmonyPatch(typeof(LootTable), nameof(LootTable.GetGuaranteedWorldLoot), new[] { typeof(int), typeof(LootRestrictions), typeof(System.Random), typeof(List<ItemInfo>) })]
        private static class LootTable_GetGuaranteedWorldLoot
        {
            private static void Prefix()
            {
                if (Cfg.AlsoAffectEventRarityWeights.Value && !Cfg.Bypass)
                {
                    SwapIn(1f);
                }
            }

            private static void Finalizer()
            {
                SwapBack();
            }
        }
    }
}
