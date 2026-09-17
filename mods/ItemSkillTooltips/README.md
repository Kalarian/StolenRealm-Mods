# ItemSkillTooltips

Hover an item that grants a skill or a passive and the skill's own tooltip appears beside the item tooltip (and beside
the equipped-item comparison tooltip when that is up). Master-config plugin: `ItemSkillTooltips = true` in
`BepInEx\config\stolenrealm.mods.cfg`, a row in the F9 mod window, own settings in `stolenrealm.itemskilltooltips.cfg`.

## What counts as a granted skill

- `ItemInfo.GrantedSkills`: the list the item tooltip prints under "Skills Granted" (a weapon's basic attack, SkillType
  Basic, is skipped like the game skips it). 14 items and 22 weapons in the current build grant a real skill this way.
- `{SKL=Name}` tokens in the item's Special text (`ItemInfo.OptionalDescription`), e.g. "Grants the passive skill
  {SKL=Child of the Abyss}" or "10% chance to cast Level [level value] {SKL=Blinding Light}". 49 items use these; the
  game colours the name but never explains it. Resolved with `Tooltip.FindSkillByName`.

## How it works

- Postfix on both `Tooltip.ShowItemTooltip` overloads (live `Item` from bag/shop/loot/character sheet, and `ItemInfo`
  from crafting): collect the skills, render up to `MaxPanels` of them.
- The panels are clones of the game's own "Comp Tooltip" object with `HasStaticLayout = true`, so the game's positioning
  code leaves them alone. `Tooltip.ShowSkillTooltip(skill, TooltipCharacter, showCanLevelDetail:false, fromLink:false,
  showTierData:true)` renders each one (ScalingTooltips' annotations apply automatically because they patch the same
  text pipeline).
- Placement: the same rule the game uses for the comparison tooltip. On the free side of the item tooltip (right when
  its pivot.x is 0, else left), at the previous panel's `compAnchorRight`/`compAnchorLeft`, then
  `GetGUIElementOffset()` pushes it back on screen. A postfix on `Tooltip.Update` of the host keeps it there.
- Item-level numbers: `Tooltip.ShowSkillTooltip` sizes bracketed values at the item's level only for an equipped item
  (`SkillInfo.ObtainedByItem` scans `EquippedItems`). While our panels render, a prefix on `ObtainedByItem` returns the
  hovered item for its own skills, so a bag or shop item shows what equipping it would give (`ItemLevelNumbers`).
- Any other tooltip on the main panel (`ShowTooltip` prefix) or `HideTooltip` on it hides the panels.
