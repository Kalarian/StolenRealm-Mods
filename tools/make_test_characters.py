"""Create the six level-18 test characters (Test1..Test6, the BattleStats test-plan builds) as CharacterN.json saves.

Uses an existing save as the template for everything cosmetic/structural, then sets name, level, stat bases, learned
skills, gear and empties the rest. Re-running replaces existing Test1..Test6 (matched by name). Run with the game closed.
Save format facts: learned skill = AttributesAndSkills[skillGuid] = 1.0; stats are *Base entries (8 + allocated, 135 total
at level 18); equipped slots: weapon 0, shield/offhand 1, armor 2, head 3, ring 4, amulet 5.
"""
import json, os, re, uuid, glob, datetime, shutil

SAVES = os.path.expandvars(r"%USERPROFILE%\AppData\LocalLow\Burst2Flame Entertainment\Stolen Realm")
GUIDS = json.load(open(r"C:\Claude\General\stolen-realm\data\item_guids.json", encoding="utf-8"))
INV = {}
for n, gs in GUIDS.items():
    for g in gs:
        INV.setdefault(g, []).append(n)

def guid(name):
    gs = GUIDS.get(name)
    assert gs and len(gs) == 1, ("ambiguous or missing asset name", name, gs)
    return gs[0]

def skill(name):
    special = {"Fire Starter": "FIRE_2_P1_FireStarter", "Rune of Exploding": "FIRE_3_A4_RuneOfExploding", "Soul Crush": "SHD_3_A1_Soul Fracture"}
    if name in special:
        return guid(special[name])
    cands = [n for n in GUIDS if re.match(r"^[A-Z]+_\d_[AP]\d+_" + re.escape(name) + r"$", n)]
    assert len(cands) == 1, ("skill name did not resolve", name, cands)
    return guid(cands[0])

LEVEL = 18.0
# stat allocation: five bases of 8 (=40) + 10 creation points + 85 level points = 135 total
def stats(might=0, dex=0, vit=0, intel=0, reflex=0):
    alloc = {"MightBase": might, "DexterityBase": dex, "VitalityBase": vit, "IntelligenceBase": intel, "ReflexBase": reflex}
    assert sum(alloc.values()) == 95, ("allocate exactly 95 points", alloc)
    return {k: 8 + v for k, v in alloc.items()}

def gear(*names):
    """(name, slot) pairs; slot None = in bag"""
    return list(names)

BUILDS = [
    ("Test1", "Bloom / Nature", stats(might=45, vit=40, intel=10),
     ["Entangle", "Faerie Swarm", "Thorns I", "Brambles", "Beast Master I", "Pack Hunter I", "The Bad Bloom", "Venomous Skin", "Thorns II", "Living Armor", "Contagion", "The Good Bloom", "Nature Summoning II", "Mass Entangle", "Circle of Life", "Titan Bloom"],
     gear(("Staff", 0), ("Leather Jerkin", 2), ("Leather Hood", 3))),
    ("Test2", "Ember / Fire", stats(might=45, vit=40, intel=10),
     ["Fireblast", "Immolate", "Fire Shield", "Hot Head I", "Fire Borne", "Burning Reach", "Burning Ground", "Fireball", "Hot Head II", "Fire Starter", "Aura of Flame", "Rune of Exploding", "Detonate", "Meteor", "Flash Fire", "Avatar of Flame"],
     gear(("Wand", 0), ("Leather Jerkin", 2), ("Leather Hood", 3))),
    ("Test3", "Chaplain / Light", stats(might=35, vit=50, intel=10),
     ["Cure", "Regenerate", "Healing Hand", "Blessed By Light I", "Empowered Light I", "Elemental Protection", "Shield of Light", "Bless", "Kindred Spirit", "Seal of Protection", "Mass Cure", "Light's Strength", "Light's Beckon", "Holy Ground", "Shield of Retribution", "Salvation"],
     gear(("Staff", 0), ("Leather Jerkin", 2), ("Leather Hood", 3))),
    ("Test4", "Bonecaller / Shadow", stats(might=45, vit=40, intel=10),
     ["Raise Skeletal Archer", "Necromancer I", "Tainted Touch", "Call of the Grave", "Ghost Armor", "Blind", "Poison Cloud", "Necromancer II", "Thirst I", "Haunt", "Raise Skeletal Warrior", "Soul Crush", "Consumption", "Vampiric Aura", "Soul Link", "Raise Skeletal Mage"],
     gear(("Wand", 0), ("Leather Jerkin", 2), ("Leather Hood", 3))),
    ("Test5", "Juggernaut / Warrior", stats(might=55, vit=40),
     ["Double Edge", "Bash", "Fracture", "Rage", "Warmonger I", "Challenger", "Cleave", "Warmonger II", "Tempered Rage", "Into the Fray", "Bleeding Cleave", "Slam", "Two-Handed Mastery", "Bone Collector", "Blood Drinker", "Colossus"],
     gear(("Iron Greatsword", 0), ("Leather Jerkin", 2), ("Leather Hood", 3))),
    ("Test6", "Cutthroat / Thief", stats(dex=60, vit=25, might=10),
     ["Poison Weapon", "Death Dealer I", "Cripple", "Ambush I", "Fencer's Finesse I", "Dagger Throw", "Death Dealer II", "Poisoned Dagger", "Ambush II", "Hide In Shadows", "Garrote", "Hidden Blade", "Fight Dirty", "Smoke Bomb", "Master Assassin", "Deathblow"],
     gear(("Shortsword", 0), ("Shortsword", 1), ("Leather Jerkin", 2), ("Leather Hood", 3))),
]

