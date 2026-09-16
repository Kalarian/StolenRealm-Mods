using BepInEx.Configuration;
using BepInEx.Logging;

namespace DifficultyXP
{
    internal sealed class DifficultyXPConfig
    {
        private const string Sec = "General";

        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<float> ExtraMultiplier;
        public readonly ConfigEntry<bool> ScaleEventXP;
        public readonly ConfigEntry<bool> IncludeGoldBuffs;
        public readonly ConfigEntry<KeyboardShortcut> ReloadKey;
        public readonly ConfigEntry<bool> Verbose;

        public DifficultyXPConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind(Sec, "Enabled", true,
                "Experience gain scales with difficulty at the same rate as gold: the active difficulty's gold bonus is used as the XP bonus (Classic +25%, Veteran +50%, Torturous +75%, Heart of the Realm +100%, Endless +25%). Applies to battle XP and quest completion XP. Roguelike XP is level-per-battle and is not affected.");
            ExtraMultiplier = cfg.Bind(Sec, "ExtraMultiplier", 1f, new ConfigDescription(
                "Additional multiplier applied on top of the mirrored bonus. 1 = exactly match gold. 2 = double that.", new AcceptableValueRange<float>(0.1f, 100f)));
            ScaleEventXP = cfg.Bind(Sec, "ScaleEventXP", true,
                "Also apply the difficulty XP modifier to island events that grant experience. Vanilla never scales event XP (or event gold) with difficulty.");
            IncludeGoldBuffs = cfg.Bind(Sec, "IncludeGoldBuffs", true,
                "Also mirror temporary gold bonuses on your characters (e.g. the campfire 'Prepared' buff: +20% item and gold drops) into XP, exactly as the game applies them to gold.");
            ReloadKey = cfg.Bind(Sec, "ReloadKey", new KeyboardShortcut(UnityEngine.KeyCode.F9), "Press in game to re-read this file and log the active values.");
            Verbose = cfg.Bind(Sec, "VerboseLogging", false, "Log every XP modifier lookup and event XP adjustment to BepInEx/LogOutput.log.");
        }

        public void LogSummary(ManualLogSource log)
        {
            log.LogInfo("XP mirrors gold: " + (Enabled.Value ? "on" : "off") + ", extra x" + ExtraMultiplier.Value + ", eventXP " + (ScaleEventXP.Value ? "on" : "off") + ", gold buffs " + (IncludeGoldBuffs.Value ? "on" : "off"));
        }
    }
}
