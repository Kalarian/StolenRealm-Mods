using BepInEx.Configuration;
using BepInEx.Logging;

namespace BattleStats
{
    internal sealed class BattleStatsConfig
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<bool> LogEveryHit;
        public readonly ConfigEntry<bool> WriteJson;
        public readonly ConfigEntry<int> TopSkills;
        public readonly ConfigEntry<bool> ShowInWindow;
        public readonly ConfigEntry<bool> ShowBreakdown;
        public readonly ConfigEntry<bool> ShowHits;
        public readonly ConfigEntry<bool> ShowElements;
        public readonly ConfigEntry<bool> ShowActivity;
        public readonly ConfigEntry<bool> TopSkillsTooltip;
        public readonly ConfigEntry<bool> CellTooltips;
        public readonly ConfigEntry<KeyboardShortcut> ReloadKey;
        public readonly ConfigEntry<bool> Verbose;
        public readonly ConfigEntry<bool> DebugHooks;

        public BattleStatsConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind("1.Recording", "Enabled", true,
                "Record every damage, healing and block credit during a battle with where it came from (direct hit, damage over time, ground tile, thorns, summon), plus crits, kills, overkill, casts, mana and movement. Recording happens on the machine that resolves the battle (the host); the numbers are shared to everyone in the party like the game's own stats.");
            LogEveryHit = cfg.Bind("1.Recording", "LogEveryHit", false,
                "Write one log line per credit (HIT / HEAL / BLOCK / KILL / CAST). Off keeps only the end-of-battle summary in the log.");
            WriteJson = cfg.Bind("1.Recording", "WriteJson", false,
                "Also write BepInEx\\BattleStats\\battle-<date-time>.json with every event and the summary, for analysis.");
            TopSkills = cfg.Bind("1.Recording", "TopSkills", 8, new ConfigDescription("How many skills/statuses to list per character in the log summary.", new AcceptableValueRange<int>(1, 50)));

            ShowInWindow = cfg.Bind("2.StatsWindow", "ShowInWindow", true,
                "Add the extra rows to the game's post-battle Stats window (the Stats button after a fight) and make the table scroll (mouse wheel, drag, or the slim bar on the right). Turn off to keep the window vanilla and only log.");
            ShowBreakdown = cfg.Bind("2.StatsWindow", "ShowBreakdown", true,
                "Section 'Damage breakdown': direct hits, damage over time (poison, burn...), ground tiles, summons, thorns.");
            ShowHits = cfg.Bind("2.StatsWindow", "ShowHits", true,
                "Section 'Hits': hits / crits (crit rate), kills / overkill, biggest hit, best skill, damage per turn.");
            ShowElements = cfg.Bind("2.StatsWindow", "ShowElements", true,
                "Section 'Damage by element': physical, fire, cold, lightning, shadow, holy.");
            ShowActivity = cfg.Bind("2.StatsWindow", "ShowActivity", true,
                "Section 'Activity': casts (free actions in brackets), mana spent, hexes moved, damage taken from ticks.");
            TopSkillsTooltip = cfg.Bind("2.StatsWindow", "TopSkillsTooltip", true,
                "Hover a character's name at the top of the Stats window to see their top damage sources (skill, total, hits).");
            CellTooltips = cfg.Bind("2.StatsWindow", "CellTooltips", true,
                "Hover any damage number in the Stats window (Damage Dealt, Direct Hits, Over Time, an element...) to see which abilities made it up for that character.");

            ReloadKey = cfg.Bind("3.General", "ReloadKey", new KeyboardShortcut(UnityEngine.KeyCode.F9), "Press in game to re-read this file.");
            Verbose = cfg.Bind("3.General", "VerboseLogging", false, "Log what the mod does (rows added to the window, per-battle summary details).");
            DebugHooks = cfg.Bind("3.General", "DebugHooks", false, "Log every hook context change (which damage path is active) - extremely spammy; only for debugging the recorder itself.");
        }

        public void LogSummary(ManualLogSource log)
        {
            log.LogInfo("Battle stats: " + (Enabled.Value ? "recording" : "off") + (ShowInWindow.Value ? ", stats window rows" : ", log only") + (LogEveryHit.Value ? ", per-hit lines" : "") + (WriteJson.Value ? ", JSON files" : ""));
        }
    }
}
