using BepInEx.Configuration;
using BepInEx.Logging;

namespace FortuneUpgrade
{
    internal sealed class FortuneUpgradeConfig
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<KeyboardShortcut> UpgradeKey;
        public readonly ConfigEntry<bool> OnlyInTown;
        public readonly ConfigEntry<bool> CostByLevelGap;
        public readonly ConfigEntry<float> CostMultiplier;
        public readonly ConfigEntry<KeyboardShortcut> ReloadKey;
        public readonly ConfigEntry<bool> Verbose;

        public FortuneUpgradeConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind("1.Upgrade", "Enabled", true,
                "Pay gold to raise a fortune to your character's level, like upgrading an item at Noor's. Hover a fortune in the Fortune window and press the upgrade key; a confirmation shows the cost. Priced exactly like upgrading a two-handed weapon of the same rarity.");
            UpgradeKey = cfg.Bind("1.Upgrade", "UpgradeKey", new KeyboardShortcut(UnityEngine.KeyCode.U),
                "Press while hovering a fortune in the Fortune window.");
            OnlyInTown = cfg.Bind("1.Upgrade", "OnlyInTown", true,
                "Only allow upgrading while in a town, like item upgrades. Off = anywhere outside battle.");
            CostByLevelGap = cfg.Bind("1.Upgrade", "CostByLevelGap", true,
                "Charge the price difference between the fortune's current level and your level (same rule as the QoL item-upgrade change). Off = vanilla-style full price at your level.");
            CostMultiplier = cfg.Bind("1.Upgrade", "CostMultiplier", 1.5f, new ConfigDescription(
                "Multiplier on the two-handed-weapon price. 1.5 matches the game's item upgrade markup.", new AcceptableValueRange<float>(0f, 10f)));
            ReloadKey = cfg.Bind("2.General", "ReloadKey", new KeyboardShortcut(UnityEngine.KeyCode.F9), "Press in game to re-read this file.");
            Verbose = cfg.Bind("2.General", "VerboseLogging", false, "Log every cost computed and upgrade performed.");
        }

        public void LogSummary(ManualLogSource log)
        {
            log.LogInfo("Fortune upgrade: " + (Enabled.Value ? "on, key " + UpgradeKey.Value + (OnlyInTown.Value ? ", town only" : "") + ", " + (CostByLevelGap.Value ? "gap" : "full") + " price x" + CostMultiplier.Value : "off"));
        }
    }
}
