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

    Returns (tx, ty, sx, sy, rotator) where rotator is the raw ChaControl float
    (sincos uses rotator * π — see EYEBROW_UV_SPEC.md).
    """
    lx = float(layout_rates[0]) if len(layout_rates) > 0 else DEFAULT_EYEBROW_LAYOUT[0]
    ly = float(layout_rates[1]) if len(layout_rates) > 1 else DEFAULT_EYEBROW_LAYOUT[1]
    lz = float(layout_rates[2]) if len(layout_rates) > 2 else DEFAULT_EYEBROW_LAYOUT[2]
    lw = float(layout_rates[3]) if len(layout_rates) > 3 else DEFAULT_EYEBROW_LAYOUT[3]
    tx = _lerp(-0.2, 0.2, lx)
    ty = _lerp(0.16, 0.0, ly)
    sx = _lerp(2.0, 0.5, lz)
    sy = _lerp(2.0, 0.5, lw)
    rot = _lerp(-0.15, 0.15, float(tilt_rate))
    return tx, ty, sx, sy, rot


def eyebrow_stamp_uv_from_uv1(
    uv1: np.ndarray, tx: float, ty: float, sx: float, sy: float, rot: float
) -> np.ndarray:
    """Mesh UV1 → _Texture3 stamp UV. Authority: research/skin_true_face/EYEBROW_UV_SPEC.md."""
    u = uv1[..., 0] + tx
    v = uv1[..., 1] + ty
    c = float(np.cos(rot * np.pi))
    s = float(np.sin(rot * np.pi))
    px, py = u - 0.3, v - 0.4
    rx = px * c + py * s
    ry = -px * s + py * c
    u, v = rx + 0.3, ry + 0.4
    u = u * sx + (sx - 1.0) * (-0.1)
    v = v * sy + (sy - 1.0) * (-0.43)
    return np.stack([u, v], axis=-1)


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


def load_o_head_uv01_faces(head_id: int = 0) -> Tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Return (uv0, uv1, faces) for fo_head o_head — eyebrow matDraw uses UV1."""
    import UnityPy

    from .extract_eyes import resolve_fo_head_entry
    from .extract_skeleton import _decode_vertex_channel

    entry = resolve_fo_head_entry(int(head_id))
    bundle_rel = entry.get("MainAB") or "chara/00/fo_head_00.unity3d"
    if bundle_rel in ("0", ""):
        bundle_rel = "chara/00/fo_head_00.unity3d"
    env = UnityPy.load(str(resolve_ab(bundle_rel)))
    for obj in env.objects:
        if obj.type.name != "Mesh":
            continue
        mesh = obj.read()
        if getattr(mesh, "m_Name", None) != "o_head":
            continue
        vd = mesh.m_VertexData
        n = int(vd.m_VertexCount)
        uv0 = _decode_vertex_channel(vd, 4, n).astype(np.float32)
        uv1 = _decode_vertex_channel(vd, 5, n).astype(np.float32)
        ib = bytes(mesh.m_IndexBuffer)
        # IndexFormat 0 = UInt16
        idx = np.frombuffer(ib, dtype=np.uint16).astype(np.int32).reshape(-1, 3)
        return uv0, uv1, idx
    raise RuntimeError(f"o_head mesh not found for head_id={head_id}")


