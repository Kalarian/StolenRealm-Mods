"""Write a test character save from a build spec (BuildN-Name.json), plus the driver's rotation.json.

Usage:  python make_build_character.py <spec.json> [--name TestBuild] [--rotation <out.json>] [--level 30] [--saves <dir>]

--saves <dir> writes the character into that folder (an instance save folder seeded from the real one) instead of the
real save folder; the template character is still read from the real saves.

The character gets: the spec's level, attributes = creation split + per_level blocks, every skill in the point order,
the gear in `test_loadout` (item level = character level, enchants from `mods`), consumables, and the four fortune
slots at level 30, equipped. Everything else is emptied. Existing character with the same name is replaced,
otherwise the next free CharacterN.json index is used. Run with the game closed; the caller restores the save folder.
"""
import json, os, sys, glob, re, uuid, datetime

ROOT = r"C:\Claude\General\stolen-realm"
DATA = os.path.join(ROOT, "data")
SAVES = os.path.expandvars(r"%USERPROFILE%\AppData\LocalLow\Burst2Flame Entertainment\Stolen Realm")
STATS = ["Might", "Dexterity", "Intelligence", "Vitality", "Reflex"]
SLOT = {"Weapon": 0, "Off-hand": 1, "Offhand": 1, "Shield": 1, "Armor": 2, "Head": 3, "Ring": 4, "Amulet": 5}
TREE = {"Fire": 0, "Lightning": 1, "Cold": 2, "Warrior": 3, "Light": 4, "Ranger": 5, "Shadow": 6, "Thief": 7, "Basic": 8, "Innate": 9, "Monk": 10, "Nature": 11, "Chaos": 12, "Bard": 13}


def load(name):
    return json.load(open(os.path.join(DATA, name + ".json"), encoding="utf-8"))


def odin_guid(d):
    sd = d.get("serializationData")
    sb = sd.get("SerializedBytes") if isinstance(sd, dict) else None
    if not sb or not isinstance(sb, str):
        return None
    try:
        b = bytes.fromhex(sb)
    except ValueError:
        return None
    k = "Guid".encode("utf-16-le")
    i = b.find(k)
    if i < 0 or i + len(k) + 16 > len(b):
        return None
    return str(uuid.UUID(bytes_le=b[i + len(k):i + len(k) + 16]))


class Resolver:
    def __init__(self):
        self.guids = json.load(open(os.path.join(DATA, "item_guids.json"), encoding="utf-8"))
        self.items = {d["m_Name"]: d for d in load("ItemInfo")}
        self.weapons = {d["m_Name"]: d for d in load("WeaponInfo")}
        self.mods = {}
        for d in load("ItemMod"):
            self.mods.setdefault(d["m_Name"], []).append((odin_guid(d), d.get("ItemTypes") or [], d.get("ItemModType")))
        self.fortunes = {d["m_Name"]: odin_guid(d) for d in load("EventStatus") if d.get("StatusType") == 1}
        self.skills = {}
        tree_asset = re.compile(r"^[A-Z]+_\d_[AP]\d+_")  # player tree assets are named like LTN_3_A1_Chain Lightning; enemy/boss copies are not
        for d in sorted(load("SkillInfo"), key=lambda d: 0 if tree_asset.match(d.get("m_Name", "")) else 1):
            g = odin_guid(d)
            if g:
                self.skills.setdefault((d.get("SkillName"), d.get("SkillType")), []).append(g)
        self.all_skill_guids = {g for gs in self.skills.values() for g in gs}
        self.stat_guids = {}
        for n, gs in self.guids.items():
            m = re.match(r"^(Might|Dexterity|Intelligence|Vitality|Reflex)Base$", n)
            if m and len(gs) == 1:
                self.stat_guids[m.group(1)] = gs[0]
        self.gold = self.guids["Gold"][0]
        self.oldxp = self.guids["OldExperience"][0]

    def item(self, name):
        gs = self.guids.get(name)
        if not gs:
            raise SystemExit(f"item '{name}' not found in item_guids.json")
        if len(gs) > 1:
            print(f"warning: '{name}' has {len(gs)} assets, using the first")
        return gs[0]

    def mod(self, name, item_type=None):
        cands = self.mods.get(name)
        if not cands:
            raise SystemExit(f"enchant '{name}' not found in ItemMod.json")
        if item_type is not None:
            fit = [c for c in cands if item_type in c[1]]
            if fit:
                cands = fit
            elif len(cands) > 1:
                print(f"warning: no '{name}' enchant lists item type {item_type}; using the first of {len(cands)}")
        return cands[0][0]

    def item_type(self, name):
        d = self.weapons.get(name) or self.items.get(name) or {}
        return d.get("ItemType", 0)

    def fortune(self, name):
        g = self.fortunes.get(name)
        if not g:
            raise SystemExit(f"fortune '{name}' not found in EventStatus.json")
        return g

    def skill(self, name, tree):
        gs = self.skills.get((name, TREE.get(tree)))
        if not gs:
            raise SystemExit(f"skill '{name}' ({tree}) not found in SkillInfo.json")
        return gs[0]  # tree assets sort first; enemy/boss copies of the same name come after


