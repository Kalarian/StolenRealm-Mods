"""Render fortunes.md from data/EventStatus.json + PartyEvent.json. Run from stolen-realm/data."""
import json, re, collections
L = lambda f: json.load(open(f + ".json", encoding="utf-8"))
es, pe, ca, sk, ai, st = (L(x) for x in ("EventStatus", "PartyEvent", "CharacterAttribute", "SkillInfo", "ActionInfo", "ActionStatusInfo"))
C = {c["_path_id"]: (c["DisplayName"] or c["m_Name"]) for c in ca}
K = {d["_path_id"]: d for d in sk}; A = {d["_path_id"]: d for d in ai}; S = {d["_path_id"]: d for d in st}
PE = {p["_path_id"]: p for p in pe}
RAR = ["Common", "Uncommon", "Rare", "Legendary", "Mythic"]
METHOD = {0: "+", 1: "+%", 2: "x", 3: "=", 4: "+%mult"}
T = ["Water Temple Ruins", "Wyrmrest Desert", "Freewind Forest", "Castle Gloom", "Forgotten Mines", "Emberlands", "Emerald Jungle", "Frostwrought Mountain", "Sunken Swamplands", "Dwarven Halls"]
TRIG = {19: "on hitting (any)", 0: "on dealing damage", 20: "on healing", 21: "on being hit (any)", 1: "on taking damage", 22: "on being healed", 2: "on casting", 3: "on any death (not self)", 4: "on turn start", 5: "on turn end", 6: "on battle start", 7: "on action completed", 8: "on moving", 9: "on death", 10: "on action completed per target", 11: "on moving or warping", 12: "on any death", 13: "on crit", 14: "on dodge", 15: "after my death", 16: "after any character death", 29: "after any character death (final)", 17: "on any summon spawn", 18: "on warp", 23: "on hitting incl. procs", 24: "on dealing damage incl. procs", 25: "on healing incl. procs", 26: "on being hit incl. procs", 27: "on taking damage incl. procs", 28: "on being healed incl. procs", 30: "on globule pickup"}
pid = lambda p: p["m_PathID"] if isinstance(p, dict) else p


def clean(s):
    s = re.sub(r"<i><color=#808080>.*?</color></i>|<i><color=#808080>.*?</i></color>", "", s, flags=re.S)  # flavour text
    s = re.sub(r"<[^>]+>", "", s)
    s = re.sub(r"\{\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*\}", lambda m: f"{m.group(1)}→{m.group(2)}", s)
    s = s.replace("{SKL=", "").replace("{STA=", "").replace("}", "").replace("@", "")
    return re.sub(r"\s+", " ", s).strip().replace("|", "/")


def flavour(s):
    m = re.search(r"<i><color=#808080>(.*?)</color></i>|<i><color=#808080>(.*?)</i></color>", s, flags=re.S)
    if not m: return ""
    return clean(m.group(1) or m.group(2) or "")


def fill(desc, exprs):
    for i, e in enumerate(exprs or []):
        desc = desc.replace(f"*{i}", "[" + e + "]").replace(f"[{i}]", "[" + e + "]")
    return desc


def status_txt(ss):
    parts = [ss.get("Name") or ss["m_Name"], clean(fill(ss.get("Description", ""), ss.get("DescriptionExpressions")))]
    if ss.get("Duration"): parts.append(f"dur {ss['Duration']}")
    ae = [f"{C.get(pid(x['CharacterAttribute']))} {METHOD.get(x['CharacterEffectMethod'])} {x['Amount']}" for x in ss.get("AttributeEffects", [])]
    if ae: parts.append("effects: " + "; ".join(ae))
    return " / ".join(p for p in parts if p)


def action_txt(a):
    if not a: return "?"
    s = a["m_Name"]
    if a.get("Description"): s += ": " + clean(fill(a["Description"], a.get("DescriptionExpressions")))
    for x in a.get("StatusEffects", []):
        ss = S.get(pid(x))
        if ss: s += " // status " + status_txt(ss)
    return s


def trigger_txt(t):
    parts = [TRIG.get(t["TriggerType"], f"trigger {t['TriggerType']}")]
    if t.get("Condition", "").strip(): parts.append("if " + re.sub(r"\s+", " ", t["Condition"]).strip())
    if t.get("ActionChanceEquations"): parts.append("chance " + "/".join(t["ActionChanceEquations"]) + "%")
    for x in t.get("Actions", []):
        parts.append("action " + action_txt(A.get(pid(x))))
    for x in t.get("ActionStatuses", []):
        ss = S.get(pid(x))
        if ss: parts.append("status " + status_txt(ss))
    if t.get("ActionStatusChanceEquations"): parts.append("status chance " + "/".join(t["ActionStatusChanceEquations"]) + "%")
    if t.get("MaxNumUses"): parts.append("max uses " + t["MaxNumUses"])
    if t.get("Cooldown"): parts.append(f"cooldown {t['Cooldown']:g}")
    return " ; ".join(parts)


