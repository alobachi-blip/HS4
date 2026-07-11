# -*- coding: utf-8 -*-
"""cf_customhead category table (game TextAsset format)."""
from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Dict, List


def parse_cf_customhead_text(text: str) -> Dict[int, List[dict]]:
    """Parse tab-separated cf_customhead.

    Columns: category, SrcName, use pos.xyz, rot.xyz, scl.xyz (\"0\" = unused).
    """
    index_to_src: Dict[int, List[dict]] = {}
    for line in text.splitlines():
        line = line.strip("\r")
        if not line.strip():
            continue
        row = line.split("\t")
        if len(row) < 2:
            continue
        try:
            cat = int(row[0])
        except ValueError:
            continue
        name = row[1].strip()
        use = row[2:11] if len(row) >= 11 else ["0"] * 9
        while len(use) < 9:
            use.append("0")
        use_flags = {
            "pos": [u != "0" for u in use[0:3]],
            "rot": [u != "0" for u in use[3:6]],
            "scl": [u != "0" for u in use[6:9]],
        }
        index_to_src.setdefault(cat, []).append({"name": name, "use": use_flags})
    return index_to_src


def load_category_table(path: str | Path) -> Dict[int, List[dict]]:
    path = Path(path)
    if path.suffix.lower() == ".json":
        raw = json.loads(path.read_text(encoding="utf-8"))
        return {int(k): v for k, v in raw.items()}
    return parse_cf_customhead_text(path.read_text(encoding="utf-8"))


def write_category_json(table: Dict[int, List[dict]], path: str | Path) -> None:
    path = Path(path)
    path.write_text(json.dumps({str(k): v for k, v in sorted(table.items())}, indent=2), encoding="utf-8")
