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
- `[3.General]` ReloadKey F9, VerboseLogging, DebugHooks (path-stack spam, off).

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
