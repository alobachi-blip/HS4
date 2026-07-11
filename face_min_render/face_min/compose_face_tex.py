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
    # HS2 makeup AddTex: typically solid RGB marker + alpha mask; tint from card color.
    # Exception: st_eyebrow often ships as full-opaque alpha with RGB silhouette; the game
    # places it via eyebrowLayout on _Texture3 — without layout, use RGB as coverage.
    if ov.shape[2] >= 4 and float(ov[..., 3].max()) >= 0.02:
        a = ov[..., 3:4] * col[3] * strength
        rgb = np.broadcast_to(col[:3], (*ov.shape[:2], 3)).copy()
        lum = ov[..., :3].mean(axis=2, keepdims=True)
        if float(ov[..., 3].min()) > 0.95 and float(lum.std()) > 0.02:
            # opaque sheet → RGB silhouette (eyebrow / some mod sheets)
            a = np.clip(ov[..., :3].max(axis=2, keepdims=True), 0, 1) * col[3] * strength
            rgb = np.broadcast_to(col[:3], (*ov.shape[:2], 3)).copy()
        elif float(lum.std()) > 0.04:
            rgb = rgb * np.clip(lum / max(float(lum.max()), 1e-6), 0, 1)
    else:
        rgb = ov[..., :3] * col[:3]
        a = ov[..., :3].max(axis=2, keepdims=True) * col[3] * strength
    out = base[..., :3] * (1.0 - a) + rgb * a
    alpha = base[..., 3:4] if base.shape[2] >= 4 else np.ones_like(a)
    return np.concatenate([np.clip(out, 0, 1), alpha], axis=2)


# ChaFileFace defaults (ChaFileFace.cs ctor)
DEFAULT_EYEBROW_LAYOUT: Tuple[float, float, float, float] = (0.5, 0.375, 0.666, 0.666)
DEFAULT_EYEBROW_TILT: float = 0.5


def _lerp(a: float, b: float, t: float) -> float:
    t = float(np.clip(t, 0.0, 1.0))
    return float(a + (b - a) * t)


def eyebrow_shader_uv_params(
    layout_rates: Sequence[float] = DEFAULT_EYEBROW_LAYOUT,
    tilt_rate: float = DEFAULT_EYEBROW_TILT,
) -> Tuple[float, float, float, float, float]:
    """ChaControl.ChangeEyebrowLayout / ChangeEyebrowTilt → (_Texture3UV, _Texture3Rotator).

    Returns (offset_x, offset_y, scale_x, scale_y, rotator_radians).
    """
    lx = float(layout_rates[0]) if len(layout_rates) > 0 else DEFAULT_EYEBROW_LAYOUT[0]
    ly = float(layout_rates[1]) if len(layout_rates) > 1 else DEFAULT_EYEBROW_LAYOUT[1]
    lz = float(layout_rates[2]) if len(layout_rates) > 2 else DEFAULT_EYEBROW_LAYOUT[2]
    lw = float(layout_rates[3]) if len(layout_rates) > 3 else DEFAULT_EYEBROW_LAYOUT[3]
    ox = _lerp(-0.2, 0.2, lx)
    oy = _lerp(0.16, 0.0, ly)
    sx = _lerp(2.0, 0.5, lz)
    sy = _lerp(2.0, 0.5, lw)
    rot = _lerp(-0.15, 0.15, float(tilt_rate))
    return ox, oy, sx, sy, rot


def _sample_uv_bilinear(tex: np.ndarray, u: np.ndarray, v: np.ndarray) -> np.ndarray:
    """Bilinear sample; u/v in 0..1, image row = v (top→bottom, matches UnityPy PNG)."""
    h, w = tex.shape[:2]
    u = np.clip(u, 0, 1) * (w - 1)
    v = np.clip(v, 0, 1) * (h - 1)
    x0 = np.floor(u).astype(np.int32)
    y0 = np.floor(v).astype(np.int32)
    x1 = np.clip(x0 + 1, 0, w - 1)
    y1 = np.clip(y0 + 1, 0, h - 1)
    x0 = np.clip(x0, 0, w - 1)
    y0 = np.clip(y0, 0, h - 1)
    fu = (u - x0)[..., None]
    fv = (v - y0)[..., None]
    c00 = tex[y0, x0]
    c10 = tex[y0, x1]
    c01 = tex[y1, x0]
    c11 = tex[y1, x1]
    return c00 * (1 - fu) * (1 - fv) + c10 * fu * (1 - fv) + c01 * (1 - fu) * fv + c11 * fu * fv


