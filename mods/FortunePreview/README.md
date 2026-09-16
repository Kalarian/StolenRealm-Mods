# FortunePreview — see which fortunes a quest can give (BepInEx 5)

Part 1: in the campaign quest-select map, hovering a quest adds a **Fortunes** section to its tooltip listing every fortune that can be earned on that island, coloured by rarity, with the event that grants it and whether your current character already owns it.

Part 2: in the island event window, each option's outcome gets a **Fortune:** line naming the fortune it grants when the game does not already say so (hidden effect text, mystery rolls), plus a **Fortune later:** line for fortunes that come from the follow-up event or from winning/losing the fight the option starts. Names are hoverable links that open the game's own fortune tooltip. When an outcome picks one fortune at random from a set, the whole set is shown as "(one of)"; `RevealRolled` shows the one already rolled for this island instead.

Part 3: every fortune tooltip (Fortune window, status links in event text, the event-window links) gets a **Source** section: where it can be found (terrain, time of day, minimum island level) and which main quests it can show up on, with the quest's act and level. Scripted quest events are tagged "(scripted)".

Two views:

- **Compact (default):** grouped by rarity, names only, slightly smaller font: `Mythic: Ball Lightning, ...` / `Legendary: ...`. Fits most islands without a cap. `MinRarity` filters this view.
- **Detailed (hold Left Shift while hovering):** one fortune per line, no rarity filter, capped by `DetailMaxListed` (turn on `ShowEventNames` to see the granting event too). The tooltip re-renders as you press and release the key.

By default the compact view lists only fortunes your current character does not own at all (`CompactHideAllOwned`), with a count of how many owned ones were skipped. The detailed view drops owned fortunes at this island's level or higher (`HideOwned`) but keeps lower-level ones, marked in green with your current level, since re-earning them there is an upgrade.

Config: `BepInEx\config\stolenrealm.fortunepreview.cfg` (F9 in game reloads it).

| Section | Key | Default | What it does |
|---|---|---|---|
| `1.QuestSelect` | `Enabled` | true | Add the Fortunes section to quest tooltips. |
| `1.QuestSelect` | `DetailKey` | LeftShift | Hold for the detailed view. |
| `1.QuestSelect` | `MinRarity` | Common | Compact view only shows this rarity and up. |
| `1.QuestSelect` | `ShowEventNames` | false | Detailed view: also show the granting event (off by default). |
| `1.QuestSelect` | `MarkOwned` | true | Mark owned fortunes with their level. |
| `1.QuestSelect` | `HideOwned` | true | Drop fortunes already owned at this quest's level or higher (the count is still shown). |
| `1.QuestSelect` | `CompactHideAllOwned` | true | Compact view: drop every owned fortune regardless of level, so only what you still need is listed. |
| `1.QuestSelect` | `CompactSizePercent` | 85 | Font size of the compact list. |
| `1.QuestSelect` | `DetailMaxListed` | 40 | Cap for the detailed view, then "+N more". |
| `2.EventWindow` | `Enabled` | true | Add the Fortune lines to event options. |
| `2.EventWindow` | `RevealHidden` | true | Show the fortune even when the outcome text is hidden or a mystery roll. |
| `2.EventWindow` | `RevealRolled` | false | For random picks, show the fortune already rolled instead of "one of" the set. |
| `2.EventWindow` | `ShowChained` | true | List fortunes from the follow-up event / fight victory or defeat. |
| `2.EventWindow` | `SizePercent` | 90 | Font size of the added lines. |
| `3.FortuneTooltip` | `SourceInTooltip` | true | Add the Source section to fortune tooltips. |
| `3.FortuneTooltip` | `MaxQuests` | 8 | Main quests listed per event before "+N more". |
| `4.FortuneWindow` | `ShowUnownedFortunes` | true | The Fortune window lists every fortune you do not own after the owned ones, greyed out and not equippable; hover for the effect and Source. |
| `4.FortuneWindow` | `UnownedAtCharacterLevel` | true | Unowned effects shown at your level (off = level 1). |
| `4.FortuneWindow` | `UnownedOpacityPercent` | 35 | How faded the unowned icons are. |
| `4.FortuneWindow` | `SortUnownedByRarity` | true | Mythic first, then by name. |
| `4.FortuneWindow` | `UseDisabledOverlay` | true | Also use the slot's own disabled overlay graphic. |
| `5.General` | `ReloadKey` | F9 | Re-read the config. |
| `5.General` | `VerboseLogging` | false | Log resolved fortunes per hovered quest and per event option. |

