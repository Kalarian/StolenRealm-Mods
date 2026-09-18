# BattleStats

Adds the missing numbers to the game's post-battle **Stats** window and records every battle in the log.

The game already credits damage dealt, taken, blocked and healed per character (`StatManager.ModifyBattleStat`, host
side, replicated to everyone through `Root.BattleStats`). It does include damage-over-time ticks in Damage Dealt, but
it does not say where the damage came from. This mod wraps the callers that give a credit its meaning and writes extra
numbers into the same replicated dictionary under keys the game never uses (1000+), then appends rows to the window.

## What the window gains

| Section | Rows |
|---|---|
| Damage Breakdown (header = total, should equal the game's Total Damage) | Direct Hits, Over Time (poison, burn, bleed... ticks), Ground Tiles (on-enter and turn-start tile actions), Summons, Thorns |
| Hits | Hits / Crits (crit rate), Kills / Overkill, Biggest Hit (with the skill), Best Skill (name + total), Damage Per Turn |
| Damage By Element | Physical, Fire, Cold, Lightning, Shadow, Holy, Untyped (Damage Dealt minus the typed rows: a self-check) |
| Activity | Casts (free actions in brackets), Mana Spent, Hexes Moved, Taken From Ticks |

The window is a fixed panel with no scroll view of its own (Content > Title, Character Name Holder, Stat Line Holder
and Stat Value Holder stretched with a -141/-144 px vertical offset, Close button at the bottom; see the dump comment
in `StatsWindowPatches.cs`). The mod builds one: a masked viewport (RectMask2D + ScrollRect) between the headers and
the Close button, a top-anchored content whose height follows the line holder's End Separator (from Tick), both holders
reparented into it with their original offsets, a slim scrollbar on the right and the game's ScrollViewExtended for
gamepad sticks. Reset() moves the holders back. Hover a character's name at the top of the window: a tooltip lists their top damage
sources (skill or status, total, number of hits). Hover any damage cell (the game's Damage Dealt / Summon / Returned
rows, Total Damage, and every one of our damage rows) for the abilities behind that number for that character.
Each section can be switched off in the config.

## How it works

- `RecorderPatches`: a path stack around `ApplyAction(ActionStatus)` (status tick), `GroundEffect.ExecuteActionOnEnter`,
  `GameLogic.StartNewTurn`, `GameLogic.AddGroundEffect(ActionInfo, ...)`; the innermost `Character.ApplyAction(source,
  target, effects, properties, ...)` prefix records the action, target and target health (kill/overkill); `ModifyHealth`
  prefixes record the damage type per target (element); `ProcessSkillTriggers(OnCrit)` marks crits; `ProcessDeath` postfix
  confirms kills (only when `IsDead` flipped: Dark Ritual and death triggers can save a character at 0 health; the death
  runs from the health setter before the credit, so the kill is attached to the credit that follows); `ExecutePerformAction` records casts and mana; `ProcessAttributesOnNewTurn` samples the game's
  `MovementThisTurn` counter and counts turns. Battle start = `StatManager.ClearStats`, end =
  `PostBattleManager.OpenPostBattleMenu` (log summary, optional JSON).
- `SharedStats`: the replicated keys. Skills are identified by index into `Game.Instance.Actions` (statuses: index into
  `Game.Instance.ActionStatuses` + 100000), the same on every machine, so names resolve on clients.
- `StatsWindowPatches`: `StatManager.BuildOutLabels` postfix appends label + value rows with the window's own prefabs,
  sizes and colours; `GetStatDisplay` postfix appends the matching strings in the same order (the game fills the grid by
  index); a `PopulateStats` prefix forces a rebuild when the wanted rows change (F9, mod switched on later). If the host
  has no mod the rows show `n/a`.

Multiplayer: only the host records (damage resolves there); everyone with the mod sees the same numbers. Roguelike is
not treated specially (the window is the same).

## Config `stolenrealm.battlestats.cfg`

- `[1.Recording]` Enabled, LogEveryHit (one log line per HIT/HEAL/BLOCK/KILL/CAST), WriteJson (`BepInEx\BattleStats\battle-*.json`), TopSkills.
- `[2.StatsWindow]` ShowInWindow, ShowBreakdown, ShowHits, ShowElements, ShowActivity, TopSkillsTooltip, CellTooltips.
- `[3.RunStats]` RunStatsButton (the on-screen button), RunStatsButtonGap, RunStatsKey F8, IncludeCurrentBattle, RoguelikeAreaResets, SaveRuns, KeepRuns, RunHistoryKey F7, ResumeRuns.
- `[4.General]` ReloadKey F9, VerboseLogging, DebugHooks (path-stack spam, off; also dumps the Stats window and the HUD button row once).

Verified with the automated driver (`tools/run_test_battle.sh headless 6 Test1,...,Test6 Statue`): the breakdown rows sum
to the game's Damage Dealt for all six test builds; the driver logs every window row as `STATS row N: label | v1 | ...`.

## Poison

`GL-Poisoned` deals no damage itself: each stack raises the victim's `PoisonDamage` attribute (max(1, 0.1 x the poisoner's
Shadow spell power) per stack) and the global `Game.Instance.PoisonTickAction` is performed by the victim on itself each
turn, so the game's `ModifyBattleStat` credits the victim (dropped for enemies; a poisoned player is credited with their own
Damage Dealt). The recorder intercepts that credit (postfix runs even when the game returned early), takes back a player's
self-credit, and re-credits the amount through `StatManager.ModifyBattleStat` to the sources of the Poisoned stacks on the
victim in proportion to stacks x per-stack damage (summons credit their master as SummonDamageDealt). Those re-credits
record as `StatusTick: Poisoned`, so they land in Over Time, the game's own Damage Dealt row, and `Poisoned (tick)` in the
top-skills tooltip. The Bad Bloom is a plain ground effect (Source = caster) whose hidden status applies 2 Poisoned stacks
per turn to enemies in range.