def blend_eyebrow_matdraw(
    base: np.ndarray,
    eyebrow: Optional[np.ndarray],
    eyebrow_color: Sequence[float],
    layout_rates: Sequence[float] = DEFAULT_EYEBROW_LAYOUT,
    tilt_rate: float = DEFAULT_EYEBROW_TILT,
) -> np.ndarray:
    """Face matDraw eyebrow: ChaControl.ChangeEyebrow* → AIT/Skin True Face _Texture3.

    Authority:
      ChaControl.ChangeEyebrowLayout/Tilt → _Texture3UV (tx,ty,sx,sy) / _Texture3Rotator
      ChaShader.EyebrowTex/Color/Layout/Tilt = _Texture3 / _Color3 / _Texture3UV / _Texture3Rotator
      Shader prop desc: Texture3 UV (tx,ty,sx,sy); stock AddTex is DXT1 white-on-black
      (A=1), same slot as body underhair (mnpk).

    UV (into UnityPy albedo; renderer uses v_img=1-mesh_v):
      mesh UV → rotate about 0.5 by rotator → centered scale (zw) → subtract offset (xy).
      Offset sign chosen so default ty=+0.1 hits fo_head brow-ridge UVs (~mesh_v 0.52)
      against c_t_eyebrow_* stamp bounds — not CreateFaceTexture makeup space.

    Coverage: min(RGB) silhouette × _Color3 (stock sheets are grayscale so min==R;
    rejects non-black clear fill on some DLC sheets e.g. c_t_eyebrow_17).
    Do NOT use blend_addtex — that path is CreateFaceTexture makeup and mis-reads A
    after UV clip.
    """
    if eyebrow is None:
        return base

    src = np.asarray(eyebrow, dtype=np.float32)
    if src.ndim == 2:
        src = np.stack([src, src, src], axis=-1)
    elif src.shape[2] >= 4:
        src = src[..., :3]

    h, w = int(base.shape[0]), int(base.shape[1])
    ox, oy, sx, sy, rot = eyebrow_shader_uv_params(layout_rates, tilt_rate)
    cos_r = float(np.cos(rot))
    sin_r = float(np.sin(rot))

    yy, xx = np.mgrid[0:h, 0:w]
    # Bake matDraw eyebrow into UnityPy albedo (renderer: alb(u,1-mesh_v)).
    #
    # ChaControl → _Texture3UV (tx,ty,sx,sy) + _Texture3Rotator (radians).
    # UV: rotate about 0.5, then centered scale (same zoom sense as eye layout /
    # Lerp(2,0.5,*): larger s → smaller stamp), then subtract offset so default
    # ty=+0.1 lifts the stamp onto brow-ridge mesh UVs (o_head ~ mesh_v 0.52).
    # (Verified against fo_head UV vs c_t_eyebrow_* stamp bounds; TRANSFORM_TEX
    # uv*s+offset without centering lands the stamp on the mouth island.)
    u = (xx + 0.5) / float(w)
    v_img = (yy + 0.5) / float(h)
    mesh_u = u
    mesh_v = 1.0 - v_img

    cu = mesh_u - 0.5
    cv = mesh_v - 0.5
    ru = cu * cos_r - cv * sin_r
    rv = cu * sin_r + cv * cos_r
    su = (ru * sx) + 0.5 - ox
    sv = (rv * sy) + 0.5 - oy

    in_uv = (su >= 0.0) & (su <= 1.0) & (sv >= 0.0) & (sv <= 1.0)
    samp = _sample_uv_bilinear(src, su, 1.0 - sv)
    # Stock st_eyebrow / st_underhair are grayscale DXT1 (R=G=B white-on-black), so any
    # channel equals the silhouette. Use min(RGB): identical to R on those sheets, and
    # ignores the non-black clear fill on some DLC sheets (e.g. c_t_eyebrow_17 red paper).
    mask = np.clip(np.min(samp[..., :3], axis=-1), 0.0, 1.0)
    mask = np.where(in_uv, mask, 0.0)

    col = np.array(
        [float(eyebrow_color[i]) if i < len(eyebrow_color) else 1.0 for i in range(4)],
        dtype=np.float32,
    )
    a = (mask * col[3])[..., None]
    rgb = np.broadcast_to(col[:3], (h, w, 3))
    out = base.copy()
    out[..., :3] = np.clip(out[..., :3] * (1.0 - a) + rgb * a, 0.0, 1.0)
    return out


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
    mole: Optional[np.ndarray] = None,
    mole_color: Sequence[float] = (1, 1, 1, 1),
    eyebrow: Optional[np.ndarray] = None,
    eyebrow_color: Sequence[float] = (0.2, 0.15, 0.12, 1),
    eyebrow_layout: Sequence[float] = DEFAULT_EYEBROW_LAYOUT,
    eyebrow_tilt: float = DEFAULT_EYEBROW_TILT,
    paint0: Optional[np.ndarray] = None,
    paint0_color: Sequence[float] = (1, 0, 0, 1),
    paint1: Optional[np.ndarray] = None,
    paint1_color: Sequence[float] = (1, 0, 0, 1),
    occlusion: Optional[np.ndarray] = None,
) -> np.ndarray:
    """Approximate CreateFaceTexture + matDraw eyebrow (ChaControl)."""
    base = main_tex.copy()
    if base.shape[2] == 3:
        base = np.concatenate([base, np.ones((*base.shape[:2], 1), dtype=np.float32)], axis=2)
    tint = np.array(list(skin_tint)[:3], dtype=np.float32)
    base[..., :3] *= tint
    # CreateFaceTexture order: eyeshadow → paint → cheek → lip → mole
    base = blend_addtex(base, eyeshadow, eyeshadow_color, strength=1.0)
    base = blend_addtex(base, paint0, paint0_color, strength=1.0)
    base = blend_addtex(base, paint1, paint1_color, strength=1.0)
    base = blend_addtex(base, cheek, cheek_color, strength=0.85)
    base = blend_addtex(base, lip, lip_color, strength=1.0)
    base = blend_addtex(base, mole, mole_color, strength=1.0)
    # matDraw ChangeEyebrowKind/Color/Layout/Tilt (not CreateFaceTexture)
    base = blend_eyebrow_matdraw(
        base, eyebrow, eyebrow_color, eyebrow_layout, eyebrow_tilt
    )
    if occlusion is not None:
        ao = _resize_to(occlusion, (base.shape[0], base.shape[1]))
        # Soft AO — strong multiply crushed faces black under our flat lighting
        factor = 0.78 + 0.22 * ao[..., :3].mean(axis=2, keepdims=True)
        base[..., :3] = np.clip(base[..., :3] * factor, 0, 1)
    return base


def load_rgba(path: Optional[str]) -> Optional[np.ndarray]:
    return _load_rgba(path)
