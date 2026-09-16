# AutoSalvage — junk sold, materials stashed, on pickup (BepInEx 5)

Whenever loot lands in your bags (battle loot, chests, event rewards) the rules run on **the items that just arrived**, never on the rest of the bag:

- **Equipment** (weapon, shield, head, armor, ring, amulet): Common, Uncommon and Rare are sold at the shop sell price (8% of purchase price), each rarity its own switch. Legendary and Mythic are always kept. Optional exception: keep Rares at or above your level.
- **Commodities**: sold, any rarity, except commodities that are an ingredient in any crafting recipe (Animal Hide, Fire Core, Wild Soul, drake scales, Cursed Coin...), which go to storage instead. 25 of the 279 recipes use commodities (16 of the 98 commodity types).
- **Materials**: moved to your storage stash, never sold.
- **Never touched:** anything equipped, consumables, tools, quest items; shop purchases, crafting results, gambling wins, items moved between characters or withdrawn from storage, Roguelike level-up picks, a new character's starting kit, and gifts from a friend (all of these arrive through non-loot paths). Because only new arrivals are judged, a Rare you bought and left in the bag survives the next drop.
- **One-time bag cleanup** (`SweepBagsOnLoad`): the first launch with the mod applies the rules once to everything already in every character's bag (unequipped only), then switches itself off (`SweepBagsOnLoad = false` is written to the config). Set it back to true to run it again.
- An overhead message per haul: `+340 gold, sold 4, stashed 2`.
- Roguelike mode and Roguelike characters are left alone.

Config: `BepInEx\config\stolenrealm.autosalvage.cfg` (F9 in game reloads it). Master switch: `AutoSalvage = true/false` in `stolenrealm.mods.cfg`.

| Section | Key | Default | What it does |
|---|---|---|---|
| `1.Rules` | `Enabled` | true | Master switch inside the mod. |
| `1.Rules` | `SellCommonEquipment` | true | Sell white equipment. |
| `1.Rules` | `SellUncommonEquipment` | true | Sell green equipment. |
| `1.Rules` | `SellRareEquipment` | true | Sell blue equipment. |
| `1.Rules` | `KeepRareAtOrAboveMyLevel` | false | Exception to the above. |
| `1.Rules` | `SellCommodities` | true | Sell commodities of any rarity. |
| `1.Rules` | `RecipeCommoditiesToStash` | true | Commodities used by any recipe go to storage instead of being sold. |
| `1.Rules` | `MaterialsToStash` | true | Materials go to storage. |
| `1.Rules` | `SweepBagsOnLoad` | true | One-time cleanup of existing bags; turns itself off after running. |
| `2.Feedback` | `OverheadSummary` | true | The per-haul message. |
| `3.Safety` | `LeaveRoguelikeUntouched` | true | Do nothing in Roguelike mode, and never touch a Roguelike character's bags. |
| `4.General` | `ReloadKey` | F9 | Re-read the config. |
| `4.General` | `VerboseLogging` | false | Log every item sold or stashed. |

Install: `stolenrealm.autosalvage.dll` in `BepInEx\plugins\AutoSalvage\`. Client-side: loot rolls on each player's own PC, so each modded player's copy handles only their own drops and gold.

## How it works

`src/Patches/SalvagePatches.cs`. Every item entering a bag goes through `GameLogic.GiveItem`; loot batches go through `GameLogic.GiveItems`, which calls it per item. The `GiveItem` postfix records the delivered item (only when the call has `save=true` and `setTimeAcquired=true`: load hand-back, gifts and preset sync use `save=false`), a batch is processed once from a `GiveItems` finalizer, and only those items (or, for a stackable that GiveItem merged into an existing unequipped stack, that stack) are judged. Non-loot deliveries are excluded by a suppression counter set in prefixes on `ShopManager.BuySelectedItem`, `CraftingManager.CraftRecipe/CraftSelectedRecipe`, `GamblingManager.GambleItem`, `InventoryManager.GiveItemToAnother` (also the storage-withdrawal path), `RoguelikeManager.ConfirmLevelUpSelection` and `GameLogic.FinalizeCharacterCreation`. Selling mirrors `ShopManager.SellSelectedItem`: `RemoveFromItemList` + `GiveGold(item.SellPrice)` (already per whole stack). Stashing uses `ItemStashData.AddItemToStash` followed by `SaveStash()`.

**Two game facts the mod has to work around:**

- The game only reads `ItemStash.json` the first time you enter town (`TownManager.OpenTown` → `LoadStash`), `SaveStash()` is a silent no-op before that, and `LoadStash` starts by clearing the list. `ReadyStash()` therefore calls `LoadStash()` itself when needed and checks the private `loaded` flag by reflection; if the stash still is not loaded, materials stay in the bag. (Version 1 lacked this and lost the stacks it stashed at the character-select screen; restored by hand on 2026-09-15.)
- `Character.Save` silently does nothing unless forced (only characters that joined the party this session count as "network loaded"), while the character is still inside `Load`, or when it is not yet in `GameLogic.AllMyCharacters`; and `QueueCharacterSave` saves immediately rather than queueing. The stash file, on the other hand, is written at once. So the one-time cleanup does not run from the `Load` hook: it runs from the plugin's Update once `GameLogic.FinishedLoadingCharacters` is true, force-saves each cleaned character and verifies the file changed (mtime/length), and logs an error if the write did not happen. Loot during play needs none of this because party members save normally.

Only characters you own are swept. Recipe ingredients are indexed once from `Game.Instance.CraftingRecipes` (`CraftingRecipe.Required[].item`).

## Rebuild

```
cd mods\AutoSalvage
dotnet build -c Release
```

Follows the master-config pattern: `src/MasterConfig.cs` and `src/Plugin.cs` are generated by `tools/gen_master.py`.
