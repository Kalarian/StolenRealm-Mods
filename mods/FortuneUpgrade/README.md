# FortuneUpgrade — pay gold to raise a fortune to your level (BepInEx 5)

Vanilla has no way to level a fortune in the campaign except re-earning it on a higher-level island. This plugin adds an upgrade, priced exactly like upgrading a **two-handed weapon of the same rarity** at Noor's.

**Use:** open the Fortune window (in town by default), hover a fortune, and its tooltip shows `Upgrade to L17: 25,920 gold (press U)`. Press **U**, confirm, done. The fortune is raised to your character's level (max 30), re-applied if equipped, saved, and (with SharedFortunes installed) pooled for your other characters.

Config: `BepInEx\config\stolenrealm.fortuneupgrade.cfg` (F9 in game reloads it).

| Section | Key | Default | What it does |
|---|---|---|---|
| `1.Upgrade` | `Enabled` | true | Master switch. |
| `1.Upgrade` | `UpgradeKey` | U | Press while hovering a fortune in the Fortune window. |
| `1.Upgrade` | `OnlyInTown` | true | Only in a town, like item upgrades. Off = anywhere outside battle. |
| `1.Upgrade` | `CostByLevelGap` | true | Charge the price difference between the fortune's level and yours (same rule as the QoL item-upgrade change). Off = full price at your level. |
| `1.Upgrade` | `CostMultiplier` | 1.5 | Markup on the weapon price; 1.5 is the game's item-upgrade markup. |
| `2.General` | `ReloadKey` | F9 | Re-read the config. |
| `2.General` | `VerboseLogging` | false | Log each cost computed and upgrade performed. |

## Cost

The game prices an item as `QuestGold(level) x ItemGoldRatioBase x rarity multiplier x item-type multiplier`, times `TwoHandedGoldModifier` for two-handers, rounded to 5. With the live settings (`QuestGold = level x 100`, base 4, weapon 1.2, two-handed 2) a two-handed weapon costs `level x 960 x rarity` gold, where rarity is Common 1, Uncommon 1.5, Rare 2, Legendary 4, Mythic 8. An upgrade charges `(price at your level - price at the fortune's level) x 1.5`.

Examples with the defaults:

| Fortune | From | To | Cost |
|---|---|---|---|
| Rare | L8 | L17 | 25,920 |
| Legendary | L13 | L17 | 23,040 |
| Mythic | L13 | L17 | 46,080 |
| Mythic | L1 | L30 | 334,080 |

All values are read from the game at runtime, so they follow any balance patch. Lower `CostMultiplier` if that feels steep.

## How it works

Three Harmony patches (`src/Patches/UpgradePatches.cs`): a prefix on `FortuneSlot.ShowTooltip` remembers the hovered slot and parks the Upgrade line; a prefix on `Tooltip.ShowTooltip` appends it; a postfix on `FortuneSlot.HideTooltip` forgets the slot. The plugin's Update watches the key, shows `ConfirmWindow.ShowConfirmMessage`, then charges `ShopMenusManager.SpendGoldAsParty` (the same call item upgrades use) and calls `Character.AddFortune(guid, level)`, which raises the level, re-applies the status and queues the save. The price replicates `GlobalSettings.GetItemPurchasePrice` for a hypothetical two-handed weapon.

## Rebuild

```
cd mods\FortuneUpgrade
dotnet build -c Release
```
