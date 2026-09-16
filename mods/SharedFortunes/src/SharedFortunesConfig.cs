using BepInEx.Configuration;
using BepInEx.Logging;

namespace SharedFortunes
{
    internal sealed class SharedFortunesConfig
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<bool> CapToCharacterLevel;
        public readonly ConfigEntry<bool> IncludeRoguelikeCharacters;
        public readonly ConfigEntry<KeyboardShortcut> ReloadKey;
        public readonly ConfigEntry<bool> Verbose;

        public SharedFortunesConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind("1.Sharing", "Enabled", true,
                "Fortunes are shared across all your characters. A pool file (SharedFortunes.json, next to your character saves) remembers every fortune any of your characters has earned and the highest level it was earned at. When a character loads, missing fortunes are added to their available list (unequipped) and lower-level ones are raised. Nothing is ever removed or lowered, and slot choices stay per character.");
            CapToCharacterLevel = cfg.Bind("1.Sharing", "CapToCharacterLevel", true,
                "A shared fortune is capped at the receiving character's current level and rises as they level up (checked every time the character loads). Turn off to give every character the pool's full level immediately.");
            IncludeRoguelikeCharacters = cfg.Bind("1.Sharing", "IncludeRoguelikeCharacters", false,
                "Also share fortunes to and from Roguelike-mode characters.");
            ReloadKey = cfg.Bind("2.General", "ReloadKey", new KeyboardShortcut(UnityEngine.KeyCode.F9), "Press in game to re-read this file and rescan all character saves into the pool.");
            Verbose = cfg.Bind("2.General", "VerboseLogging", false, "Log every fortune added, raised or pooled.");
        }

        public void LogSummary(ManualLogSource log, FortunePool pool)
        {
            log.LogInfo("Shared fortunes: " + (Enabled.Value ? "on" : "off") + ", pool holds " + pool.Count + " fortunes"
                + (CapToCharacterLevel.Value ? ", capped to character level" : ", full pool level")
                + (IncludeRoguelikeCharacters.Value ? ", incl. roguelike" : ""));
        }
    }
}
