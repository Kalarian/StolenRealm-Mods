"""Deep skill reference for Stolen Realm, generated from the extracted assets in data/ (run from the project root:
`python tools/skill_dump.py`, after `fast_classes.py` + `dump2.py` refreshed data/*.json for the current build).

Writes data/skills/<Tree>.md (every player skill of that tree, with every linked action, status, ground effect, summon
and trigger resolved inline, several levels deep), data/skills/INDEX.md (one row per skill), data/skills/ACTIONS.md and
data/skills/STATUSES.md (every action / status by the index the game uses at runtime: Game.Instance.Actions and
Game.Instance.ActionStatuses are filled from ListHolderActions / ListHolderActionStatuses in that order, and the
BattleStats mod encodes damage sources as that action index, or 100000 + status index).

Reading guide: `a#123` = action index, `s#45` = status index, `id N` = asset path id (stable across builds unless the
asset is recreated). Descriptions keep the game's own markup: [expr] is a damage/number expression the game evaluates
(SP = spell power, AP = attack power, physMod/fireMod... = the caster's % damage modifiers), {STA=Name} is a status
link, @text@ is highlighted text. Expressions are the source of truth when the prose is vague.
"""
import json, re, os, collections, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(ROOT, "data")
OUT = os.path.join(DATA, "skills")
DECOMP = os.path.join(ROOT, "decomp")
os.makedirs(OUT, exist_ok=True)


def load(name):
    return json.load(open(os.path.join(DATA, name + ".json"), encoding="utf-8"))


def enum(path):
    """Parse a C# enum file from the decompile into {int: name} (handles explicit values and bare names)."""
    s = open(os.path.join(DECOMP, path), encoding="utf-8").read()
    body = re.search(r"enum\s+\w+[^{]*\{(.*)\}", s, re.S).group(1)
    body = re.sub(r"\[[^\]]*\]", "", body)          # attributes
    body = re.sub(r"//.*", "", body)
    out, nxt = {}, 0
    for part in body.split(","):
        part = part.strip()
        if not part:
            continue
        m = re.match(r"(\w+)\s*(?:=\s*(-?\d+))?", part)
        if not m:
            continue
        if m.group(2) is not None:
            nxt = int(m.group(2))
        out[nxt] = m.group(1)
        nxt += 1
    return out


DAMAGE = enum("DamageType.cs")
TAG = enum("SkillTag.cs")
TRIGGER = enum("TriggerType.cs")
STACK = enum("StackType.cs")
TICK = enum("TickType.cs")
ACTIONTYPE = enum("ActionType.cs")
TREE = enum("SkillType.cs")
METHOD = {0: "+", 1: "+%", 2: "x", 3: "=", 4: "+%mult"}
TARGET = {0: "self", 1: "target"}

skills, actions, statuses, attrs, chars, grounds = (load(n) for n in
    ("SkillInfo", "ActionInfo", "ActionStatusInfo", "CharacterAttribute", "CharacterInfo", "GroundEffectInfo"))
K = {d["_path_id"]: d for d in skills}
A = {d["_path_id"]: d for d in actions}
S = {d["_path_id"]: d for d in statuses}
C = {d["_path_id"]: d for d in attrs}
CH = {d["_path_id"]: d for d in chars}
GE = {d["_path_id"]: d for d in grounds}
AIDX = {p["m_PathID"]: i for i, p in enumerate(load("ListHolderActions")[0]["Actions"])}
SIDX = {p["m_PathID"]: i for i, p in enumerate(load("ListHolderActionStatuses")[0]["ActionStatuses"])}
PLAYER = [p["m_PathID"] for p in load("ListHolderSkills")[0]["Skills"]]

# who owns what (for the index files)
owner_a = collections.defaultdict(set)   # action pid -> skill names
owner_s = collections.defaultdict(set)   # status pid -> skill names


# ---------------------------------------------------------------- text helpers

def simp(e):
    e = e.replace("\r", " ").replace("\n", " ")
    e = e.replace("Source.", "").replace("Target.", "T.").replace("TargetStored", "stored")
    e = re.sub(r"(\d)f\b", r"\1", e)
    e = e.replace("DamageModPhysical", "physMod").replace("DamageModHealing", "healMod")
    e = re.sub(r"DamageMod(\w+)", lambda m: m.group(1).lower() + "Mod", e)
    e = e.replace("SpellPower()", "SP").replace('SpellPower("', 'SP("').replace("AttackPower", "AP")
    e = re.sub(r"\s+", " ", e).strip()
    return e


