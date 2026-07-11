# -*- coding: utf-8 -*-
"""Read-only access to HS2 abdata (default: D:\\HS2 - Copy). Never writes into the game tree."""
from __future__ import annotations

import os
from pathlib import Path

# Official live game must not be used; only the Copy.
DEFAULT_HS2_COPY = Path(os.environ.get("HS2_COPY_ROOT", r"D:\HS2 - Copy"))


def hs2_root() -> Path:
    root = Path(DEFAULT_HS2_COPY)
    if not root.is_dir():
        raise FileNotFoundError(f"HS2 Copy not found: {root}")
    return root


def abdata() -> Path:
    p = hs2_root() / "abdata"
    if not p.is_dir():
        raise FileNotFoundError(f"abdata missing under {hs2_root()}")
    return p


def resolve_ab(rel: str) -> Path:
    """Resolve AssetBundle path like 'chara/00/ft_skin_f_00.unity3d' under abdata."""
    rel = rel.replace("\\", "/").lstrip("/")
    if rel.startswith("abdata/"):
        rel = rel[len("abdata/") :]
    path = abdata() / Path(*rel.split("/"))
    if not path.is_file():
        raise FileNotFoundError(f"AssetBundle not found: {path}")
    return path
