using BepInEx.Configuration;
using BepInEx.Logging;

namespace TargetTooltip
{
    public enum TooltipStyle
    {
        Compact,
        Full
    }

    internal sealed class TargetTooltipConfig
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<TooltipStyle> Style;
        public readonly ConfigEntry<bool> ShowOnEmptyCells;
        public readonly ConfigEntry<bool> DamagePreview;
        public readonly ConfigEntry<bool> PreviewDetail;
        public readonly ConfigEntry<bool> IncludeTileEffects;
        public readonly ConfigEntry<KeyboardShortcut> StyleToggleKey;
        public readonly ConfigEntry<KeyboardShortcut> ReloadKey;
        public readonly ConfigEntry<bool> Verbose;

        public TargetTooltipConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind("1.Tooltip", "Enabled", true,
                "In battle, show a tooltip naming the action that will fire when you click the hovered cell: the basic attack that will be used when hovering an enemy with the attack cursor, or the selected skill when it is targeting a valid cell.");
            Style = cfg.Bind("1.Tooltip", "Style", TooltipStyle.Full,
                "Compact = just the skill icon and name in a small translucent box (the original BetterTooltips look). Full = the game's normal skill tooltip with damage, cost, cooldown and status.");
            ShowOnEmptyCells = cfg.Bind("1.Tooltip", "ShowOnEmptyCells", true,
                "When a skill is selected, also show the tooltip on valid cells that have no enemy (ground-target skills, buffs on allies, movement skills). Enemy cells always show it.");
            DamagePreview = cfg.Bind("1.Tooltip", "DamagePreview", true,
                "When hovering an enemy, add 'vs <enemy>: 71-96 (45-60% of its health)' to the tooltip: the action's damage against THAT enemy with its resistances, armor, damage reduction and any target-specific bonuses applied, using the game's own damage calculation. Shown in both Full and Compact styles.");
            PreviewDetail = cfg.Bind("1.Tooltip", "PreviewDetail", true,
                "Add a small line under the preview with the per-school split and what reduced it (resist %, armor, damage reduction, dodge chance).");
            IncludeTileEffects = cfg.Bind("1.Tooltip", "IncludeTileEffects", true,
                "When the hovered cell has a ground effect (burning ground, poison cloud...), keep the game's description of it inside our tooltip under 'On this tile' instead of letting it replace the skill tooltip.");
            StyleToggleKey = cfg.Bind("1.Tooltip", "StyleToggleKey", new KeyboardShortcut(UnityEngine.KeyCode.F10), "Press in game to flip Style between Compact and Full. The change applies immediately and is saved to this file.");
            ReloadKey = cfg.Bind("2.General", "ReloadKey", new KeyboardShortcut(UnityEngine.KeyCode.F9), "Press in game to re-read this file.");
            Verbose = cfg.Bind("2.General", "VerboseLogging", false, "Log every tooltip shown (spammy; only for troubleshooting).");
        }

        public void LogSummary(ManualLogSource log)
        {
            log.LogInfo("Target tooltip: " + (Enabled.Value ? Style.Value.ToString() + (ShowOnEmptyCells.Value ? ", incl. empty cells" : ", enemy cells only") : "off"));
        }
    }
}