def fill(desc, exprs, alt=None):
    if not desc:
        return ""
    exprs = exprs or alt or []
    for i, e in enumerate(exprs):
        desc = desc.replace("*" + str(i), "[" + simp(e) + "]").replace("[" + str(i) + "]", "[" + simp(e) + "]")
    return re.sub(r"\s+", " ", desc.replace("\r", " ").replace("\n", " ")).strip()


def pid(p):
    return p.get("m_PathID", 0) if isinstance(p, dict) else 0


def removals(lst):
    """StatusRemovals entries are rules (condition + target), not status references."""
    out = []
    for r in lst or []:
        if not isinstance(r, dict) or "RemovalCondition" not in r:
            continue
        who = {0: "self", 1: "target"}.get(r.get("RemovalTarget"), "?")
        n = simp(str(r["NumStatusesToRemove"])) if r.get("NumStatusesToRemove") else "all"
        s = "%s status(es) on %s where [%s]" % (n, who, simp(r.get("RemovalCondition") or "any"))
        if r.get("StealStatuses"):
            s += " (stolen)"
        if r.get("GiveStatuses"):
            s += " (given)"
        out.append(s)
    return out


def truthy(v):
    return v not in (0, 0.0, "", [], None, False, {})


def attr_name(p):
    c = C.get(pid(p))
    return (c["DisplayName"] or c["m_Name"]) if c else "attr%d" % pid(p)


def attr_effects(lst, label):
    out = []
    for x in lst or []:
        tgt = TARGET.get(x.get("EffectTarget", 0), "")
        s = "%s %s %s" % (attr_name(x["CharacterAttribute"]), METHOD.get(x["CharacterEffectMethod"], x["CharacterEffectMethod"]), simp(str(x["Amount"])))
        if tgt == "target":
            s += " (on target)"
        if x.get("Infinite"):
            s += " (infinite)"
        out.append(s)
    return ["%s: %s" % (label, "; ".join(out))] if out else []


def grouped(pids):
    cnt = collections.OrderedDict()
    for p in pids:
        cnt[p] = cnt.get(p, 0) + 1
    return list(cnt.items())


def tags(lst):
    return ", ".join(TAG.get(t, str(t)) for t in lst or [])


def flags(d, names):
    return [n.replace("Ignore", "ignores ").lower() for n in names if d.get(n)]


def status_name(d):
    n = d.get("Name") or d["m_Name"]
    return n if n == d["m_Name"] else "%s (%s)" % (n, d["m_Name"])


def action_name(d):
    return d["ActionNameOverride"] if d.get("OverrideActionName") and d.get("ActionNameOverride") else d["m_Name"]


# ---------------------------------------------------------------- renderers (return lists of lines, indented by depth)

MAXDEPTH = 6


def ind(depth, text):
    return "  " * depth + "- " + text


def render_trigger(t, depth, seen, owner):
    lines = []
    head = "trigger %s" % TRIGGER.get(t["TriggerType"], t["TriggerType"])
    if t.get("Condition"):
        head += " if [%s]" % simp(t["Condition"])
    if t.get("Targets"):
        head += " targets [%s]" % simp(t["Targets"])
    extras = []
    if t.get("MaxNumUses"):
        extras.append("max uses %s" % simp(t["MaxNumUses"]))
    if t.get("Cooldown"):
        extras.append("cooldown %s" % t["Cooldown"])
    if t.get("ChooseRandomStatus"):
        extras.append("random %s of the statuses" % t.get("NumRandomStatuses", 1))
    if t.get("GetRandomTargetsFromTargetPool"):
        extras.append("random %s target(s)" % t.get("NumRandomTargets"))
    if t.get("AddBasicAttackToActionList"):
        extras.append("adds basic attack")
    if t.get("UseTriggerSource"):
        extras.append("credited to trigger source")
    if extras:
        head += " (" + ", ".join(extras) + ")"
    lines.append(ind(depth, head))
    for i, p in enumerate(t.get("Actions", [])):
        ch = t.get("ActionChanceEquations", [])
        chance = (" chance [%s]" % simp(ch[i])) if i < len(ch) and ch[i] else ""
        lines += render_action(pid(p), depth + 1, seen, owner, prefix="does" + chance + ": ")
    for p, n in grouped(pid(x) for x in t.get("ActionStatuses", [])):
        lines += render_status(p, depth + 1, seen, owner, prefix="applies%s: " % (" x%d" % n if n > 1 else ""))
    ge = [simp(g["Action"]) for g in t.get("GeneralEffects", []) if g.get("Action")]
    if ge:
        lines.append(ind(depth + 1, "effects: " + "; ".join(ge)))
    rm = removals(t.get("StatusRemovals"))
    if rm:
        lines.append(ind(depth + 1, "removes: " + "; ".join(rm)))
    if t.get("UsePopupText"):
        lines.append(ind(depth + 1, "popup: " + t["UsePopupText"]))
    return lines


