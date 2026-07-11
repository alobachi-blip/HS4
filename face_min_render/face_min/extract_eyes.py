# -*- coding: utf-8 -*-
"""Export full non-hair CmpFace draw set from fo_head (p_cf_head_XX).

CmpFace renderers (no hair):
  o_head, o_eyebase_L/R, o_eyelashes, o_eyeshadow, o_tooth, o_tang, o_namida
"""
from __future__ import annotations

import json
from pathlib import Path
from typing import Dict, List, Optional, Sequence, Tuple

import numpy as np

from .cha_list import list_entry_by_id, load_category_list, load_cha_list_from_textasset_object
from .hs2_abdata import abdata, resolve_ab

# Draw order: opaque head parts first, then translucent overlays.
FACE_DRAW_PARTS: Tuple[str, ...] = (
    "o_head",
    "o_tooth",
    "o_tang",
    "o_eyebase_L",
    "o_eyebase_R",
    "o_eyeshadow",
    "o_eyelashes",
    "o_namida",
)

# Default: skip tears (usually off).
DEFAULT_SKIP_PARTS = frozenset({"o_namida"})

PART_MAT_MAINTEX = {
    "o_eyelashes": "_MainTex",
    "o_eyeshadow": "_MainTex",
    "o_tooth": "_MainTex",
    "o_tang": "_MainTex",
    "o_namida": "_MainTex",
}


def _mesh_to_numpy(mesh) -> Tuple[np.ndarray, np.ndarray, np.ndarray]:
    from .obj_io import load_obj_from_text

    obj_text = mesh.export()
    if not obj_text or not isinstance(obj_text, str):
        raise RuntimeError("Mesh.export() returned empty")
    return load_obj_from_text(obj_text)


def resolve_fo_head_entry(head_id: int) -> Dict[str, str]:
    for prefix_bundle in sorted((abdata() / "list" / "characustom").glob("*.unity3d")):
        if prefix_bundle.name == "namelist.unity3d":
            continue
        import UnityPy

        env = UnityPy.load(str(prefix_bundle))
        for obj in env.objects:
            if obj.type.name != "TextAsset":
                continue
            name = obj.read().m_Name
            if not name.startswith("fo_head_"):
                continue
            data = load_cha_list_from_textasset_object(obj)
            entry = list_entry_by_id(data, int(head_id))
            if entry:
                return entry
    data = load_category_list("fo_head_00")
    return list_entry_by_id(data, 0) or {}


def _transform_for_go(env, go) -> Optional[object]:
    go_id = go.object_reader.path_id
    for tobj in env.objects:
        if tobj.type.name != "Transform":
            continue
        t = tobj.read()
        try:
            if t.m_GameObject.path_id == go_id:
                return t
        except Exception:
            continue
    return None


def _parent_prefab_name(env, go) -> Optional[str]:
    cur = _transform_for_go(env, go)
    for _ in range(12):
        if cur is None:
            return None
        try:
            name = cur.m_GameObject.read().m_Name
        except Exception:
            return None
        if name.startswith("p_cf_head_") and not name.endswith("_hit"):
            return name
        try:
            if not cur.m_Father:
                return None
            cur = cur.m_Father.read()
        except Exception:
            return None
    return None


def _mat_tex_map(mat) -> Dict[str, str]:
    out: Dict[str, str] = {}
    props = getattr(mat, "m_SavedProperties", None)
    if not props:
        return out
    for t in props.m_TexEnvs or []:
        try:
            pname, envt = t[0], t[1]
            if envt.m_Texture:
                out[str(pname)] = envt.m_Texture.read().m_Name
        except Exception:
            continue
    return out


def _export_texture_from_env(env, asset_name: str, dest: Path) -> bool:
    if not asset_name or asset_name in ("0", "None"):
        return False
    for obj in env.objects:
        if obj.type.name != "Texture2D":
            continue
        data = obj.read()
        if getattr(data, "m_Name", None) != asset_name:
            continue
        if data.image is None:
            return False
        dest.parent.mkdir(parents=True, exist_ok=True)
        data.image.save(dest)
        return True
    return False


