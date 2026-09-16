using BepInEx.Configuration;
using BepInEx.Logging;

namespace FortunePreview
{
    internal sealed class FortunePreviewConfig
    {
        public readonly ConfigEntry<bool> QuestTooltip;
        public readonly ConfigEntry<KeyboardShortcut> DetailKey;
        public readonly ConfigEntry<ItemQuality> MinRarity;
        public readonly ConfigEntry<bool> ShowEventNames;
        public readonly ConfigEntry<bool> MarkOwned;
        public readonly ConfigEntry<bool> HideOwned;
        public readonly ConfigEntry<bool> CompactHideAllOwned;
        public readonly ConfigEntry<int> CompactSizePercent;
        public readonly ConfigEntry<int> DetailMaxListed;
        public readonly ConfigEntry<bool> EventWindow;
        public readonly ConfigEntry<bool> RevealHidden;
        public readonly ConfigEntry<bool> RevealRolled;
        public readonly ConfigEntry<bool> ShowChained;
        public readonly ConfigEntry<int> EventSizePercent;
        public readonly ConfigEntry<bool> SourceInTooltip;
        public readonly ConfigEntry<int> SourceMaxQuests;
        public readonly ConfigEntry<bool> ShowUnowned;
        public readonly ConfigEntry<bool> UnownedAtCharacterLevel;
        public readonly ConfigEntry<int> UnownedAlpha;
        public readonly ConfigEntry<bool> SortUnownedByRarity;
        public readonly ConfigEntry<bool> UseDisabledOverlay;
        public readonly ConfigEntry<KeyboardShortcut> ReloadKey;
        public readonly ConfigEntry<bool> Verbose;