def render_character(p, depth, seen, owner, prefix=""):
    d = CH.get(p)
    if not d:
        return [ind(depth, prefix + "summon id %d (not in dump)" % p)]
    if "_error" in d:
        return [ind(depth, prefix + "summon '%s' [id %d] (record did not parse)" % (d["m_Name"], p))]
    key = ("c", p)
    if key in seen or depth > MAXDEPTH:
        return [ind(depth, prefix + "summon '%s' (see above)" % d["m_Name"])]
    seen = seen | {key}
    be = d.get("BasicEffects") or {}
    stats = []
    for k, label in (("weaponDamage", "weapon dmg"), ("spellPower", "SP"), ("health", "HP"), ("mana", "mana"), ("armor", "armor"),
                     ("magicArmor", "magic armor"), ("movementPoints", "move"), ("recovery", "recovery")):
        v = be.get(k)
        if v not in (None, 0, 0.0, 1, 1.0) or (k == "movementPoints" and v):
            stats.append("%s x%s" % (label, round(v, 2)) if k != "movementPoints" else "move %s" % round(v, 1))
    res = [k.replace("Resist", " res") + " " + str(round(v, 1)) for k, v in be.items() if k.endswith("Resist") and v]
    head = prefix + "summon '%s' [id %d]: " % (d["m_Name"], p) + ", ".join(stats + res)
    head += ", basic attack " + DAMAGE.get(d.get("basicAttackDamageType"), "?")
    if d.get("SpecialEffects"):
        head += ", special effects " + ",".join(str(x) for x in d["SpecialEffects"])
    lines = [ind(depth, head.rstrip(": "))]
    ov = {pid(o["Skill"]): o for o in d.get("Overrides", [])}
    for sa in d.get("SkillsAndAI", []):
        sk = K.get(pid(sa["Skill"]))
        if not sk:
            continue
        note = []
        if sa.get("UseCooldownOverride"):
            note.append("cd %s" % sa["CooldownOverride"])
        if sa.get("HasCondition") and sa.get("Condition"):
            note.append("if [%s]" % simp(sa["Condition"]))
        o = ov.get(pid(sa["Skill"]))
        if o:
            if o.get("overrideCooldown"):
                note.append("cd %s" % o["Cooldown"])
            if o.get("overrideManaCost"):
                note.append("mana x%s" % round(o["ManaCostRatio"], 2))
            if o.get("DamageMultiplier") not in (None, 1, 1.0):
                note.append("dmg x%s" % round(o["DamageMultiplier"], 2))
        lines.append(ind(depth + 1, "skill '%s'%s" % (sk["SkillName"], (" (" + ", ".join(note) + ")") if note else "")))
        for ap in sk.get("ActionsGranted", []):
            lines += render_action(pid(ap), depth + 2, seen, owner)
        for sp in sk.get("PassiveActionStatuses", []):
            lines += render_status(pid(sp), depth + 2, seen, owner, prefix="passive: ")
    for t in d.get("SkillTriggers", []):
        if t.get("Actions") or t.get("ActionStatuses") or t.get("GeneralEffects"):
            lines += render_trigger(t, depth + 1, seen, owner)
    return lines