def _export_smr_part(env, go_name: str, prefab: str, out_dir: Path) -> Optional[Dict[str, object]]:
    for obj in env.objects:
        if obj.type.name != "SkinnedMeshRenderer":
            continue
        data = obj.read()
        try:
            go = data.m_GameObject.read()
            if go.m_Name != go_name:
                continue
        except Exception:
            continue
        if _parent_prefab_name(env, go) != prefab:
            continue
        if not data.m_Mesh:
            continue
        mesh = data.m_Mesh.read()
        verts, faces, uvs = _mesh_to_numpy(mesh)
        npz = out_dir / f"{go_name}.npz"
        np.savez_compressed(npz, verts=verts, faces=faces, uvs=uvs)

        mat_name = None
        tex_map: Dict[str, str] = {}
        mats = getattr(data, "m_Materials", None) or []
        if mats:
            try:
                mat = mats[0].read()
                mat_name = mat.m_Name
                tex_map = _mat_tex_map(mat)
            except Exception:
                pass

        tex_paths: Dict[str, str] = {}
        for prop, tex_name in tex_map.items():
            safe = prop.replace(" ", "_").lstrip("_")
            dest = out_dir / f"{go_name}_{safe}_{tex_name}.png"
            if _export_texture_from_env(env, tex_name, dest):
                tex_paths[prop] = str(dest)

        return {
            "npz": str(npz),
            "n_verts": int(verts.shape[0]),
            "n_faces": int(faces.shape[0]),
            "center": verts.mean(axis=0).tolist(),
            "aabb_min": verts.min(axis=0).tolist(),
            "aabb_max": verts.max(axis=0).tolist(),
            "prefab": prefab,
            "mesh_name": getattr(mesh, "m_Name", go_name),
            "mesh_path_id": int(data.m_Mesh.path_id),
            "material": mat_name,
            "tex_map": tex_map,
            "tex_paths": tex_paths,
        }
    return None


