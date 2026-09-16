# SpecialTooltips — hover explanations for the "Special" list in the Examine window (BepInEx 5)

Right-click an enemy and the Examine window lists its stats, its passives (hoverable in vanilla) and its **specials** (Armored, Unpredictable, Pack Hunter...). Vanilla shows the specials as plain text with no explanation. This plugin makes each special hoverable and shows a tooltip describing what it does, with the enemy's live numbers where they matter (resistances, dodge, crit, life steal).

Config: `BepInEx\config\stolenrealm.specialtooltips.cfg` (F9 in game reloads it and the descriptions file).

| Section | Key | Default | What it does |
|---|---|---|---|
| `1.Tooltip` | `Enabled` | true | Master switch. |
| `1.Tooltip` | `UnderlineLinks` | false | Underline the special entries so they look hoverable. |
| `2.General` | `ReloadKey` | F9 | Re-read the config and the descriptions file. |
| `2.General` | `VerboseLogging` | false | Log each tooltip shown, and dump the game's conditional special-effect rules once. |

**Descriptions file:** `BepInEx\config\stolenrealm.specialtooltips.descriptions.txt`, created with defaults on first run. One line per special, `EnumName = text`. `\n` makes a line break, `{ResistFire}` and any other character attribute name is replaced with the examined enemy's live value. Delete the file to get the defaults back.

Install: `stolenrealm.specialtooltips.dll` goes in `BepInEx\plugins\SpecialTooltips\`. Purely client-side UI; other players unaffected. Uninstall by deleting the folder.

## Where the descriptions come from

The game has no text for these tags. `SpecialEffect` is an enum of about 90 designer labels, and nothing in the code reads them except Mana Shield. The behaviour comes from the enemy's passives, skills, triggers, stats and AI. The defaults were written by correlating each tag with what the tagged enemies actually carry (`data/special_effect_research.txt`):

- 43 tags are also enemy affixes (Berserking, Toxic, Vengeful, the Fodder/Soldiers/Champions/Bosses tiers...). Their text is the affix description from the game data.
- Resistances and weaknesses show the enemy's actual resist values.
- The rest map to a specific passive or trigger: Abyssal = Child of the Abyss, Pack Hunter = +40% damage per adjacent ally, Ambusher = Ambush II, Indestructible = 50% resist to the last damage type taken, Cannibal = heal 20% on any death, Undying = Dark Ritual on death, Rampage = +50% damage dealt/taken and +1 AP when hit below half health, Reflects Damage = Iron Golem's Physical Thorns, and so on.
- Unpredictable correlates with the random-target AI (13 of 23 enemies); the tooltip says so.
- Glory is unused by any enemy.

## How it works

Two Harmony postfixes on `ExamineWindow` (`src/Patches/ExaminePatches.cs`):

1. `ShowCharacterExamine`: rewrites `SpecialText` with one TMP `<link>` per special and pads the window's own `specialLinkList` with empty entries. The vanilla `Update` already looks for links in that text but never had any to find; with null skill/status entries it does nothing except hide the tooltip when the mouse leaves, which is exactly what we want.
2. `Update`: finds the hovered link with `TMP_TextUtilities.FindIntersectingLink` and shows `Tooltip.ShowUniversalTooltip(name, "", description)`.

## Rebuild

```
cd mods\SpecialTooltips
dotnet build -c Release
```

Same setup as the other mods, plus a reference to `Unity.TextMeshPro`. The build copies the DLL into the game's plugins folder.
