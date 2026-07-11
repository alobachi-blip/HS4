# -*- coding: utf-8 -*-
import json
from pathlib import Path

import UnityPy

root = Path(r"D:\HS2\abdata\list\h\animationinfo")
keys = ("105", "106", "107", "覗", "トイレ", "風呂", "Peep", "peep", "Bath", "Toilet", "入浴")

for p in sorted(root.glob("*.unity3d")):
    env = UnityPy.load(str(p))
    for obj in env.objects:
        typ = obj.type.name
        try:
            data = obj.read()
        except Exception:
            continue
        name = getattr(data, "name", None) or getattr(data, "m_Name", None) or ""

        if typ == "TextAsset":
            text = getattr(data, "m_Script", None)
            if text is None:
                text = getattr(data, "script", None)
            if isinstance(text, bytes):
                decoded = None
                for enc in ("utf-8", "utf-16-le", "cp932"):
                    try:
                        decoded = text.decode(enc)
                        break
                    except Exception:
                        pass
                text = decoded
            if not text:
                continue
            s = str(text)
            if not any(k in s for k in keys):
                continue
            print(f"TEXT {p.name} name={name!r} len={len(s)}")
            for line in s.splitlines():
                if any(k in line for k in keys):
                    print(" ", line[:240])
            continue

        if typ not in ("MonoBehaviour", "GameObject"):
            continue
        try:
            tree = obj.read_typetree()
        except Exception:
            continue
        blob = json.dumps(tree, ensure_ascii=False)
        if not any(k in blob for k in keys):
            continue
        print(f"MB {p.name} typ={typ} name={name!r}")
        if isinstance(tree, dict):
            # ExcelData often has list
            for k, v in list(tree.items())[:30]:
                vs = repr(v)
                if any(x in vs for x in keys) or k.lower() in ("list", "data", "param"):
                    print(f"  key={k} type={type(v).__name__} sample={vs[:200]}")
print("done")