def render_status(p, depth, seen, owner, prefix=""):
    d = S.get(p)
    if not d:
        return [ind(depth, prefix + "status id %d (not in dump)" % p)]
    owner_s[p].add(owner)
    key = ("s", p)
    label = "%s [s#%s, id %d]" % (status_name(d), SIDX.get(p, "?"), p)
    if key in seen or depth > MAXDEPTH:
        return [ind(depth, prefix + "status " + label + " (see above)")]
    seen = seen | {key}
    props = []
    if d.get("Duration"):
        props.append("dur " + simp(str(d["Duration"])))
    if d.get("Infinite"):
        props.append("infinite")
    if d.get("MaxStacks") and d["MaxStacks"] not in (1, 1.0):
        props.append("max %d stacks" % d["MaxStacks"])
    props.append("stack " + STACK.get(d.get("StackType"), str(d.get("StackType"))))
    if d.get("TickType") or d.get("StatusEffectsOnTick") or d.get("ActionsOnTick"):
        props.append("ticks " + TICK.get(d.get("TickType", 0), str(d.get("TickType"))))
    if d.get("DamageType"):
        props.append("dmg " + DAMAGE.get(d["DamageType"], str(d["DamageType"])))
    if d.get("IsAura"):
        props.append("AURA r%s (%s)" % (d.get("AuraRadius"), "/".join(x for x, f in (("allies", d.get("AuraEffectsAllies")), ("enemies", d.get("AuraEffectsEnemies"))) if f)))
    if d.get("TauntsTarget"):
        props.append("TAUNTS")
    if d.get("CannotBeDispelled"):
        props.append("undispellable")
    if d.get("CannotKill"):
        props.append("cannot kill")
    if d.get("EndOnAction"):
        props.append("ends on action" + (" if [%s]" % simp(d["EndOnActionCondition"]) if d.get("EndOnActionCondition") else ""))
    if d.get("EndOnCrit"):
        props.append("ends on crit")
    if d.get("DecrementOnTurnEnd"):
        props.append("counts down at turn end")
    if d.get("StatusEndCondition"):
        props.append("ends if [%s]" % simp(d["StatusEndCondition"]))
    if d.get("UseFlatDamageModifier") and d.get("FlatDamageModifier") not in (None, 1, 1.0):
        props.append("flat dmg x%s" % round(d["FlatDamageModifier"], 2))
    props += flags(d, ["IgnoreArmor", "IgnoreResists", "IgnoreDodge", "IgnoreBlock", "IgnoreCrit", "IgnoreMiss", "IgnoreProcs", "IgnoreMultipliers", "IgnoreGlobalEffects"])
    if d.get("SkillTags"):
        props.append("tags " + tags(d["SkillTags"]))
    lines = [ind(depth, prefix + "status " + label + ": " + ", ".join(props))]
    desc = fill(d.get("Description"), d.get("DescriptionExpressions"), d.get("DescriptionExpressionsNonCharacterBased"))
    if desc:
        lines.append(ind(depth + 1, '"' + desc + '"'))
    lines += [ind(depth + 1, x) for x in attr_effects(d.get("AttributeEffects"), "effects")]
    for e in d.get("DamageExpressionOverrides", []) or []:
        lines.append(ind(depth + 1, "damage: " + simp(e)))
    ge = [simp(g["Action"]) for g in d.get("GeneralEffectsOnApply", []) if g.get("Action")]
    if ge:
        lines.append(ind(depth + 1, "on apply: " + "; ".join(ge)))
    if pid(d.get("ActionOnApply")):
        cond = (" if [%s]" % simp(d["ActionOnApplyCondition"])) if d.get("ActionOnApplyCondition") else ""
        lines += render_action(pid(d["ActionOnApply"]), depth + 1, seen, owner, prefix="on apply%s: " % cond)
    tick = grouped(pid(x) for x in d.get("StatusEffectsOnTick", []))
    if tick:
        extra = ""
        if d.get("StatusEffectsOnTickCondition"):
            extra += " if [%s]" % simp(d["StatusEffectsOnTickCondition"])
        if d.get("StatusEffectsOnTickTargets"):
            extra += " targets [%s]" % simp(d["StatusEffectsOnTickTargets"])
        lines.append(ind(depth + 1, "each tick%s applies:" % extra))
        for q, n in tick:
            lines += render_status(q, depth + 2, seen, owner, prefix=("x%d " % n) if n > 1 else "")
    for p2 in d.get("ActionsOnTick", []) or []:
        cond = (" if [%s]" % simp(d["ActionsOnTickCondition"])) if d.get("ActionsOnTickCondition") else ""
        lines += render_action(pid(p2), depth + 1, seen, owner, prefix="each tick%s does: " % cond)
    ge = [simp(g["Action"]) for g in d.get("GeneralEffectsOnEnd", []) if g.get("Action")]
    if ge:
        lines.append(ind(depth + 1, "on end: " + "; ".join(ge)))
    if pid(d.get("ActionOnEnd")):
        lines += render_action(pid(d["ActionOnEnd"]), depth + 1, seen, owner, prefix="on end does: ")
    if pid(d.get("ActionStatusOnEnd")):
        lines += render_status(pid(d["ActionStatusOnEnd"]), depth + 1, seen, owner, prefix="on end applies: ")
    if d.get("IsAura"):
        for k, lab in (("AuraSourceStatus", "aura source status"), ("AuraTriggerStatus", "aura applies")):
            if pid(d.get(k)):
                lines += render_status(pid(d[k]), depth + 1, seen, owner, prefix=lab + ": ")
    gs = [K[pid(x)]["SkillName"] for x in d.get("GrantedSkills", []) or [] if pid(x) in K]
    if gs:
        lines.append(ind(depth + 1, "grants skills: " + ", ".join(gs)))
    for t in d.get("SkillTriggers", []) or []:
        lines += render_trigger(t, depth + 1, seen, owner)
    rm = removals(d.get("StatusRemovals"))
    if rm:
        lines.append(ind(depth + 1, "removes: " + "; ".join(rm)))
    for l in d.get("SkillChangeLinks", []) or []:
        o, n = K.get(pid(l["OldSkillInfo"])), K.get(pid(l["NewSkillInfo"]))
        if o and n:
            lines.append(ind(depth + 1, "swaps skill '%s' for '%s'" % (o["SkillName"], n["SkillName"])))
    return lines


