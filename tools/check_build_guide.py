"""Check a build guide's HTML for the things a reader must find in it. Today: the Attributes table (section 1) must
name every effect of every attribute from data/attributes.md and give a reason per attribute.

Usage:  python C:/Claude/General/stolen-realm/tools/check_build_guide.py <BuildN-Name.html>
Exit code 1 when anything is missing.
"""
import re, sys, io, html

# per attribute: (label, regex that must match the row's "per point" cell, case-insensitive)
REQUIRED = {
    "Might": [("damage & healing", r"damage\s*(&amp;|&|and)\s*healing"), ("summon damage", r"summon"), ("armor and magic armor", r"magic armor")],
    "Dexterity": [("crit rating / chance curve", r"crit rating|crit chance|critical"), ("crit damage", r"crit damage|critical hit damage"), ("movement per 25", r"movement")],
    "Intelligence": [("mana", r"mana"), ("skill range per 25", r"range"), ("summon health", r"summon")],
    "Vitality": [("+5 health", r"\+\s*5\s*health|5 health"), ("+1% max health", r"%\s*(max\s*)?health|health\s*%")],
    "Reflex": [("dodge rating curve", r"dodge rating"), ("dodge counter chance", r"counter.{0,40}chance|chance.{0,40}counter"), ("counter attack damage", r"counter.{0,40}damage"), ("opportunity attack damage", r"opportunity"), ("counters per turn per 25", r"per turn|per 25|counter attack per")],
}


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    t = io.open(sys.argv[1], encoding="utf-8").read()
    errors = []
    sec = re.search(r"<h2>\s*1\.\s*Attributes.*?</table>", t, re.S)
    if not sec:
        sys.exit("ERROR   no '1. Attributes' section with a table")
    rows = re.findall(r"<tr>(.*?)</tr>", sec.group(0), re.S)
    cells = {}
    for r in rows:
        tds = [html.unescape(re.sub(r"<[^>]+>", " ", c)).strip() for c in re.findall(r"<td[^>]*>(.*?)</td>", r, re.S)]
        if len(tds) >= 3:
            cells[tds[0].strip()] = (tds[1], tds[2])
    for attr, reqs in REQUIRED.items():
        row = next((v for k, v in cells.items() if k.lower().startswith(attr.lower())), None)
        if row is None:
            errors.append(f"{attr}: no row in the attribute table")
            continue
        per, why = row
        for label, rx in reqs:
            if not re.search(rx, per, re.I):
                errors.append(f"{attr}: the 'per point' cell does not mention {label}")
        if len(why.split()) < 8:
            errors.append(f"{attr}: the 'why for this build' cell is empty or too short ({len(why.split())} words); say how the build uses or ignores each effect")
    for e in errors:
        print("ERROR  ", e)
    print(f"== attributes table: {len(errors)} error(s)")
    sys.exit(1 if errors else 0)


if __name__ == "__main__":
    main()
