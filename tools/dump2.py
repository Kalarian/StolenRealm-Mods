"""Dump MonoBehaviour classes from resources.assets using a self-written flat-typetree parser."""
import UnityPy, pickle, json, struct, sys, os, traceback
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

G = r"C:\Program Files (x86)\Steam\steamapps\common\Stolen Realm\Stolen Realm_Data"
ROOT = G.rsplit("\\", 1)[0]
classes = sys.argv[1:]
index = pickle.load(open("mb_index.pkl", "rb"))

# namespaces
gg = UnityPy.load(os.path.join(G, "globalgamemanagers.assets"))
ns_of = {}
for obj in gg.objects:
    if obj.type.name == "MonoScript":
        s = obj.read()
        ns_of[s.m_ClassName] = (s.m_Namespace, s.m_AssemblyName)

env = UnityPy.load(G + r"\resources.assets")
byid = {obj.path_id: obj for obj in env.objects if obj.type.name == "MonoBehaviour"}
gen = TypeTreeGenerator(env.assets[0].unity_version)
gen.load_local_game(ROOT)


class N:
    __slots__ = ("type", "name", "flag", "level", "children")

    def __init__(self, t, n, f, l):
        self.type, self.name, self.flag, self.level, self.children = t, n, f, l, []


def build(flat):
    root = None
    stack = []
    for x in flat:
        n = N(x.m_Type, x.m_Name, x.m_MetaFlag, x.m_Level)
        while stack and stack[-1].level >= n.level:
            stack.pop()
        if stack:
            stack[-1].children.append(n)
        else:
            root = n
        stack.append(n)
    return root


PRIM = {
    "int": "<i", "SInt32": "<i", "UInt32": "<I", "unsigned int": "<I", "SInt64": "<q", "UInt64": "<Q",
    "SInt16": "<h", "UInt16": "<H", "SInt8": "<b", "UInt8": "<B", "char": "<B", "bool": "<?",
    "float": "<f", "double": "<d", "Type*": "<i", "FileSize": "<Q",
}


class R:
    def __init__(self, data):
        self.d = data; self.p = 0

    def align(self):
        self.p = (self.p + 3) & ~3

    def prim(self, fmt):
        v = struct.unpack_from(fmt, self.d, self.p)[0]; self.p += struct.calcsize(fmt); return v

    def read(self, node):
        t = node.type
        if t in PRIM:
            v = self.prim(PRIM[t])
        elif t == "string" and (not node.children or (
                node.children[0].type == "Array" and node.children[0].children[1].type == "char"
                and not node.children[0].children[1].children)):
            size = self.prim("<i")
            v = self.d[self.p:self.p + size].decode("utf-8", "replace"); self.p += size
            self.align()
        elif node.children and node.children[0].type == "Array":
            arr = node.children[0]
            size = self.prim("<i")
            elem = arr.children[1]
            if elem.type == "UInt8" and not elem.children:
                v = self.d[self.p:self.p + size]; self.p += size
                v = v.hex() if size <= 64 else f"<{size} bytes>"
                if arr.flag & 0x4000:
                    self.align()
            else:
                v = [self.read(elem) for _ in range(size)]
                if arr.flag & 0x4000:
                    self.align()
        else:
            v = {c.name: self.read(c) for c in node.children}
        if node.flag & 0x4000:
            self.align()
        return v


for cls in classes:
    ns, asm = ns_of.get(cls, ("", "Assembly-CSharp"))
    full = (ns + "." if ns else "") + cls
    try:
        flat = gen.get_nodes(asm.replace(".dll", ""), full)
    except Exception as e:
        print("no tree for", full, repr(e)); continue
    root = build(flat)
    out, bad = [], 0
    for pid, name in index.get(cls, []):
        raw = byid[pid].get_raw_data()
        r = R(raw)
        try:
            d = r.read(root)
            d["_path_id"] = pid
            d["_leftover"] = len(raw) - r.p
        except Exception as e:
            bad += 1
            d = {"_path_id": pid, "m_Name": name, "_error": repr(e), "_pos": r.p, "_len": len(raw)}
        out.append(d)
    json.dump(out, open(f"{cls}.json", "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    lo = [d.get("_leftover") for d in out if "_leftover" in d]
    print(f"{cls}: {len(out)} objects, {bad} errors, leftover bytes distinct={sorted(set(lo))[:10]}")
