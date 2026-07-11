# -*- coding: utf-8 -*-
"""Resolve ChaList entries across list/characustom and compose face albedo overlays.

Mirrors ChaControl.CreateFaceTexture SetCreateTexture for st_lip/st_cheek/st_eyeshadow.
"""
from __future__ import annotations

from pathlib import Path
from typing import Any, Dict, List, Optional, Sequence, Tuple

import numpy as np
from PIL import Image

from .cha_list import list_entry_by_id, load_cha_list_from_textasset_object
from .hs2_abdata import abdata, resolve_ab


def _list_name_matches(asset_name: str, list_prefix: str) -> bool:
    """Match st_eye_00 but not st_eye_hl_ / st_eyeshadow_ when prefix is st_eye_."""
    import re

    p = list_prefix
    if not p.endswith("_"):
        p = p + "_"
    # Exact category: prefix + digits (+ optional trailing junk after digits group)
    return re.match(rf"^{re.escape(p)}\d", asset_name) is not None


def find_list_entry(list_prefix: str, entry_id: int) -> Optional[Dict[str, str]]:
    import UnityPy

    for bundle_path in sorted((abdata() / "list" / "characustom").glob("*.unity3d")):
        if bundle_path.name == "namelist.unity3d":
            continue
        env = UnityPy.load(str(bundle_path))
        for obj in env.objects:
            if obj.type.name != "TextAsset":
                continue
            name = obj.read().m_Name
            if not _list_name_matches(name, list_prefix):
                continue
            data = load_cha_list_from_textasset_object(obj)
            entry = list_entry_by_id(data, int(entry_id))
            if entry is not None:
                return entry
    return None


def export_texture_asset(main_ab: str, asset_name: str, out_png: Path) -> bool:
    if not main_ab or main_ab == "0" or not asset_name or asset_name == "0":
        return False
    import UnityPy

    bundle = resolve_ab(main_ab)
    env = UnityPy.load(str(bundle))
    for obj in env.objects:
        if obj.type.name != "Texture2D":
            continue
        data = obj.read()
        name = getattr(data, "m_Name", None) or getattr(data, "name", None)
        if name != asset_name:
            continue
        img = data.image
        if img is None:
            return False
        out_png.parent.mkdir(parents=True, exist_ok=True)
        img.save(out_png)
        return True
    return False


def export_addtex_layer(
    list_prefix: str,
    entry_id: int,
    out_dir: Path,
    *,
    label: str,
) -> Optional[Dict[str, Any]]:
    entry = find_list_entry(list_prefix, entry_id)
    if entry is None:
        return None
    main_ab = entry.get("MainAB", "0")
    add_tex = entry.get("AddTex", "0")
    if main_ab == "0" or add_tex == "0":
        return {"id": entry_id, "disabled": True, "list_entry": entry, "ok": True, "path": ""}
    out_dir = Path(out_dir)
    dest = out_dir / f"{label}_{add_tex}.png"
    ok = export_texture_asset(main_ab, add_tex, dest)
    return {
        "id": entry_id,
        "disabled": False,
        "path": str(dest) if ok else "",
        "asset": add_tex,
        "main_ab": main_ab,
        "list_entry": entry,
        "ok": ok,
    }


def _load_rgba(path: Optional[str]) -> Optional[np.ndarray]:
    if not path:
        return None
    p = Path(path)
    if not p.is_file():
        return None
    return np.asarray(Image.open(p).convert("RGBA"), dtype=np.float32) / 255.0


def _resize_to(img: np.ndarray, hw: Tuple[int, int]) -> np.ndarray:
    h, w = hw
    if img.shape[0] == h and img.shape[1] == w:
        return img
    pil = Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8), mode="RGBA")
    pil = pil.resize((w, h), Image.Resampling.BILINEAR)
    return np.asarray(pil, dtype=np.float32) / 255.0


def blend_addtex(
    base: np.ndarray,
    overlay: Optional[np.ndarray],
    color: Sequence[float],
    *,
    strength: float = 1.0,
) -> np.ndarray:
    if overlay is None:
        return base
    ov = _resize_to(overlay, (base.shape[0], base.shape[1]))
    col = np.array(
        [float(color[i]) if i < len(color) else 1.0 for i in range(4)],
        dtype=np.float32,
    )
    # HS2 makeup AddTex: typically solid RGB marker + alpha mask; tint comes from card color.
    if ov.shape[2] >= 4 and float(ov[..., 3].max()) >= 0.02:
        a = ov[..., 3:4] * col[3] * strength
        rgb = np.broadcast_to(col[:3], (*ov.shape[:2], 3)).copy()
        # If texture has real luminance variation in RGB, modulate intensity
        lum = ov[..., :3].mean(axis=2, keepdims=True)
        if float(lum.std()) > 0.04:
            rgb = rgb * np.clip(lum / max(float(lum.max()), 1e-6), 0, 1)
    else:
        rgb = ov[..., :3] * col[:3]
        a = ov[..., :3].max(axis=2, keepdims=True) * col[3] * strength
    out = base[..., :3] * (1.0 - a) + rgb * a
    alpha = base[..., 3:4] if base.shape[2] >= 4 else np.ones_like(a)
    return np.concatenate([np.clip(out, 0, 1), alpha], axis=2)


def compose_face_albedo(
    main_tex: np.ndarray,
    *,
    skin_tint: Sequence[float],
    eyeshadow: Optional[np.ndarray] = None,
    eyeshadow_color: Sequence[float] = (1, 1, 1, 1),
    cheek: Optional[np.ndarray] = None,
    cheek_color: Sequence[float] = (1, 1, 1, 1),
    lip: Optional[np.ndarray] = None,
    lip_color: Sequence[float] = (1, 1, 1, 1),
    occlusion: Optional[np.ndarray] = None,
) -> np.ndarray:
    base = main_tex.copy()
    if base.shape[2] == 3:
        base = np.concatenate([base, np.ones((*base.shape[:2], 1), dtype=np.float32)], axis=2)
    tint = np.array(list(skin_tint)[:3], dtype=np.float32)
    base[..., :3] *= tint
    base = blend_addtex(base, eyeshadow, eyeshadow_color, strength=1.0)
    base = blend_addtex(base, cheek, cheek_color, strength=0.85)
    base = blend_addtex(base, lip, lip_color, strength=1.0)
    if occlusion is not None:
        ao = _resize_to(occlusion, (base.shape[0], base.shape[1]))
        factor = 0.55 + 0.45 * ao[..., :3].mean(axis=2, keepdims=True)
        base[..., :3] = np.clip(base[..., :3] * factor, 0, 1)
    return base


def load_rgba(path: Optional[str]) -> Optional[np.ndarray]:
    return _load_rgba(path)
