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
