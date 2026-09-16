using BepInEx.Configuration;
using BepInEx.Logging;

namespace QoL
{
    internal sealed class QoLConfig
    {
        public readonly ConfigEntry<bool> UpgradeCostByLevelGap;
        public readonly ConfigEntry<float> UpgradeCostMultiplier;
        public readonly ConfigEntry<KeyboardShortcut> ReloadKey;
        public readonly ConfigEntry<bool> Verbose;

        public QoLConfig(ConfigFile cfg)
        {
            UpgradeCostByLevelGap = cfg.Bind("1.ItemUpgrade", "CostByLevelGap", true,
                "Vanilla charges the item's full shop price at the target level (x1.5) no matter how far behind the item is. With this on, the upgrade at Noor's Materials costs only the price difference between the item's current level and the target level (x the multiplier below), so topping up an item every few levels costs the same overall as one big upgrade.");
            UpgradeCostMultiplier = cfg.Bind("1.ItemUpgrade", "CostMultiplier", 1.5f, new ConfigDescription(
                "Multiplier on the price difference. Vanilla uses 1.5 on the full price.", new AcceptableValueRange<float>(0f, 10f)));
            ReloadKey = cfg.Bind("3.General", "ReloadKey", new KeyboardShortcut(UnityEngine.KeyCode.F9), "Press in game to re-read this file.");
            Verbose = cfg.Bind("3.General", "VerboseLogging", false, "Log each upgrade cost computation.");
        }

        public void LogSummary(ManualLogSource log)
        {
            log.LogInfo("Upgrade cost by level gap: " + (UpgradeCostByLevelGap.Value ? "on x" + UpgradeCostMultiplier.Value : "off"));
        }
    }
}
