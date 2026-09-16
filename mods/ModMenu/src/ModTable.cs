using System.Collections.Generic;

namespace ModMenu
{
    /// <summary>What the window says about each plugin. Keys are the switchboard names (MasterConfig.AllPlugins).
    /// RestartRequired: set to true for a mod that cannot be switched live; today every mod applies and removes its
    /// patches on the spot, so none is flagged. Effects a mod has already applied (levels, gold, fortunes) stay.</summary>
    internal static class ModTable
    {
        internal sealed class Entry { public string Title; public string Description; public bool RestartRequired; }

        private static readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>
        {
            { "DropRates", new Entry { Title = "Better loot", Description = "Fewer greys and greens, more rares, legendaries and mythics; town armorers and jewelers sell rare or better." } },
            { "DifficultyXP", new Entry { Title = "XP that scales", Description = "Harder difficulties give more experience, at the same rate they give more gold." } },
            { "QoL", new Entry { Title = "Cheaper upgrades", Description = "Upgrading an item at Noor's only charges the level difference." } },
            { "TargetTooltip", new Entry { Title = "\"What will this click do?\"", Description = "In battle, hover an enemy or target cell to see the attack a click fires and its damage. F10 flips the style." } },
            { "SpecialTooltips", new Entry { Title = "Enemy specials explained", Description = "The Examine window's Special entries (Armored, Unpredictable...) get tooltips." } },
            { "ScalingTooltips", new Entry { Title = "Skill scaling shown", Description = "Every damage number in a skill tooltip says where it comes from." } },
            { "SharedFortunes", new Entry { Title = "Fortunes shared", Description = "Fortunes are shared across your characters. Fortunes already granted stay if you switch this off." } },
            { "FortunePreview", new Entry { Title = "Fortune hunting", Description = "Quest hover lists the fortunes you can still earn there; the Fortune window shows every fortune, unowned ones greyed out." } },
            { "FortuneUpgrade", new Entry { Title = "Fortune upgrades", Description = "In town, hover a fortune and press U to raise it to your level for gold." } },
            { "LevelSync", new Entry { Title = "Levels shared", Description = "Every character stays at your highest character's level. Levels already granted stay if you switch this off." } },
            { "AutoSalvage", new Entry { Title = "Junk auto-sold", Description = "White, green and blue drops and trade commodities are sold as they drop; crafting materials go to storage." } },
            { "SharedGold", new Entry { Title = "One gold purse", Description = "All your campaign characters share one purse. Gold already pooled stays where it is if you switch this off." } },
            { "BattleStats", new Entry { Title = "Battle stats that add up", Description = "The post-battle Stats window gains a damage breakdown, ticks and tiles, elements, crits, kills and more." } },
            { "ThreatOverlay", new Entry { Title = "Threat overlay", Description = "Hold Left Alt in battle: red where enemies can move next turn, orange where they can hit." } },
            { "SharedProgress", new Entry { Title = "Campaign progress shared", Description = "Every character has your most advanced character's quest map, act and shop level. Progress already granted stays if you switch this off." } },
            { "NumberFormat", new Entry { Title = "Thousands separators", Description = "Every number the game shows gets thousands separators: 12345 becomes 12,345." } },
            { "ModMenu", new Entry { Title = "Mod menu (this window)", Description = "F9 opens this window. If it is off, F9 reloads the config files directly instead." } },
        };

        public static Entry Get(string name)
        {
            Entry e;
            return _entries.TryGetValue(name, out e) ? e : new Entry { Title = name, Description = "" };
        }
    }
}
