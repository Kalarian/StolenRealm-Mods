using BepInEx.Configuration;
using BepInEx.Logging;

namespace ModMenu
{
    internal sealed class ModMenuConfig
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<KeyboardShortcut> OpenKey;
        public readonly ConfigEntry<bool> ShowDescriptions;
        public readonly ConfigEntry<bool> CloseOnApply;
        public readonly ConfigEntry<KeyboardShortcut> ReloadKey;
        public readonly ConfigEntry<bool> Verbose;

        public ModMenuConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind("1.Menu", "Enabled", true,
                "Show the in-game mod switchboard window. It lists every mod with a checkbox; Apply writes stolenrealm.mods.cfg and reloads every mod on the spot. While this window is available, the other mods' F9 no longer reloads directly (Apply does that).");
            OpenKey = cfg.Bind("1.Menu", "OpenKey", new KeyboardShortcut(UnityEngine.KeyCode.F9), "Key that opens and closes the window.");
            ShowDescriptions = cfg.Bind("1.Menu", "ShowDescriptions", true, "Show a one-line description under each mod name.");
            CloseOnApply = cfg.Bind("1.Menu", "CloseOnApply", true, "Close the window after Apply.");
            ReloadKey = cfg.Bind("2.General", "ReloadKey", new KeyboardShortcut(UnityEngine.KeyCode.F9), "Same key as OpenKey: with the menu present, F9 opens the window and Apply re-reads every config file.");
            Verbose = cfg.Bind("2.General", "VerboseLogging", false, "Log what the window does (open, apply, each state change).");
        }

        public void LogSummary(ManualLogSource log)
        {
            log.LogInfo("Mod menu: " + (Enabled.Value ? "on, key " + OpenKey.Value : "off"));
        }
    }
}
