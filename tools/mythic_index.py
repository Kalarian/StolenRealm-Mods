"""Build mythic item index + source map from the dumped JSON. Run from stolen-realm/data."""
import json, collections, re
L = lambda f: json.load(open(f + ".json", encoding="utf-8"))
ii, wi, ci, sk, ai, st, ca, cr, sh, q, g = (L(x) for x in ("ItemInfo", "WeaponInfo", "CharacterInfo", "SkillInfo", "ActionInfo", "ActionStatusInfo", "CharacterAttribute", "CraftingRecipe", "Shopkeeper", "QuestInfo", "GlobalSettings"))
g = g[0]
items = {i["_path_id"]: i for i in ii + wi}
K = {d["_path_id"]: d for d in sk}; A = {d["_path_id"]: d for d in ai}; S = {d["_path_id"]: d for d in st}; C = {d["_path_id"]: d for d in ca}
CI = {d["_path_id"]: d for d in ci}
ITEMTYPE = ["Weapon", "Shield", "Head", "Armor", "Ring", "Amulet", "Consumable", "Material", "Tool", "Commodity"]
EQUIP = ["1H Sword", "2H Axe", "2H Sword", "Polearm", "Bow", "1H Gun", "2H Gun", "1H Axe", "Staff", "Wand", "1H Mace", "2H Mace", "Unarmed", "Shield", "Fist Weapon", "None"]
ENEMYTYPE = {0: "Fodder", 1: "Soldier", 2: "Elite", 3: "Boss", 4: "Champion"}  # matches decomp/EnemyType.cs (Champion = 4, Boss = 3)
METHOD = {0: "+", 1: "+%", 2: "x", 3: "=", 4: "+%mult"}
pid = lambda p: p["m_PathID"] if isinstance(p, dict) else p


def attr(p):
    c = C.get(pid(p)); return (c["DisplayName"] or c["m_Name"]) if c else f"attr{pid(p)}"


def fill(desc, exprs):
    for i, e in enumerate(exprs or []):
        desc = desc.replace(f"*{i}", "[" + e + "]")
    return desc.replace("\n", " ").strip()


def status_txt(ss):
    parts = [ss["Name"] or ss["m_Name"], fill(ss.get("Description", ""), ss.get("DescriptionExpressions"))]
    if ss.get("Duration"): parts.append(f"dur {ss['Duration']}")
    ae = [f"{attr(x['CharacterAttribute'])} {METHOD.get(x['CharacterEffectMethod'])} {x['Amount']}" for x in ss.get("AttributeEffects", [])]
    if ae: parts.append("effects: " + "; ".join(ae))
    return " | ".join(p for p in parts if p)


def action_txt(a):
    if not a: return "?"
    s = f"{a['m_Name']}"
    if a.get("Description"): s += ": " + fill(a["Description"], a.get("DescriptionExpressions"))
    for x in a.get("StatusEffects", []):
        ss = S.get(pid(x))
        if ss: s += " || status " + status_txt(ss)
    return s


def skill_txt(p):
    d = K.get(pid(p))
    if not d: return f"skill{pid(p)}"
    s = f"{d['SkillName']}: {fill(d['Description'], d['DescriptionExpressions'])}"
    for x in d["AttributeEffects"]:
        s += f" || effect {attr(x['CharacterAttribute'])} {METHOD.get(x['CharacterEffectMethod'])} {x['Amount']}"
    for x in d["ActionsGranted"]:
        a = A.get(pid(x))
        if a: s += f" || action(type={'free' if a['ActionType']==1 else 'action'}, cd={a['Cooldown']}) " + action_txt(a)
    for x in d["PassiveActionStatuses"]:
        ss = S.get(pid(x))
        if ss: s += " || passive status " + status_txt(ss)
    return s


def trigger_txt(t):
    parts = [f"TriggerType={t['TriggerType']}"]
    if t.get("Condition", "").strip(): parts.append("if " + t["Condition"].replace("\r", " ").replace("\n", " ").strip())
    if t.get("ActionChanceEquations"): parts.append("chance " + "/".join(t["ActionChanceEquations"]) + "%")
    for x in t.get("Actions", []):
        parts.append("action " + action_txt(A.get(pid(x))))
    for x in t.get("ActionStatuses", []):
        ss = S.get(pid(x))
        if ss: parts.append("status " + status_txt(ss))
    if t.get("ActionStatusChanceEquations"): parts.append("status chance " + "/".join(t["ActionStatusChanceEquations"]) + "%")
    if t.get("Targets"): parts.append("targets " + t["Targets"].strip())
    if t.get("MaxNumUses"): parts.append("max uses " + t["MaxNumUses"])
    if t.get("Cooldown"): parts.append(f"cooldown {t['Cooldown']}")
    for ge in t.get("GeneralEffects", []):
        parts.append("general " + json.dumps(ge)[:200])
    return " ; ".join(parts)