        public FortunePreviewConfig(ConfigFile cfg)
        {
            QuestTooltip = cfg.Bind("1.QuestSelect", "Enabled", true,
                "In the campaign quest-select map, the quest tooltip lists every fortune that can be earned on that island: all island events that lead to a fortune and are allowed on the quest's terrain, time of day and level, plus the quest's own scripted events. Default view is compact (grouped by rarity, names only); hold DetailKey for the full list with event names.");
            DetailKey = cfg.Bind("1.QuestSelect", "DetailKey", new KeyboardShortcut(UnityEngine.KeyCode.LeftShift),
                "Hold while hovering a quest to switch the tooltip to the detailed list (one fortune per line, with the granting event, no rarity filter).");
            MinRarity = cfg.Bind("1.QuestSelect", "MinRarity", ItemQuality.Common,
                "Compact view only shows fortunes of this rarity or better (Common, Uncommon, Rare, Legendary, Mythic). The detailed view always shows all.");
            ShowEventNames = cfg.Bind("1.QuestSelect", "ShowEventNames", false,
                "Detailed view: also show the event that grants each fortune next to its name (off by default; the name alone is what matters when choosing a quest).");
            MarkOwned = cfg.Bind("1.QuestSelect", "MarkOwned", true,
                "Mark fortunes your current character already owns with the level they have it at.");
            HideOwned = cfg.Bind("1.QuestSelect", "HideOwned", true,
                "Leave out fortunes your current character already owns at this quest's level or higher (both views).");
            CompactHideAllOwned = cfg.Bind("1.QuestSelect", "CompactHideAllOwned", true,
                "Compact view: leave out every fortune the current character already owns, whatever its level (only what you still need is listed). The detailed view still shows lower-level owned ones as upgrades.");
            CompactSizePercent = cfg.Bind("1.QuestSelect", "CompactSizePercent", 85, new ConfigDescription(
                "Font size of the compact list relative to the tooltip text.", new AcceptableValueRange<int>(60, 100)));
            DetailMaxListed = cfg.Bind("1.QuestSelect", "DetailMaxListed", 40, new ConfigDescription(
                "Detailed view: maximum fortunes listed before '+N more' (keeps a huge tooltip on screen).", new AcceptableValueRange<int>(5, 100)));
            EventWindow = cfg.Bind("2.EventWindow", "Enabled", true,
                "In the island event window, add a 'Fortune:' line under an option's outcome naming the fortune it grants when the game does not already show it. Hover the name for the fortune's full tooltip.");
            RevealHidden = cfg.Bind("2.EventWindow", "RevealHidden", true,
                "Show the fortune even when the option's outcome text is hidden or the roll is a mystery ('Unknown Effect').");
            RevealRolled = cfg.Bind("2.EventWindow", "RevealRolled", false,
                "When an outcome picks one fortune at random from a set, show the one already rolled for this island instead of 'one of' the whole set.");
            ShowChained = cfg.Bind("2.EventWindow", "ShowChained", true,
                "Also list fortunes that come later: from the follow-up event, or from the victory/defeat event of a fight this option starts ('Fortune later: X (after winning)').");
            EventSizePercent = cfg.Bind("2.EventWindow", "SizePercent", 90, new ConfigDescription(
                "Font size of the added line relative to the outcome text.", new AcceptableValueRange<int>(60, 100)));
            SourceInTooltip = cfg.Bind("3.FortuneTooltip", "SourceInTooltip", true,
                "Every fortune tooltip (Fortune window, status links, event options) gets a 'Source' section: the event that grants it, where that event can appear (terrain, time of day, minimum level) and which main quests it can show up on.");
            SourceMaxQuests = cfg.Bind("3.FortuneTooltip", "MaxQuests", 8, new ConfigDescription(
                "Maximum main quests listed per event before '+N more'.", new AcceptableValueRange<int>(1, 40)));
            ShowUnowned = cfg.Bind("4.FortuneWindow", "ShowUnownedFortunes", true,
                "In the Fortune window, list every fortune the character does not own after the owned ones, greyed out. They cannot be equipped; hovering shows the full effect and the Source section, so the window doubles as a catalogue of all 83 fortunes.");
            UnownedAtCharacterLevel = cfg.Bind("4.FortuneWindow", "UnownedAtCharacterLevel", true,
                "Show unowned fortunes' effect numbers at your character's level (what you would get if you found it today). Off = level 1.");
            UnownedAlpha = cfg.Bind("4.FortuneWindow", "UnownedOpacityPercent", 35, new ConfigDescription("How faded the unowned icons are (10 = barely visible, 80 = almost normal).", new AcceptableValueRange<int>(10, 80)));
            SortUnownedByRarity = cfg.Bind("4.FortuneWindow", "SortUnownedByRarity", true, "Order the unowned block mythic first, then by name. Off = by name only.");
            UseDisabledOverlay = cfg.Bind("4.FortuneWindow", "UseDisabledOverlay", true, "Also switch on the slot's own 'disabled' overlay graphic for unowned fortunes. Turn off if it looks wrong.");
            ReloadKey = cfg.Bind("5.General", "ReloadKey", new KeyboardShortcut(UnityEngine.KeyCode.F9), "Press in game to re-read this file.");
            Verbose = cfg.Bind("5.General", "VerboseLogging", false, "Log the events and fortunes resolved for each hovered quest.");
        }

        public void LogSummary(ManualLogSource log)
        {
            log.LogInfo("Fortune preview: quest tooltips " + (QuestTooltip.Value ? "on" : "off")
                + ", compact min rarity " + MinRarity.Value + ", detail key " + DetailKey.Value
                + (MarkOwned.Value ? ", owned marked" : "") + (HideOwned.Value ? ", owned hidden" : "")
                + " | event window " + (EventWindow.Value ? "on" : "off") + (RevealHidden.Value ? ", hidden revealed" : "") + (RevealRolled.Value ? ", rolled revealed" : "") + (ShowChained.Value ? ", chained" : "")
                + " | source in fortune tooltips " + (SourceInTooltip.Value ? "on" : "off")
                + " | unowned in Fortune window " + (ShowUnowned.Value ? "on (" + (UnownedAtCharacterLevel.Value ? "at your level" : "at L1") + ", " + UnownedAlpha.Value + "% opacity)" : "off"));
        }
    }
}