def render_action(p, depth, seen, owner, prefix=""):
    d = A.get(p)
    if not d:
        return [ind(depth, prefix + "action id %d (not in dump)" % p)]
    owner_a[p].add(owner)
    key = ("a", p)
    label = "'%s' [a#%s, id %d]" % (action_name(d), AIDX.get(p, "?"), p)
    if key in seen or depth > MAXDEPTH:
        return [ind(depth, prefix + "action " + label + " (see above)")]
    seen = seen | {key}
    props = [ACTIONTYPE.get(d.get("ActionType"), str(d.get("ActionType")))]
    if d.get("Cost") not in (None, "", "0"):
        props.append("cost %s%s" % (simp(str(d["Cost"])), (" x%s mana" % round(d["ManaCostRatio"], 2)) if d.get("ManaCostRatio") not in (None, 0, 1, 1.0) else ""))
    if d.get("AddWeaponCost"):
        props.append("+weapon cost")
    if d.get("Cooldown") not in (None, "", "0"):
        props.append("cd " + simp(str(d["Cooldown"])))
    if d.get("HasCharges"):
        props.append("charges %s%s%s" % (d.get("MaxCharges"), (" (%s/battle)" % d["ChargesPerBattle"]) if d.get("ChargesPerBattle") else "", (" (%s/turn)" % d["ChargesPerTurn"]) if d.get("ChargesPerTurn") else ""))
    if d.get("UseMaxRangeOverride"):
        props.append("range " + str(d.get("MaxRangeOverride")))
    if d.get("UseMaxRangeBlastOverride"):
        props.append("blast " + str(d.get("MaxRangeBlastOverride")))
    if d.get("UseMultipleHits"):
        props.append("AoE range %s%s" % (simp(str(d.get("MultipleHitRange"))), (" where [%s]" % simp(d["MultipleHitTarget"])) if d.get("MultipleHitTarget") else ""))
    if d.get("UseNumHitsEquation") and d.get("NumHitsEquation"):
        props.append("hits [%s]" % simp(d["NumHitsEquation"]))
    elif d.get("NumHits", 1) not in (1, 0, "", "1", None):
        props.append("hits %s" % simp(str(d["NumHits"])))
    if d.get("NumCasts", 1) not in (1, 0, "", "1", None):
        props.append("casts %s" % simp(str(d["NumCasts"])))
    if d.get("DamageType"):
        props.append("dmg " + DAMAGE.get(d["DamageType"], str(d["DamageType"])))
    if d.get("OverrideDamageStatType") and d.get("DamageStatTypeOverride") is not None:
        props.append("scales with %s" % {0: "none", 1: "AP", 2: "SP"}.get(d["DamageStatTypeOverride"], d["DamageStatTypeOverride"]))
    if d.get("UseFlatDamageModifier") and d.get("FlatDamageModifier") not in (None, 1, 1.0):
        props.append("flat dmg x%s" % round(d["FlatDamageModifier"], 2))
    if d.get("UseKnockback"):
        props.append("knockback %s" % d.get("KnockbackAmount"))
    if d.get("PullToCenter"):
        props.append("pulls to center")
    if d.get("TeleportToDestination"):
        props.append("teleports")
    if d.get("DashToTarget"):
        props.append("dash")
    if d.get("CanCastWhileDisabled"):
        props.append("castable while disabled")
    if d.get("CannotKill"):
        props.append("cannot kill")
    if d.get("ProjectilePierceCount"):
        props.append("pierces %s" % d["ProjectilePierceCount"])
    if d.get("ProjectileChainCount", 1) not in (0, 1):
        props.append("chains %s" % d["ProjectileChainCount"])
    props += flags(d, ["IgnoreArmor", "IgnoreResists", "IgnoreDodge", "IgnoreBlock", "IgnoreCrit", "IgnoreMiss", "IgnoreProcs", "IgnoreMultipliers", "IgnoreGlobalEffects"])
    if d.get("RequiredWeaponTypes"):
        props.append("needs weapon type " + ",".join(str(x) for x in d["RequiredWeaponTypes"]))
    if d.get("SkillTags"):
        props.append("tags " + tags(d["SkillTags"]))
    lines = [ind(depth, prefix + "action " + label + ": " + ", ".join(props))]
    desc = fill(d.get("Description"), d.get("DescriptionExpressions"))
    if desc:
        lines.append(ind(depth + 1, '"' + desc + '"'))
    for e in d.get("DamageExpressionOverrides", []) or []:
        lines.append(ind(depth + 1, "damage: " + simp(e)))
    for eo in d.get("EffectOverrides", []) or []:
        eff = "; ".join(simp(x["Action"]) for x in eo.get("Effects", []) if x.get("Action"))
        if eff:
            lines.append(ind(depth + 1, "effect%s: %s" % ((" if [%s]" % simp(eo["Condition"])) if eo.get("Condition") else "", eff)))
    conds = [d["UseCondition"]] if d.get("UseCondition") else []
    conds += [c["condition"] for c in d.get("UseConditions", []) or [] if c.get("condition")]
    if conds:
        lines.append(ind(depth + 1, "usable only if [%s]" % "] and [".join(simp(c) for c in conds)))
    for q, n in grouped(pid(x) for x in d.get("StatusEffects", [])):
        lines += render_status(q, depth + 1, seen, owner, prefix="applies%s: " % (" x%d" % n if n > 1 else ""))
    for q, n in grouped(pid(x) for x in d.get("SourceStatusEffects", [])):
        lines += render_status(q, depth + 1, seen, owner, prefix="applies to self%s: " % (" x%d" % n if n > 1 else ""))
    for so in d.get("StatusEffectOverrides", []) or []:
        for q, n in grouped(pid(x) for x in so.get("ActionStatuses", [])):
            lines += render_status(q, depth + 1, seen, owner, prefix="if [%s] applies%s: " % (simp(so.get("Condition", "")), " x%d" % n if n > 1 else ""))
    rm = removals(d.get("StatusRemovals"))
    if rm:
        lines.append(ind(depth + 1, "removes: " + "; ".join(rm)))
    rm = removals(d.get("PostActionStatusRemovals"))
    if rm:
        lines.append(ind(depth + 1, "removes after the action: " + "; ".join(rm)))
    if d.get("UseGroundEffect"):
        g = "ground effect: %s turns" % (simp(str(d["GroundDuration"])) if d.get("GroundDuration") else "?")
        if d.get("GroundIsInfinite"):
            g += " (infinite)"
        if d.get("GroundTargetConditions"):
            g += ", affects [%s]" % simp(d["GroundTargetConditions"])
        if d.get("GroundMaxNumTriggers") and d["GroundMaxNumTriggers"] < 100:
            g += ", max %s triggers" % d["GroundMaxNumTriggers"]
        if d.get("GroundEffectSkipTickStatusOnCreate"):
            g += ", no tick on creation"
        if d.get("GroundEffectDescription"):
            g += ' | "%s"' % fill(d["GroundEffectDescription"], [])
        lines.append(ind(depth + 1, g))
        for q, n in grouped(pid(x) for x in d.get("GroundActionStatuses", [])):
            lines += render_status(q, depth + 2, seen, owner, prefix="while inside%s: " % (" x%d" % n if n > 1 else ""))
        if pid(d.get("GroundActionOnEnter")):
            tg = (" targets [%s]" % simp(d["GroundActionOnEnterTargets"])) if d.get("GroundActionOnEnterTargets") else ""
            lines += render_action(pid(d["GroundActionOnEnter"]), depth + 2, seen, owner, prefix="on enter%s: " % tg)
        if pid(d.get("GroundActionOnExpire")):
            lines += render_action(pid(d["GroundActionOnExpire"]), depth + 2, seen, owner, prefix="on expire: ")
        for x in d.get("GroundActionOnExpireArray", []) or []:
            if pid(x):
                lines += render_action(pid(x), depth + 2, seen, owner, prefix="on expire: ")
    if d.get("Summons"):
        lines.append(ind(depth + 1, "summons %s:" % ("one at random of" if d.get("RandomSummonFromList") else "all of")))
        for x in d["Summons"]:
            lines += render_character(pid(x), depth + 2, seen, owner)
    if d.get("CloneSelf"):
        lines.append(ind(depth + 1, "clones the caster as a summon"))
    if d.get("CloneCharactersAsSummons"):
        lines.append(ind(depth + 1, "clones the targets as summons"))
    if d.get("Destructibles"):
        lines.append(ind(depth + 1, "places %d destructible object(s)" % len(d["Destructibles"])))
    if pid(d.get("DashAction")):
        lines += render_action(pid(d["DashAction"]), depth + 1, seen, owner, prefix="dash does: ")
    if pid(d.get("ActionOnTurnStart")):
        lines += render_action(pid(d["ActionOnTurnStart"]), depth + 1, seen, owner, prefix="each turn start does: ")
    pw = d.get("PeriodicWorldEffect") or {}
    if pid(pw.get("ActionInfo")):
        lines += render_action(pid(pw["ActionInfo"]), depth + 1, seen, owner, prefix="periodic world effect: ")
    return lines


