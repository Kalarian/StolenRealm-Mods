using HarmonyLib;

namespace DropRates.Patches
{
    /// <summary>
    /// Records which enemy is currently having its loot rolled, so the inner LootTable patches
    /// can (a) rewrite the rarity cap and (b) scale only enemy loot tables, not event/gathering ones.
    /// </summary>
    internal static class EnemyContextPatches
    {
        // Campaign / normal battles: Character.GetLootDrop(float modifier)
        [HarmonyPatch(typeof(Character), nameof(Character.GetLootDrop), new[] { typeof(float) })]
        private static class Character_GetLootDrop
        {
            private static void Prefix(Character __instance)
            {
                LootContext.EnemyActive = true;
                LootContext.EnemyType = __instance.EnemyType;
                LootContext.IsSummon = __instance.IsSummon; // SummonMaster != null
            }

            private static void Finalizer()
            {
                LootContext.EnemyActive = false;
                LootContext.IsSummon = false;
            }
        }

        // Roguelike boss rewards: Burst2Flame.CharacterInfo.GetLootDrop(float modifier, int level)
        [HarmonyPatch(typeof(Burst2Flame.CharacterInfo), nameof(Burst2Flame.CharacterInfo.GetLootDrop), new[] { typeof(float), typeof(int) })]
        private static class CharacterInfo_GetLootDrop
        {
            private static void Prefix(Burst2Flame.CharacterInfo __instance)
            {
                LootContext.EnemyActive = true;
                LootContext.EnemyType = __instance.enemyType;
                LootContext.IsSummon = false; // roguelike boss reward path, never a summon
            }

            private static void Finalizer()
            {
                LootContext.EnemyActive = false;
                LootContext.IsSummon = false;
            }
        }
    }
}
