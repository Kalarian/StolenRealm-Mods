using BepInEx.Configuration;
using BepInEx.Logging;

namespace SharedProgress
{
    internal sealed class SharedProgressConfig
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<bool> ShareQuestMap;
        public readonly ConfigEntry<bool> ShareActAndShops;
        public readonly ConfigEntry<bool> IncludeRoguelikeCharacters;
        public readonly ConfigEntry<KeyboardShortcut> ReloadKey;
        public readonly ConfigEntry<bool> Verbose;

        public SharedProgressConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind("1.Sync", "Enabled", true,
                "Every one of your campaign characters has the campaign progress of your most advanced one: completed quests and towns on the quest map, the act you are in, and the shop stock level. A new or lagging character catches up when it is loaded; quests completed by any of your characters count for all of them. Progress is never removed.");
            ShareQuestMap = cfg.Bind("1.Sync", "ShareQuestMap", true,
                "Share the completed quest and town nodes (what the quest map shows as done and unlocked) and the main-quest progress that decides the act.");
            ShareActAndShops = cfg.Bind("1.Sync", "ShareActAndShops", true,
                "Share the town you start in (last act visited), the highest completed quest level (max selectable quest level) and the shop stock level.");
            IncludeRoguelikeCharacters = cfg.Bind("1.Sync", "IncludeRoguelikeCharacters", false,
                "Also read from / write to Roguelike-mode characters. Off by default: Roguelike has its own progression and never looks at campaign progress.");
            ReloadKey = cfg.Bind("2.General", "ReloadKey", new KeyboardShortcut(UnityEngine.KeyCode.F9), "Press in game to re-read this file and rescan all character saves.");
            Verbose = cfg.Bind("2.General", "VerboseLogging", false, "Log every merge (which character gained what).");
        }

        public void LogSummary(ManualLogSource log, ProgressPool pool)
        {
            log.LogInfo("Shared progress: " + (Enabled.Value ? "on" : "off") + ", account " + pool.Describe()
                + (ShareQuestMap.Value ? "" : ", quest map NOT shared") + (ShareActAndShops.Value ? "" : ", act/shops NOT shared") + (IncludeRoguelikeCharacters.Value ? ", incl. roguelike" : ""));
        }
    }
}
