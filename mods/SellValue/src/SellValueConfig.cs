using BepInEx.Configuration;
using BepInEx.Logging;

namespace SellValue
{
    internal sealed class SellValueConfig
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<bool> OnComparison;
        public readonly ConfigEntry<bool> PerUnit;
        public readonly ConfigEntry<string> Label;
        public readonly ConfigEntry<KeyboardShortcut> ReloadKey;
        public readonly ConfigEntry<bool> Verbose;
        public readonly ConfigEntry<bool> DebugDump;

        public SellValueConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind("1.Tooltips", "Enabled", true,
                "Show what a shop pays for the item (its sell value, gold) in the bottom right of every item tooltip: bag, character sheet, stash, shops, loot, crafting result, chat links.");
            OnComparison = cfg.Bind("1.Tooltips", "OnComparison", true,
                "Also show it on the equipped-item comparison tooltip that opens beside a bag/shop item.");
            PerUnit = cfg.Bind("1.Tooltips", "PerUnit", true,
                "For a stack, add the value of one unit after the stack total, e.g. '600 (120 each)'.");
            Label = cfg.Bind("1.Tooltips", "Label", "Sell",
                "The word in front of the number. Empty = number and gold icon only.");
            ReloadKey = cfg.Bind("2.General", "ReloadKey", new KeyboardShortcut(UnityEngine.KeyCode.F9),
                "Re-read this file in game (with the mod window present, F9 opens the window and Apply reloads).");
            Verbose = cfg.Bind("2.General", "VerboseLogging", false, "Log every item hover with the value shown.");
            DebugDump = cfg.Bind("2.General", "DebugDump", false, "Log the item tooltip's layout once per launch (only for debugging the mod).");
        }

        public void LogSummary(ManualLogSource log)
        {
            log.LogInfo("Sell value on item tooltips: " + (Enabled.Value
                ? "on (label '" + Label.Value + "', comparison " + (OnComparison.Value ? "yes" : "no") + ", per-unit " + (PerUnit.Value ? "yes" : "no") + ")"
                : "off"));
        }
    }
}