def blend_eyebrow_matdraw(
    base: np.ndarray,
    eyebrow: Optional[np.ndarray],
    eyebrow_color: Sequence[float],
    layout_rates: Sequence[float] = DEFAULT_EYEBROW_LAYOUT,
    tilt_rate: float = DEFAULT_EYEBROW_TILT,
    *,
    head_id: int = 0,
    uv0: Optional[np.ndarray] = None,
    uv1: Optional[np.ndarray] = None,
    faces: Optional[np.ndarray] = None,
) -> np.ndarray:
    """Face matDraw eyebrow per research/skin_true_face/EYEBROW_UV_SPEC.md.

    Bakes runtime `_Texture3` sampling into UV0 albedo by rasterizing o_head
    triangles (shader samples with mesh UV1, not UV0).
    """
    if eyebrow is None:
        return base

    src = np.asarray(eyebrow, dtype=np.float32)
    if src.ndim == 2:
        src = np.stack([src, src, src], axis=-1)
    elif src.shape[2] >= 4:
        src = src[..., :3]

    h, w = int(base.shape[0]), int(base.shape[1])
    tx, ty, sx, sy, rot = eyebrow_shader_uv_params(layout_rates, tilt_rate)

    if uv0 is None or uv1 is None or faces is None:
        uv0, uv1, faces = load_o_head_uv01_faces(head_id)

    # Coverage in UV0 albedo space — vectorized per-triangle raster
    cov = np.zeros((h, w), dtype=np.float32)
    faces = np.asarray(faces, dtype=np.int32)
    for tri in faces:
        i0, i1, i2 = int(tri[0]), int(tri[1]), int(tri[2])
        q0, q1, q2 = uv1[i0], uv1[i1], uv1[i2]
        if max(float(q0[1]), float(q1[1]), float(q2[1])) < 0.04 and max(
            float(q0[0]), float(q1[0]), float(q2[0])
        ) < 0.04:
            continue
        p0, p1, p2 = uv0[i0], uv0[i1], uv0[i2]
        xs = np.array([p0[0], p1[0], p2[0]], dtype=np.float32)
        ys = 1.0 - np.array([p0[1], p1[1], p2[1]], dtype=np.float32)
        x0i = max(0, int(np.floor(xs.min() * w)))
        x1i = min(w - 1, int(np.ceil(xs.max() * w)))
        y0i = max(0, int(np.floor(ys.min() * h)))
        y1i = min(h - 1, int(np.ceil(ys.max() * h)))
        if x0i > x1i or y0i > y1i:
            continue
        a0x, a0y = float(xs[0] * w), float(ys[0] * h)
        a1x, a1y = float(xs[1] * w), float(ys[1] * h)
        a2x, a2y = float(xs[2] * w), float(ys[2] * h)
        area = (a1x - a0x) * (a2y - a0y) - (a2x - a0x) * (a1y - a0y)
        if abs(area) < 1e-8:
            continue
        yy, xx = np.mgrid[y0i : y1i + 1, x0i : x1i + 1]
        cx = xx.astype(np.float32) + 0.5
        cy = yy.astype(np.float32) + 0.5
        w0 = ((a1x - cx) * (a2y - cy) - (a2x - cx) * (a1y - cy)) / area
        w1 = ((a2x - cx) * (a0y - cy) - (a0x - cx) * (a2y - cy)) / area
        w2 = 1.0 - w0 - w1
        inside = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
        if not np.any(inside):
            continue
        u1x = q0[0] * w0 + q1[0] * w1 + q2[0] * w2
        u1y = q0[1] * w0 + q1[1] * w1 + q2[1] * w2
        stamp = eyebrow_stamp_uv_from_uv1(
            np.stack([u1x[inside], u1y[inside]], axis=-1), tx, ty, sx, sy, rot
        )
        su, sv = stamp[:, 0], stamp[:, 1]
        ok = (su >= 0.0) & (su <= 1.0) & (sv >= 0.0) & (sv <= 1.0)
        if not np.any(ok):
            continue
        samp = _sample_uv_bilinear(src, su[ok], 1.0 - sv[ok])
        # Hanmen Face uses tex.g; stock DXT1 ⇒ R=G=B. Threshold drops red-paper /
        # soft sheet clears so UV1 island does not paint a gray forehead panel.
        g = samp[..., 1]
        cval = np.clip((g - 0.45) / 0.55, 0.0, 1.0).astype(np.float32)
        ys_i = yy[inside][ok].astype(np.int32)
        xs_i = xx[inside][ok].astype(np.int32)
        np.maximum.at(cov, (ys_i, xs_i), cval)
    col = np.array(
        [float(eyebrow_color[i]) if i < len(eyebrow_color) else 1.0 for i in range(4)],
        dtype=np.float32,
    )
    a = (cov * col[3])[..., None]
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
    eyebrow_head_id: int = 0,
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
        base,
        eyebrow,
        eyebrow_color,
        eyebrow_layout,
        eyebrow_tilt,
        head_id=int(eyebrow_head_id),
    )
    if occlusion is not None:
        ao = _resize_to(occlusion, (base.shape[0], base.shape[1]))
        # Soft AO — strong multiply crushed faces black under our flat lighting
        factor = 0.78 + 0.22 * ao[..., :3].mean(axis=2, keepdims=True)
        base[..., :3] = np.clip(base[..., :3] * factor, 0, 1)
    return base


def load_rgba(path: Optional[str]) -> Optional[np.ndarray]:
    return _load_rgba(path)
