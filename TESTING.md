# Stolen Realm mods — running test script

Tick items as you go. "Log:" lines are what to look for in `<game>\BepInEx\LogOutput.log` (verbose is on in your configs). The log now keeps every session (BepInEx `AppendLog = true`); each launch begins with a `SESSION START <date time>` line, so search for the last one to find the current session. Delete the file whenever it gets large. When something fails, note what you saw next to the item and send me the log.

Legend: **[NEW]** = not yet verified in game. **[REG]** = worked before, quick regression check only.

**Game update 2026-09-15 (Steam build 25240684, Unity 2022.3.62):** with the old BepInEx.cfg every mod silently switched itself off 0.4 s after launch (log: `patches removed - mod is OFF` right after `Chainloader startup complete`). The new pack ships BepInEx.cfg with `[Preloader.Entrypoint] Type = MonoBehaviour`; the installer always replaces that file. Check after installing: the log must NOT contain `patches removed` lines at startup, and must contain `Log owner: Steam '<name>'`.

Before starting: launch the game once and confirm the log has `Loading [X 1.0.0]` for all fourteen plugins (AutoSalvage, BattleStats, DifficultyXP, DropRates, FortunePreview, FortuneUpgrade, LevelSync, QoL, ScalingTooltips, SharedFortunes, SharedGold, SpecialTooltips, TargetTooltip, ThreatOverlay) and no `[Error` lines.

---

## FortunePreview

### Quest-select tooltip [REG]
- [ ] Hover an unlocked quest on the campaign map. A **Fortunes (L.. on this island, hold LShift for details)** section appears under the rewards, grouped by rarity.
- [ ] Fortunes your character already owns do NOT appear in the compact list. A grey "N already owned" line shows the count.
- [ ] Hold **Left Shift** while hovering: view switches to one-per-line with no rarity filter. Owned fortunes at a lower level than the island appear with a green "(L8)" tag. Release: back to compact. No flicker or stuck tooltip.
- [ ] Hover a quest with a different terrain: the list changes.
- Log: `Quest <Terrain>|<Time>|<Level>|...: N fortunes`

### Event window [NEW]
- [ ] Find any island event. Options whose outcome the game already spells out (you can see a fortune name in the outcome text) get NO extra line (no duplicate).
- [ ] An option with hidden outcome text or a **?** mystery roll shows **Fortune: [Name]** under the outcome, coloured by rarity.
- [ ] An option that starts a fight leading to a fortune shows **Fortune later: [Name] (after winning)**.
- [ ] Hover a bracketed fortune name in those lines: the game's fortune tooltip opens (with the Source section, see below).
- [ ] Pick the option and win/finish: the fortune you were promised is the one you get.
- [ ] Outcome that "picks one of" a set shows several names with "(one of)". (Optional: set `RevealRolled = true`, F9, reopen the event: only the rolled one shows.)
- Log: `Event '<event>' option '<title>' ...: direct N, later N`

### Source section in fortune tooltips [NEW]
- [ ] Open the Fortune window, hover any fortune. Tooltip ends with **Source** / **Where: <terrains> · <time> · L4+** / **Quests: Name (A2 L7), ...**.
- [ ] The listed quests make sense: their terrain matches the Where line and their level is at or above the minimum.
- [ ] A fortune from a scripted quest event lists that quest with "(scripted)".
- [ ] Hover a fortune link inside event text: same Source section appears there.
- [ ] Nothing appears on non-fortune status tooltips (buffs, debuffs, quest statuses).

