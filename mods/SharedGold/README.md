# SharedGold — one gold pool for all your campaign characters (BepInEx 5)

All the gold your campaign characters hold is treated as one pool. Whichever character you are playing holds all of it, so a solo character and a full party both see and can spend the entire balance. Roguelike characters are ignored.

**It cannot create or destroy gold.** The game's "party gold" is already the sum of the gold on the characters in your party, and spending drains them in turn. This mod only moves gold between your own characters: everything onto the one you control, zero on the others. The total is preserved by construction, and the first run simply consolidates what your characters already had.

- The holder is the character you are currently controlling, else the first member of your party, else the first loaded character. Switching characters moves the pool with you.
- Every gold change (loot, quest rewards, shop spending, upgrades, sending gold to a friend) triggers a re-consolidation, and a light half-second check catches party or selection changes.
- All your characters are loaded objects from startup, so every one of them queues its own save when its gold moves; save files stay consistent.
- **Multiplayer:** only characters you own are ever touched. Your co-op partners' characters and their gold are never read or changed; the game already keeps each player's party gold separate. Sending gold to a friend works exactly as before.
- Removing the mod leaves the whole pool on whichever character held it last; nothing is lost.
- `SharedGold.json` next to the saves mirrors the current pool and holder for your information; it is not the source of truth.

Config: `BepInEx\config\stolenrealm.sharedgold.cfg` (F9 in game reloads it). Master switch: `SharedGold = true/false` in `stolenrealm.mods.cfg`.

| Section | Key | Default | What it does |
|---|---|---|---|
| `1.Pool` | `Enabled` | true | Master switch inside the mod. |
| `1.Pool` | `IncludeHardcoreCharacters` | true | Pool hardcore characters with normal ones. Off keeps their gold separate. |
| `2.General` | `ReloadKey` | F9 | Re-read the config and re-consolidate. |
| `2.General` | `VerboseLogging` | false | Log every transfer. |

## How it works

`src/Patches/GoldPatches.cs`: `Consolidate()` gathers every owned, non-roguelike, non-deleted character that has a save file on disk, taken from the game's own lists (`GameLogic.instance.AllMyCharacters`, which the game fills from the saves at startup, plus your party and the selected character as a safety net, one entry per save slot), picks the holder (`GameLogic.instance.CurrentlySelectedCharacter` > first of `NetworkingManager.Instance.MyPartyCharacters` > first listed), sums their gold, sets non-holders to zero and the holder to the sum via `Character.AddAttribute(Game.GoldAttribute, delta)`, and queues each character's save. The mod keeps no character list of its own: the game rebuilds its `Character` objects when it reloads the saves, and a stale object would count its old gold a second time. A `Character.Load` postfix (low priority, so it runs after LevelSync, SharedFortunes and AutoSalvage) only flags that a consolidation is due; the plugin's half-second tick performs it once the game has listed the character, and a `Character.GiveGold` postfix (owned characters only) consolidates right after any gold change. Nothing runs while the character-creation screen is open, and a character that has never been saved is not part of the pool until its first save. Follows the master-config pattern (`tools/gen_master.py`).

## Rebuild

```
cd mods\SharedGold
dotnet build -c Release
```
