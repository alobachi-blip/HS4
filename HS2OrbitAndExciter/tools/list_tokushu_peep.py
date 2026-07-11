# -*- coding: utf-8 -*-
"""List tokushu (特殊) animations with ActionCtrl Item2 in {4,5} = masturbation/peeping."""
from pathlib import Path
import UnityPy

root = Path(r"D:\HS2\abdata\list\h\animationinfo")


def fix(s):
    if not isinstance(s, str):
        return str(s)
    try:
        return s.encode("latin1").decode("cp932")
    except Exception:
        return s


rows = []
for p in sorted(root.glob("*.unity3d")):
    env = UnityPy.load(str(p))
    for obj in env.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        try:
            tree = obj.read_typetree()
        except Exception:
            continue
        name = tree.get("m_Name") or ""
        if not str(name).startswith("tokushu"):
            continue
        lst = tree.get("list")
        if not isinstance(lst, list) or len(lst) < 2:
            continue
        header = [fix(x) for x in lst[0].get("list", [])]
        # expect: name, ID, ... 行動系, 制御系, 場所, ...
        # From dump: index 14=行動系, 15=制御系, 16=場所, 11=female anim name
        for row in lst[1:]:
            cells = row.get("list")
            if not isinstance(cells, list) or len(cells) < 16:
                continue
            cells_f = [fix(c) for c in cells]
            try:
                act1 = int(cells_f[14])
                act2 = int(cells_f[15])
            except Exception:
                continue
            if act1 != 3 or act2 not in (4, 5):
                continue
            rows.append({
                "mb": name,
                "file": p.name,
                "title": cells_f[0],
                "id": cells_f[1],
                "act": (act1, act2),
                "place": cells_f[16] if len(cells_f) > 16 else "",
                "female_asset": cells_f[10],
                "female_file": cells_f[11],
                "female_base": cells_f[9],
            })

print(f"found {len(rows)} tokushu peep/masturb rows")
for r in rows:
    kind = "覗き/Peeping" if r["act"][1] == 5 else "自慰/Masturbation"
    print(f"[{kind}] id={r['id']:>3}  {r['title']}  place={r['place']}  anim={r['female_file']}  asset={r['female_asset']}")
