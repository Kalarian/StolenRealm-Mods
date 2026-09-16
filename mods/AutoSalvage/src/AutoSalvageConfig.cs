using BepInEx.Configuration;
using BepInEx.Logging;

namespace AutoSalvage
{
    internal sealed class AutoSalvageConfig
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<bool> SellCommon;
        public readonly ConfigEntry<bool> SellUncommon;
        public readonly ConfigEntry<bool> SellRare;
        public readonly ConfigEntry<bool> KeepRareAtOrAboveLevel;
        public readonly ConfigEntry<bool> SellCommodities;
        public readonly ConfigEntry<bool> RecipeCommoditiesToStash;
        public readonly ConfigEntry<bool> MaterialsToStash;
        public readonly ConfigEntry<bool> SweepBagsOnLoad;
        public readonly ConfigEntry<bool> ShowSummary;
        public readonly ConfigEntry<bool> LeaveRoguelikeUntouched;
        public readonly ConfigEntry<KeyboardShortcut> ReloadKey;
        public readonly ConfigEntry<bool> Verbose;

        public AutoSalvageConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind("1.Rules", "Enabled", true,
                "Whenever loot lands in your bags (battle loot, chests, event rewards), junk is sold on the spot at the shop sell price and materials go to your storage. Equipped items are never touched; Legendary and Mythic equipment is always kept; consumables, tools and quest items are ignored.");
            SellCommon = cfg.Bind("1.Rules", "SellCommonEquipment", true, "Sell Common (white) weapons, shields, head, armor, rings and amulets.");
            SellUncommon = cfg.Bind("1.Rules", "SellUncommonEquipment", true, "Sell Uncommon (green) equipment.");
            SellRare = cfg.Bind("1.Rules", "SellRareEquipment", true, "Sell Rare (blue) equipment.");
            KeepRareAtOrAboveLevel = cfg.Bind("1.Rules", "KeepRareAtOrAboveMyLevel", false,
                "Exception: keep a Rare whose item level is at or above the character's level (only matters when SellRareEquipment is on).");
            SellCommodities = cfg.Bind("1.Rules", "SellCommodities", true, "Sell commodities of any rarity.");
            RecipeCommoditiesToStash = cfg.Bind("1.Rules", "RecipeCommoditiesToStash", true,
                "Commodities that are an ingredient in any crafting recipe (Animal Hide, Fire Core, Wild Soul, drake scales, Cursed Coin...) go to storage instead of being sold. 25 recipes use 16 commodity types (Animal Hide, Fire Core, Living Stone, Bear Claw, Enchanted Bark, Wild Soul, Wolf Pelt, the three drake scales, Fox Pelt, Rare Blue Feather, Cursed Coin, Cursed Idol, Particle of Light, Ectoplasm); the other 82 commodities are pure vendor trash.");
            MaterialsToStash = cfg.Bind("1.Rules", "MaterialsToStash", true, "Send crafting materials straight to storage. Materials are never sold.");
            SweepBagsOnLoad = cfg.Bind("1.Rules", "SweepBagsOnLoad", true,
                "Also apply the rules to what is already in a character's bags when the character loads (unequipped items only). Handy the first time; harmless afterwards.");
            ShowSummary = cfg.Bind("2.Feedback", "OverheadSummary", true, "Show '+340 gold, sold 4, stashed 2' over the character after each haul.");
            LeaveRoguelikeUntouched = cfg.Bind("3.Safety", "LeaveRoguelikeUntouched", true, "Do nothing in Roguelike mode.");
            ReloadKey = cfg.Bind("4.General", "ReloadKey", new KeyboardShortcut(UnityEngine.KeyCode.F9), "Press in game to re-read this file.");
            Verbose = cfg.Bind("4.General", "VerboseLogging", false, "Log every item sold or stashed.");
        }

        public void LogSummary(ManualLogSource log)
        {
            log.LogInfo("Auto-salvage: " + (Enabled.Value ? "on" : "off") + " | sell equipment: "
                + (SellCommon.Value ? "Common " : "") + (SellUncommon.Value ? "Uncommon " : "") + (SellRare.Value ? "Rare" + (KeepRareAtOrAboveLevel.Value ? "(<level)" : "") : "")
                + " | commodities " + (SellCommodities.Value ? "sold" + (RecipeCommoditiesToStash.Value ? " (recipe ones stashed)" : "") : "kept") + " | materials " + (MaterialsToStash.Value ? "to stash" : "kept")
                + (SweepBagsOnLoad.Value ? " | sweep on load" : "") + (LeaveRoguelikeUntouched.Value ? " | roguelike untouched" : ""));
        }
    }
}
