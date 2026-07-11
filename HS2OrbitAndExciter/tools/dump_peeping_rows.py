# -*- coding: utf-8 -*-
"""Dump H animationinfo rows where ActionCtrl category == 5 (Peeping)."""
from pathlib import Path
import UnityPy

root = Path(r"D:\HS2\abdata\list\h\animationinfo")


def fix(s):
    if not isinstance(s, str):
        return s
    # UnityPy often mis-decodes Shift-JIS as latin1/cp1252
    try:
        return s.encode("latin1").decode("cp932")
    except Exception:
        return s


for p in sorted(root.glob("*.unity3d")):
    env = UnityPy.load(str(p))
    for obj in env.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        try:
            tree = obj.read_typetree()
        except Exception:
            continue
        lst = tree.get("list") if isinstance(tree, dict) else None
        if not isinstance(lst, list) or not lst:
            continue
        # header row
        header = lst[0].get("list") if isinstance(lst[0], dict) else None
        if not isinstance(header, list):
            continue
        header_f = [fix(x) for x in header]
        # find ID / ActionCtrl / name columns by header text
        # typical: ID near start, ActionCtrl as tuple fields, nameAnimation
        name = tree.get("m_Name") or getattr(obj.read(), "name", "")
        # print header once if looks like anim list
        joined = "\t".join(header_f)
        if "ID" not in joined and "id" not in joined.lower():
            continue
        # dump peeping-ish rows: look for ActionCtrl values 5 or names containing bath/toilet
        peep_rows = []
        for row in lst[1:]:
            cells = row.get("list") if isinstance(row, dict) else None
            if not isinstance(cells, list):
                continue
            cells_f = [fix(c) for c in cells]
            line = "\t".join(str(c) for c in cells_f)
            # ActionCtrl often two ints; category 5 peeping / 4 masturbation
            # Heuristic: any cell == '5' with peep-like name, or id 105/106
            id_cell = cells_f[0] if cells_f else ""
            if str(id_cell) in ("105", "106", "107") or any(
                k in line for k in ("覗", "トイレ", "風呂", "入浴", "peep", "Peep", "Toilet", "Bath")
            ):
                peep_rows.append(cells_f)
            # also ActionCtrl first component == 5 (often columns around mid)
            # try parse known layout from HScene.AnimationListInfo loaders
        if peep_rows or "覗" in joined or "アニメ" in joined:
            print("=" * 60)
            print(f"FILE {p.name} MB={name}")
            print("HEADER:", " | ".join(header_f[:25]))
            for r in peep_rows[:20]:
                print("ROW:", " | ".join(str(x) for x in r[:25]))
            if not peep_rows:
                # dump first data row sample
                if len(lst) > 1 and isinstance(lst[1], dict):
                    sample = [fix(c) for c in lst[1].get("list", [])]
                    print("SAMPLE:", " | ".join(str(x) for x in sample[:25]))
print("done")
