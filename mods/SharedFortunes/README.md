# SharedFortunes — account-wide fortunes (BepInEx 5)

Fortunes are earned per character in vanilla, so every alt starts with none. This plugin shares them: any fortune one of your characters has earned becomes available to all of them.

- **Pool file:** `SharedFortunes.json` next to your character saves (`%USERPROFILE%\AppData\LocalLow\Burst2Flame Entertainment\Stolen Realm\`). It maps each fortune to the highest level anyone earned it at. It is built from your own saves on your own PC; nothing is shipped in the mod and nothing is sent to other players.
- **On game start** (and on F9) every `CharacterN.json` on disk is read and pooled, so all existing characters contribute without being loaded first.
- **When a character loads**, fortunes they lack are added to their available list, unequipped and marked new; fortunes they have at a lower level are raised. Slot choices stay per character. The merged list is written through the game's normal save, so it survives removing the mod.
- **While playing**, earning a fortune (or an event levelling all your fortunes) updates the pool.
- **Never removes or lowers anything.** Installing it on an existing account only adds.

Config: `BepInEx\config\stolenrealm.sharedfortunes.cfg` (F9 in game reloads it and rescans the saves).

| Section | Key | Default | What it does |
|---|---|---|---|
| `1.Sharing` | `Enabled` | true | Master switch. |
| `1.Sharing` | `CapToCharacterLevel` | true | A shared fortune is capped at the receiving character's level and rises as they level (re-checked on every load). Off = full pool level immediately. |
| `1.Sharing` | `IncludeRoguelikeCharacters` | false | Also share to/from Roguelike-mode characters. |
| `2.General` | `ReloadKey` | F9 | Re-read config, rescan saves. |
| `2.General` | `VerboseLogging` | false | Log every fortune pooled, added or raised. |

Install: `stolenrealm.sharedfortunes.dll` goes in `BepInEx\plugins\SharedFortunes\`. Client-side only; each player who installs it shares among their own characters. Uninstall by deleting the folder; characters keep what they gained.

## How it works

`Character.Load(int)` copies `CharacterSaveFile.FortuneSaveData` (Guid, Level, EquippedSlotIndex, IsNew) into `Character.FortuneData` and applies the equipped ones. Three Harmony postfixes (`src/Patches/FortunePatches.cs`):

1. `Character.Load`: pool this character's fortunes, then merge the pool in (add missing / raise lower, capped per config). If anything changed, call `LoadFortunes()` to re-apply equipped ones and `QueueCharacterSave()`.
2. `Character.AddFortune`: pool the new fortune.
3. `Character.LevelUpAllMyFortunesToMyLevel`: pool the raised levels.

The pool (`src/FortunePool.cs`) reads saves with the game's own `CharacterSaveFile` class via Newtonsoft.Json, skips `.backup`/`.temp` files, deleted characters and (by default) roguelike ones, and writes atomically via a temp file. Fortune levels are capped at 30 like the game does.

## Rebuild

```
cd mods\SharedFortunes
dotnet build -c Release
```

Same setup as the other mods, plus a reference to `Newtonsoft.Json.dll` from the game folder.