def render_skill(d):
    p = d["_path_id"]
    name = d["SkillName"]
    kind = "ACTIVE" if d.get("ActionsGranted") else "PASSIVE"
    head = "### T%s %s (%s)" % (d["Tier"], name, kind)
    meta = ["id %d" % p]
    dep = K.get(pid(d.get("Dependency")))
    if dep:
        meta.append("requires " + dep["SkillName"])
    rep = [K[pid(x)]["SkillName"] for x in d.get("SkillsThatReplace", []) if pid(x) in K]
    if rep:
        meta.append("replaces " + ", ".join(rep))
    dis = [K[pid(x)]["SkillName"] for x in d.get("DisablingSkills", []) if pid(x) in K]
    if dis:
        meta.append("disabled by " + ", ".join(dis))
    if d.get("SkillTags"):
        meta.append("tags " + tags(d["SkillTags"]))
    if d.get("DamageType"):
        meta.append("dmg " + DAMAGE.get(d["DamageType"], str(d["DamageType"])))
    if d.get("Disabled"):
        meta.append("DISABLED")
    lines = [head + "  ·  " + "  ·  ".join(meta)]
    desc = fill(d.get("Description"), d.get("DescriptionExpressions"))
    lines.append(desc if desc else "(no description)")
    seen = frozenset()
    lines += [ind(0, x) for x in attr_effects(d.get("AttributeEffects"), "passive effects")]
    for e in d.get("DamageExpressionOverrides", []) or []:
        lines.append(ind(0, "damage: " + simp(e)))
    for x in d.get("ActionsGranted", []):
        lines += render_action(pid(x), 0, seen, name)
    for x in d.get("PassiveActionStatuses", []):
        lines += render_status(pid(x), 0, seen, name, prefix="passive: ")
    for t in d.get("SkillTriggers", []) or []:
        lines += render_trigger(t, 0, seen, name)
    for l in d.get("SkillChangeLinks", []) or []:
        o, n = K.get(pid(l["OldSkillInfo"])), K.get(pid(l["NewSkillInfo"]))
        if o and n:
            lines.append(ind(0, "swaps skill '%s' for '%s'" % (o["SkillName"], n["SkillName"])))
    return "\n".join(lines)


