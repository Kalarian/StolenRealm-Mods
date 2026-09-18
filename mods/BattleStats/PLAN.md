# BattleStats — plan (2026-09-15)

Goal: make the post-battle stats page tell the truth about who did what, including damage that arrives through poison ticks, burning ground and other tiles, summons and thorns, which the game lumps into one "Damage Dealt" number per character.

## Phase 1 (built): recorder, no UI

`stolenrealm.battlestats.dll` records every credit the game makes during a battle and prints a summary at the end. Nothing is displayed in game. It runs on the machine that resolves the battle (the host), so the user must host the test battle.

Log lines while fighting (`LogEveryHit = true`):

- `HIT t3 Jon Shadow -> Goblin Archer: 142 DamageDealt [Direct | Garrote]` — one per damage credit; `[StatusTick: Poisoned]`, `[GroundEnter | Poison Cloud]`, `[TurnStart | Burning Ground]`, `[GroundCreate | ...]`, `[Returned]`, `[Summon]` name the path.
- `  CRIT (142 Garrote)` under a hit that critted; `  KILL Goblin Archer by Jon Shadow (Garrote, Direct)`; `overkill N` on the hit line.
- `TAKEN`, `HEAL`, `BLOCK` lines for the other stats. HIT/TAKEN lines also carry the damage element (Physical, Fire, Cold, Lightning, Shadow, Holy).
- `CAST t2 Jon Shadow: Garrote (Action, 3 mana)` for every skill a player uses (tile turn-start actions the game performs on the caster's behalf are not counted).

At battle end: `===== BATTLE SUMMARY =====` with, per player character: damage split by path, hits/crits/kills/overkill, biggest hit and what did it, damage taken (and how much from ticks), healing, blocked totals, top skills, damage by element, casts (actions used, free actions, mana spent, most-cast skills, casts per turn), hexes moved (sampled from the game's own MovementThisTurn counter each turn), and `GAME'S OWN TOTALS` (the dictionary the vanilla page reads) for comparison. A JSON with every event goes to `BepInEx\BattleStats\battle-<date>.json`.

## What the test battle must prove

1. A poison-tile build (nature): the poison ticks and the tile's on-enter damage appear with `StatusTick` / `GroundEnter` paths credited to the caster, and the caster's `GAME'S OWN TOTALS DamageDealt` equals our `damage` total. If the game total is lower, the game is dropping that damage and we know exactly which path.
2. A burning-ground build (fire): same for `TurnStart` (tiles tick on the caster's turn) and `GroundEnter`.
3. A summoner: the summon's hits show as `Summon` on the master; the game's `SummonDamageDealt` matches.
4. Thorns / damage returned: shows as `Returned`.
5. A healer: `HEAL` lines, HealingAdministered vs HealingReceived, self-heals.
6. Direct hitters: crit lines line up with the floating crit numbers; hit counts match what you saw; multi-target skills produce one HIT per target.
7. Kills and overkill: every enemy death has a KILL line naming the right killer; a kill by a poison tick names the poison's owner; a player death is marked `[PLAYER DOWN]`.
8. Turn count in the summary equals the battle's last turn number.

## Phase 2 (built 2026-09-15): display

Built as designed below: `SharedStats.cs` (replicated keys 1000+), `Patches/StatsWindowPatches.cs` (rows + hover
tooltip), recorder writes the keys per credit. Verified by the automated Statue battle: Direct + Over Time + Tiles equals
the game's Damage Dealt for every test character; 24 extra rows in four sections; the driver reads every window row
back into the log (`STATS row N`). Element matching is now per target and per action context (overkill and shield-split
hits no longer come out "Unknown"). Still open: the game's own Blocked By Resistance can be negative (vulnerability); left
as the game shows it.

Rows appended to the existing stats window (same prefabs, same style), values per character:

- Direct hits / Damage over time / Tile damage / Summons / Thorns (the split above).
- Biggest hit (with skill name), best skill (name + total).
- Hits, crit rate, kills, overkill.
- Damage per turn.
- Damage by element.
- Casts, actions used, free actions, mana spent, most-cast skill.
- Hexes moved.
- Damage taken split direct / ticks.
- Hover a character's column header: full per-skill breakdown tooltip.

Extra stats are stored in the game's own replicated per-character stat dictionary under keys the game does not use, so clients with the mod see them too (host must have the mod for anything to be recorded). Hooks used for display: `StatManager.BuildOutLabels` (add rows) and `StatManager.GetStatDisplay` (add values), which keeps the game's own fill loop in step.

Open questions for the test: how many extra rows fit before the window needs scrolling; whether `StartNewTurn` wraps anything other than tile ticks that should be labelled differently; whether any credit arrives with no ApplyAction context (would log with no skill name).

## Phase 3 (built 2026-09-17): run stats

User request: "a button on the main UI that's always visible that lets me see the battle stats for the entire run ... a
clone of the current battle stats page but the numbers are cumulative for the run". Built as `src/RunStats.cs` (store +
merge rules) and `src/Patches/RunStatsPatches.cs` (fold at battle end, reset on leaving town / retry / menu, run mode in
the same StatManager window with rewritten game rows and redirected mod rows, title, HUD button cloned from the Battle
Log button, F8). Decisions: totals reset when the next quest starts (readable in town), Roguelike runs count as one run
(new area keeps counting, retry resets), configurable key. See README "Run stats".

### Phase 3b (built 2026-09-17): run history

User request: "every run saved locally and a way to pull up older runs". `src/RunHistory.cs` (one JSON per run under
BepInEx\BattleStats\runs, rewritten after each battle, readable summary + raw keys, KeepRuns pruning) and
`src/RunHistoryWindow.cs` (the run list, built like the mod menu, rows open the Stats window on that run). Saved runs are
shown by laying their characters onto the window's columns (stand-in Characters, names and numbers from the file).

