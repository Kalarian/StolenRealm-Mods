# TargetTooltip — "what will this click do?" tooltip in battle (BepInEx 5)

In battle, when the mouse is over a cell you can act on, a small tooltip names the action a click would fire:

- **Movement state, attack cursor on an enemy** → the basic attack the game is going to use (it picks the first basic attack that can reach, exactly the way `PlayerMovement` does, so dual-wield / ranged-vs-melee cases show the right one).
- **A skill selected, hovering a valid target cell** → that skill. Enemy cells always show it; empty cells (ground targets, allies, dashes) show it when `ShowOnEmptyCells` is on.

Nothing is shown while placing characters, after the fight ends, over usable objects (chests, shrines), or when the game's own ground-effect tooltip is up. The tooltip goes away as soon as the cell stops being a valid target.

This is a port of just the *target-hover action tooltip* feature of Eradev's **BetterTooltips** (2023, GPL-3.0), rewritten for the June 2025 game build. Stack counts, remaining turns and the other BetterTooltips features are already in the vanilla game now and were not ported. Licensed GPL-3.0 (see `LICENSE`) because of that lineage.

Config: `BepInEx\config\stolenrealm.targettooltip.cfg` (F9 in game reloads it; F10 flips Compact/Full).

| Section | Key | Default | What it does |
|---|---|---|---|
| `1.Tooltip` | `Enabled` | true | Master switch. |
| `1.Tooltip` | `Style` | Full | `Compact` = icon + name in a small translucent box (the BetterTooltips look). `Full` = the game's normal skill tooltip with damage, cost, cooldown and the "Status: can cast / out of range" line. |
| `1.Tooltip` | `ShowOnEmptyCells` | true | With a skill selected, also show the tooltip on valid cells without an enemy. |
| `1.Tooltip` | `DamagePreview` | true | Hovering an enemy adds `vs <enemy>: 71-96 (45-60% of its health)`: the action's damage against that enemy with its resistances, armor, damage reduction and target-specific bonuses applied. Both styles. |
| `1.Tooltip` | `IncludeTileEffects` | true | If the hovered cell has a ground effect, the game's description of it is kept inside our tooltip under "On this tile" instead of replacing the skill tooltip. |
| `1.Tooltip` | `PreviewDetail` | true | Small line under it: per-school split and what reduced the damage (resist %, armor, damage reduction, dodge chance). |
| `1.Tooltip` | `StyleToggleKey` | F10 | Flips Style between Compact and Full in game, applies immediately and saves it to the cfg. |
| `2.General` | `ReloadKey` | F9 | Re-read the config in game. |
| `2.General` | `VerboseLogging` | false | Log each tooltip shown (spammy). |

Install: `stolenrealm.targettooltip.dll` goes in `BepInEx\plugins\TargetTooltip\`. Purely client-side UI; other players unaffected. Uninstall by deleting the folder.

## Tile effects

The game shows its own ground-effect tooltip (burning ground, poison cloud...) from the hovered-cell setter, just before our postfix runs. Originally we stood aside; now a postfix on `Tooltip.ShowGroundEffectTooltip` captures the text it rendered, and when we replace that tooltip with the skill one we append it under **On this tile**. Hovering an effect cell with no action still shows the vanilla ground tooltip.

## Damage preview

`src/DamagePreview.cs` runs the game's own `Character.GetActionDamage(source, target, ...)` for each `GeneralEffect.Action` formula of the hovered action (so all source modifiers and target-side ones like Marked Prey and flat target damage are included), then applies the target's mitigation exactly as `Character.ApplyAction` does: general damage reduction, resistances per school, and armor (physical) / magic armor (fire, cold, lightning) with the 90% armor cap, in `GlobalSettings.DamageReductionOrder`. `GetActionDamage` rolls crits randomly when given a target, so it is resampled up to six times for a non-crit baseline unless the crit is guaranteed, in which case the line says "(crit)". The result is appended to the tooltip's description through the existing `ShowTooltip` prefix.

## How it works

Two Harmony patches (`src/Patches/HoverTooltipPatches.cs`):

1. Postfix on the `HexCellManager.CurrentlyHoveringHexCell` setter (called every frame from `GUIManager`'s hex raycast). It decides whether the hovered cell is a valid target for the pending action, using the game's own `PlayerMovement.CanCast` and `HexCell.GetLineOfSightHitPoint`, and shows/hides the shared `GUIManager.instance.tooltip`. It remembers the last cell/action it showed so it only touches the tooltip when something changes, and it only ever hides a tooltip it put up itself.
2. Prefix on `Tooltip.ShowTooltip` that restores the tooltip's background `Image` (the compact style turns it off) and notes that another system now owns the tooltip.

Upstream relied on the game's `hideOnNotHoveringGO` flag to auto-hide; the current build never reads that flag, hence the explicit tracking.

## Rebuild

```
cd mods\TargetTooltip
dotnet build -c Release
```

Same setup as the other mods (netstandard2.1, BepInEx.Core 5.4.21 + HarmonyX from the BepInEx NuGet feed, game DLLs referenced from the Steam install). This one additionally references `UnityEngine.UI` and `UnityEngine.UIModule` for the `Image` component. The build copies the DLL into the game's plugins folder.
