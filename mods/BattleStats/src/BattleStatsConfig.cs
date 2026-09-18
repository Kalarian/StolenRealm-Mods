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
        public readonly ConfigEntry<bool> RunStatsButton;
        public readonly ConfigEntry<float> RunStatsButtonGap;
        public readonly ConfigEntry<KeyboardShortcut> RunStatsKey;
        public readonly ConfigEntry<bool> IncludeCurrentBattle;
        public readonly ConfigEntry<bool> RoguelikeAreaResets;
        public readonly ConfigEntry<bool> SaveRuns;
        public readonly ConfigEntry<int> KeepRuns;
        public readonly ConfigEntry<KeyboardShortcut> RunHistoryKey;
        public readonly ConfigEntry<bool> ResumeRuns;
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

            RunStatsButtonGap = cfg.Bind("3.RunStats", "RunStatsButtonGap", 6f, new ConfigDescription(
                "The space between the gold and difficulty box at the top right and the Run Stats button that sits under it, in UI units.", new AcceptableValueRange<float>(0f, 400f)));
            RunStatsButton = cfg.Bind("3.RunStats", "RunStatsButton", true,
                "Show a 'Run Stats' button under the gold and difficulty box at the top right of the screen (town, island and battle), a copy of the post-battle Stats button. It hides whenever any menu, the post-battle screen or a loading screen is up. It opens the same Stats window with the totals of every battle since you left town: sums for everything, the biggest hit of the run, the run-wide best skill and top sources. Totals reset when you leave town for the next quest (and on a defeat Retry); they stay readable in town after a run. In a Roguelike run they cover the whole run.");
            RunStatsKey = cfg.Bind("3.RunStats", "RunStatsKey", new KeyboardShortcut(UnityEngine.KeyCode.F8),
                "Key that opens and closes the run stats page (same as the button; the only way in with a gamepad).");
            IncludeCurrentBattle = cfg.Bind("3.RunStats", "IncludeCurrentBattle", true,
                "When the page is opened during a battle, add the battle so far on top of the finished ones (the title says '+ current').");
            RoguelikeAreaResets = cfg.Bind("3.RunStats", "RoguelikeAreaResets", false,
                "Roguelike only: also reset the run totals when the party moves to the next area. Off = one Roguelike run is one set of totals.");
            SaveRuns = cfg.Bind("3.RunStats", "SaveRuns", true,
                "Write every run to BepInEx\\BattleStats\\runs as one .json per run, rewritten after each battle so a crash loses nothing. The History button in the run stats window (or the RunHistoryKey) lists them; clicking one shows its stats page.");
            KeepRuns = cfg.Bind("3.RunStats", "KeepRuns", 50, new ConfigDescription(
                "How many run files to keep; the oldest are deleted beyond this. 0 = keep them all.", new AcceptableValueRange<int>(0, 1000)));
            RunHistoryKey = cfg.Bind("3.RunStats", "RunHistoryKey", new KeyboardShortcut(UnityEngine.KeyCode.F7),
                "Key that opens and closes the run history list.");
            ResumeRuns = cfg.Bind("3.RunStats", "ResumeRuns", true,
                "Close the game in the middle of a quest and the totals carry on where they left off when you come back to it. A run is only picked up again for the same quest and the same party, and never once it has ended.");

            ReloadKey = cfg.Bind("4.General", "ReloadKey", new KeyboardShortcut(UnityEngine.KeyCode.F9), "Press in game to re-read this file.");
            Verbose = cfg.Bind("4.General", "VerboseLogging", false, "Log what the mod does (rows added to the window, per-battle summary details, run stats folds and resets).");
            DebugHooks = cfg.Bind("4.General", "DebugHooks", false, "Log every hook context change (which damage path is active) - extremely spammy; only for debugging the recorder itself. Also dumps the Stats window and the HUD button row once.");
        }

        public void LogSummary(ManualLogSource log)
        {
            log.LogInfo("Battle stats: " + (Enabled.Value ? "recording" : "off") + (ShowInWindow.Value ? ", stats window rows" : ", log only") + (LogEveryHit.Value ? ", per-hit lines" : "") + (WriteJson.Value ? ", JSON files" : "")
                + ", run stats " + (RunStatsButton.Value ? "button + " : "") + RunStatsKey.Value.MainKey
                + (SaveRuns.Value ? ", runs saved (history " + RunHistoryKey.Value.MainKey + ", keep " + (KeepRuns.Value > 0 ? KeepRuns.Value.ToString() : "all") + ")" : ""));
        }
    }
}
