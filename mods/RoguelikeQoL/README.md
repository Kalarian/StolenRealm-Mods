# RoguelikeQoL

Roguelike quality of life: more island events lead to a Fortune, the forced-roll hazard events are gone, the meta currency comes in faster, gold earned is boosted, and the gear offered after a battle rolls higher rarities.
Master-config plugin: `RoguelikeQoL = true` in `BepInEx\config\stolenrealm.mods.cfg`, a row in the F9 mod window,
own settings in `stolenrealm.roguelikeqol.cfg`. In co-op the HOST generates the islands, so the host's settings decide.

## What the game does (build 25240684)

- An island's main event is drawn in `WorldMapGenerator.GetRandomEvent`. For a main Event node the game first flips a
  fair coin (`rand.NextDouble() >= 0.5`) and then only allows events whose `LeadsToFortune` matches the flip, so exactly
  half of all event nodes lead to a fortune, whatever the terrain or level (both halves of the pool are never empty).
  Nodes flagged `restrictEventsWithBattle` (a navigation override) never get fortune events.
- Of the 48 fortune-leading events about half are guaranteed picks (shrines, monoliths, Mana Spring), about a third
  need a battle won first, and 8 need a d20 + stat bonus roll of 10-18.
- The deck (`DeckRandomizer.WeightedRandom`) sorts candidates by recency, drops the most recently used third, weights
  the next third down, then draws by `ChanceRatio` (1.0 for every event-node event). History is seeded from
  `GlobalSaveData.LastVisitedEvents` and persists across runs within a session.
- Every event debuff is `PersistentDurationType.Quest`; only the campaign town clears Quest statuses. A Roguelike run is
  one quest, so a failed roll's debuff lasts the run.
- Lesser (minor) event slots use a separate 9-event pool (runes, chests, gold) with no fortunes; untouched here.

## What the mod does

1. **FortuneNodeChance** (default 80, vanilla 50): a prefix on `GetRandomEvent` with `ref System.Random rand` swaps the
   caller's fresh `System.Random` for `BiasedRandom`, whose FIRST `NextDouble()` answers 0.75 (fortune) or 0.25 (not)
   according to the configured chance and which delegates every other call to the wrapped instance. The coin flip is the
   first use of `rand` in the method, so the rest of the draw (deck history, weights, fallbacks) is vanilla. Only for
   `NodeType.Event`, not minor, not battle-restricted. A verbose postfix logs every pick:
   `Event node Event: forced fortune=True (roll 23 vs 80%) -> 'Shrine of the Rogue' [fortune]`.
2. **RemoveEvents**: a prefix on all three `PartyEvent.CanSpawn` overloads returns false for events whose `name` or
   `EventNameOverride` is in the list (case-insensitive), so they are excluded from the draw and from the fallback
   passes. Default list = the 19 forced-roll hazards with no Leave option, XP-only reward and a run-long debuff (plus
   20-30% max health) on a failed roll of 10-12: Acid Trap, Bear Trap, Below Zero, Cursed Clock, Dwarven Sentry Wall,
   Enchanted Axe, Eruption!, Fickle Fungus, Furnace Trap, Grinder Trap, Lightning Sentry, Mechanical Crusher,
   Rockslide!, Sandstorm!, Saw Trap, Snowstorm!, Spear Trap, Tornado!, Witch's Cauldron. Deliberately not listed:
   Heavy Safe, Hold your Breath, Stone Covered Grave, The Old Well, Unidentified Remains (real rewards or a Leave
   option), Goblin Scout Tower (fail = a battle), Shawn the Benevolent, Ogre Debt Collector, Party like a Pirate.
   The event names are the asset names (same as shown in game); chained and after-battle events are never in the list.

Both apply only while `Game.Instance.RoguelikeModeActive` unless the campaign switches are on.