## Per-cell breakdown

Every damage credit is also written per (bucket, source) into the replicated dictionary: key = 2,000,000 + bucket x 200,000 +
source code (damage) and 6,000,000 + ... (hit count). Buckets: 0 Direct, 1 Ticks, 2 Tiles, 3 Summons, 4 Thorns, 10 + DamageType
for elements, 20 Untyped, 98 Damage Dealt, 99 everything. `SharedStats.Breakdown(character, bucket)` reads them on any
machine; `StatsWindowPatches.HookCells` puts a `CellHover` on each value cell (game rows mapped by BattleStat, ours by
`Row.Bucket`) and makes the cell a raycast target.

## Run stats (the whole run, not one battle)

The post-battle page only ever shows the fight that just ended. A **RUN** button next to the game's Battle Log button on
the main HUD (town, island and battle; config `RunStatsButton`) and the `RunStatsKey` (F8) open **the same Stats window
with the totals of every battle since the party left town**. Nothing about the window changes except the numbers and the
title (`Run Stats - N battles`, `+ current` when opened during a fight): the game's own rows, the mod's rows and both
hover tooltips all read the run store instead of the live battle dictionary.

- Store: `src/RunStats.cs`, one `Dictionary<int,float>` per character (keyed by owner id + name, because the game resets
  `Character` objects on town return) mirroring the whole key space of `Root.BattleStats`. Every key is a **sum** except
  the Biggest Hit pair (max, copied together) and the derived keys (Best Skill, the top-8 list), which are recomputed
  from the summed per-source Damage Dealt bucket after each fold. The 12 vanilla `BattleStat` values are plain
  accumulators in the game, so summing them is exact.
- Fold: once per battle end, from the recorder's `PostBattleManager.OpenPostBattleMenu` prefix after the recorder wrote
  its last numbers (`RunStatsPatches.OnBattleEnded`). The host folds at once; a client's replicated dictionary may still be
  catching up, so a client folds the next time the numbers are needed (run window opened, post-battle screen closed, next
  battle starting). Verbose log: `Run stats: folded battle N (why): <name> DamageDealt=... keys=...`.
- Reset (every machine, no server-only hook): leaving town for the island (GUI state InTown -> InWorldMap), the defeat
  **Retry** (`Root.SendQuestFailChoice(retry)`, an All RPC), a Roguelike retry, and returning to the main menu or
  character select. **Arriving in town keeps the totals**, so the run just finished can still be read in town; the next
  quest starts from zero. A new Roguelike area does not reset unless `RoguelikeAreaResets` is on.
- Run mode: `RunStatsPatches.RunMode` is set by `OpenRunWindow()` (`StatManager.LoadInstanceReference`, `SetCharacters`
  (party), `OpenWindow`) and cleared by a `StatManager.CloseWindow` postfix, which also restores the title. The post-battle
  Stats button (`PostBattleManager.OpenStats`) forces run mode off, so the battle page is never the run page. In run mode
  the `GetStatDisplay` postfix rebuilds the game's rows with the game's own walk (ceil per child, header = sum of the
  ceiled children, hidden children omitted, same size/colour tags, plain digits) and `G()` reads the store for the mod's
  rows.
- The button: a clone of the post-battle menu's own Stats button, found by the `OpenStats` listener on it, taken from the
  live menu or straight from its prefab (`ReferenceLoader.Instance.PostBattleManager`) so it exists even in town; the
  fallbacks are the Stats window's large button and the confirm dialog's. It is labelled "Run Stats", parented to the
  container the game's windows live in and made the first sibling so anything else in that container draws over it. It
  is positioned under the gold and difficulty box at the top right, lined up with its right edge and `RunStatsButtonGap`
  (6) below it: the box is found from `GUIManager.difficultyHolder` by climbing to the nearest parent that also holds a
  `GoldDisplay` and is still panel-sized ('Difficulty Text BG', 213x44 in the current build), and its bottom-right world
  corner is converted into the button's own parent space every frame, so it follows resolution and UI scale. With no such
  box the button falls back to the top-right corner. Every colour, canvas group and button tint state is forced to full
  alpha, because the post-battle menu fades itself in and the clone inherits its see-through colours.
  It yields to everything: any open window (`UIWindowManager.AnyUIWindowOpen`), the post-battle screen, a loading screen,
  a camera event, a hidden HUD, or a GUI state that is not town, island or battle. It is always clickable: with battles in the run it opens the run page, and with none it opens the run
  history list, so the saved runs are never out of reach. Mouse only, since the game builds no gamepad navigation for it, so the key is the pad
  fallback.