def export_face_meshes_from_head(
    out_dir: Path,
    *,
    head_id: int = 0,
) -> Dict[str, object]:
    """DEPRECATED for rendering: geometry-only, rest-pose export (no skin weights).

    Superseded by extract_skeleton.export_real_head_rig(), which additionally
    decodes real bone weights + bind pose so shapeValueFace actually deforms
    these parts together. render_from_cards.py no longer calls this. Kept only
    for quick inspection scripts.

    Export all non-hair CmpFace meshes + default mat textures for one headId.
    """
    import UnityPy

    out_dir = Path(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    entry = resolve_fo_head_entry(head_id)
    bundle_rel = entry.get("MainAB") or "chara/00/fo_head_00.unity3d"
    if bundle_rel in ("0", ""):
        bundle_rel = "chara/00/fo_head_00.unity3d"
    bundle = resolve_ab(bundle_rel)
    env = UnityPy.load(str(bundle))
    prefab = f"p_cf_head_{int(head_id):02d}"

    exported: Dict[str, object] = {}
    for want in FACE_DRAW_PARTS:
        info = _export_smr_part(env, want, prefab, out_dir)
        exported[want] = info if info is not None else {"error": f"no SMR under {prefab}"}

    # Shared eye white / normal / occlusion (also on eyebase mats)
    for tex_name, label in (
        ("c_t_eye_white_01", "eye_white"),
        ("c_t_eye_o_01", "eye_occlusion"),
        ("c_t_eye_n", "eye_normal"),
        ("c_t_eyeblack_00", "eye_black"),
    ):
        dest = out_dir / f"{label}_{tex_name}.png"
        if _export_texture_from_env(env, tex_name, dest):
            exported[label] = str(dest)

    meta = {
        "head_id": head_id,
        "prefab": prefab,
        "bundle": str(bundle),
        "list_entry": entry,
        "parts": list(FACE_DRAW_PARTS),
        "note": "Full CmpFace non-hair draw set from matching p_cf_head_XX",
        "meshes": exported,
    }
    (out_dir / "face_meshes_meta.json").write_text(json.dumps(meta, indent=2), encoding="utf-8")
    return meta


def export_eyebase_from_head(out_dir: Path, *, head_id: int = 0) -> Dict[str, object]:
    return export_face_meshes_from_head(out_dir, head_id=head_id)


def _resize_rgb(tex: np.ndarray, hw: Tuple[int, int]) -> np.ndarray:
    from PIL import Image

    h, w = hw
    if tex.shape[0] == h and tex.shape[1] == w:
        return tex
    mode = "RGBA" if tex.shape[2] >= 4 else "RGB"
    pil = Image.fromarray((np.clip(tex[..., : (4 if mode == "RGBA" else 3)], 0, 1) * 255).astype(np.uint8), mode=mode)
    pil = pil.resize((w, h), Image.Resampling.BILINEAR)
    arr = np.asarray(pil, dtype=np.float32) / 255.0
    if arr.ndim == 2:
        arr = arr[..., None]
    return arr


def _sample_uv(tex: np.ndarray, u: np.ndarray, v: np.ndarray) -> np.ndarray:
    """Bilinear sample; u/v in 0..1 (OpenGL-style v, already flipped for image row)."""
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


def compose_eye_albedo(
    white_tex: np.ndarray,
    pupil_tex: Optional[np.ndarray],
    pupil_color: Sequence[float],
    *,
    white_color: Sequence[float] = (1, 1, 1, 1),
    black_tex: Optional[np.ndarray] = None,
    black_color: Sequence[float] = (0, 0, 0, 1),
    hl_tex: Optional[np.ndarray] = None,
    hl_color: Sequence[float] = (1, 1, 1, 0.6),
    pupil_w: float = 0.5,
    pupil_h: float = 0.5,
    black_w: float = 0.8,
    black_h: float = 0.8,
    size: int = 512,
) -> np.ndarray:
    """Bake eye shader layers roughly like c_m_eye (ChangeEyesKind path)."""
    # ChaControl.ChangeEyesWH: Lerp(2, 0.5, pupilW/H) → UV scale
    def layout_scale(t: float) -> float:
        return float(2.0 + (0.5 - 2.0) * float(np.clip(t, 0, 1)))

    psx, psy = layout_scale(pupil_w), layout_scale(pupil_h)
    bsx, bsy = layout_scale(black_w), layout_scale(black_h)

    white = _resize_rgb(white_tex, (size, size))
    if white.shape[2] == 3:
        white = np.concatenate([white, np.ones((*white.shape[:2], 1), dtype=np.float32)], axis=2)
    wc = np.array([float(white_color[i]) if i < len(white_color) else 1.0 for i in range(3)], dtype=np.float32)
    out = white[..., :3] * wc

    yy, xx = np.mgrid[0:size, 0:size]
    u = (xx + 0.5) / size
    v = (yy + 0.5) / size  # image row already top→bottom

    def blend_layer(tex: Optional[np.ndarray], color: Sequence[float], sx: float, sy: float, strength: float = 1.0):
        nonlocal out
        if tex is None:
            return
        t = _resize_rgb(tex, (size, size))
        uu = (u - 0.5) * sx + 0.5
        vv = (v - 0.5) * sy + 0.5
        # outside scaled UV → no contribute
        mask = (uu >= 0) & (uu <= 1) & (vv >= 0) & (vv <= 1)
        samp = _sample_uv(t, uu, vv)
        col = np.array([float(color[i]) if i < len(color) else 1.0 for i in range(4)], dtype=np.float32)
        rgb = samp[..., :3] * col[:3]
        if samp.shape[2] >= 4:
            a = samp[..., 3] * col[3] * strength
        else:
            a = samp[..., :3].max(axis=2) * col[3] * strength
        a = np.where(mask, a, 0.0)[..., None]
        out = out * (1.0 - a) + rgb * a

    blend_layer(pupil_tex, pupil_color, psx, psy, 1.0)
    blend_layer(black_tex, black_color, bsx, bsy, 1.0)
    if hl_tex is not None:
        blend_layer(hl_tex, hl_color, 1.0, 1.0, float(hl_color[3] if len(hl_color) > 3 else 0.6))

    alpha = np.ones((*out.shape[:2], 1), dtype=np.float32)
    return np.concatenate([np.clip(out, 0, 1).astype(np.float32), alpha], axis=2)


def tinted_alpha_albedo(
    tex: np.ndarray,
    color: Sequence[float],
    *,
    alpha_mul: float = 1.0,
) -> np.ndarray:
    """Eyelash / namida style: MainTex RGB*color, keep alpha."""
    t = tex.copy()
    if t.shape[2] == 3:
        t = np.concatenate([t, np.ones((*t.shape[:2], 1), dtype=np.float32)], axis=2)
    col = np.array([float(color[i]) if i < len(color) else 1.0 for i in range(4)], dtype=np.float32)
    out = t.copy()
    out[..., :3] = t[..., :3] * col[:3]
    # many eyelash maps are near-black RGB with alpha; use alpha as silhouette
    if float(t[..., :3].mean()) < 0.08 and float(t[..., 3].max()) > 0.05:
        out[..., :3] = col[:3]
    out[..., 3] = np.clip(t[..., 3] * col[3] * alpha_mul, 0, 1)
    return out
