using BepInEx.Configuration;
using BepInEx.Logging;

namespace BardPreview
{
    internal sealed class BardPreviewConfig
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<string> Trees;
        public readonly ConfigEntry<KeyboardShortcut> ReloadKey;
        public readonly ConfigEntry<bool> Verbose;

        public BardPreviewConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind("1.Preview", "Enabled", true,
                "Unlock skill trees that ship in the game files but are still hidden as an unreleased DLC (today: the Bard tree). "
                + "The game grants exactly this through its own FreeAccessDlcs override. Fixed rule that no setting changes: the unlock only "
                + "works while the game itself marks the pack as hidden (unreleased); the day it is released for sale this mod does nothing "
                + "and the normal purchase check applies.");
            Trees = cfg.Bind("1.Preview", "Trees", "Bard",
                "Comma-separated skill tree names to preview (SkillType names: Bard, Chaos, ...). Only trees the game locks behind a DLC that is still hidden qualify.");
            ReloadKey = cfg.Bind("2.General", "ReloadKey", new KeyboardShortcut(UnityEngine.KeyCode.F9),
                "Re-read this file in game (with the mod window present, F9 opens the window and Apply reloads).");
            Verbose = cfg.Bind("2.General", "VerboseLogging", false, "Log every DLC check the mod answers and every skill-cache refresh.");
        }

        public void LogSummary(ManualLogSource log)
        {
            log.LogInfo("Bard tree preview: " + (Enabled.Value ? "on, trees " + Trees.Value + " (only while the game still hides them as unreleased)" : "off"));
        }
    }
}