# ---------------------------------------------------------------- tree files

trees = collections.defaultdict(list)
index_rows = []
for p in PLAYER:
    d = K.get(p)
    if not d or d.get("DontIncludeInTree"):
        continue
    tree = TREE.get(d["SkillType"], str(d["SkillType"]))
    trees[tree].append((d["Tier"], d.get("xVal", 0), d))

for tree, items in trees.items():
    items.sort(key=lambda t: (t[0], t[1], t[2]["SkillName"]))
    note = ""
    if tree == "Bard":
        note = ("

**UNRELEASED (2026-09-16):** the Bard tree is the BardPack DLC (DlcType.BardPack). SkillTreeManager only shows the "
                "Chaos and Bard tabs when `!SteamManager.IsSkillTypeHidden(type)`, and DLCInfo.Hidden is documented as 'Unreleased: hide "
                "the skill tree tab and block the skills for everyone'. The tab is absent in the live game, so nothing here is playable yet; "
                "the assets are complete and will apply when the pack ships.")
    parts = ["# %s tree (%d skills)%s" % (tree, len(items), note),
             "Generated by tools/skill_dump.py from the current game build. a# / s# are the runtime action / status indices "
             "(BattleStats codes), id is the asset path id. Nested lines are the linked actions, statuses, ground effects, "
             "summons and triggers, resolved from the assets.", ""]
    for tier, _, d in items:
        parts.append(render_skill(d))
        parts.append("")
        acts = [AIDX.get(pid(x), "?") for x in d.get("ActionsGranted", [])]
        index_rows.append((d["SkillName"], tree, d["Tier"], "active" if d.get("ActionsGranted") else "passive",
                           ",".join(str(a) for a in acts), K[pid(d["Dependency"])]["SkillName"] if pid(d.get("Dependency")) in K else ""))
    open(os.path.join(OUT, tree + ".md"), "w", encoding="utf-8").write("\n".join(parts))
    print(tree, len(items))

