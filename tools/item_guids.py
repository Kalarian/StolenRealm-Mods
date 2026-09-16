"""Name -> Guid for every ItemInfo-like MonoBehaviour in resources.assets, read from the raw Odin serializationData bytes."""
import UnityPy, struct, uuid, json, os
G = "C:/Program Files (x86)/Steam/steamapps/common/Stolen Realm/Stolen Realm_Data"
env = UnityPy.load(G + "/resources.assets")
key = "Guid".encode("utf-16-le")
out = {}
for obj in env.objects:
    if obj.type.name != "MonoBehaviour": continue
    raw = obj.get_raw_data()
    if len(raw) < 40: continue
    n = struct.unpack_from("<i", raw, 28)[0]
    if n <= 0 or n > 100 or 32 + n > len(raw): continue
    try: name = raw[32:32+n].decode("utf-8")
    except Exception: continue
    i = raw.find(key)
    if i < 0: continue
    g = str(uuid.UUID(bytes_le=raw[i+len(key):i+len(key)+16]))
    out.setdefault(name, []).append(g)
json.dump(out, open("../data/item_guids.json", "w"), indent=1, sort_keys=True)
names = ['Particle of Light','Gold Ore','Wild Soul','Animal Hide','Ectoplasm','Turtle Crab','Bristlethorn','Goldbloom','Cursed Coin','Rainbow Fish','Enchanted Bark','Peace Lily']
print("objects with a Guid:", len(out))
for n in names: print(n, out.get(n))
inv = {g: n for n, gs in out.items() for g in gs}
print("stash sample 96be18bb-ecd7-4690-91c5-e5af3f6821aa ->", inv.get("96be18bb-ecd7-4690-91c5-e5af3f6821aa"))
