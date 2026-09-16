using BepInEx.Configuration;
using BepInEx.Logging;

namespace SharedGold
{
    internal sealed class SharedGoldConfig
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<bool> IncludeHardcore;
        public readonly ConfigEntry<KeyboardShortcut> ReloadKey;
        public readonly ConfigEntry<bool> Verbose;

        public SharedGoldConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind("1.Pool", "Enabled", true,
                "All your campaign characters share one pool of gold. The pool is simply the sum of what your characters hold; the mod only moves gold between your own characters so the one you are playing holds all of it and party gold always shows the full amount. It never creates gold. Roguelike characters are ignored.");
            IncludeHardcore = cfg.Bind("1.Pool", "IncludeHardcoreCharacters", true,
                "Pool hardcore characters together with normal ones. Off = hardcore characters keep their own gold.");
            ReloadKey = cfg.Bind("2.General", "ReloadKey", new KeyboardShortcut(UnityEngine.KeyCode.F9), "Press in game to re-read this file.");
            Verbose = cfg.Bind("2.General", "VerboseLogging", false, "Log every consolidation.");
        }

        public void LogSummary(ManualLogSource log)
        {
            log.LogInfo("Shared gold: " + (Enabled.Value ? "on" : "off") + (IncludeHardcore.Value ? ", hardcore pooled" : ", hardcore separate"));
        }
    }
}