def final_stats(spec, level):
    a = spec.get("attributes", {})
    st = {s: int(a.get("creation", {}).get(s, 8)) for s in STATS}
    lv = 1
    for b in a.get("per_level", []):
        until = min(level, int(b.get("until_level", level)))
        n = max(0, until - lv)
        for s in STATS:
            st[s] += int(b.get(s, 0)) * n
        lv = until
    return st


def rotation_list(spec):
    out = []
    for e in spec.get("rotation", {}).get("priority", []):
        when = e.get("when", [])
        if isinstance(when, str):
            when = [when]
        out.append({"skill": e.get("skill"), "target": e.get("target", "enemy"), "when": when})
    return out


def build_rotation(spec):
    if spec.get("members"):
        return {"characters": {m["member_name"]: {"formation": m.get("formation", "front"), "priority": rotation_list(m)} for m in spec["members"]}}
    return {"priority": rotation_list(spec)}


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    spec = json.load(open(sys.argv[1], encoding="utf-8"))
    args = sys.argv[2:]
    if spec.get("members"):
        # party spec: write every member under its member_name, then one combined rotation file
        for m in spec["members"]:
            m = dict(m); m.setdefault("level", spec.get("level", 30))
            write_character(m, m["member_name"], args, allow_rotation=False)
        if "--rotation" in args:
            rot = args[args.index("--rotation") + 1]
            json.dump(build_rotation(spec), open(rot, "w", encoding="utf-8"), indent=1)
            print("wrote party rotation", rot)
        return
    write_character(spec, "TestBuild", args, allow_rotation=True)


