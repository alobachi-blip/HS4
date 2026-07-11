# -*- coding: utf-8 -*-
"""Export face textures from HS2 Copy abdata using ChaList MainAB/MainTex paths."""
from __future__ import annotations

import json
from pathlib import Path
from typing import Dict, Optional

import numpy as np
from PIL import Image

from .cha_list import resolve_face_skin
from .hs2_abdata import resolve_ab


def _export_texture(bundle_path: Path, asset_name: str, out_png: Path) -> bool:
    import UnityPy

    env = UnityPy.load(str(bundle_path))
    for obj in env.objects:
        if obj.type.name != "Texture2D":
            continue
        data = obj.read()
        name = getattr(data, "m_Name", None) or getattr(data, "name", None)
        if name != asset_name:
            continue
        img = data.image  # PIL Image
        if img is None:
            return False
        out_png.parent.mkdir(parents=True, exist_ok=True)
        img.save(out_png)
        return True
    return False


def extract_face_skin_pack(
    out_dir: Path,
    *,
    skin_id: int = 0,
    head_id: int = 0,
) -> Dict[str, str]:
    """Extract MainTex / Normal / Occlusion for one skinId into out_dir.

    Paths follow ChaControl.CreateFaceTexture + ChaListDefine.KeyType.
    """
    out_dir = Path(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    entry = resolve_face_skin(skin_id, head_id)
    main_ab = entry["MainAB"]
    bundle = resolve_ab(main_ab)

    mapping = {
        "main": entry.get("MainTex", ""),
        "normal": entry.get("NormalMapTex", ""),
        "occlusion": entry.get("OcclusionMapTex", ""),
    }
    paths = {"list_entry": entry, "bundle": str(bundle)}
    for key, asset in mapping.items():
        if not asset or asset.lower() == "0":
            continue
        dest = out_dir / f"{key}_{asset}.png"
        ok = _export_texture(bundle, asset, dest)
        paths[key] = str(dest) if ok else ""
        paths[f"{key}_asset"] = asset

    (out_dir / "skin_meta.json").write_text(
        json.dumps({"skin_id": skin_id, "head_id": head_id, **{k: v for k, v in paths.items() if k != "list_entry"}, "list_entry": entry}, indent=2),
        encoding="utf-8",
    )
    return paths


def load_rgba(path: Optional[str]) -> Optional[np.ndarray]:
    if not path:
        return None
    p = Path(path)
    if not p.is_file():
        return None
    img = Image.open(p).convert("RGBA")
    return np.asarray(img, dtype=np.float32) / 255.0
