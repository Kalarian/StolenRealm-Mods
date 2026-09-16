using BepInEx.Configuration;
using BepInEx.Logging;

namespace LevelSync
{
    internal sealed class LevelSyncConfig
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<bool> IncludeRoguelikeCharacters;
        public readonly ConfigEntry<int> MaxLevel;
        public readonly ConfigEntry<KeyboardShortcut> ReloadKey;
        public readonly ConfigEntry<bool> Verbose;

        public LevelSyncConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind("1.Sync", "Enabled", true,
                "Every one of your characters is kept at the level of your highest character. On load, a lower character is raised to the account's highest level (skill and stat points come with it, since the game derives them from level). While playing, XP earned by one character raises the others in your party too. Levels are never lowered.");
            IncludeRoguelikeCharacters = cfg.Bind("1.Sync", "IncludeRoguelikeCharacters", false,
                "Also sync Roguelike-mode characters (both directions). Off by default: Roguelike has its own level-per-battle progression.");
            MaxLevel = cfg.Bind("1.Sync", "MaxLevel", 30, new ConfigDescription(
                "Never raise a character above this level (the game's cap is 30).", new AcceptableValueRange<int>(1, 30)));
            ReloadKey = cfg.Bind("2.General", "ReloadKey", new KeyboardShortcut(UnityEngine.KeyCode.F9), "Press in game to re-read this file and rescan all character saves.");
            Verbose = cfg.Bind("2.General", "VerboseLogging", false, "Log every level check and raise.");
        }

        public void LogSummary(ManualLogSource log, LevelPool pool)
        {
            log.LogInfo("Level sync: " + (Enabled.Value ? "on" : "off") + ", account highest " + pool.Describe()
                + ", cap L" + MaxLevel.Value + (IncludeRoguelikeCharacters.Value ? ", incl. roguelike" : ""));
        }
    }
}