### Fortune window catalogue [NEW]
- [ ] Open the Fortune window: after your owned fortunes, every other fortune appears faded with a `?` level. Total slots = 83.
- [ ] Hover a faded one: tooltip shows its effect at your level, a red **Not owned** line, then the Source section.
- [ ] Click a faded one: a "Not owned" popup, nothing equipped, nothing changes in your list. Press **U** on it: nothing happens (no confirm dialog).
- [ ] Equip/unequip a real fortune: the window refreshes and the faded block is still there, still faded, owned ones still normal.
- [ ] Earn or upgrade a fortune: it moves from the faded block into the owned block on the next window open.
- [ ] Set `ShowUnownedFortunes = false`, F9, reopen: back to owned only.
- Log: `Fortune window: N owned, M unowned shown greyed out (effects at L18)`

---

## FortuneUpgrade [NEW]
- [ ] Outside town, Fortune window, hover a fortune: tooltip says "Upgrade: only in town". Pressing **U** shows a popup saying the same, nothing is charged.
- [ ] In town, hover a fortune below your level: tooltip shows **Upgrade to L<yours>: <cost> gold (press U)**.
- [ ] The cost matches the formula: `(yourLevel - fortuneLevel) x 960 x rarity x 1.5`, rarity = Common 1 / Uncommon 1.5 / Rare 2 / Legendary 4 / Mythic 8. Example: Rare L8 -> L17 = 25,920.
- [ ] Press U: confirm dialog shows name, "L8 -> L17" and the cost. **No** closes it, nothing charged.
- [ ] **Yes**: party gold drops by the cost, the fortune's level text in the window updates, tooltip now says "already at your level".
- [ ] If the fortune was equipped, its effect numbers in the tooltip reflect the new level immediately.
- [ ] With less gold than the cost: Yes shows "Not enough gold", nothing changes.
- [ ] Hover a fortune already at your level: tooltip says "already at your level", U does nothing harmful.
- [ ] Switch to another character (SharedFortunes): that character's copy of the fortune is at the new level on next load (capped to their level).
- [ ] Reload the game: the upgraded level persisted.
- Log: `cost <name> <rarity> L8->L17: full X - already Y x1.5 = Z` and `<char> upgraded <name> L8 -> L17 for Z gold`

---

## SharedFortunes [REG]
- [ ] Load a character: log shows either "N fortunes added, N raised from the shared pool" or "already has everything in the pool".
- [ ] Earn a new fortune on one character, then load another: it appears in their available list, unequipped, marked new, at min(pool level, their level).
- [ ] Nothing ever disappears from any character's list; equipped slots unchanged.
- [ ] Roguelike character is unaffected (default `IncludeRoguelikeCharacters = false`).
- Log: `Scanned N character saves`, `Pool += <guid> L<n>`

---

