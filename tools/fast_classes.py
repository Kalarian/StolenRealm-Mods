import UnityPy, collections, struct, sys, pickle
G = r"C:\Program Files (x86)\Steam\steamapps\common\Stolen Realm\Stolen Realm_Data"
env = UnityPy.load(G + r"\resources.assets")
# map MonoScript path_id -> class name (cheap: MonoScript has a built-in typetree)
scripts = {}
for obj in env.objects:
    if obj.type.name == "MonoScript":
        s = obj.read()
        scripts[obj.path_id] = s.m_ClassName
print(len(scripts), "MonoScripts")
# raw-parse MonoBehaviour header: PPtr gameobject(int32,int64), enabled(uint8+3pad), PPtr script(int32,int64), name(str)
cnt = collections.Counter()
index = collections.defaultdict(list)  # class -> [(path_id, name)]
for obj in env.objects:
    if obj.type.name != "MonoBehaviour":
        continue
    raw = obj.get_raw_data()
    if len(raw) < 28:
        continue
    _fid, _pid = struct.unpack_from("<iq", raw, 0)
    sfid, spid = struct.unpack_from("<iq", raw, 16)
    nlen = struct.unpack_from("<i", raw, 28)[0]
    name = raw[32:32 + nlen].decode("utf-8", "replace") if 0 <= nlen < 200 else "?"
    cls = scripts.get(spid, f"?{spid}")
    cnt[cls] += 1
    index[cls].append((obj.path_id, name))
print(len(cnt), "classes")
for k, v in cnt.most_common(60):
    print(v, k)
pickle.dump(dict(index), open("mb_index.pkl", "wb"))
open("classes.txt", "w", encoding="utf-8").write("\n".join(f"{v}\t{k}" for k, v in cnt.most_common()))
