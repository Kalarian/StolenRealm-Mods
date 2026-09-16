using HarmonyLib;

namespace QoL.Patches
{
    /// <summary>
    /// ItemUpgradeManager.GetUpgradeCost(item, upgradedLevel) = PurchasePriceByLevel(upgradedLevel) * 1.5 in vanilla,
    /// i.e. the full price at the target level regardless of the item's current level.
    /// We charge (price at target - price at current level) * multiplier instead.
    /// UpgradeItem sets item.itemLevel to the target BEFORE it calls GetUpgradeCost, so we capture the
    /// pre-upgrade level in a prefix on UpgradeItem and use it for that one call.
    /// </summary>
    internal static class UpgradeCostPatches
    {
        private static Item _upgrading;
        private static int _levelBefore;

        [HarmonyPatch(typeof(ItemUpgradeManager), nameof(ItemUpgradeManager.UpgradeItem), new[] { typeof(ItemSlot) })]
        private static class ItemUpgradeManager_UpgradeItem
        {
            private static void Prefix(ItemSlot itemSlot)
            {
                if (itemSlot == null || itemSlot.Item == null) return;
                _upgrading = itemSlot.Item;
                _levelBefore = itemSlot.Item.itemLevel;
            }

            private static void Finalizer()
            {
                _upgrading = null;
            }
        }

        [HarmonyPatch(typeof(ItemUpgradeManager), nameof(ItemUpgradeManager.GetUpgradeCost), new[] { typeof(Item), typeof(int) })]
        private static class ItemUpgradeManager_GetUpgradeCost
        {
            private static void Postfix(Item item, int upgradedLevel, ref float __result)
            {
                QoLConfig cfg = QoLPlugin.Cfg;
                if (!cfg.UpgradeCostByLevelGap.Value || item == null) return;
                int current = (item == _upgrading) ? _levelBefore : item.itemLevel;
                if (upgradedLevel <= current) return;
                float full = item.PurchasePriceByLevel(upgradedLevel);
                float already = item.PurchasePriceByLevel(current);
                float gap = full - already;
                if (gap <= 0f) return;
                float vanilla = __result;
                __result = gap * cfg.UpgradeCostMultiplier.Value;
                if (cfg.Verbose.Value)
                {
                    QoLPlugin.Log.LogInfo("Upgrade cost " + item.ItemName + " " + current + "->" + upgradedLevel + ": vanilla " + vanilla + " -> " + __result);
                }
            }
        }
    }
}
