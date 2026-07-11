# -*- coding: utf-8 -*-
"""Build demo_pack from assets/o_head.obj."""
from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from face_min.demo_assets import build_demo_pack


def main():
    obj = ROOT / "assets" / "o_head.obj"
    out = ROOT / "assets" / "demo_pack"
    if not obj.exists():
        raise SystemExit(f"Missing {obj}")
    pack = build_demo_pack(obj, out)
    print(f"Wrote demo pack → {pack}")
    meta = (pack / "meta.json").read_text(encoding="utf-8")
    print(meta)


if __name__ == "__main__":
    main()
