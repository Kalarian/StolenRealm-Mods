import re, collections, json, sys
G = r"C:\Program Files (x86)\Steam\steamapps\common\Stolen Realm\Stolen Realm_Data"
data = open(G + r"\resources.assets", "rb").read()

# 1. all icon names of the form PREFIX_tier_slot_Name__Icon
pat = re.compile(rb"[A-Za-z]{2,8}_\d_[A-Z]\d+_[^\x00]{1,60}?__Icon")
icons = sorted(set(m.group(0).decode("utf-8", "replace") for m in pat.finditer(data)))
open("icons.txt", "w", encoding="utf-8").write("\n".join(icons))
print(len(icons), "icons")
print(collections.Counter(i.split("_")[0] for i in icons))

# 2. localization keys in order, first block only
blk = data[1089000000:1091000000]
kpat = re.compile(rb'"Key":"((?:[^"\\]|\\.)*)"')
keys = [m.group(1).decode("utf-8", "replace") for m in kpat.finditer(blk)]
print(len(keys), "keys")
open("loc_keys.txt", "w", encoding="utf-8").write("\n".join(keys))
i = keys.index("Warrior's Boon")
print("\n".join(keys[i - 8:i + 8]))