5. **RarityBoost** (default 2): after every battle each character levels and `RoguelikeManager.GetItemChoices(level,
   recipient, boss)` rolls the six offers (one per equipment slot; the player keeps one). Non-boss: rarities limited to
   Uncommon..Mythic, up to 21 iterations of `LootTable.GetWorldLoot` (independent per-rarity rolls at
   `GlobalSettings.UnassignedItemChancePercentage`: Uncommon 5, Rare 1, Legendary 0.2, Mythic 0.05 percent), keep the
   best of the first non-empty iteration, else `GetGuaranteedWorldLoot` (weighted by the same numbers). Boss offers:
   `CharacterInfo.GetLootDrop` x20, top 6. No difficulty/world/level multiplier exists on this path (Roguelike
   difficulties have LootModifierPerc 0; the Tier chance nodes are skill tiers). A prefix swaps that table for a scaled
   copy for the duration of the call (chance x B^(rarity-1): Uncommon unchanged, Rare xB, Legendary xB^2, Mythic xB^3,
   capped at 100), a finalizer restores it (same shape as DropRates' WorldLootPatches; DropRates itself stays out of
   Roguelike via LeaveRoguelikeUntouched, so they never stack by default). Per offered item at level 5+:

   | RarityBoost | Uncommon | Rare | Legendary | Mythic | at least one Legendary+ among the 6 |
   |---|---|---|---|---|---|
   | 1 (vanilla) | 79.9 | 16.1 | 3.2 | 0.8 | 22% |
   | 2 (default) | 60.5 | 24.6 | 9.9 | 5.0 | 62% |
   | 3 | 43.9 | 27.1 | 16.5 | 12.5 | 87% |

   Legendary still needs character level 3+, Mythic 5+ (pool filter after the roll). `BoostBossRewards` includes the
   boss chooser. Chest events and the Merchant keep their fixed rarity lists.

3. **CurrencyMultiplier** (default 1.5): every meta-currency grant goes through `GlobalSaveData.ModifyRoguelikeCurrency`
   (per-battle reward = CurrencyPerLevelNodes x CurrencyDifficultyMultiplierNodes, ceil'd, applied locally on every
   client from the battle result; plus the 1200 run-completion bonus in `Root.SendRoguelikeComplete`). A prefix scales
   positive amounts (`Mathf.Ceil(amount x mult)`); spending never uses that method. The level-up screen's "currency
   earned" number (`RoguelikeManager.AddToSkillSelectionQueue currencyEarned`) is scaled the same way so what you see is
   what you get. Each player's own game applies the grant, so every co-op member needs the mod for their own currency.
4. **GoldMultiplier** (default 1.25), two hooks gated on `RoguelikeModeActive`:
   - Battle gold: postfix on `GameLogic.GetTotalGoldValue` (host only; already x5 in Roguelike via
     `RoguelikeSettings.GoldMultiplier`). The host sends the result to every client with the battle outcome, so the
     post-battle window's printed number and the grant agree for everyone in the lobby, mod or not.
   - Event gold and gold piles: postfix on `GameLogic.ApplyGoldModifiers(originalGold)`, the helper under every
     earned-gold path (`EventWindow` events, `AdventureRewards` quest gold, and also the post-battle grant, which is
     skipped while `PostBattleManager.IsNotNullAndIsActive` because it was scaled at the source). Selling, trading and
     buy-backs never call it. The event option text still prints the base amount (vanilla does the same for the GoldMod
     buff); the gold actually given is the boosted number.

## Settings

| Key | Default | Meaning |
|---|---|---|
| FortuneNodeChance | 80 | % of main event nodes forced to a fortune-leading event (50 = vanilla, 100 = all). |
| ApplyInCampaign | false | Also on campaign islands. |
| RemoveEvents | 19 hazards | Comma-separated event names that never spawn. Empty = none. |
| RemoveInCampaign | false | Also on campaign islands. |
| CurrencyMultiplier | 1.5 | Multiplier on every Roguelike currency grant (battle reward and run bonus). 1 = vanilla. |
| GoldMultiplier | 1.25 | Multiplier on gold earned in Roguelike (battles, events, piles; not selling). 1 = vanilla. |
| RarityBoost | 2 | Each rarity step of post-battle gear offers this many times more likely than the step below. 1 = vanilla. |
| BoostBossRewards | true | Boost the boss chooser too. |
| ReloadKey / VerboseLogging | F9 / false (live true) | Re-read the file; log every pick and every blocked event. |

Data used for the defaults: scratch scripts over `data/PartyEvent.json` + `data/EventStatus.json` (see `docs` in the
plan file `in-roguelike-mode-we-ancient-cook.md`).