# ---------------------------------------------------------------- index files

index_rows.sort(key=lambda r: (r[1], r[2], r[0]))
lines = ["# Skill index (%d player skills; the Bard tree is an unreleased DLC, hidden in the live game)" % len(index_rows), "",
         "Tree files: " + ", ".join("%s.md" % t for t in sorted(trees)) + ". ACTIONS.md and STATUSES.md list every action / status by runtime index.", "",
         "| Skill | Tree | Tier | Kind | a# | Requires |", "|---|---|---|---|---|---|"]
lines += ["| %s | %s | %s | %s | %s | %s |" % r for r in index_rows]
open(os.path.join(OUT, "INDEX.md"), "w", encoding="utf-8").write("\n".join(lines) + "\n")

lines = ["# Actions by runtime index (Game.Instance.Actions order = ListHolderActions; BattleStats source code = this index)", "",
         "| a# | Action | id | Type | Dmg | Used by |", "|---|---|---|---|---|---|"]
for p, i in sorted(AIDX.items(), key=lambda kv: kv[1]):
    d = A.get(p)
    if not d:
        lines.append("| %d | (not in dump) | %d | | | |" % (i, p)); continue
    lines.append("| %d | %s | %d | %s | %s | %s |" % (i, action_name(d), p, ACTIONTYPE.get(d.get("ActionType"), ""), DAMAGE.get(d.get("DamageType"), "") if d.get("DamageType") else "", ", ".join(sorted(owner_a.get(p, [])))[:120]))
open(os.path.join(OUT, "ACTIONS.md"), "w", encoding="utf-8").write("\n".join(lines) + "\n")

lines = ["# Statuses by runtime index (Game.Instance.ActionStatuses order = ListHolderActionStatuses; BattleStats source code = 100000 + this index; fortunes are appended after these at runtime)", "",
         "| s# | Status | asset | id | Dur | Dmg | Description | Used by |", "|---|---|---|---|---|---|---|---|"]
for p, i in sorted(SIDX.items(), key=lambda kv: kv[1]):
    d = S.get(p)
    if not d:
        lines.append("| %d | (not in dump) | | %d | | | | |" % (i, p)); continue
    desc = fill(d.get("Description"), d.get("DescriptionExpressions"), d.get("DescriptionExpressionsNonCharacterBased")).replace("|", "/")
    lines.append("| %d | %s | %s | %d | %s | %s | %s | %s |" % (i, d.get("Name") or d["m_Name"], d["m_Name"], p, simp(str(d.get("Duration") or "")), DAMAGE.get(d.get("DamageType"), "") if d.get("DamageType") else "", desc[:160], ", ".join(sorted(owner_s.get(p, [])))[:100]))
open(os.path.join(OUT, "STATUSES.md"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
print("index:", len(index_rows), "skills;", len(AIDX), "actions;", len(SIDX), "statuses ->", OUT)
