using BepInEx.Configuration;
using BepInEx.Logging;

namespace ItemSkillTooltips
{
    internal sealed class ItemSkillTooltipsConfig
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<bool> ShowGrantedSkills;
        public readonly ConfigEntry<bool> ShowNamedSkills;
        public readonly ConfigEntry<int> MaxPanels;
        public readonly ConfigEntry<bool> ItemLevelNumbers;
        public readonly ConfigEntry<KeyboardShortcut> ReloadKey;
        public readonly ConfigEntry<bool> Verbose;

        public ItemSkillTooltipsConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind("1.Tooltips", "Enabled", true,
                "When you hover an item that grants a skill or a passive, show that skill's own tooltip next to the item tooltip (and next to the "
                + "comparison tooltip when one is up). Works in the bag, the character sheet, shops, loot and crafting.");
            ShowGrantedSkills = cfg.Bind("1.Tooltips", "ShowGrantedSkills", true,
                "Skills listed under the item's 'Skills Granted' header (the weapon's basic attack is never shown).");
            ShowNamedSkills = cfg.Bind("1.Tooltips", "ShowNamedSkills", true,
                "Skills the item's Special text names, e.g. 'Grants the passive skill Child of the Abyss' or '10% chance to cast Blinding Light'.");
            MaxPanels = cfg.Bind("1.Tooltips", "MaxPanels", 2, new ConfigDescription("How many skill tooltips to show side by side for one item (1-4).", new AcceptableValueRange<int>(1, 4)));
            ItemLevelNumbers = cfg.Bind("1.Tooltips", "ItemLevelNumbers", true,
                "Show the skill's numbers at the item's level (what you would get from equipping it), as the game does for an equipped item. Off: at your character's level.");
            ReloadKey = cfg.Bind("2.General", "ReloadKey", new KeyboardShortcut(UnityEngine.KeyCode.F9),
                "Re-read this file in game (with the mod window present, F9 opens the window and Apply reloads).");
            Verbose = cfg.Bind("2.General", "VerboseLogging", false, "Log every item hover that produced skill tooltips.");
        }

        public void LogSummary(ManualLogSource log)
        {
            log.LogInfo("Item skill tooltips: " + (Enabled.Value
                ? "on (granted " + (ShowGrantedSkills.Value ? "yes" : "no") + ", named " + (ShowNamedSkills.Value ? "yes" : "no") + ", up to " + MaxPanels.Value + " panels, " + (ItemLevelNumbers.Value ? "item" : "character") + "-level numbers)"
                : "off"));
        }
    }
}
