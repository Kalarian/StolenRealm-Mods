using BepInEx.Configuration;
using BepInEx.Logging;

namespace ScalingTooltips
{
    public enum LabelStyle
    {
        Short,
        Full
    }

    internal sealed class ScalingTooltipsConfig
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<LabelStyle> Style;
        public readonly ConfigEntry<bool> Breakdown;
        public readonly ConfigEntry<string> Color;
        public readonly ConfigEntry<int> SizePercent;
        public readonly ConfigEntry<bool> ShowDamageType;
        public readonly ConfigEntry<bool> ShowRawFormula;
        public readonly ConfigEntry<KeyboardShortcut> ReloadKey;
        public readonly ConfigEntry<bool> Verbose;

        public ScalingTooltipsConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind("1.Label", "Enabled", true,
                "After every damage/heal number in a skill, action or status tooltip, add the scaling it came from, e.g. '118-159 (0.7x AP)'. Works in skill trees, the action bar, Examine and text links.");
            Style = cfg.Bind("1.Label", "Style", LabelStyle.Short,
                "Short = '118-159 (0.7x AP)'. Full = '118-159 (70% Attack Power)'. AP = Attack Power (weapon-based), SP = Spell Power.");
            Breakdown = cfg.Bind("1.Label", "Breakdown", true,
                "Add a 'Scaling:' line under the description showing the whole chain for your character: factor x stat value x every percentage modifier you have (Power, school damage, mana power, basic-attack, Might) + flat damage = result. Modifiers at 0 are left out.");
            Color = cfg.Bind("1.Label", "Color", "9AA5B1", "Hex colour (no #) of the scaling label.");
            SizePercent = cfg.Bind("1.Label", "SizePercent", 80, new ConfigDescription("Label font size as a percentage of the description text.", new AcceptableValueRange<int>(50, 120)));
            ShowDamageType = cfg.Bind("1.Label", "ShowDamageType", true,
                "Append the damage school when the formula names one, e.g. '(50% Spell Power, Fire)'. Physical on Attack Power skills is implied and not shown.");
            ShowRawFormula = cfg.Bind("1.Label", "ShowRawFormula", false,
                "For the few formulas that are not 'stat x factor' (flat values, level scaling, armor multiples), show the cleaned-up formula instead of nothing.");
            ReloadKey = cfg.Bind("2.General", "ReloadKey", new KeyboardShortcut(UnityEngine.KeyCode.F9), "Press in game to re-read this file.");
            Verbose = cfg.Bind("2.General", "VerboseLogging", false, "Log every formula parsed (spammy).");
        }

        public void LogSummary(ManualLogSource log)
        {
            log.LogInfo("Scaling labels: " + (Enabled.Value ? "on, " + Style.Value + (Breakdown.Value ? " + breakdown" : "") + ", #" + Color.Value + " " + SizePercent.Value + "%" + (ShowRawFormula.Value ? ", raw formulas" : "") : "off"));
        }
    }
}
