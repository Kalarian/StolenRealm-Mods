"""Index every MonoBehaviour in resources.assets by its C# class (writes mb_index.pkl + classes.txt in the cwd).
Since the 2026-09 build (Unity 2022.3.62) every MonoScript lives in globalgamemanagers.assets and resources.assets
points at it through its externals table (script file id 1); the old build kept them inside resources.assets (file id 0).
Both are handled: the key is (file id, path id)."""
import UnityPy, collections, struct, sys, pickle, os
G = r"C:\Program Files (x86)\Steam\steamapps\common\Stolen Realm\Stolen Realm_Data"
env = UnityPy.load(os.path.join(G, "resources.assets"))
externals = [getattr(x, "path", getattr(x, "name", "")) for x in env.assets[0].externals]
scripts = {}


def harvest(e, fid):
    for obj in e.objects:
        if obj.type.name == "MonoScript":
            s = obj.read()
            scripts[(fid, obj.path_id)] = s.m_ClassName


harvest(env, 0)
for i, ext in enumerate(externals):
    if ext.endswith(".assets"):
        harvest(UnityPy.load(os.path.join(G, ext.rsplit("/", 1)[-1])), i + 1)
print(len(scripts), "MonoScripts", externals)
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
    cls = scripts.get((sfid, spid), f"?{sfid}:{spid}")
    cnt[cls] += 1
    index[cls].append((obj.path_id, name))
print(len(cnt), "classes")
for k, v in cnt.most_common(60):
    print(v, k)
pickle.dump(dict(index), open("mb_index.pkl", "wb"))
open("classes.txt", "w", encoding="utf-8").write("\n".join(f"{v}\t{k}" for k, v in cnt.most_common()))
