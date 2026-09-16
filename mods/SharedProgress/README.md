# SharedProgress

Campaign progress shared across all your campaign characters, the way LevelSync shares levels and SharedFortunes
shares fortunes. Every character always has the most advanced character's completed quest and town nodes, main-quest
progress (which decides the act), last visited act (which town you load into), highest completed quest level (max
selectable quest level) and shop stock level. Never removes anything. Roguelike characters are ignored unless
`IncludeRoguelikeCharacters`.

## Game facts (build 25240684)

- Per-character fields: `CompletedQuestNodes` (quest AND town node GUIDs, `Observable`), `LastMainQuestLevelCompleted`
  (a quest LEVEL; `QuestManager.CurrentAct` counts main quests with level <= it against `NumMainQuestsToUnlockAct`
  [3,6,9]), `HighestCompletedLevel` (`QuestSelectManager.MaxChoosableLevel`), `ShopHighestLevel` (shop/gambling item
  tier; set alongside HighestCompletedLevel on completion), `LastVisitedActIndex` (plain property, 0..3; the town at
  session start = party max, `GameLogic.cs:1192`), legacy `CompletedMainQuests` / `CurrentMainQuestIndex`.
- The game already unions the party: `QuestSelectManager.MyCompletedQuestNodes`, `QuestManager.CompleteQuest` credits
  every party member, `TownManager.OpenTown` adds the town node and act index to each party member.
- `Character.Load` clamps `LastMainQuestLevelCompleted` to the last main quest's level and derives `ShopHighestLevel`
  from `HighestCompletedLevel` when 0; our Load postfix runs after that, so it clamps itself.
- The vanilla one-time migration `RunQuestMapSaveDataMigration` is skipped for a character that already has nodes;
  harmless, the merge gives the full set.
- "Campaign Completed!" / "New Act Unlocked!" popups are suppressed by the game when a party member already has that
  main-quest level (vanilla veteran behaviour); achievements are only granted on real completions.

## How it works

`ProgressPool` (file `SharedProgress.json` in the save folder): load the pool, seed from every `Character*.json`
(skip `.backup`/`.temp`, deleted, roguelike), save atomically. `ProgressPatches`: `Character.Load(int)` postfix
(priority on the method) offers + merges and queues a force-save for Tick (the game's `Save()` skips non-party
characters); `QuestManager.CompleteQuest` and `TownManager.OpenTown` postfixes mark a sync that Tick performs outside
battle: offer the party's progress, merge into every owned campaign character in `AllMyCharacters`, `SaveNow` the
changed ones. `Eligible` = Owned, not AI, not roguelike (live flag + file flag + mode), and for writes not
`HardcoreDeath` (dead hardcore characters still count as a source).

## Config `stolenrealm.sharedprogress.cfg`

`[1.Sync]` Enabled, ShareQuestMap, ShareActAndShops, IncludeRoguelikeCharacters; `[2.General]` ReloadKey F9 (rescans
saves), VerboseLogging.
