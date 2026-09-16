using System;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace DropRates
{
    internal sealed class DropRatesConfig
    {
        private const string SecWorld = "1.WorldLoot.RarityMultipliers";
        private const string SecCap = "2.WorldLoot.MaxRarityByEnemyType";
        private const string SecPersonal = "3.PersonalLoot";
        private const string SecMerchant = "4.Merchant";
        private const string SecGamble = "5.Gambling";
        private const string SecTown = "5.TownShops";
        private const string SecGeneral = "6.General";

        private static readonly AcceptableValueRange<float> MultRange = new AcceptableValueRange<float>(0f, 100000f);

        // 1. world pool multipliers
        public readonly ConfigEntry<float> MultCommon, MultUncommon, MultRare, MultLegendary, MultMythic;
        public readonly ConfigEntry<bool> AlsoAffectEventRarityWeights;
        public readonly ConfigEntry<float> CapCommon, CapUncommon, CapRare, CapLegendary, CapMythic;
        public readonly ConfigEntry<bool> LeaveRoguelikeUntouched;

        // 2. rarity caps
        public readonly ConfigEntry<ItemQuality> CapFodder, CapSoldier, CapElite, CapChampion, CapBoss;

        // 3. personal loot
        public readonly ConfigEntry<float> PersonalLootMultiplier;
        public readonly ConfigEntry<ItemQuality> PersonalLootMinRarity;
        public readonly ConfigEntry<bool> PersonalLootEquipmentOnly;

        // 2b. summons
        public readonly ConfigEntry<bool> SummonsUseVanillaRolls;

        // 4. merchant
        public readonly ConfigEntry<bool> MerchantEnabled, AffectRoguelikeMerchant;
        public readonly ConfigEntry<float> MerchantLegendary, MerchantMythic;

        // 5. gambling
        public readonly ConfigEntry<bool> GamblingEnabled;
        public readonly ConfigEntry<float> GambleCommon, GambleUncommon, GambleRare, GambleLegendary, GambleMythic;

        // 5b. town shops
        public readonly ConfigEntry<bool> TownShopsEnabled;
        public readonly ConfigEntry<float> TownAct1Rare, TownAct1Legendary, TownAct1Mythic, TownLegendaryPerAct, TownMythicPerAct;

        // 6. general
        public readonly ConfigEntry<KeyboardShortcut> ReloadKey;
        public readonly ConfigEntry<bool> Verbose;

        public DropRatesConfig(ConfigFile cfg)
        {
            MultCommon = cfg.Bind(SecWorld, "Common", 1f, new ConfigDescription("Multiplier on the vanilla per-kill world-pool roll for Common items (vanilla 5%). Effective chance >= 100% is a guaranteed drop.", MultRange));
            MultUncommon = cfg.Bind(SecWorld, "Uncommon", 1f, new ConfigDescription("Multiplier for Uncommon (vanilla 5%).", MultRange));
            MultRare = cfg.Bind(SecWorld, "Rare", 1f, new ConfigDescription("Multiplier for Rare (vanilla 1%).", MultRange));
            MultLegendary = cfg.Bind(SecWorld, "Legendary", 1f, new ConfigDescription("Multiplier for Legendary (vanilla 0.2%).", MultRange));
            MultMythic = cfg.Bind(SecWorld, "Mythic", 1f, new ConfigDescription("Multiplier for Mythic (vanilla 0.05%). Example: 20 => 1% per eligible kill, 2000 => guaranteed.", MultRange));
            AlsoAffectEventRarityWeights = cfg.Bind(SecWorld, "AlsoAffectEventRarityWeights", false,
                "Also apply the multipliers where the same numbers are used as relative weights: island events that give a guaranteed Legendary-or-Mythic item (Heavy Safe, Hold your Breath, Mistress of Chaos) and the Roguelike reward fallback. Vanilla is 20% Mythic there.");

            CapCommon = cfg.Bind(SecWorld, "MaxChanceCommon", 100f, new ConfigDescription("Upper limit on the FINAL per-kill chance (percent) for Common after every game modifier (difficulty loot bonus, Endless battle modifiers, enemy affixes). 100 = no cap.", new AcceptableValueRange<float>(0f, 100f)));
            CapUncommon = cfg.Bind(SecWorld, "MaxChanceUncommon", 100f, new ConfigDescription("Upper limit on the final per-kill chance for Uncommon. 100 = no cap.", new AcceptableValueRange<float>(0f, 100f)));
            CapRare = cfg.Bind(SecWorld, "MaxChanceRare", 100f, new ConfigDescription("Upper limit on the final per-kill chance for Rare. 100 = no cap.", new AcceptableValueRange<float>(0f, 100f)));
            CapLegendary = cfg.Bind(SecWorld, "MaxChanceLegendary", 100f, new ConfigDescription("Upper limit on the final per-kill chance for Legendary. 100 = no cap.", new AcceptableValueRange<float>(0f, 100f)));
            CapMythic = cfg.Bind(SecWorld, "MaxChanceMythic", 100f, new ConfigDescription("Upper limit on the final per-kill chance for Mythic. Keeps Endless (+25% base, up to +300% from battle modifiers) from turning a tuned campaign rate into a near-guarantee. 100 = no cap.", new AcceptableValueRange<float>(0f, 100f)));
            LeaveRoguelikeUntouched = cfg.Bind(SecGeneral, "LeaveRoguelikeUntouched", true,
                "Disable every change made by this mod while playing Roguelike mode. Roguelike builds its reward choices by repeatedly rolling the world pool and keeping the best rarity, so the campaign multipliers would make its rewards mostly Legendary/Mythic.");

            CapFodder = cfg.Bind(SecCap, "Fodder", ItemQuality.Uncommon, "Highest world-pool rarity a Fodder enemy can drop (vanilla Uncommon).");
            CapSoldier = cfg.Bind(SecCap, "Soldier", ItemQuality.Uncommon, "Highest world-pool rarity a Soldier enemy can drop (vanilla Uncommon).");
            CapElite = cfg.Bind(SecCap, "Elite", ItemQuality.Rare, "Highest world-pool rarity an Elite enemy can drop (vanilla Rare).");
            CapChampion = cfg.Bind(SecCap, "Champion", ItemQuality.Legendary, "Highest world-pool rarity a Champion enemy can drop (vanilla Legendary).");
            CapBoss = cfg.Bind(SecCap, "Boss", ItemQuality.Mythic, "Highest world-pool rarity a Boss can drop (vanilla Mythic).");

            SummonsUseVanillaRolls = cfg.Bind(SecCap, "SummonedEnemiesUseVanillaRolls", true,
                "Enemies summoned by other enemies (boss cauldrons, fetishes, totems, clones, mirrors) keep vanilla loot rolls: no rarity cap change, no multipliers, no personal-loot boost. Without this, a boss that spawns 30 destructible props gives 30 mythic rolls.");

            PersonalLootMultiplier = cfg.Bind(SecPersonal, "ChanceMultiplier", 1f, new ConfigDescription(
                "Multiplier on the percent of non-guaranteed enemy personal loot tables and enemy-group loot tables (e.g. Dark Cultist Attendant's 2% Mourning Star). Guaranteed 'pick one' boss tables are unaffected. Island events and gathering are unaffected.", MultRange));

            PersonalLootMinRarity = cfg.Bind(SecPersonal, "OnlyTablesWithRarity", ItemQuality.Legendary,
                "Apply ChanceMultiplier only to loot tables that contain at least one item of this rarity or better. Default Legendary: the named elite/boss drop tables get the boost, the themed enemy-group tables (goblin gear, bone weapons...) stay vanilla. Set Common to boost every table.");
            PersonalLootEquipmentOnly = cfg.Bind(SecPersonal, "EquipmentOnly", true,
                "Only gear (weapon, shield, head, armor, ring, amulet) counts when deciding whether a table qualifies. The game ranks Greater/Super potions as Legendary/Mythic, so without this a potion-only table would get the named-drop boost too.");

            MerchantEnabled = cfg.Bind(SecMerchant, "Enabled", true, "Override the Legendary/Mythic weights of The Merchant's single equipment slot.");
            MerchantLegendary = cfg.Bind(SecMerchant, "LegendaryWeight", 70f, new ConfigDescription("Relative weight for Legendary (vanilla 70).", MultRange));
            MerchantMythic = cfg.Bind(SecMerchant, "MythicWeight", 30f, new ConfigDescription("Relative weight for Mythic (vanilla 30).", MultRange));
            AffectRoguelikeMerchant = cfg.Bind(SecMerchant, "AffectRoguelikeMerchant", true, "Also apply to 'The Merchant (Roguelike)'.");

            GamblingEnabled = cfg.Bind(SecGamble, "Enabled", false, "Override the rarity weights used by gambling (Ulf's Wager).");
            GambleCommon = cfg.Bind(SecGamble, "Common", 25f, new ConfigDescription("Relative weight (vanilla 25).", MultRange));
            GambleUncommon = cfg.Bind(SecGamble, "Uncommon", 50f, new ConfigDescription("Relative weight (vanilla 50).", MultRange));
            GambleRare = cfg.Bind(SecGamble, "Rare", 15f, new ConfigDescription("Relative weight (vanilla 15).", MultRange));
            GambleLegendary = cfg.Bind(SecGamble, "Legendary", 8f, new ConfigDescription("Relative weight (vanilla 8).", MultRange));
            GambleMythic = cfg.Bind(SecGamble, "Mythic", 2f, new ConfigDescription("Relative weight (vanilla 2).", MultRange));

            TownShopsEnabled = cfg.Bind(SecTown, "Enabled", true,
                "The town armorers and jewelers (Zarek's, Gareth's and their counterparts in every act) normally stock only Uncommon 80 / Rare 20 equipment, in every act. With this on they stock Rare, Legendary and Mythic instead, with the weights below rising by act. Island vendors, potion makers and Roguelike are untouched. Mythics need character level 5 (the game's rule); below that a mythic roll becomes a legendary.");
            TownAct1Rare = cfg.Bind(SecTown, "Act1Rare", 94f, new ConfigDescription("Act 1 weight for Rare.", new AcceptableValueRange<float>(0f, 100f)));
            TownAct1Legendary = cfg.Bind(SecTown, "Act1Legendary", 5f, new ConfigDescription("Act 1 weight for Legendary.", new AcceptableValueRange<float>(0f, 100f)));
            TownAct1Mythic = cfg.Bind(SecTown, "Act1Mythic", 1f, new ConfigDescription("Act 1 weight for Mythic.", new AcceptableValueRange<float>(0f, 100f)));
            TownLegendaryPerAct = cfg.Bind(SecTown, "LegendaryPerAct", 5f, new ConfigDescription("Added to the Legendary weight for each act after the first (and taken from Rare).", new AcceptableValueRange<float>(0f, 100f)));
            TownMythicPerAct = cfg.Bind(SecTown, "MythicPerAct", 1f, new ConfigDescription("Added to the Mythic weight for each act after the first (and taken from Rare). Defaults give Act 1 94/5/1, Act 2 88/10/2, Act 3 82/15/3, Act 4 76/20/4.", new AcceptableValueRange<float>(0f, 100f)));

            ReloadKey = cfg.Bind(SecGeneral, "ReloadKey", new KeyboardShortcut(UnityEngine.KeyCode.F9), "Press in game to re-read this file and log the active values.");
            Verbose = cfg.Bind(SecGeneral, "VerboseLogging", false, "Log every patched loot roll (enemy type, rarity list, effective chances, results) to BepInEx/LogOutput.log.");
        }

        public float WorldMult(ItemQuality q)
        {
            switch (q)
            {
                case ItemQuality.Common: return MultCommon.Value;
                case ItemQuality.Uncommon: return MultUncommon.Value;
                case ItemQuality.Rare: return MultRare.Value;
                case ItemQuality.Legendary: return MultLegendary.Value;
                case ItemQuality.Mythic: return MultMythic.Value;
                default: return 1f;
            }
        }

        public float MaxChance(ItemQuality q)
        {
            switch (q)
            {
                case ItemQuality.Common: return CapCommon.Value;
                case ItemQuality.Uncommon: return CapUncommon.Value;
                case ItemQuality.Rare: return CapRare.Value;
                case ItemQuality.Legendary: return CapLegendary.Value;
                case ItemQuality.Mythic: return CapMythic.Value;
                default: return 100f;
            }
        }

        public bool AnyWorldMultNotOne()
        {
            return MultCommon.Value != 1f || MultUncommon.Value != 1f || MultRare.Value != 1f || MultLegendary.Value != 1f || MultMythic.Value != 1f
                || CapCommon.Value < 100f || CapUncommon.Value < 100f || CapRare.Value < 100f || CapLegendary.Value < 100f || CapMythic.Value < 100f;
        }

        /// <summary>True when the mod should do nothing at all (Roguelike mode with LeaveRoguelikeUntouched).</summary>
        public bool Bypass
        {
            get
            {
                if (!LeaveRoguelikeUntouched.Value) return false;
                Burst2Flame.Game g = Burst2Flame.Game.Instance;
                return g != null && g.RoguelikeModeActive;
            }
        }

        public ItemQuality CapFor(EnemyType t)
        {
            switch (t)
            {
                case EnemyType.Fodder: return CapFodder.Value;
                case EnemyType.Soldier: return CapSoldier.Value;
                case EnemyType.Elite: return CapElite.Value;
                case EnemyType.Champion: return CapChampion.Value;
                case EnemyType.Boss: return CapBoss.Value;
                default: return ItemQuality.Uncommon;
            }
        }

        public string TownSummary()
        {
            if (!TownShopsEnabled.Value) return "town shops vanilla";
            var parts = new System.Collections.Generic.List<string>();
            for (int act = 1; act <= 4; act++)
            {
                float r, l, m;
                Patches.TownShopPatches.WeightsFor(act, this, out r, out l, out m);
                parts.Add("A" + act + " " + r + "/" + l + "/" + m);
            }
            return "town shops R/L/M " + string.Join(", ", parts.ToArray());
        }

        public void LogSummary(ManualLogSource log)
        {
            log.LogInfo(TownSummary());
            log.LogInfo(string.Format(
                "World mult C={0} U={1} R={2} L={3} M={4} | max% C={17} U={18} R={19} L={20} M={21} | eventWeights={5} | caps Fodder={6} Soldier={7} Elite={8} Champion={9} Boss={10} | summons vanilla={16} | personal x{11} | merchant {12}/{13} ({14}) | gambling {15} | roguelike untouched={22}",
                MultCommon.Value, MultUncommon.Value, MultRare.Value, MultLegendary.Value, MultMythic.Value,
                AlsoAffectEventRarityWeights.Value,
                CapFodder.Value, CapSoldier.Value, CapElite.Value, CapChampion.Value, CapBoss.Value,
                PersonalLootMultiplier.Value,
                MerchantLegendary.Value, MerchantMythic.Value, MerchantEnabled.Value ? "on" : "off",
                GamblingEnabled.Value ? string.Format("on {0}/{1}/{2}/{3}/{4}", GambleCommon.Value, GambleUncommon.Value, GambleRare.Value, GambleLegendary.Value, GambleMythic.Value) : "off",
                SummonsUseVanillaRolls.Value,
                CapCommon.Value, CapUncommon.Value, CapRare.Value, CapLegendary.Value, CapMythic.Value,
                LeaveRoguelikeUntouched.Value));
        }
    }
}
