using BepInEx.Configuration;
using BepInEx.Logging;

namespace RoguelikeQoL
{
    internal sealed class RoguelikeQoLConfig
    {
        public const string DefaultJunk = "Acid Trap, Bear Trap, Below Zero, Cursed Clock, Dwarven Sentry Wall, Enchanted Axe, Eruption!, Fickle Fungus, "
            + "Furnace Trap, Grinder Trap, Lightning Sentry, Mechanical Crusher, Rockslide!, Sandstorm!, Saw Trap, Snowstorm!, Spear Trap, Tornado!, Witch's Cauldron";

        public readonly ConfigEntry<int> FortuneNodeChance;
        public readonly ConfigEntry<bool> ApplyInCampaign;
        public readonly ConfigEntry<string> RemoveEvents;
        public readonly ConfigEntry<bool> RemoveInCampaign;
        public readonly ConfigEntry<float> CurrencyMultiplier;
        public readonly ConfigEntry<float> GoldMultiplier;
        public readonly ConfigEntry<float> RarityBoost;
        public readonly ConfigEntry<bool> BoostBossRewards;
        public readonly ConfigEntry<KeyboardShortcut> ReloadKey;
        public readonly ConfigEntry<bool> Verbose;

        public RoguelikeQoLConfig(ConfigFile cfg)
        {
            FortuneNodeChance = cfg.Bind("1.Fortune", "FortuneNodeChance", 80, new ConfigDescription(
                "Chance (0-100) that an island's main event node gets an event that can lead to a Fortune. The game flips a fair coin (50) before it "
                + "draws the event; this replaces that coin. 100 = every event node leads to a fortune, 50 = vanilla, 0 = never.",
                new AcceptableValueRange<int>(0, 100)));
            ApplyInCampaign = cfg.Bind("1.Fortune", "ApplyInCampaign", false, "Also use FortuneNodeChance for campaign islands (off: Roguelike runs only).");
            RemoveEvents = cfg.Bind("2.Junk", "RemoveEvents", DefaultJunk,
                "Events that never spawn, comma-separated, by their in-game name. Default: the forced-roll hazards with no Leave option that only give "
                + "XP on success and a run-long debuff on a failed roll. Chained and after-battle events are never affected. Empty = remove nothing.");
            RemoveInCampaign = cfg.Bind("2.Junk", "RemoveInCampaign", false, "Also remove them from campaign islands (off: Roguelike runs only).");
            CurrencyMultiplier = cfg.Bind("3.Currency", "CurrencyMultiplier", 1.5f, new ConfigDescription(
                "Multiplier on every Roguelike currency grant (the meta currency for permanent power-ups): the per-battle reward and the run-completion bonus. "
                + "1 = vanilla. Each player's own game applies it, so friends need the mod for their own currency.",
                new AcceptableValueRange<float>(0f, 10f)));
            GoldMultiplier = cfg.Bind("3.Currency", "GoldMultiplier", 1.25f, new ConfigDescription(
                "Multiplier on gold EARNED in Roguelike runs: battle rewards, event gold and gold piles. Selling items is not affected. 1 = vanilla. "
                + "Roguelike only; each player's own game applies it.",
                new AcceptableValueRange<float>(0f, 10f)));
            RarityBoost = cfg.Bind("4.Rarity", "RarityBoost", 2f, new ConfigDescription(
                "Gear offered after a Roguelike battle (the six items, one per slot, you pick one): each rarity step becomes this many times more likely "
                + "relative to the step below. 1 = vanilla (per item: Uncommon 80%, Rare 16%, Legendary 3%, Mythic 1%); 2 = Uncommon 60%, Rare 25%, "
                + "Legendary 10%, Mythic 5%; 3 = 44 / 27 / 17 / 13. Legendary still needs level 3+, Mythic level 5+.",
                new AcceptableValueRange<float>(0.1f, 10f)));
            BoostBossRewards = cfg.Bind("4.Rarity", "BoostBossRewards", true, "Also apply the boost to the gear offered after a Roguelike boss (already skewed high in vanilla).");
            ReloadKey = cfg.Bind("5.General", "ReloadKey", new KeyboardShortcut(UnityEngine.KeyCode.F9),
                "Re-read this file in game (with the mod window present, F9 opens the window and Apply reloads).");
            Verbose = cfg.Bind("5.General", "VerboseLogging", false, "Log every event pick (node, forced-fortune result, chosen event) and every event the junk list blocked.");
        }

        public void LogSummary(ManualLogSource log)
        {
            log.LogInfo("Roguelike events: fortune node chance " + FortuneNodeChance.Value + "%" + (ApplyInCampaign.Value ? " (campaign too)" : " (roguelike only)")
                + "; removed events: " + Patches.EventPatches.JunkCount(this) + (RemoveInCampaign.Value ? " (campaign too)" : " (roguelike only)")
                + "; currency x" + CurrencyMultiplier.Value + "; gold x" + GoldMultiplier.Value + "; rarity boost x" + RarityBoost.Value);
        }
    }
}