def load_saves():
    out = {}
    for f in sorted(glob.glob(os.path.join(SAVES, "Character*.json"))):
        if "backup" in f.lower() or ".temp" in f.lower():
            continue
        try:
            d = json.load(open(f, encoding="utf-8-sig"))
        except Exception:
            continue
        out[f] = d
    return out

def main():
    saves = load_saves()
    template_path = next(f for f, d in saves.items() if not d.get("IsRoguelikeCharacter") and not d.get("IsDeleted"))
    template = json.load(open(template_path, encoding="utf-8-sig"))
    print("template:", os.path.basename(template_path), template["CharacterName"])
    skill_re = re.compile(r"^[A-Z]+_\d_[AP]\d+_")
    used = set(int(re.search(r"Character(\d+)\.json$", f).group(1)) for f in saves)
    existing_by_name = {d["CharacterName"]: f for f, d in saves.items()}
    now = datetime.datetime.now()
    made = []
    for name, label, statvals, skills, items in BUILDS:
        c = json.loads(json.dumps(template))
        c["Guid"] = str(uuid.uuid4())
        c["CharacterName"] = name
        c["ExperienceLevel"] = LEVEL
        c["IsDeleted"] = False; c["IsHardcore"] = False; c["HardcoreDeath"] = False; c["IsRoguelikeCharacter"] = False
        c["CompletedStartingBattle"] = True
        c["FortuneSaveData"] = []
        c["SkillSlotArrangement"] = {}
        c["ActionCharges"] = {}
        c["QuestStatuses"] = []; c["QuestStatusSaveData"] = c.get("QuestStatusSaveData") if isinstance(c.get("QuestStatusSaveData"), dict) else []
        c["CompletedQuestNodes"] = []; c["LastStartedQuestNode"] = ""
        c["LastTimePlayedString"] = now.strftime("%m/%d/%Y %H:%M:%S")
        a = c["AttributesAndSkills"]
        for k in list(a.keys()):
            if any(skill_re.match(n) for n in INV.get(k, [])):  # a GUID can carry several asset names (e.g. Howl / Enemy_Howl of Terror)
                a[k] = 0.0
        a[guid("Gold")] = 0.0
        a[guid("OldExperience")] = 0.0
        for k, v in statvals.items():
            a[guid(k)] = float(v)
        for s in skills:
            a[skill(s)] = 1.0
        c["Items"] = []
        for iname, slot in items:
            c["Items"].append({"ItemGuid": guid(iname), "TransmogItemGuid": None, "Equipped": slot is not None, "EquippedSlotIndex": slot if slot is not None else -1,
                               "NumStacks": 1, "ItemLevel": int(LEVEL), "ItemModID": None, "TimeAcquiredString": now.strftime("%m/%d/%Y %H:%M:%S"), "ItemModIDs": [], "HardcoreSetting": 1, "IsFromPreset": False})
        for pot in ("Minor Healing Potion", "Minor Mana Potion"):
            c["Items"].append({"ItemGuid": guid(pot), "TransmogItemGuid": None, "Equipped": False, "EquippedSlotIndex": -1, "NumStacks": 3, "ItemLevel": 1, "ItemModID": None,
                               "TimeAcquiredString": now.strftime("%m/%d/%Y %H:%M:%S"), "ItemModIDs": [], "HardcoreSetting": 1, "IsFromPreset": False})
        if name in existing_by_name:
            path = existing_by_name[name]
        else:
            idx = 0
            while idx in used:
                idx += 1
            used.add(idx)
            path = os.path.join(SAVES, "Character%d.json" % idx)
        json.dump(c, open(path, "w", encoding="utf-8"), separators=(",", ":"))
        made.append((name, label, os.path.basename(path), len(skills), sum(statvals.values())))
    for m in made:
        print("wrote %s (%s) -> %s: %d skills, stat total %d" % m)

if __name__ == "__main__":
    main()
