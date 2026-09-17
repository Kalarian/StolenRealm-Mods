**RETIRED 2026-09-17** (user: "we won't be using it until it's completed"): not in gen_master/pack_zip/ModMenu, not shipped; re-add to those lists to revive.

# BardPreview

Unlocks the Bard skill tree, which ships complete in the game files (31 skills, `SkillType.Bard`) but is hidden behind
the unreleased Bard Pack DLC. Master-config plugin like the others: `BardPreview = true` in
`BepInEx\config\stolenrealm.mods.cfg`, a row in the F9 mod window, own settings in `stolenrealm.bardpreview.cfg`.

## The one rule

The unlock only works while the game itself marks the pack as **Hidden** (its "unreleased" flag on
`SteamManager.DLCInfos`). The day the Bard Pack is released for sale, that flag flips, this mod does nothing for it,
and the game's normal purchase check applies. There is no setting to change that. The mod previews content the game
already ships but has not switched on; it never bypasses a purchase.

## How it works

- `SkillTreeManager.Initialize` only shows the Chaos/Bard tabs when `!SteamManager.IsSkillTypeHidden(type)`;
  `ChooseTree`, `Character.Skills` and `Character.SkillsFromPoints` require `SteamManager.MeetsDLCRequirements(type)`.
  Every one of those funnels through the private `SteamManager.IsFreeAccess(DlcType)`: a DLC in `FreeAccessDlcs` counts
  as neither hidden nor unowned. The game's own `BardSkillTest` harness grants the tree exactly that way.
- `PreviewPatches.FreeAccess`: prefix on `IsFreeAccess` that answers true when the DLC locks a configured tree
  (`Trees`, default `Bard`, looked up in `SteamManager.DlcLockedSkillTypes`) **and** `GetDlcInfo(dlc).Hidden` is true.
- `PreviewPatches.TabRevive`: prefix on `SkillTreeManager.Initialize` that re-activates every tab under `tabHolder`
  before the game's loop runs. The game collects tabs with `GetComponentsInChildren` (active only) and deactivates a
  hidden DLC tab, so without this a tab hidden once could never come back in the session (matters when the mod is
  switched on after the skill tree was already opened).
- After a config reload or switchboard change the mod dirties `SkillsFromPointsCalculation`, `SkillsCalculation` and
  `SkillTriggersCalculation` on the player's own characters (the same caches the game's harness dirties), outside battle.

## Caveats

- **Co-op:** every client computes a character's skill list locally through its own DLC check. A peer without this mod
  drops your Bard skills from their copy of your character, so everyone in the session needs the pack installed.
- **Switching it off later:** learned Bard skills stay in the save (skills are stored as raw GUIDs) but become invisible
  and the points stay spent. Respec in town while the mod is still on before turning it off.
- Nothing else is gated: saves, loot, quests and achievements never look at the DLC flag.
