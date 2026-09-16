# ScalingTooltips — show the scaling behind every damage number (BepInEx 5)

Skill tooltips in Stolen Realm show the damage a skill will do for your character, but not how it scales. This plugin appends the scaling next to each number, everywhere skill, action and status tooltips are shown (skill trees, action bar, Examine window, text links):

```
Deals 118-159 (0.7x AP) weapon damage. Lowers target's Resistance by 20% for 2 turns.
Deals 88 (0.5x SP, Fire) fire damage to all enemies within 2 hexes.
```

AP = Attack Power (weapon-based), SP = Spell Power. Set `Style = Full` for `(70% Attack Power)` wording instead.

With `Breakdown` on (default) a line is added under the description showing the whole chain for your character, every modifier included:

```
Scaling: 0.7 x 142 AP x 1.33 = 132 (112-152)  ·  Power +12%, Physical +21%, Might +8%
```

Modifiers at zero are left out. The pieces are: the skill factor, your stat value, the combined multiplier (1 + Power% + school% + mana power% + basic-attack%) x (1 + Might%), plus any flat school damage, then the ±15% range the game shows.

The label is small and grey by default so it does not compete with the real number.

Config: `BepInEx\config\stolenrealm.scalingtooltips.cfg` (F9 in game reloads it).

| Section | Key | Default | What it does |
|---|---|---|---|
| `1.Label` | `Enabled` | true | Master switch. |
| `1.Label` | `Style` | Short | `Short` = `(0.7x AP)`, `Full` = `(70% Attack Power)`. |
| `1.Label` | `Breakdown` | true | Append the `Scaling:` line with stat value x every modifier = result. |
| `1.Label` | `Color` | 9AA5B1 | Hex colour of the label. |
| `1.Label` | `SizePercent` | 80 | Label size relative to the description text. |
| `1.Label` | `ShowDamageType` | true | Append the school when the formula names one (Fire, Cold, Lightning, Shadow, Light, Healing). Physical on Attack Power skills is implied and omitted. |
| `1.Label` | `ShowRawFormula` | false | For formulas that are not "stat × factor", show the cleaned formula instead of nothing. |
| `2.General` | `ReloadKey` | F9 | Re-read the config. |
| `2.General` | `VerboseLogging` | false | Log every formula parsed. |

Install: `stolenrealm.scalingtooltips.dll` goes in `BepInEx\plugins\ScalingTooltips\`. Purely client-side UI. Uninstall by deleting the folder.

## How it works

Tooltip descriptions carry two kinds of number tokens, resolved by two different game functions:

- `[N]` tokens, resolved by `Tooltip.ApplyDescriptionExpressions(text, expressions, ...)` where `expressions[N]` is the formula (e.g. `Source.AttackPower * .7f * Source.DamageModPhysical`). This is what skill-tree and skill-bar tooltips use, and it is the path that produces the `118-159` ranges.
- `*N` tokens, resolved by `Tooltip.GetDamageString(text, List<string> effects, ...)` where `effects[N]` comes from the action's `GeneralEffect.Action` strings. Action and status tooltips use this.

Both game loops replace the token in place, so a Harmony **prefix** on each function inserts the label immediately after every token; the game then fills in the number and the label stays glued to it. Only formulas of the shape `Source.<Stat> * factor [* factor] [* Source.DamageMod<School>]` get a label (88 of the 94 distinct formulas in the game assets); others get nothing unless `ShowRawFormula` is on. The label never contains `[` or `*`, which the game's parsers would choke on.

The breakdown mirrors the game's tooltip math (`Character.DamageModPhysical` etc. = `(1 + (DamageMod + DamageMod<School> + ManaPowerMod)/100) x (1 + AbilityPower/100)`, then `DamageFlat<School>` added; `Character.GetActionDamage` applies the same pieces for a target-less tooltip, with ManaPowerMod only when the action costs mana and DamageModBasic only for basic attacks). Target-dependent bonuses (Ambush, Marked Prey, frozen targets) are not part of a tooltip and are not shown.

## Rebuild

```
cd mods\ScalingTooltips
dotnet build -c Release
```

Same setup as the other mods. The build copies the DLL into the game's plugins folder.