# ---- event graph: child event -> parents (via chainedEvent), and fortune -> (event, option, action, how)
parents = collections.defaultdict(set)
grants = collections.defaultdict(list)
EVENT_IDS = set(PE)


def link_children(o, parent_id):
    """Any PPtr inside an event that points at another PartyEvent (chainedEvent, VictoryEvent, DefeatEvent...) makes that event a child."""
    if isinstance(o, dict):
        t = o.get("m_PathID")
        if isinstance(t, int) and t in EVENT_IDS and t != parent_id and o.get("m_FileID", 0) == 0:
            parents[t].add(parent_id)
        for v in o.values(): link_children(v, parent_id)
    elif isinstance(o, list):
        for v in o: link_children(v, parent_id)


for p in pe:
    link_children(p.get("eventOptions", []), p["_path_id"])
    for oi, o in enumerate(p.get("eventOptions", [])):
        for ai_, a in enumerate(o.get("eventActions", [])):
            for e in a.get("EventActionEffects", []):
                for s_ in e.get("eventStatuses", []):
                    grants[pid(s_)].append((p, o, a, e))
# events forced by an event status (ForcedNextEvent / ForcedEventBeforeBoss / ForcedEventAtBoss) or required by one
# (requiredStatusesToSpawn): the events that GRANT that status are the parents.
ES_BY_ID = {s_["_path_id"]: s_ for s_ in es}
for st_ in es:
    for key in ("ForcedNextEvent", "ForcedEventBeforeBoss", "ForcedEventAtBoss"):
        t = pid(st_.get(key)) if st_.get(key) else 0
        if t in EVENT_IDS:
            for (gp, _o, _a, _e) in grants.get(st_["_path_id"], []):
                if gp["_path_id"] != t: parents[t].add(gp["_path_id"])
for p in pe:
    for r in p.get("requiredStatusesToSpawn", []):
        sid = pid(r)
        for (gp, _o, _a, _e) in grants.get(sid, []):
            if gp["_path_id"] != p["_path_id"]: parents[p["_path_id"]].add(gp["_path_id"])
for p in pe:  # name-based fallback for victory/success follow-ups
    m = re.match(r"(.+?)\s*-?\s*(Victory|Battle|Success|Win|Defeat|Failure)", p["m_Name"])
    if m:
        root = next((q for q in pe if q["m_Name"] == m.group(1).strip()), None)
        if root and root["_path_id"] != p["_path_id"]: parents[p["_path_id"]].add(root["_path_id"])


def roots(p, seen=None):
    seen = seen or set()
    if p["_path_id"] in seen: return []
    seen.add(p["_path_id"])
    ps = parents.get(p["_path_id"])
    if not ps or (p.get("EventSpawnTypes") != [3]):
        return [p]
    out = []
    for q in ps:
        out += roots(PE[q], seen)
    return out or [p]


def ev_where(p):
    terr = "any terrain" if p.get("allowOnAllTerrainTypes") else ", ".join(T[t] for t in p.get("allowedTerrainTypes", []))
    lvl = "" if p.get("allowAtAllLevels") else f", level {p.get('minLevel')}+"
    modes = {0: "Campaign", 1: "Roguelike"}
    md = "+".join(modes.get(m, str(m)) for m in p.get("GameModes", []))
    spawn = "chained (not in the random pool)" if p.get("EventSpawnTypes") == [3] else "random island event"
    extra = ""
    if p.get("requiredStatusesToSpawn") and any(pid(r) for r in p["requiredStatusesToSpawn"]): extra = ", requires a prior status"
    return f"{terr}{lvl}, {md}, {spawn}{extra}"


def how_txt(o, a, e):
    how = []
    if o.get("title"): how.append(f"option \"{clean(o['title'])}\"")
    if e.get("pickRandomStatus") and len(e.get("eventStatuses", [])) > 1:
        how.append(f"{e.get('randomStatusCount', 1)} random of {len(e['eventStatuses'])}")
    rs = a.get("eventResultSetting") or {}
    RT = {0: "success", 1: "failure", 2: "critical success", 3: "critical failure"}
    if rs.get("RollLimit"): how.append(f"on a {RT.get(rs.get('EventResultType'), '?')} roll (need {rs['RollLimit']}+; Might/Dex/Vit/Int/Reflex can add a bonus)")
    elif rs.get("EventResultType"): how.append(f"on {RT.get(rs['EventResultType'])}")
    if a.get("startBattle"): how.append("after winning the battle")
    if o.get("mysteryRollValue"): how.append(f"mystery roll {o['mysteryRollValue']}")
    return ", ".join(how)


