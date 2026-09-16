# DropRates — Stolen Realm loot-roll mod (BepInEx 5)

Changes the item drop rolls for **your own** loot in Stolen Realm. Loot is rolled on each player's PC, so this affects your drops in solo and in co-op regardless of who hosts, and never changes anyone else's drops.

Built against the Steam build of 2025-06-17 (Chaos Pack era), Unity 2022.3.10f1 Mono.

## What it can change (all in one config file)

| Section | Keys | Vanilla | What it does |
|---|---|---|---|
| `1.WorldLoot.RarityMultipliers` | `Common`, `Uncommon`, `Rare`, `Legendary`, `Mythic` | 1.0 each | Multiplies the per-kill world-pool roll for that rarity. Vanilla rolls: 5 / 5 / 1 / 0.2 / 0.05 %. `Mythic = 20` → 1% per eligible kill. `Mythic = 2000` → guaranteed. |
| `1.WorldLoot.RarityMultipliers` | `MaxChanceCommon` … `MaxChanceMythic` | 100 (no cap) | Ceiling on the FINAL per-kill chance after the game's own modifiers (difficulty loot bonus, Endless battle modifiers, enemy affixes). Stops Endless stacking from turning a tuned rate into a near-guarantee. |
| `6.General` | `LeaveRoguelikeUntouched` | true | Disables the whole mod while playing Roguelike, whose reward picker would otherwise skew heavily to Legendary/Mythic. |
| `1.WorldLoot.RarityMultipliers` | `AlsoAffectEventRarityWeights` | false | Also skew island events that give a guaranteed Legendary-or-Mythic (vanilla 20% Mythic there). |
| `2.WorldLoot.MaxRarityByEnemyType` | `Fodder`, `Soldier`, `Elite`, `Champion`, `Boss` | Uncommon, Uncommon, Rare, Legendary, Mythic | Highest world-pool rarity each enemy type can drop. Set `Champion = Mythic` to let champions roll mythics. |
| `2.WorldLoot.MaxRarityByEnemyType` | `SummonedEnemiesUseVanillaRolls` | true | Enemies summoned by other enemies (boss cauldrons, fetishes, totems, clones) keep vanilla rolls. Otherwise a boss that spawns 30 props gives 30 mythic rolls. |
| `3.PersonalLoot` | `EquipmentOnly` | true | Only gear counts when judging a table; Greater/Super potions are ranked Legendary/Mythic in the data and would otherwise turn potion tables into boosted tables (seen 2026-09-15: 6 of 14 high-rarity drops were potions). |
| `3.PersonalLoot` | `OnlyTablesWithRarity` | Legendary | The multiplier only applies to loot tables holding at least one item of this rarity or better, i.e. the named elite/boss tables, not the themed group tables (goblin gear, bone weapons). |
| `3.PersonalLoot` | `ChanceMultiplier` | 1.0 | Multiplies the percent on non-guaranteed boss/enemy loot tables (e.g. the 2% Mourning Star). "Pick one" boss tables are unchanged. |
| `4.Merchant` | `Enabled`, `LegendaryWeight`, `MythicWeight`, `AffectRoguelikeMerchant` | on, 70, 30, on | The Merchant's single equipment slot. `0 / 100` = always Mythic. |
| `5.Gambling` | `Enabled`, `Common`…`Mythic` | off, 25/50/15/8/2 | Ulf's Wager rarity weights. |
| `5.TownShops` | `Enabled`, `Act1Rare`, `Act1Legendary`, `Act1Mythic`, `LegendaryPerAct`, `MythicPerAct` | on, 94, 5, 1, 5, 1 | The town armorers and jewelers (one of each per act) stock Rare/Legendary/Mythic instead of the vanilla Uncommon 80 / Rare 20 (Gareth's 70/30) in every act. Weights per act with the defaults: A1 94/5/1, A2 88/10/2, A3 82/15/3, A4 76/20/4. The game's own fallback (drop one rarity and retry) covers slots with no item at your level; mythics need level 5. Island vendors, potion makers, Roguelike untouched. |
| `6.General` | `ReloadKey`, `VerboseLogging` | F9, false | Reload the config in game; log every patched roll. |

Config file: `BepInEx\config\stolenrealm.droprates.cfg` (created on first launch with the mod installed).

## Install (already done on this PC)

1. BepInEx 5.4.23.5 (x64) is extracted into the game folder: `winhttp.dll`, `doorstop_config.ini`, `BepInEx\` next to `Stolen Realm.exe`.
2. The plugin is at `BepInEx\plugins\DropRates\stolenrealm.droprates.dll`.
3. Launch the game once. `BepInEx\LogOutput.log` should contain `Loading [DropRates 1.0.0]` and `Patched 8 methods`. Edit the cfg, then press **F9** in game (or restart).

To uninstall: delete `BepInEx\plugins\DropRates\`. To remove BepInEx entirely, also delete `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` and the `BepInEx\` folder.

Optional console window: in `BepInEx\config\BepInEx.cfg` set `[Logging.Console] Enabled = true`.

## Rebuild after editing the source

```
cd C:\Claude\General\stolen-realm\mods\DropRates
dotnet build -c Release
```
The build copies the DLL into `BepInEx\plugins\DropRates\` automatically. If the game is installed elsewhere: `dotnet build -c Release -p:GamePath="D:\Games\Stolen Realm"`.

## How it works (for future maintenance)

- `Character.GetLootDrop` / `Burst2Flame.CharacterInfo.GetLootDrop` — prefix/finalizer record the dying enemy's type and whether it is a summon (`LootContext`). Summons are left fully vanilla when `SummonedEnemiesUseVanillaRolls` is on.
- `LootTable.GetWorldLoot` — prefix rewrites the allowed-rarity list from the config cap and temporarily swaps `GlobalSettings.UnassignedItemChancePercentage` for a scaled copy; finalizer restores it. Nothing is left modified.
- `LootTable.GetGuaranteedWorldLoot` — same swap, only if `AlsoAffectEventRarityWeights`.
- `LootTable.GetLoot` — prefix scales `chanceModifier` for non-guaranteed tables while an enemy is dropping.
- `ShopManager.RefreshItemDictSingle` + `ShopItemTypeChanceSet.GetRarityChances` — return replacement weights for The Merchant's {Legendary, Mythic} set (`MerchantPatches`) and, for shopkeepers with `assignedAct >= 1` and `shopType` Armorer/Jeweler, the per-act Rare/Legendary/Mythic table (`TownShopPatches`; the `act` argument is `QuestManager.CurrentAct`).
- `GamblingManager.PopulateGamblingItems` — swap/restore `RarityChances`.

After a game update, re-decompile (`decomp/`) and re-check those signatures; each patch group is applied separately and logs its own error if a signature changed.