## ScalingTooltips [REG]
- [ ] Hover a skill in a tree: every damage number has a grey "(0.7x AP)" style label right after it.
- [ ] A blank line, then **Scaling: 0.7 x 142 AP x 1.33 = 132 (112-152) · Power +12%, Physical +21%, Might +8%**. The result matches the number the game shows (the range in brackets = the game's range).
- [ ] A spell shows SP and its school, e.g. "(0.5x SP, Fire)". Basic attack shows "1x AP".
- [ ] Action bar and Examine-window skill tooltips show the same.
- [ ] Change a piece of gear with +X% physical damage: the Physical modifier in the Scaling line changes accordingly.
- Log: `[N] token 0 formula [...] -> 0.7x AP` and `breakdown: Scaling: ...`

---

## SpecialTooltips [REG]
- [ ] Right-click an enemy, hover each entry under Special: a tooltip explains it. Resist/weakness entries show that enemy's actual numbers.
- [ ] Hover a passive (unchanged vanilla behaviour) still works.
- [ ] Edit a line in `BepInEx\config\stolenrealm.specialtooltips.descriptions.txt`, press F9, hover again: new text.

---

## TargetTooltip [REG] + damage preview [NEW]
- [ ] In battle, hover an enemy with the attack cursor: full tooltip of the basic attack the click will use. Move off: it disappears.
- [ ] **[NEW]** The tooltip ends with **vs <Enemy>: 71-96 (45-60% of its health)** and a small grey line like "fire resist 50% · armor 12 · dodge 15%".
- [ ] **[NEW]** Attack that enemy: the damage number that pops up falls inside the previewed range (unless it crit or was dodged).
- [ ] **[NEW]** Hover a Fire Resistant enemy with a fire skill vs a normal enemy: the preview drops accordingly and the grey line names the resist.
- [ ] **[NEW]** An enemy at low health shows "kills" instead of a percentage when the whole range exceeds its health.
- [ ] **[NEW]** Compact style (F10) also shows the vs line under the icon and name.
- [ ] **[NEW]** Hovering an empty cell with a skill selected shows no vs line (no target).
- [ ] **[NEW]** Hover an enemy standing in a ground effect (fire, poison...): the skill tooltip still shows, with the effect's description under "On this tile" at the bottom. Hovering the same effect cell with no action still shows the vanilla ground tooltip.
- Log: `Preview <action> -> <enemy>: vs ...`
- [ ] Select a skill, hover a valid target cell: tooltip for that skill. Hover an invalid cell: none.
- [ ] Press **F10**: tooltip flips to compact (icon + name). F10 again: back. Setting persists after restart.
- [ ] Hovering the skill bar / inventory still shows their normal tooltips, no fighting between them.

---

## DropRates [REG]
- [ ] After a few fights, log has `[DropRates]` roll lines; commons rare, rares common, a legendary every few fights, mythics only from champions/bosses.
- [ ] Summoned adds (totems, cauldrons) log as vanilla rolls.
- [ ] The Merchant offers mythics noticeably often; gambling gives a spread of rarities.
- [ ] **[NEW] Town shops:** in Act 1, Zarek's Armory and Gareth's Trinkets show only blue or better; a legendary or mythic shows up now and then (about 1 in 7 slots). In later acts the share grows (Act 4 roughly 40% rare, 40% legendary, 20% mythic). Below character level 5 no mythics appear (game rule). Potion makers and island vendors unchanged.
- Log: `Town shop weights for '<shop>' (act N): Rare=.. Legendary=.. Mythic=..` on each restock, and at startup `town shops R/L/M A1 94/5/1, A2 88/10/2, A3 82/15/3, A4 76/20/4`.

---

## DifficultyXP [REG]
- [ ] On Classic, a battle's XP is 25% higher than the same fight on Adventurer (Veteran +50%, Torturous +75%). Quest completion XP too.
- [ ] Campfire "Prepared" adds its bonus to XP.
- Log: `ExperienceMod -> 1.25 (gold 25%)` and `Event XP a -> b`

---

## QoL [REG]
- [ ] Noor's upgrade cost equals the price difference between the item's level and yours, x1.5 (an item 2 levels behind is cheap; 1 level behind is cheaper still).
- Log: `Upgrade cost <item> a->b: vanilla X -> Y`

---

## LevelSync [NEW]
- [ ] Start the game: log shows "Highest level on account: L18.x (Killery Shanks) from 2 saves" and, when Jon Shadow loads, "Jon Shadow: L17.x -> L18.x (account highest)".
- [ ] Open Jon's skill tree: he has the extra unspent skill point(s) and stat points for the new level, health/mana bars still full.
- [ ] Play a fight with a party of two or more of your own characters, all at the same level: after the reward, everyone's XP bar moved by ONE battle's worth (not two or three). If one member ended higher (an XP fortune, say), the others log "party sync after XP for <name>" and catch up right away.
- [ ] Next session, a character that was not in the party is raised on load.
- [ ] Shiloh (roguelike) is untouched. `LevelSync.json` next to the saves shows the highest level and who set it.
- [ ] Create a brand-new character: the creation screen shows the normal starting points; after confirming and entering the game it is at the account level (log: "<name>: L1 -> L18.x (account highest)").
- [ ] Co-op as host with a friend at a different level: your characters follow YOUR highest only; the friend's level never appears in your log.
- [ ] Set `LevelSync = false` in `stolenrealm.mods.cfg`, F9: "LevelSync patches removed" and no further syncing.
- [ ] A character raised on load but never played that session keeps the level next launch (log: "<name>: level saved to file").
- [ ] Create a Roguelike character: it starts at level 1, not the account level, and gets no shared fortunes.

## AutoSalvage [NEW]
- [ ] First load of a character: log shows "<name> (bags on load): +N gold, sold N, stashed N"; the bag has no white/green/blue equipment or commodities left, materials AND recipe commodities (Wild Soul, Animal Hide, Fire Core...) are gone from the bag and present in storage, non-recipe commodities sold, equipped gear untouched, consumables untouched, legendaries/mythics untouched.
- [ ] **[REGRESSION 2026-09-15, fixed]** Win a fight that drops junk (battle loot arrives as a batch): the log shows `Sold ...` / `Stashed ...` lines and a `<name> (loot): +N gold, sold N, stashed N` line right after the post-battle screen, and the bag is clean. Same for an island event that gives several items. (Before the fix the batch was skipped entirely and every drop reached the bag.)
- [ ] Win a fight that drops junk: the post-battle loot list still shows the items, then an overhead "+N gold, sold N, stashed N" appears and the junk never reaches the bag. Legendary/mythic drops do.
- [ ] Buy a white/green/blue item from a shop: it stays in the bag (purchases are excluded). Craft something: it stays.
- [ ] Set `KeepRareAtOrAboveMyLevel = true`, F9: a Rare drop at your level or above is kept.
- [ ] Roguelike run: nothing is sold or stashed.
- [ ] **Stash really keeps it:** pick up a material or recipe commodity before entering town in a session (a character that loads with one in the bag counts). Log shows "Stash loaded early (N stacks)" once, then "Stashed ...". Enter town, open storage: the item is there. Quit and restart: still there.
- [ ] Create a new character: its starting kit (spare weapon, hood, jerkin, shield) is still in the bag, nothing sold. Log shows no "(loot)" lines during creation.
- [ ] **Bought gear is safe:** buy a blue or green item at a shop, leave it unequipped in the bag, then win a fight that drops junk. The bought item is still there; only the new drops were sold/stashed. Same for an item withdrawn from storage.
- [ ] **One-time cleanup:** on the first launch the log shows "Bag cleanup finished for N character(s); SweepBagsOnLoad set to false" and the config now says false. Later launches show no cleanup. A character you did NOT play that session still has its cleaned bag next launch (the save was forced).
- [ ] Co-op: a friend sends you an item; it stays in your bag (gifts are not loot).
- Log: per item "Sold <name> (<rarity> <type> L<n>) for <gold>" / "Stashed <name>"; a "Stash is not loaded; keeping materials in the bag" warning means the safety net fired and nothing was moved.

## SharedGold [NEW]
- [ ] Note each character's gold before the first run (character screen). After the first launch the log shows "Pool <total> gold held by <name>" and the total equals the sum you noted. Nothing gained, nothing lost.
- [ ] Play solo with character A: party gold = the whole pool. Switch to character B: B now shows the whole pool, A shows 0 (log: "holder is now B").
- [ ] Earn gold (fight/quest) and spend gold (shop, fortune upgrade): the pool moves by exactly those amounts.
- [ ] Party of two of your own characters: party gold shows the pool once, not twice.
- [ ] Co-op with a friend: your party gold is unchanged by their gold; sending gold to them works and deducts once. Their log must never show your characters.
- [ ] Roguelike character: gold untouched. `SharedGold.json` next to the saves shows the pool and holder.
- [ ] Set `SharedGold = false` in `stolenrealm.mods.cfg`, F9: patches removed; the pool stays on the last holder. Switch character: the gold does NOT follow (the mod is really off). Set true, F9: it follows again.
- [ ] Return to the main menu and back into a session (which makes the game reload its character objects), then check the pool total on the character screen: still the same number, not doubled.
- [ ] **Non-party persistence:** play character A so the purse moves to A, quit. Next launch pick B straight away and quit again without ever selecting A. Third launch: total is unchanged (A's file was force-saved with 0 when the purse moved to B). Log shows "-> B (both saved)" lines.
- [ ] At the character-select screen nothing moves at launch: the purse stays on whoever already holds it until you pick a character.
- [ ] Create a brand-new character while the pool exists: the pool never lands on the new character before it has a save file; after its first save it joins normally.
- Log at startup with everything on: for each character, LevelSync lines first, then SharedFortunes, then AutoSalvage's "bags on load", then one SharedGold "Pool ... (characters loaded, N characters)" line.

## BattleStats [NEW]
Host the session yourself (recording happens where the battle is resolved). The six ready-made characters **Test1..Test6** on your character list are the six builds from the test-plan PDF (Bloom, Ember, Chaplain, Bonecaller, Juggernaut, Cutthroat), level 18, common gear, all shared fortunes. Use them; no respec needed. One long fight is better than several short ones. The automated driver already fought with them and the rows add up; your fight judges whether the numbers make sense to a player.

### Stats window
- [ ] After the fight, open **Stats** on the post-battle screen. The table is now one scrolling list: the game's own rows first, then **Damage Breakdown**, **Hits**, **Damage By Element**, **Activity**. Mouse wheel over the table, dragging it, or the slim bar on the right scrolls; the character names at the top stay put; nothing runs over the Close button. Reopening the window starts at the top.
- [ ] For each character: Direct Hits + Over Time + Ground Tiles = the game's **Damage Dealt** row (Summons and Thorns are the game's own Summon Damage Dealt / Damage Returned repeated).
- [ ] Test1 (Bloom): most damage under Over Time (poison) and Summons; Thorns non-zero if anything hit them in melee. Test2 (Ember): Over Time (burn) large, Ground Tiles non-zero if someone stood in Burning Ground or walked into a tile. Test5 (Juggernaut) and Test6 (Cutthroat): almost all Direct Hits.
- [ ] **Hits / Crits**: the crit percentage looks right for the build (Cutthroat with Death Dealer high, others low). **Kills / Overkill**: kills add up to the number of enemies (a kill by a poison tick counts for the poisoner).
- [ ] **Biggest Hit** names the skill you remember hitting hardest; **Best Skill** is the skill with the biggest total. A status shows as "Name (tick)".
- [ ] **Damage Per Turn** = (Damage Dealt + Summons + Thorns) / that character's turns.
- [ ] **Damage By Element**: Ember all Fire, Bonecaller Shadow (+ Fire from the wand's weapon hits), Juggernaut/Cutthroat Physical; the header equals **Damage Dealt** exactly, and the **Untyped** row is 0 or tiny (anything there is damage whose element the mod could not identify: note the fight). **Damage Breakdown** header equals the game's **Total Damage** exactly.
- [ ] **Activity**: Casts counts every skill and weapon attack you clicked (free actions in brackets), Mana Spent adds up, Hexes Moved matches roughly how far each walked, Taken From Ticks = damage you took from enemy poison/burn.
- [ ] Hover a character's name at the top of the window: a tooltip lists their top damage sources with totals and hit counts. Move off: it closes.
- [ ] Hover any damage NUMBER (Damage Dealt, Total Damage, Summon Damage Dealt, Damage Returned, Direct Hits, Over Time, Ground Tiles, Summons, Thorns, each element, Untyped): a tooltip names that character and lists the abilities behind that number with hits and percentages; the lines add up to the cell. Damage Taken / Blocked / Healing cells show nothing. Scrolling still works with the pointer over cells.
- [ ] Open Stats after a second fight: the rows are still there, values are for the new fight only.
- [ ] Set `ShowElements = false`, F9, open Stats: the element section is gone, the others intact. Set `BattleStats = false` in the master file, F9, open Stats: vanilla window. Back to true, F9: rows return.
- [ ] Co-op with a modded friend, **you host**: their Stats window shows the same numbers as yours. **They host**: same. Friend without the mod hosting: your rows show `n/a`, nothing breaks for them.

### Log
- [ ] `===== BATTLE START` / `===== BATTLE SUMMARY (...)` around the fight; each character's summary damage equals their `GAME'S OWN TOTALS DamageDealt`.
- [ ] **Poison (The Bad Bloom, Poison Cloud, Poisoned Dagger...):** the game makes the victim poison itself, so vanilla credits nobody (or the poisoned player). The log now shows `POISON t<n> <victim> takes N from its stacks [Test1x6 ...]` followed by HIT lines crediting the poisoners as `[StatusTick: Poisoned]`; Test1's Damage Dealt and Over Time rows include the Bad Bloom poison; a poisoned player's own Damage Dealt does NOT grow from their poison.
- [ ] Poison ticks appear as `[StatusTick: <status>]` credited to the caster, tile entries as `[GroundEnter | <tile action>]`, burning ground ticks as `[TurnStart | ...]`; `KILL <enemy> by <player> (<skill>, <path>)` names the right person.
- [ ] Every HIT line names an element (note any without one); every skill you cast has a `CAST` line with the right mana cost.
- [ ] No `record failed` / `share failed` / `Stats window rows failed` warnings.
- Then send me `LogOutput.log`.

---

## ThreatOverlay [NEW] (test during the BattleStats battle)
- [ ] In battle, hold **Left Alt**: hexes enemies can walk to turn red, hexes they could hit from there turn orange. Release: the map looks normal again, your own move/target highlights come back.
- [ ] Sanity: an enemy's red area is roughly a circle of its movement points around it and stops at walls/gaps; a rooted or entangled enemy shows no red at all (only orange around where it stands). Archers show a wider orange ring than melee.
- [ ] Move one of your characters into an orange hex and end the turn: at least one enemy should be able to hit them. Move to a hex with no tint: nobody reaches you (barring teleports/dashes, which the overlay does not model).
- [ ] Hold the key while it is the enemies' turn and while one of them is moving: no errors, overlay updates every quarter second.
- [ ] Set `Mode = HoveredEnemy`, F9: holding the key now tints only the enemy under the mouse.
- [ ] Typing in co-op chat with Alt held does nothing odd; the overlay never appears outside battle.
- Log (verbose): `Threat overlay: N enemies, R reach + S strike hexes` and one line per enemy with its move budget and range.

## SharedProgress [NEW]
Test1..Test6 were generated with an EMPTY quest list, so they are the perfect test: before the mod they start the campaign from scratch.
- [ ] Launch: log shows `Account campaign progress: N quest nodes, main quest L<x>, act <y>, shop L<z> (from <name>) from 10 saves`, then `Test1: +N quest nodes, main quest L0 -> L<x>, act 1 -> <y>, ...` for each Test character (Jon/Killery/Zap say nothing or "already has"). `SharedProgress.json` sits next to the saves.
- [ ] Play **Test1 alone**: you land in the same town/act as Jon last visited; the quest map shows Jon's completed nodes and unlocks; the armorer's stock level matches what Jon sees. Jon/Killery/Zap unchanged (never lower).
- [ ] Complete one quest with Test1 alone, back in town, quit: log shows `Jon Shadow: +1 quest nodes ... (quest completed)` (and Killery, Zap, Test2..6); next session Jon's map shows that node done.
- [ ] Shiloh (Roguelike) file unchanged; a Roguelike run behaves as before.
- [ ] Mod menu: untick SharedProgress, Apply: no more merges (nothing is taken back); tick, Apply: log `Account campaign progress` again.
- [ ] No `[Warning:SharedProgress]` lines.

## NumberFormat [NEW]
- [ ] Gold in town shows as 418,218 (or whatever you have) everywhere it appears: top bar, shop, character sheet, party gold, fortune upgrade prices.
- [ ] Health/mana numbers, XP bar, damage floats in battle (a 1250 hit shows 1,250), the post-battle Stats window, tooltips (skill damage ranges, item stats, prices) all show separators; 3-digit numbers unchanged.
- [ ] Things that must NOT change: hex colours in text (no visible garbage), lobby / join codes, dates in the character list, timers, decimals like 12.5%, item level "L18", chat text you type, the Fortune/inventory search box while you type, quantities you type when sending gold.
- [ ] Verbose log: the first 40 `Formatted: '...' -> '...'` lines look sane (no codes or tags touched).
- [ ] Mod menu: untick NumberFormat, Apply: texts set from now on are plain again (already-displayed labels update when they next change).

## ModMenu (F9 window) [NEW]
- [ ] Startup log: `Loading [ModMenu 1.0.0]`, `Patched 0 methods`, every plugin says `Master config: 15/15 mods enabled`.
- [ ] In town press **F9**: a centred window "Stolen Realm Mods" with one checkbox row per mod (15) and "Verbose logging (all mods)", Apply and Cancel. Game input behind it is blocked. Escape or F9 closes it without changes; Cancel too.
- [ ] Untick TargetTooltip, Apply: window closes, every plugin logs `Config reloaded (14/15 mods enabled ...)`, TargetTooltip logs `patches removed`; in battle no mod tooltip on hover. F9, tick, Apply: `Patched 3 methods`, tooltip back. Same with ThreatOverlay (Alt) and AutoSalvage.
- [ ] Untick Verbose logging, Apply: each plugin logs `VerboseLogging = False`; tick, Apply: back to True.
- [ ] Edit a per-mod cfg in Notepad, F9, Apply with no box changed: the edit is picked up (`Config reloaded` lines, "(no mod changed; configs re-read)" in the ModMenu line).
- [ ] `stolenrealm.mods.cfg` after Apply still has the header comments, one line per mod and the VerboseLogging line.
- [ ] Set `ModMenu = false` in the file with Notepad, F9: no window, plugins reload directly like before. Set true, F9: window back.
- [ ] Typing F9 into chat, the Fortune search or inventory search: no window opens.
- [ ] Main menu (no character loaded): F9 either opens the window (Escape closes) or logs "no UI container found yet"; no error.
- [ ] Hover a row: the game's tooltip shows the mod's description. Click anywhere on a row: its box flips.

## Master switchboard (stolenrealm.mods.cfg) [NEW]
- [ ] `BepInEx\config\stolenrealm.mods.cfg` exists with one line per mod (all true) and `VerboseLogging = true`.
- [ ] Set `TargetTooltip = false`, press **F9** in game: log shows "TargetTooltip patches removed (2 methods) - mod is OFF"; hovering an enemy in battle no longer shows the mod tooltip. Set it back to true, F9: "Patched 2 methods" and the tooltip returns. No restart.
- [ ] Set `VerboseLogging = false`, F9: every plugin logs a "[General] VerboseLogging = False" line and the per-mod cfg files now say false too. Set true again.
- [ ] Delete the file, F9 or restart: it is recreated with defaults.
- [ ] Set `FortuneUpgrade = false`, F9, hover a fortune and press U: nothing happens (no confirm window). Back to true, F9: the U prompt is back.
- Log at startup: each plugin prints "Master config: 14/14 mods enabled, verbose on for all".

## General
- [ ] **F9** in game: every plugin logs "Config reloaded".
- [ ] No `[Error` or `[Warning :<ModName>]` lines in the log after a full session.
- [ ] Multiplayer session (host): friends without mods see nothing odd; friends with the zip see their own loot/XP/tooltips.

When all [NEW] items pass, tell me and I'll add FortunePreview and FortuneUpgrade to the share zip and PDF.