F = [s for s in es if s.get("StatusType") == 1]
F.sort(key=lambda s: (-s.get("Rarity", 0), s["m_Name"]))
Lns = ["# Stolen Realm — Fortunes (complete list)\n",
       "Source: `data/EventStatus.json` (fortunes are event statuses with StatusType = Fortune) and `data/PartyEvent.json` (which island events grant them), plus the decompiled award code (`EventWindow.cs`, `Character.AddFortune`, `FortuneWindow.cs`). Build 2025-06-17 (Chaos Pack era).\n",
       f"Total fortunes: **{len(F)}** — " + ", ".join(f"{RAR[r]} {n}" for r, n in sorted(collections.Counter(s.get('Rarity', 0) for s in F).items(), reverse=True)) + ".\n",
       "## How fortunes work (verified in code)\n",
       """- A fortune is a permanent passive earned from an **island event**. When an event outcome lists a fortune, every party character gets it (`EventWindow` → `SendAddFortuneToClient`) at a **fortune level equal to the current game level, capped at 30**. Earning the same fortune again at a higher game level raises its level; it never goes down.
- Numbers written `a→b` scale linearly with fortune level from level 1 (a) to level 30 (b). Roguelike has its own "fortunes level with you" rule.
- **Slots:** 4 fortune slots, unlocked at character levels 1, 8, 15 and 22. Equip/unequip freely in the Fortune window.
- **Rarity rule:** while the campaign is incomplete you cannot equip two fortunes of the same rarity at once (the live setting `RestrictFortuneRarities` is currently off, so this may not be enforced in this build; the Endless unlock text says the restriction is removed on campaign completion either way).
- Fortunes are per character and saved with the character; they carry into Endless and (if the option allows) Roguelike.
- "Where" below names the island event that grants the fortune. Events marked *chained* are not in the random pool themselves; they are follow-ups of the parent event listed with them (e.g. a "- Victory" event after winning the event's battle). Terrain and level come from the parent event that actually spawns on the map.
"""]
for s in F:
    name = s["m_Name"]
    Lns.append(f"### {name}  — {RAR[s.get('Rarity', 0)]}")
    desc = clean(s.get("statusDescription", ""))
    fl = flavour(s.get("statusDescription", ""))
    if desc: Lns.append(f"- Effect: {desc}")
    for e in s.get("eventStatusEffects", []):
        Lns.append(f"- Attribute: {C.get(pid(e['characterAttribute']), '?')} {METHOD.get(e['method'])} {clean(e['amount'])}")
    for g in s.get("GrantedSkills", []):
        d = K.get(pid(g))
        if d: Lns.append(f"- Granted skill: {d['SkillName']}: {clean(fill(d['Description'], d['DescriptionExpressions']))}")
    for t in s.get("SkillTriggers", []):
        Lns.append(f"- Trigger: {clean(trigger_txt(t))}")
    for lk in s.get("SkillChangeLinks", []):
        o = K.get(pid(lk.get("OldSkillInfo"))); n = K.get(pid(lk.get("NewSkillInfo")))
        if o and n:
            Lns.append(f"- Replaces skill: {o['SkillName']} -> {n['SkillName']}: {clean(fill(n['Description'], n['DescriptionExpressions']))}")
        else:
            Lns.append(f"- Modifies skill: {json.dumps(lk)[:160]}")
    srcs = grants.get(s["_path_id"], [])
    if not srcs:
        Lns.append("- Where: **not granted by any event in the data** (unused)")
    seen = set()
    for (p, o, a, e) in srcs:
        rs = [r for r in roots(p) if not r["m_Name"].startswith("Test")]
        if any(r.get("EventSpawnTypes") != [3] for r in rs):
            rs = [r for r in rs if r.get("EventSpawnTypes") != [3]]  # prefer real map events over intermediate chain links
        for r in rs:
            key = (p["_path_id"], r["_path_id"], o.get("title"))
            if key in seen: continue
            seen.add(key)
            chain = "" if r is p else f" (chained from **{r['m_Name']}**: {ev_where(r)})"
            Lns.append(f"- Where: **{p['m_Name']}** — {ev_where(p) if r is p else 'chained event'}{chain}; {how_txt(o, a, e)}")
    if fl: Lns.append(f"- Flavour: *{fl}*")
    Lns.append("")
open("../fortunes.md", "w", encoding="utf-8").write("\n".join(Lns))
print("wrote fortunes.md", len(Lns), "lines; fortunes with no event:", [s['m_Name'] for s in F if not grants.get(s['_path_id'])])