# ---- sources ----
sources = collections.defaultdict(list)
for c in ci:
    for lt in c.get("PersonalLoot") or []:
        for x in lt.get("itemLootChances", []):
            sources[pid(x["ItemInfo"])].append({"kind": "personal_loot", "enemy": c["m_Name"], "enemyType": ENEMYTYPE.get(c.get("enemyType"), c.get("enemyType")), "group": c.get("EnemyGroup"), "minLevel": c.get("MinLevel"), "maxLevel": c.get("MaxLevel"), "chance": x["Chance"], "mustGetOne": lt.get("mustGetOne"), "nInTable": len(lt.get("itemLootChances", [])), "excludeFromPool": c.get("ExcludeFromBattlePool")})
for lt in g["LootTables"]:
    for x in lt["LootTable"].get("itemLootChances", []):
        sources[pid(x["ItemInfo"])].append({"kind": "group_loot_table", "group": lt["EnemyGroup"], "chance": x["Chance"], "mustGetOne": lt["LootTable"]["mustGetOne"]})
for x in g["GlobalLootTable"].get("itemLootChances", []):
    sources[pid(x["ItemInfo"])].append({"kind": "global_loot_table", "chance": x["Chance"]})
for r in cr:
    res = r["Result"]
    sources[pid(res["item"])].append({"kind": "recipe", "recipe": r["m_Name"], "rarity": r["Rarity"], "gold": r["GoldCost"], "required": [(items.get(pid(x["item"]), {}).get("m_Name", pid(x["item"])), x["quantity"]) for x in r["Required"]], "stacks": res.get("stacks"), "resultType": res.get("craftingResultType")})
for x in q:
    r = pid(x["rewardItem"])
    if r: sources[r].append({"kind": "quest_reward", "quest": x["questName"], "act": x["act"], "level": x["questLevel"], "main": x["isMainQuest"], "boss": x["bossName"]})
# quests naming bosses -> which boss CharacterInfo
boss_quests = collections.defaultdict(list)
for x in q:
    # only current campaign quests ("Act N-M"); "[N-Main]" and act-0 entries are legacy per MiscSettings.OldQuests
    if not x["m_Name"].startswith("Act "): continue
    b = pid(x["boss"]) if isinstance(x["boss"], dict) else None
    if b: boss_quests[b].append((x["questName"], x["act"], x["questLevel"], x["isMainQuest"]))
shopnames = {s["_path_id"]: s["m_Name"] for s in sh}

# ---- mythics ----
out = []
for i in ii + wi:
    if i["Rarity"] != 4: continue
    d = {"name": i["m_Name"], "path_id": i["_path_id"], "itemType": ITEMTYPE[i["ItemType"]], "isWeapon": "EquipmentType" in i}
    if "EquipmentType" in i:
        d["equipment"] = EQUIP[i["EquipmentType"]]; d["damageRatio"] = i["DamageRatio"]; d["attackRange"] = i["AttackRange"]; d["damageType"] = i["DamageType"]
    d["stats"] = {k: i[k] for k in ("Might", "Dexterity", "Intelligence", "Vitality", "Reflex") if i[k]}
    d["armorRatio"] = i["ArmorRatio"]; d["magicArmorRatio"] = i["MagicArmorRatio"]; d["armorRatioShield"] = i["ArmorRatioShield"]; d["magicArmorRatioShield"] = i["MagicArmorRatioShield"]
    d["scaledStats"] = i["ScaledStats"].replace("\n", " / ").strip()
    d["description"] = i["OptionalDescription"].replace("\n", " ").strip()
    d["attributeEffects"] = [f"{attr(x['CharacterAttribute'])} {METHOD.get(x['CharacterEffectMethod'])} {x['Amount']}" for x in i["AttributeEffects"]]
    d["attributeEffectsScaling"] = i["AttributeEffectsScaling"]
    d["grantedSkills"] = [skill_txt(p) for p in i["GrantedSkills"]]
    d["triggers"] = [trigger_txt(t) for t in i["SkillTriggers"]]
    d["consumable"] = action_txt(A.get(pid(i["ConsumableAction"]))) if pid(i["ConsumableAction"]) else ""
    d["minLevel"] = i["MinLevel"]; d["removeFromUnassignedPool"] = i["RemoveFromUnassignedPool"]; d["allowInAllShops"] = i["AllowInAllShops"]
    d["allowedShops"] = [shopnames.get(pid(p), pid(p)) for p in i["AllowedShops"]]
    d["disallowedMods"] = len(i["DisallowedMods"]); d["fixedBuy"] = i["FixedBuyPrice"] if i["HasFixedBuyPrice"] else None
    d["sources"] = sources.get(i["_path_id"], [])
    for s_ in d["sources"]:
        if s_["kind"] == "personal_loot":
            cpid = next((c["_path_id"] for c in ci if c["m_Name"] == s_["enemy"]), None)
            s_["quests"] = boss_quests.get(cpid, [])
    out.append(d)
out.sort(key=lambda d: (d["itemType"], d["name"]))
json.dump(out, open("mythics.json", "w", encoding="utf-8"), indent=1, ensure_ascii=False)
print(len(out), "mythics written")
print("by type:", collections.Counter(d["itemType"] for d in out))
print("with no explicit source:", sum(1 for d in out if not d["sources"]), "| removed from pool AND no source:", [d["name"] for d in out if d["removeFromUnassignedPool"] and not d["sources"]])
print("source kinds:", collections.Counter(s["kind"] for d in out for s in d["sources"]))
