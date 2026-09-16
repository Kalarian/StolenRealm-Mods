using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace ThreatOverlay
{
    internal enum ThreatMode { AllEnemies, HoveredEnemy }

    internal sealed class ThreatOverlayConfig
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<KeyboardShortcut> HoldKey;
        public readonly ConfigEntry<ThreatMode> Mode;
        public readonly ConfigEntry<bool> ShowReach;
        public readonly ConfigEntry<bool> ShowStrike;
        public readonly ConfigEntry<bool> IncludeSkillRanges;
        public readonly ConfigEntry<string> ReachColor;
        public readonly ConfigEntry<string> StrikeColor;
        public readonly ConfigEntry<float> RefreshSeconds;
        public readonly ConfigEntry<KeyboardShortcut> ReloadKey;
        public readonly ConfigEntry<bool> Verbose;

        public ThreatOverlayConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind("1.Overlay", "Enabled", true,
                "In battle, hold the key below to tint the hexes enemies can reach on their next turn (where they can stand) and the hexes they could hit from there (movement plus attack range). Release to go back to the normal view. Purely visual and local; nothing is sent to other players.");
            HoldKey = cfg.Bind("1.Overlay", "HoldKey", new KeyboardShortcut(KeyCode.LeftAlt), "Hold to show the overlay.");
            Mode = cfg.Bind("1.Overlay", "Mode", ThreatMode.AllEnemies, "AllEnemies = every living enemy at once. HoveredEnemy = only the enemy under the mouse.");
            ShowReach = cfg.Bind("1.Overlay", "ShowReach", true, "Tint the hexes an enemy can move to.");
            ShowStrike = cfg.Bind("1.Overlay", "ShowStrike", true, "Tint the hexes an enemy could attack after moving (its longest attack range from any reachable hex).");
            IncludeSkillRanges = cfg.Bind("1.Overlay", "IncludeSkillRanges", true, "Use the enemy's longest harmful skill range, not just its weapon range, for the strike area.");
            ReachColor = cfg.Bind("1.Overlay", "ReachColor", "C83232", "Hex colour (no #) for hexes enemies can move to.");
            StrikeColor = cfg.Bind("1.Overlay", "StrikeColor", "E08A3C", "Hex colour (no #) for hexes enemies can hit but not stand on.");
            RefreshSeconds = cfg.Bind("1.Overlay", "RefreshSeconds", 0.25f, new ConfigDescription("How often the overlay is recomputed while the key is held.", new AcceptableValueRange<float>(0.05f, 2f)));
            ReloadKey = cfg.Bind("2.General", "ReloadKey", new KeyboardShortcut(KeyCode.F9), "Press in game to re-read this file.");
            Verbose = cfg.Bind("2.General", "VerboseLogging", false, "Log each recomputation (enemies, budgets, ranges, cell counts).");
        }

        public void LogSummary(ManualLogSource log)
        {
            log.LogInfo("Threat overlay: " + (Enabled.Value ? "on" : "off") + ", hold " + HoldKey.Value + ", " + Mode.Value
                + (ShowReach.Value ? ", reach" : "") + (ShowStrike.Value ? ", strike" : "") + (IncludeSkillRanges.Value ? " (skill ranges)" : " (weapon range)"));
        }
    }
}
