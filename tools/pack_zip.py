"""Rebuild the friends' share zip (mods/StolenRealm-Mods-install.zip) from the game folder.

- Loader files (winhttp.dll, doorstop_config.ini, .doorstop_version, BepInEx/core/*) and BepInEx.cfg are copied from the
  game folder (BepInEx.cfg must have AppendLog = true; checked).
- One plugin DLL per mod in PLUGINS (never TestDriver), taken from the game's BepInEx/plugins/<Mod>/.
- Configs are the live ones with the share rules applied: VerboseLogging = true everywhere (for now), AutoSalvage
  SweepBagsOnLoad = true (one-time cleanup on the friend's first launch), BattleStats WriteJson = false. Orphaned
  sections are not carried over (BepInEx keeps them in the live file after a rename).
- README txt from mods/share, PDF rendered from mods/share/Mods-OnePager.html with Edge headless.
Run with the game closed (so the live configs are final). Then: bash tools/build_installer.sh
"""
import os, re, subprocess, sys, zipfile, shutil

ROOT = r"C:\Claude\General\stolen-realm"
GAME = r"C:\Program Files (x86)\Steam\steamapps\common\Stolen Realm"
SHARE = os.path.join(ROOT, "mods", "share")
ZIP = os.path.join(ROOT, "mods", "StolenRealm-Mods-install.zip")
EDGE = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
PLUGINS = ["DropRates", "DifficultyXP", "QoL", "TargetTooltip", "SpecialTooltips", "ScalingTooltips", "SharedFortunes",
           "FortunePreview", "FortuneUpgrade", "LevelSync", "AutoSalvage", "SharedGold", "BattleStats", "ThreatOverlay", "ModMenu", "SharedProgress", "NumberFormat", "BardPreview"]
PDF_NAME = "Stolen Realm Mods - Read Me.pdf"
README_NAME = "README - Stolen Realm mods.txt"
# (config file basename, section, key) -> value forced in the share copy
FORCED = {
    ("stolenrealm.autosalvage.cfg", "SweepBagsOnLoad"): "true",
    ("stolenrealm.battlestats.cfg", "WriteJson"): "false",
    ("stolenrealm.battlestats.cfg", "LogEveryHit"): "true",
}

def render_pdf():
    src = os.path.join(SHARE, "Mods-OnePager.html")
    out = os.path.join(SHARE, PDF_NAME)
    tmp = out + ".tmp.pdf"
    cmd = [EDGE, "--headless=new", "--disable-gpu", "--no-pdf-header-footer", "--print-to-pdf=" + tmp, "file:///" + src.replace("\\", "/")]
    subprocess.run(cmd, check=True, timeout=120, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        os.replace(tmp, out)
    except PermissionError:
        print("PDF is locked by a viewer; wrote", tmp); out = tmp
    try:
        import pypdf
        n = len(pypdf.PdfReader(out).pages)
        print("PDF pages:", n)
    except Exception as e:
        print("pypdf check skipped:", e)
    return out

def share_config(path):
    """Apply the share rules to one live cfg; drop sections that no plugin uses any more (heuristic: a section whose
    name reappears with a different number, e.g. old 2.General next to 3.General, is the orphan)."""
    text = open(path, encoding="utf-8").read()
    base = os.path.basename(path).lower()
    # split into sections
    parts = re.split(r"(?m)^(\[[^\]]+\])\s*$", text)
    head, rest = parts[0], parts[1:]
    sections = [(rest[i], rest[i + 1]) for i in range(0, len(rest), 2)]
    names = [s[0][1:-1] for s in sections]
    keep = []
    for name, body in sections:
        n = name[1:-1]
        stem = n.split(".", 1)[-1]
        dupes = [m for m in names if m.split(".", 1)[-1] == stem]
        if len(dupes) > 1 and n != max(dupes):      # the highest-numbered copy is the live one
            print("  dropping orphaned section", n, "in", base)
            continue
        keep.append((name, body))
    out = head
    for name, body in keep:
        body = re.sub(r"(?m)^VerboseLogging = .*$", "VerboseLogging = true", body)
        for (fname, key), val in FORCED.items():
            if fname == base:
                body = re.sub(r"(?m)^" + re.escape(key) + r" = .*$", key + " = " + val, body)
        out += name + "\n" + body
    return out

def main():
    pdf = render_pdf()
    tmpzip = ZIP + ".tmp"
    with zipfile.ZipFile(tmpzip, "w", zipfile.ZIP_DEFLATED) as z:
        for f in ("winhttp.dll", "doorstop_config.ini", ".doorstop_version"):
            z.write(os.path.join(GAME, f), f)
        core = os.path.join(GAME, "BepInEx", "core")
        for f in sorted(os.listdir(core)):
            z.write(os.path.join(core, f), "BepInEx/core/" + f)
        bep = open(os.path.join(GAME, "BepInEx", "config", "BepInEx.cfg"), encoding="utf-8").read()
        assert re.search(r"(?m)^AppendLog = true", bep), "BepInEx.cfg must have AppendLog = true"
        z.writestr("BepInEx/config/BepInEx.cfg", bep)
        for p in PLUGINS:
            d = os.path.join(GAME, "BepInEx", "plugins", p)
            dlls = [f for f in os.listdir(d) if f.lower().endswith(".dll")]
            assert len(dlls) == 1, (p, dlls)
            z.write(os.path.join(d, dlls[0]), "BepInEx/plugins/%s/%s" % (p, dlls[0]))
            cfg = os.path.join(GAME, "BepInEx", "config", "stolenrealm.%s.cfg" % p.lower())
            z.writestr("BepInEx/config/" + os.path.basename(cfg), share_config(cfg))
        for extra in ("stolenrealm.specialtooltips.descriptions.txt",):
            z.write(os.path.join(GAME, "BepInEx", "config", extra), "BepInEx/config/" + extra)
        master = open(os.path.join(GAME, "BepInEx", "config", "stolenrealm.mods.cfg"), encoding="utf-8").read()
        for p in PLUGINS:
            assert re.search(r"(?m)^%s = " % p, master), "master file lacks " + p
        assert "TestDriver" not in master
        master = re.sub(r"(?m)^VerboseLogging = .*$", "VerboseLogging = true", master)
        for p in PLUGINS:  # the share copy always ships every mod ON, whatever the user has toggled locally
            master = re.sub(r"(?m)^%s = .*$" % p, "%s = true" % p, master)
        z.writestr("BepInEx/config/stolenrealm.mods.cfg", master)
        z.write(os.path.join(SHARE, README_NAME), README_NAME)
        z.write(pdf, PDF_NAME)
        version = open(os.path.join(ROOT, "VERSION"), encoding="utf-8").read().strip()
        z.writestr("version.txt", version + chr(10))
    os.replace(tmpzip, ZIP)
    with zipfile.ZipFile(ZIP) as z:
        names = z.namelist()
    print("zip:", ZIP, "v" + version, len(names), "entries,", sum(1 for n in names if n.endswith(".dll") and "/plugins/" in n), "plugin DLLs")

if __name__ == "__main__":
    main()
