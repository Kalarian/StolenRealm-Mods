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

        /// <summary>
        /// The upgrade tab's hover (ItemSlot.OnPointerEnter) decides between the Upgrade button and "Not Enough Gold" with
        /// Item.UpgradePrice = full purchase price at the item's CURRENT level x 1.5, not with GetUpgradeCost. In vanilla
        /// that gate is always at or below the real charge, so nobody notices; with the gap-based price it can sit far
        /// above what we actually charge and refuse an affordable upgrade (found 2026-09-16: 33,750 gold, robes shown at
        /// 26,880, gate at 67,200). While the upgrade window exists, UpgradePrice now answers with the same number the
        /// window shows and charges.
        /// </summary>
        [HarmonyPatch(typeof(Item), nameof(Item.UpgradePrice), MethodType.Getter)]
        private static class Item_UpgradePrice
        {
            private static void Postfix(Item __instance, ref float __result)
            {
                QoLConfig cfg = QoLPlugin.Cfg;
                if (cfg == null || !cfg.UpgradeCostByLevelGap.Value || __instance == null) return;
                try
                {
                    ItemUpgradeManager mgr = LoadableUIWindow<ItemUpgradeManager>.Instance;
                    if (mgr == null || __instance.Owner == null) return;
                    int target = __instance.Owner.Level;
                    if (target <= __instance.itemLevel) return;
                    __result = mgr.GetUpgradeCost(__instance, target); // goes through the postfix above: the gap-based price
                }
                catch { }
            }
        }
    }
}