- Multiplayer: modded host -> identical run totals on every modded machine; host without the mod -> the game's rows
  sum, the mod's rows show `n/a`.
- Harness: the test driver opens the run page after every fight (`RUNSTATS row N:` lines next to `STATS row N:`) and
  `tools/check_run_stats.py <tests/<stamp>>` checks that the run page after fight k equals the sum (max for Biggest Hit)
  of the battle pages 1..k. Fights of one launch never pass through town, so they form one run.

## Run history (every run kept on disk)

With `SaveRuns` on (the default) each run is written to `BepInEx\BattleStats\runs\run-<date-time>.json` **after every
battle**, so a crash or an alt-F4 never costs more than the fight in progress. The file is written by
`RunHistory.Save` from `RunStats.BuildFile()` and holds both halves of the story:

- a readable header (`started`, `ended`, `mode`, `difficulty`, `quest`, `act`, `battles`, `wins`, a `fights` list with
  the result and turn count of each battle) and a `summary` per character (damage dealt and taken, healing, hits, crits,
  kills, biggest hit with its skill, best skill) for reading the file outside the game;
- the raw `keys` map per character, which is what the Stats window reads, so a saved run can be redrawn exactly as it
  looked in game.

`KeepRuns` (50) deletes the oldest files beyond that; 0 keeps everything. The run in progress is never pruned.

**Browsing.** The **History** button beside Close in the run stats window, or `RunHistoryKey` (F7) anywhere, opens the
run list: one row per run, newest first, with the date, battle count, difficulty and quest on the first line and the
party and totals on the second; the run in progress sits at the top, highlighted. Clicking a row opens the Stats window
on that run and closes the list, so Escape from the page goes straight back to the game. The window is built the same way as the mod menu (`RunHistoryWindow.cs`:
panel sprite and fonts borrowed from the confirm dialog, `UIWindow` registration, scrolling row list).

**Harness.** `run_build_test.py` copies each instance's run files into `<out>/runs/<instance>/` and `tools/check_run_stats.py` checks them against the run pages the driver read (battle and win counts, per-character Damage Dealt, raw keys present). Fights of one launch are one run, so `--resetruns` (driver flag `-srresetruns`) ends the run after every fight, which is how the "a new run writes a new file" path is tested.

**Closing the game in the middle of a quest.** The totals live in memory, so they would be gone on the next launch; instead each run file records the quest it is being played on and whether the run has ended. A quest instance has no id of its own, so the fingerprint is its boss, level, terrain, time of day and task, all of which the game saves in `QuestSaveCampaign.json` (`RunStats.QuestId`). When the game reopens straight onto an island, which is what it does when the save was made mid-quest, the mod looks for the newest unfinished run with that fingerprint and the same party and carries on with it, appending to the same file (`RunStatsPatches.TryResume`, config `ResumeRuns`). Going to the main menu or the character list only drops the run from memory; the file stays open. A run's file is closed, and so never picked up again, when the run really ends: leaving town for the next quest, a defeat Retry, or a Roguelike retry.

**Roguelike.** There is no town, so a Roguelike run is one set of totals from the first battle to the end of the run, across every area (`RoguelikeAreaResets` splits them per area instead). The run is identified by the final boss it is heading for (`ObservableQuestData.RoguelikeEndBossGuid`, picked once when the run starts and kept to the end), which is what lets a Roguelike run be picked up again after closing the game, the same way a campaign quest is. `Root.SendRoguelikeComplete` closes the run's file win or lose, so it is never resumed afterwards, while the totals stay on screen until the next run starts. How deep the run got (`CurrentRoguelikeLevelIndex + 1`) is saved as `area` and shown in the history list.

**Rebuilding old runs.** `tools/rebuild_runs.py` turns the per-battle JSON files (config `WriteJson`) into run files for runs fought before this feature existed: it keeps the player's own characters, groups their battles by time (`--gap`, 45 minutes by default), merges them with the same rules as the mod and writes one file per run, marked `"reconstructed": true`. Numbers missing from a battle file's key map, which is everything the mod shares, are taken from the recorder's own fields instead, so battles from before the shared keys existed still rebuild in full; skill names are turned back into source codes through `data/skills/ACTIONS.md` and `STATUSES.md`. What cannot come back: the per-ability breakdown behind each number, and the run's difficulty and quest. Run it without `--write` for a dry run.

**How a saved run is shown.** The window draws its columns from live `Character` objects, so a saved run's characters are
laid onto the columns: the live character of the same name where there is one, any spare live character otherwise. Only
the column's name and numbers come from the file (`RunStatsPatches.ApplyHistoryNames` / `ViewFor`), so a stand-in is
invisible; a run with more characters than the game currently has loaded shows as many columns as it can and logs the rest.

