import json, re, collections
sk = json.load(open("SkillInfo.json", encoding="utf-8"))
ai = json.load(open("ActionInfo.json", encoding="utf-8"))
st = json.load(open("ActionStatusInfo.json", encoding="utf-8"))
ca = json.load(open("CharacterAttribute.json", encoding="utf-8"))
lh = json.load(open("ListHolderSkills.json", encoding="utf-8"))[0]
A = {d["_path_id"]: d for d in ai}
S = {d["_path_id"]: d for d in st}
C = {d["_path_id"]: d for d in ca}
K = {d["_path_id"]: d for d in sk}
player = [p["m_PathID"] for p in lh["Skills"]]
TREE = {0: "Fire", 1: "Lightning", 2: "Cold", 3: "Warrior", 4: "Light", 5: "Ranger", 6: "Shadow", 7: "Thief",
        8: "Basic", 9: "Forms", 10: "Monk", 11: "Nature", 12: "Chaos"}
METHOD = {0: "+", 1: "+%", 2: "x", 3: "=", 4: "+%mult"}


def simp(e):
    e = e.replace("Source.", "").replace("Target.", "T.").replace("f ", " ").replace("f*", "*")
    e = re.sub(r"(\d)f\b", r"\1", e)
    e = e.replace("DamageModPhysical", "physMod").replace("DamageModHealing", "healMod")
    e = re.sub(r"DamageMod(\w+)", lambda m: m.group(1).lower() + "Mod", e)
    e = e.replace("SpellPower()", "SP").replace("AttackPower", "AP")
    return e.replace(" ", "")


def fill(desc, exprs):
    for i, e in enumerate(exprs):
        desc = desc.replace(f"*{i}", "[" + simp(e) + "]")
    return desc.replace("\n", " ").strip()


def attr_name(p):
    c = C.get(p["m_PathID"])
    return (c["DisplayName"] or c["m_Name"]) if c else f"attr{p['m_PathID']}"


def status_line(ss):
    parts = [f"{ss['Name'] or ss['m_Name']}"]
    d = fill(ss.get("Description", ""), ss.get("DescriptionExpressions", []))
    if d: parts.append(d)
    if ss.get("Duration"): parts.append(f"dur {ss['Duration']}")
    if ss.get("MaxStacks"): parts.append(f"max {ss['MaxStacks']} stacks")
    if ss.get("TauntsTarget"): parts.append("TAUNTS")
    if ss.get("CannotBeDispelled"): parts.append("undispellable")
    ae = [f"{attr_name(x['CharacterAttribute'])} {METHOD.get(x['CharacterEffectMethod'], x['CharacterEffectMethod'])} {x['Amount']}" for x in ss.get("AttributeEffects", [])]
    if ae: parts.append("effects: " + "; ".join(ae))
    return " | ".join(parts)


out = collections.defaultdict(list)
for pid in player:
    d = K.get(pid)
    if not d or d["DontIncludeInTree"]:
        continue
    tree = TREE.get(d["SkillType"], str(d["SkillType"]))
    lines = []
    kind = "PASSIVE" if not d["ActionsGranted"] else "ACTIVE"
    head = f"### T{d['Tier']} {d['SkillName']} ({kind})"
    dep = K.get(d["Dependency"]["m_PathID"])
    if dep: head += f" requires {dep['SkillName']}"
    rep = [K[x["m_PathID"]]["SkillName"] for x in d["SkillsThatReplace"] if x["m_PathID"] in K]
    if rep: head += f" replaces {', '.join(rep)}"
    if d["Disabled"]: head += " [DISABLED]"
    lines.append(head)
    lines.append(fill(d["Description"], d["DescriptionExpressions"]))
    for x in d["AttributeEffects"]:
        lines.append(f"- effect: {attr_name(x['CharacterAttribute'])} {METHOD.get(x['CharacterEffectMethod'], x['CharacterEffectMethod'])} {x['Amount']}")
    for p in d["ActionsGranted"]:
        a = A.get(p["m_PathID"])
        if not a: continue
        info = f"- action '{a['m_Name']}': type={a['ActionType']} cost={a['Cost']!r} manaRatio={round(a['ManaCostRatio'], 2)} cd={a['Cooldown']!r}"
        if a["HasCharges"]: info += f" charges={a['MaxCharges']}"
        if a["CanCastWhileDisabled"]: info += " castableWhileDisabled"
        if a["UseMaxRangeOverride"]: info += f" range={a['MaxRangeOverride']}"
        if a["Description"]: info += " | " + fill(a["Description"], a["DescriptionExpressions"])
        lines.append(info)
        for s in a["StatusEffects"]:
            ss = S.get(s["m_PathID"])
            if ss: lines.append("  - status: " + status_line(ss))
        for s in a.get("SourceStatusEffects", []):
            ss = S.get(s["m_PathID"])
            if ss: lines.append("  - self-status: " + status_line(ss))
    for s in d["PassiveActionStatuses"]:
        ss = S.get(s["m_PathID"])
        if ss: lines.append("- passive status: " + status_line(ss))
    out[tree].append((d["Tier"], d["xVal"], "\n".join(lines)))

for tree, items in out.items():
    items.sort(key=lambda t: (t[0], t[1]))
    open(f"tree_{tree}.md", "w", encoding="utf-8").write(f"# {tree} ({len(items)} skills)\n\n" + "\n\n".join(t[2] for t in items))
    print(tree, len(items))
