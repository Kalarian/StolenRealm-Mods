using BepInEx.Configuration;
using BepInEx.Logging;

namespace SpecialTooltips
{
    internal sealed class SpecialTooltipsConfig
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<bool> UnderlineLinks;
        public readonly ConfigEntry<KeyboardShortcut> ReloadKey;
        public readonly ConfigEntry<bool> Verbose;

        public SpecialTooltipsConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind("1.Tooltip", "Enabled", true,
                "In the right-click Examine window, make each entry under 'Special' hoverable and show a tooltip explaining it. The wording lives in stolenrealm.specialtooltips.descriptions.txt next to this file; edit it freely.");
            UnderlineLinks = cfg.Bind("1.Tooltip", "UnderlineLinks", false,
                "Underline the special entries so it is obvious they can be hovered.");
            ReloadKey = cfg.Bind("2.General", "ReloadKey", new KeyboardShortcut(UnityEngine.KeyCode.F9), "Press in game to re-read this file and the descriptions file.");
            Verbose = cfg.Bind("2.General", "VerboseLogging", false, "Log each tooltip shown, and log the game's conditional special-effect rules once.");
        }

        public void LogSummary(ManualLogSource log, Descriptions desc)
        {
            log.LogInfo("Special tooltips: " + (Enabled.Value ? "on" : "off") + ", " + desc.Count + " descriptions loaded" + (desc.CustomCount > 0 ? " (" + desc.CustomCount + " customised)" : ""));
        }
    }
}
