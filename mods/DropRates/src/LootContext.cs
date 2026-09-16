namespace DropRates
{
    /// <summary>
    /// Ambient context set by outer patches so inner patches know what kind of roll is happening.
    /// All loot code is synchronous and single-threaded, so plain statics are safe.
    /// </summary>
    internal static class LootContext
    {
        /// <summary>True while Character.GetLootDrop / CharacterInfo.GetLootDrop is executing.</summary>
        public static bool EnemyActive;

        /// <summary>Enemy type of the dying enemy whose loot is being rolled.</summary>
        public static EnemyType EnemyType;

        /// <summary>True when the dying enemy was summoned by another enemy (boss cauldrons, fetishes, clones...).</summary>
        public static bool IsSummon;

        /// <summary>True when this enemy's loot should be left exactly as vanilla (summon + config switch).</summary>
        public static bool UseVanilla => EnemyActive && IsSummon && DropRatesPlugin.Cfg.SummonsUseVanillaRolls.Value;

        /// <summary>Shopkeeper whose stock is currently being generated (ShopManager.RefreshItemDictSingle).</summary>
        public static Shopkeeper CurrentShopkeeper;
    }
}