def write_character(spec, name, args, allow_rotation):
    rot_path = None
    level = int(spec.get("level", 30))
    out_dir = SAVES
    for i, a in enumerate(args):
        if a == "--saves": out_dir = args[i + 1]
        if a == "--name" and allow_rotation: name = args[i + 1]
        if a == "--rotation" and allow_rotation: rot_path = args[i + 1]
        if a == "--level": level = int(args[i + 1])
    R = Resolver()

    saves = {}
    for f in sorted(glob.glob(os.path.join(SAVES, "Character*.json"))):
        if not re.search(r"Character\d+\.json$", f):
            continue
        try:
            saves[f] = json.load(open(f, encoding="utf-8-sig"))
        except Exception:
            pass
    template_path = next(f for f, d in saves.items() if not d.get("IsRoguelikeCharacter") and not d.get("IsDeleted"))
    c = json.loads(json.dumps(saves[template_path]))
    now = datetime.datetime.now().strftime("%m/%d/%Y %H:%M:%S")

    c["Guid"] = str(uuid.uuid4())
    c["CharacterName"] = name
    c["ExperienceLevel"] = float(level)
    c["IsDeleted"] = False; c["IsHardcore"] = False; c["HardcoreDeath"] = False; c["IsRoguelikeCharacter"] = False
    c["CompletedStartingBattle"] = True
    c["SkillSlotArrangement"] = {}
    c["ActionCharges"] = {}
    c["QuestStatuses"] = []
    c["QuestStatusSaveData"] = c.get("QuestStatusSaveData") if isinstance(c.get("QuestStatusSaveData"), dict) else []
    c["LastStartedQuestNode"] = ""
    c["LastTimePlayedString"] = now

    a = c["AttributesAndSkills"]
    for k in list(a.keys()):
        if k in R.all_skill_guids:
            a[k] = 0.0
    a[R.gold] = 0.0
    a[R.oldxp] = 0.0
    st = final_stats(spec, level)
    for s in STATS:
        a[R.stat_guids[s]] = float(st[s])
    skills = spec.get("skills", [])
    for e in skills:
        a[R.skill(e["skill"], e["tree"])] = 1.0

    items = []
    lo = spec.get("test_loadout", {})
    for slot, entry in lo.items():
        if slot == "consumables" or entry is None:
            continue
        if slot not in SLOT:
            raise SystemExit(f"test_loadout slot '{slot}' unknown (use Weapon, Off-hand, Armor, Head, Ring, Amulet, consumables)")
        iname = entry["item"] if isinstance(entry, dict) else entry
        mods = entry.get("mods", []) if isinstance(entry, dict) else []
        items.append({"ItemGuid": R.item(iname), "TransmogItemGuid": None, "Equipped": True, "EquippedSlotIndex": SLOT[slot], "NumStacks": 1,
                      "ItemLevel": int(entry.get("level", level)) if isinstance(entry, dict) else level, "ItemModID": None, "TimeAcquiredString": now,
                      "ItemModIDs": [R.mod(m, R.item_type(iname)) for m in mods], "HardcoreSetting": 1, "IsFromPreset": False})
    for con in lo.get("consumables", []):
        cname = con["item"] if isinstance(con, dict) else con
        stacks = int(con.get("stacks", 3)) if isinstance(con, dict) else 3
        items.append({"ItemGuid": R.item(cname), "TransmogItemGuid": None, "Equipped": False, "EquippedSlotIndex": -1, "NumStacks": stacks, "ItemLevel": level,
                      "ItemModID": None, "TimeAcquiredString": now, "ItemModIDs": [], "HardcoreSetting": 1, "IsFromPreset": False})
    c["Items"] = items

    fort = []
    for i, fname in enumerate(spec.get("fortunes", {}).get("slots", [])[:4]):
        fort.append({"Guid": R.fortune(fname), "Level": 30.0, "EquippedSlotIndex": i, "IsNew": False, "IsEquipped": True})
    c["FortuneSaveData"] = fort

    out_saves = {}
    for f in sorted(glob.glob(os.path.join(out_dir, "Character*.json"))):
        if re.search(r"Character\d+\.json$", f):
            try:
                out_saves[f] = json.load(open(f, encoding="utf-8-sig"))
            except Exception:
                pass
    existing = {d.get("CharacterName"): f for f, d in out_saves.items()}
    if name in existing:
        path = existing[name]
    else:
        used = {int(re.search(r"Character(\d+)\.json$", f).group(1)) for f in out_saves}
        idx = 0
        while idx in used:
            idx += 1
        path = os.path.join(out_dir, "Character%d.json" % idx)
    json.dump(c, open(path, "w", encoding="utf-8"), separators=(",", ":"))
    print("wrote %s -> %s: level %d, stats %s, %d skills, %d items, %d fortunes" % (name, os.path.basename(path), level, st, len(skills), len(items), len(fort)))

    if rot_path:
        json.dump(build_rotation(spec), open(rot_path, "w", encoding="utf-8"), indent=1)
        print("wrote rotation", rot_path)


if __name__ == "__main__":
    main()
