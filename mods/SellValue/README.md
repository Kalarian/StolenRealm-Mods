# SellValue

Every item tooltip shows the item's sell value (what a shop pays for it) as a right-aligned line at the bottom:
`Sell: 1200 <gold icon>`, and for a stack `Sell: 600 (120 each) <gold icon>`. Works wherever the game shows a live
item tooltip: bag, character sheet, stash, shops (buy and sell tabs), loot windows, gambling, reforge, chat/event links,
and on the equipped-item comparison tooltip beside a bag/shop item. Master-config plugin: `SellValue = true` in
`BepInEx\config\stolenrealm.mods.cfg`, a row in the F9 mod window, own settings in `stolenrealm.sellvalue.cfg`.

## Where the number comes from

`Item.SellPrice` (the exact number the shop's Sell tab uses): the item's fixed sell price when it has one (commodities),
otherwise `PurchasePrice x SellBackPercent / 100` rounded to `PriceRoundToNearest` (asset values 8% and 5), times the
stack size. Mods that change prices are reflected automatically because we read the same property.

## How it works

- The game's tooltip prefab has a `FooterText` line under the description. `Tooltip.ShowItemTooltip(Item, ...)` fills it
  from its `footerText` argument (`ItemSlot` passes "Not Enough Gold" / "Requirements Not Met" / "Owner: X" there; most
  callers pass nothing). A Harmony prefix with `ref string footerText` appends our line, so the game lays the footer out
  itself and there is no layout work in the mod. A TMP `<align="right">` tag keeps only our line on the right; the game's
  own footer lines stay left.
- Comparison tooltip: `ShowItemTooltip` calls `ComparisonTooltip.ShowTooltip(item3.ItemName, ..., isItem: true, ...,
  footerText: null)` for the equipped item. While `ShowItemTooltip` runs (flag set by the prefix, cleared by a
  finalizer) a prefix on `Tooltip.ShowTooltip` recognises that call (comparison instance, `isItem`, title equal to the
  equipped item's name, computed with the same rule as the game's `item2`) and appends the equipped item's value.
- The `ShowItemTooltip(ItemInfo, ...)` overload (crafting catalogue entries with no live item) has no level and therefore
  no price; it is left alone.
- Gold icon: the coin lives in the game's "Money Icon TMP" sprite asset (sprites `Gold` and `Currency Icons TMP_1`),
  which only the tooltip footer references; any other text falls back to EmojiOne and renders a bare sprite tag as a
  yellow "?" box (seen 2026-09-17). `GoldIcon.Tag()` therefore emits `<sprite="Money Icon TMP" name="Gold">` and registers
  the asset with TextMeshPro's `MaterialReferenceManager` once it is found loaded, so the tag resolves on any text.

## Settings

| Key | Default | Meaning |
|---|---|---|
| Enabled | true | Show the line. |
| OnComparison | true | Also on the equipped-item comparison tooltip. |
| PerUnit | true | For stacks, add `(N each)` after the stack total. |
| Label | Sell | Word before the number; empty = number and icon only. |
| ReloadKey | F9 | Re-read the file (with the mod window present F9 opens the window, Apply reloads). |
| VerboseLogging | false | Log each hover: `Item 'X' sells for N; equipped 'Y' sells for M`. |
| DebugDump | false | Log the tooltip prefab's hierarchy once per launch (mod debugging only). |
