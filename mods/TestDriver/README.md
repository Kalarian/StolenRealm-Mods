# TestDriver — automated smoke test (development only, never shipped)

A BepInEx plugin that is inert unless the game is launched with `-srtest`. With it, the plugin plays the game by itself:
loads, starts a single-player campaign session with the first N campaign characters (`-srparty N`, default 3), opens the
Fortune window in town and counts its slots, picks the lowest-level available quest, lands on the island, starts the
game's built-in debug test battle (`GlobalSettings.testBattle`, "BattleTest": 4 enemies), accepts the placement phase,
forces the threat overlay on for a moment, plays the party with a deliberately dumb policy (real harmful skills first,
then the weapon, one move toward the nearest enemy, then end turn; enemies use the game's own AI), waits for the
post-battle screen, writes `BepInEx\TestDriver\result.txt` (PASS/FAIL + step log) and quits. Every step logs
`TESTDRIVER: ...`. `-srai` hands the party to the game's AI instead (it only casts free actions for players).

Run it with `bash tools/run_test_battle.sh [headless|windowed] [partysize]`: backs up the save folder, launches with
`-srtest`, restores the saves afterwards (the battle changes XP, gold, items), prints the result and the recorder's
battle summary. Headless (`-batchmode -nographics`) works for the whole flow.

## Build-test flags (2026-09-17)

- `-srdifficulty N` sets `Root.CurrentDifficultyIndex` before the quest (3 = Veteran).
- `-srseed N` calls `UnityEngine.Random.InitState(N)` at battle start and sets `BattleInfo.RandomSeed`.
- `-srscenario pack|elite|boss` clones the debug battle asset and fills it from the island's enemy groups at the quest level (`Scenario.cs`): pack 12 points normal mix, elite 22 points Elite/Champion, boss one super-unique Champion + 22 points of escorts; the game's own affix roll (`GetEnemyModsForBattle`) is applied.
- `-srspeed N` sets `Time.timeScale` (1..20) every frame while the driver is armed, so animations, the game's waits and enemy turns run N times faster; the driver's own act tick shrinks to match. Only exists inside the dev-only driver, so normal play is never affected. `run_build_test.py --speed` (default 4).
- `-srrotation <file>` plays the party from a priority list (`Rotation.cs`; format and condition vocabulary in the file header). Casts go through `PlayerMovement.ExecuteAction`, the game's own path, so costs, cooldowns, charges and Recharge apply; `Character.PerformAction` alone pays nothing and lets a free action be spammed forever.
- `-srresetruns` - after every fight, tell BattleStats the run ended (the same as leaving town), so each fight becomes its own saved run. Test-only, for checking the run history.
- Every rotation decision is written to result.txt as `ROTATION T<turn> <name>: <skill> -> <target>`; the `SCENARIO ...` line lists the spawned enemies with affixes; `battle over ... party alive N` gives the outcome.
- `-srfights pack:11,elite:22,...` plays several fights in ONE launch. After each fight the driver writes `BepInEx\TestDriver\fights\<scenario>-s<seed>.txt` (that fight's lines + rotation trace), closes the stats window, then re-activates the quest (`PortalManager.ClearCurrentIsland` + `QuestManager.ActivateQuest(skipPortalEffect: true)`, what the defeat window's Retry does) for a fresh island, waits for `IslandGenerationInProgress` to clear, heals the party (`ResetCharacter`, full health/mana, IsDead=false) and starts the next fight. Facts that cost a day: (1) `PostBattleManager.OpenIsland` on the same island leaves `GameLogic.SetupGame` throwing a NullReferenceException because `PortalManager.currentEventHex` is null after a won fight, so every fight is now anchored on the party leader's cell and `Root.BattleObjectCharacter` is cleared; (2) the game switches Unity's logger off, so coroutine exceptions never reach LogOutput.log; the driver sets `Debug.unityLogger.logEnabled = true` and echoes errors as `UNITY ...` lines while armed; (3) the post-battle flow leaves `Time.timeScale` at 0 until a button is pressed, so the driver re-applies the time scale every frame; (4) `-srseed` also replaces the private static `System.Random` in `ListExtensions` (used by `List.Shuffle()`, i.e. the enemy pool) and `WorldMapGenerator`, otherwise the same seed spawns different enemies.
- `-srsaves <dir>` redirects every save and pool file: the game and all our mods read paths through `FileSystem.persistentDataPath`, which prefers a private cached field (`_cachedPersistentDataPath`); the driver sets it in Awake. Combined with the instance folders from `tools/make_instances.py` this lets several copies run at once.
- Driven by `tools/run_build_test.py`, which handles the save backup/restore and collects BattleStats JSON per fight.
- Enemy level = `Game.Instance.CurrentActiveGameLevel` = the active quest's level; with LevelSync the lowest available quest sits at the roster level, so a level-30 test character meets level-30 enemies.

## Game facts learned while building it

- `MainMenu.StartSinglePlayer(false)` opens character choice; a party is formed by `Root.AddToCharacterList(c)`,
  `IsNetworkLoaded = true`, `OwnerID = 0`, `SelectedForBattle = true`, then `GameLogic.AcceptCharacterChoices()` → town.
- Quest start without UI: `QuestSelectManager.SelectedQuestNode = node; Root.SetLastAcceptedQuest(node.CurrentQuestInstance); QuestManager.ActivateQuest()` → island (`GUIState.InWorldMap`, `WorldMapGenerator.instance.CurrentRoom`).
- `GameLogic.StartTestBattle()` is buggy (calls `SendOpenBattle` before setting `CurrentBattle`); set `CurrentBattle` first, then `Root.SendOpenBattle()`. Battles need an island room (`CurrentRoom.Hexes`), not town.
- The placement phase waits until every party member has `EndedTurn = true`; then `InitBattleAndStart` → `BattleCurrentlyActive`.
- `Character.PerformAction` does not pay costs: the UI pays in `PlayerMovement` (`Character["ActionPoints"] -= cost; ActionPointsUsedThisTurn += cost`, mana likewise).
- The turn advances by itself once every living party member has `EndedTurn` (`PlayerMovement.EndTurn()`).
- `AiControlledPlayer` is a saved-map attribute (`AddAttribute`, like gold); the game's AI then plays the character, but for players it only used free actions in the test.
- `WaitForEndOfFrame` coroutines run fine in `-batchmode -nographics` for this game.
