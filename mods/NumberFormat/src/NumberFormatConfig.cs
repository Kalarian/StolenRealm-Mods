using BepInEx.Configuration;
using BepInEx.Logging;

namespace NumberFormat
{
    internal sealed class NumberFormatConfig
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<string> Separator;
        public readonly ConfigEntry<int> MinDigits;
        public readonly ConfigEntry<bool> SkipInputFields;
        public readonly ConfigEntry<bool> IncludeLegacyText;
        public readonly ConfigEntry<KeyboardShortcut> ReloadKey;
        public readonly ConfigEntry<bool> Verbose;

        public NumberFormatConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind("1.Format", "Enabled", true,
                "Put thousands separators into every number the game shows (gold, damage, health, XP, tooltips, stats...): 12345 becomes 12,345. Works on the text as it is handed to the UI, so it covers everything at once. Numbers glued to letters (codes, names), dates, times, colour codes and text you type into a box are left alone.");
            Separator = cfg.Bind("1.Format", "Separator", ",", "The separator to insert (\",\" or \".\" or a space).");
            MinDigits = cfg.Bind("1.Format", "MinDigits", 4, new ConfigDescription("Only numbers with at least this many digits are touched (4 = 1,000 and up).", new AcceptableValueRange<int>(4, 7)));
            SkipInputFields = cfg.Bind("1.Format", "SkipInputFields", true, "Never reformat text inside an input box (chat, search bars, lobby codes, amounts you type).");
            IncludeLegacyText = cfg.Bind("1.Format", "IncludeLegacyText", true, "Also format the game's few old-style (non-TextMeshPro) labels.");
            ReloadKey = cfg.Bind("2.General", "ReloadKey", new KeyboardShortcut(UnityEngine.KeyCode.F9), "Press in game to re-read this file.");
            Verbose = cfg.Bind("2.General", "VerboseLogging", false, "Log the first few reformatted texts after each launch (to check nothing odd gets touched).");
        }

        public void LogSummary(ManualLogSource log)
        {
            log.LogInfo("Number format: " + (Enabled.Value ? "on, separator '" + Separator.Value + "', from " + MinDigits.Value + " digits" : "off") + (SkipInputFields.Value ? ", input boxes skipped" : ""));
        }
    }
}
