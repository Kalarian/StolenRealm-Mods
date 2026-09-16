"""Compare the bodies of every game method our mods patch between two decompiles (decomp_20244830 = the 2025-06 build, decomp = the current build).
Usage: python tools/diff_patched_methods.py [old_dir] [new_dir]
Prints SAME / CHANGED / MISSING per Class.Method (all overloads concatenated), with a unified diff for the changed ones."""
import os, re, sys, difflib

OLD = sys.argv[1] if len(sys.argv) > 1 else "decomp_20244830"
NEW = sys.argv[2] if len(sys.argv) > 2 else "decomp"
TARGETS = """Character.AddFortune Character.ExecutePerformAction Character.GetLootDrop Character.GiveExperience Character.GiveGold
Character.LevelUpAllMyFortunesToMyLevel Character.Load Character.ProcessAttributesOnNewTurn Character.ProcessDeath Character.ProcessSkillTriggers
Character.Save Character.ApplyAction Character.ModifyHealth CraftingManager.CraftRecipe CraftingManager.CraftSelectedRecipe DifficultySetting.ExperienceMod
EventOption.InitActions EventOptionSelectionItem.PopulateResultEffects ExamineWindow.ShowCharacterExamine FortuneSlot.HideTooltip FortuneSlot.ShowTooltip
FortuneWindow.SelectedAvailableSlot FortuneWindow.UpdateFortuneWindow GamblingManager.GambleItem GamblingManager.PopulateGamblingItems
GameLogic.FinalizeCharacterCreation GameLogic.GiveItem GameLogic.GiveItems GameLogic.StartNewTurn GameLogic.AddGroundEffect GroundEffect.ExecuteActionOnEnter
HexCell.UpdateHexCell HexCellManager.CurrentlyHoveringHexCell InventoryManager.GiveItemToAnother ItemUpgradeManager.GetUpgradeCost ItemUpgradeManager.UpgradeItem
LootTable.GetGuaranteedWorldLoot LootTable.GetLoot LootTable.GetWorldLoot PostBattleManager.OpenPostBattleMenu RoguelikeManager.ConfirmLevelUpSelection
ShopItemTypeChanceSet.GetRarityChances ShopManager.BuySelectedItem ShopManager.RefreshItemDictSingle StatManager.BuildOutLabels StatManager.ClearStats
StatManager.GetStatDisplay StatManager.ModifyBattleStat StatManager.PopulateStats Tooltip.ApplyDescriptionExpressions Tooltip.GetDamageString
Tooltip.ShowActionStatusTooltip Tooltip.ShowGroundEffectTooltip Tooltip.ShowQuestNodeTooltip Tooltip.ShowTooltip Tooltip.ShowUniversalTooltip
HexCellManager.HexCost ItemStashData.LoadStash ItemStashData.AddItemToStash ItemStashData.SaveStash Character.GetPrice Root.CreateNewCharacterBattleStatSet""".split()

def find_file(d, cls):
    for root, _, files in os.walk(d):
        if cls + ".cs" in files:
            return os.path.join(root, cls + ".cs")
    return None

def methods(text, name):
    """All method bodies named `name` (any overload), by brace matching from the signature line."""
    out = []
    for m in re.finditer(r"(?m)^\t(?:\[[^\n]*\]\n\t)*(?:public|private|protected|internal)[^\n=;]*\b" + re.escape(name) + r"\s*(?:<[^>]*>)?\(", text):
        i = text.find("{", m.end())
        if i < 0: continue
        # a lambda/expression-bodied member would have ';' before '{'
        if ";" in text[m.end():i]: continue
        depth, j = 0, i
        while j < len(text):
            c = text[j]
            if c == "{": depth += 1
            elif c == "}":
                depth -= 1
                if depth == 0: break
            j += 1
        out.append(text[m.start():j + 1])
    return "\n".join(out)

changed = same = missing = 0
for t in TARGETS:
    cls, name = t.split(".")
    fo, fn = find_file(OLD, cls), find_file(NEW, cls)
    if not fo or not fn:
        print("MISSING FILE", t, "" if fo else "(old)", "" if fn else "(new)"); missing += 1; continue
    bo, bn = methods(open(fo, encoding="utf-8").read(), name), methods(open(fn, encoding="utf-8").read(), name)
    if not bo or not bn:
        print("MISSING METHOD", t, "old" if not bo else "", "new" if not bn else ""); missing += 1; continue
    if bo == bn:
        same += 1
    else:
        changed += 1
        print("CHANGED", t)
        for line in difflib.unified_diff(bo.splitlines(), bn.splitlines(), "old", "new", lineterm="", n=1):
            if line.startswith(("+", "-")) and not line.startswith(("+++", "---")):
                print("   ", line[:160])
print("same %d, changed %d, missing %d" % (same, changed, missing))
