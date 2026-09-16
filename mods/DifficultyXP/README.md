# DifficultyXP — XP scales with difficulty (BepInEx 5)

In vanilla Stolen Realm the harder difficulties give more gold and loot but exactly the same experience. This plugin makes experience gain use the same bonus as gold:

| Difficulty | Gold / Loot bonus (vanilla) | XP bonus with this mod |
|---|---|---|
| Casual, Adventurer | +0% | +0% |
| Classic | +25% | +25% |
| Veteran | +50% | +50% |
| Torturous | +75% | +75% |
| Heart of the Realm | +100% | +100% |
| Endless mode | +25% | +25% |

Applies to battle XP and quest completion XP. Island events that grant XP are also scaled (vanilla never scales those; turn off `ScaleEventXP` if you don't want that). Roguelike XP is "one level per battle won" and is not affected.

Config: `BepInEx\config\stolenrealm.difficultyxp.cfg`

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | true | XP bonus = the active difficulty's gold bonus |
| `ExtraMultiplier` | 1 | Extra multiplier on top. 1 = exactly match gold. |
| `ScaleEventXP` | true | Also scale island-event XP |
| `IncludeGoldBuffs` | true | Temporary gold bonuses on your characters (campfire *Prepared*, +20%) also apply to XP |
| `ReloadKey` | F9 | Reload the config in game |
| `VerboseLogging` | false | Log every XP modifier lookup |

## Multiplayer

Battle XP is calculated on the **host** and sent to everyone, so the host's copy of the mod gives the whole party the battle-XP bonus. Quest completion XP and event XP are calculated on **each player's own PC**, so every player who wants those bonuses needs the mod installed too. Settings do not need to match. Players without the mod simply get vanilla quest/event XP.

## Install

Requires BepInEx 5 in the game folder (already installed on this PC; friends get it from the DropRates install zip or from github.com/BepInEx/BepInEx/releases, `BepInEx_win_x64_5.4.23.x.zip`, extracted next to `Stolen Realm.exe`).

1. Put `stolenrealm.difficultyxp.dll` in `BepInEx\plugins\DifficultyXP\`.
2. Launch the game once; `BepInEx\LogOutput.log` should show `Loading [DifficultyXP 1.0.0]` and `Patched 2 methods`.
3. Edit the cfg if wanted, then press F9 in game.

Uninstall: delete `BepInEx\plugins\DifficultyXP\`.

## Rebuild

```
cd C:\Claude\General\stolen-realm\mods\DifficultyXP
dotnet build -c Release
```
The DLL is copied into `BepInEx\plugins\DifficultyXP\` automatically.

## How it works

- Harmony postfix on the `DifficultySetting.ExperienceMod` getter returns `1 + GoldModifierPerc/100` (times `ExtraMultiplier`), then times `1 + sum of owned characters' GoldMod / 100` when `IncludeGoldBuffs` is on (same sum the game uses in `GameLogic.ApplyGoldModifiers`). Both vanilla XP consumers (`Character.GetExpValue`, `QuestInstance.ExpReward`) read that getter.
- Harmony postfix on `EventOption.InitActions` multiplies each event effect's `modifyExpAmount` by the same modifier.