Install: `stolenrealm.fortunepreview.dll` in `BepInEx\plugins\FortunePreview\`. Client-side UI only.

## How it works

**Fortune window catalogue** (`Patches/FortuneWindowPatches.cs`): postfix on `FortuneWindow.UpdateFortuneWindow` appends one slot per fortune missing from `character.FortuneData` (from `Game.Instance.Fortunes`), each backed by a throw-away `FortuneSaveData` that is never added to the character; icon/border faded, level text `?`, the slot's `Disabled` object switched on. A prefix on `FortuneWindow.SelectedAvailableSlot` returns false for those slots (popup instead of equipping). `FortuneSlot.ShowTooltip` prefix sets `GhostNote`, which `FortuneTooltipPatches` prepends to the Source text. FortuneUpgrade independently refuses any slot whose data is not in the character's list.

The list is computed the way the game itself picks island events, so it reflects the real pool:

- `PartyEvent.LeadsToFortune` is a flag the game sets at load by walking each event's chain (`LeadsToFortuneHelper`). We only look at events with that flag.
- `PartyEvent.CanSpawn(terrain/time, nodeType, statuses, spawnType, level)` is the game's eligibility check used by `WorldMapGenerator`. We call it with the quest's terrain, time of day and level, for every node type and spawn type an island uses; an event counts if any combination passes. Chained-only events (`NotInEventPool`) are skipped as roots but reached through their parents.
- The quest's own scripted events (`QuestInfo.middleEvents`) are always included and tagged "(quest)".
- From each root we walk option -> action -> effects `eventStatuses` (StatusType Fortune), then `chainedEvent` and battle `victoryEvent` / `failureEvent` (both the manual `battle` and `battleOverrideInfo` forms), exactly the paths the game's own helper follows.
- Events that require a party status to spawn are evaluated with no statuses, matching how the game seeds its event decks; fortunes behind multi-island status chains may therefore be listed under their first event only.

Results are cached per terrain/time/level/quest and cleared on F9 or a config change.

Hook: prefix on `Tooltip.ShowQuestNodeTooltip` builds the text (compact or detailed depending on whether the detail key is held) and parks it; prefix on `Tooltip.ShowTooltip` appends it to the description; postfix clears it. The plugin's Update re-calls `ShowQuestNodeTooltip` with the remembered node/glyphs/footer whenever the key state changes while the tooltip is visible.

### Event window

`EventOptionSelectionItem.PopulateResultEffects` fills the outcome text boxes (`SingleActionEffectText`, or `SuccessEffectText`/`FailureEffectText` for dice options) via the game's `GetActionDetails`, which already names statuses granted directly by a visible outcome. A postfix reads the same `EventAction`: its `EventActionEffects.eventStatuses` (and `rolledEventStatuses`, decided when the island was generated) for direct grants, and `chainedEvent` / `battle.victoryEvent|failureEvent` / `battleOverrideInfo.VictoryEvent|FailureEvent` for later ones. It appends the lines to the text box, registers each name with `TextLinkManager.linkDict` so the game's link hover shows `ShowEventStatusTooltip`, and calls `TweenShow` so a box the game had hidden as empty becomes visible.

### Fortune tooltips

All fortune tooltips end in `Tooltip.ShowActionStatusTooltip` with a Fortune `ActionStatusInfo` (whose `LinkedEventStatus` is the fortune). A prefix builds the Source text from a reverse index (`src/FortuneSources.cs`: root event -> fortunes, built once from the same chain walk) and parks it for the shared `ShowTooltip` prefix. Main quests come from `MiscSettings.ReleaseMainQuests`; each is tested with the event's `CanSpawn` for that quest's terrain, time and level.

## Rebuild

```
cd mods\FortunePreview
dotnet build -c Release
```
