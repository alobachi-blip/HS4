# -*- coding: utf-8 -*-
from pathlib import Path
import sys
import UnityPy

sys.stdout.reconfigure(encoding="utf-8")
root = Path(r"D:\HS2\abdata\list\h\animationinfo")


def fix(s):
    if not isinstance(s, str):
        return str(s)
    try:
        return s.encode("latin1").decode("cp932")
    except Exception:
        return s


want_ids = {"8", "9", "15", "102", "105", "106", "107"}
for p in sorted(root.glob("*.unity3d")):
    env = UnityPy.load(str(p))
    for obj in env.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        try:
            tree = obj.read_typetree()
        except Exception:
            continue
        name = str(tree.get("m_Name") or "")
        lst = tree.get("list")
        if not isinstance(lst, list) or len(lst) < 2:
            continue
        for row in lst[1:]:
            cells = row.get("list")
            if not isinstance(cells, list) or len(cells) < 2:
                continue
            cells_f = [fix(c) for c in cells]
            if str(cells_f[1]) not in want_ids:
                continue
            # only anim-list shaped (have 行動系 around col14)
            if len(cells_f) < 16:
                continue
            print(f"{p.name}/{name}: id={cells_f[1]} title={cells_f[0]} act=({cells_f[14]},{cells_f[15]}) place={cells_f[16]} fanim={cells_f[11]} path={cells_f[10]}")
