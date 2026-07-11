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


def mat_tex_map(mat) -> Dict[str, str]:
    """Material.m_SavedProperties.m_TexEnvs → {prop_name: Texture2D.m_Name}."""
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


def export_texture_from_env(env, asset_name: str, dest: Path) -> bool:
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
                tex_map = mat_tex_map(mat)
            except Exception:
                pass

        tex_paths: Dict[str, str] = {}
        for prop, tex_name in tex_map.items():
            safe = prop.replace(" ", "_").lstrip("_")
            dest = out_dir / f"{go_name}_{safe}_{tex_name}.png"
            if export_texture_from_env(env, tex_name, dest):
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
        if export_texture_from_env(env, tex_name, dest):
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


def _is_dxt1_style_opaque_alpha(tex: np.ndarray) -> bool:
    """True when UnityPy gave us a DXT1-like decode: A≈1 everywhere.

    HS2 ships st_eyelash / many st_eye / st_eye_hl AddTex as TextureFormat DXT1
    (enum 10). Those have no alpha channel; Illusion packs coverage into R.
    Makeup sheets (lip/cheek/eyeshadow) and st_eyeblack are DXT5 with real A.

    IMPORTANT: only call this on the *source* texture, never on a UV-zoomed
    sample — zooming into an opaque region of a DXT5 sheet also yields A≈1 and
    would false-trigger (that bug painted eyeblack coverage as 0).
    """
    if tex.ndim != 3 or tex.shape[2] < 4:
        return False
    return float(tex[..., 3].min()) > 0.95


def normalize_hs2_addtex(tex: np.ndarray) -> np.ndarray:
    """Bake DXT1 R-coverage into A once (ChaControl AddTex used as shader alpha).

    After this, callers always read coverage from A — safe under UV layout zoom.
    Verified on c_t_eyelash_* (strands in R), c_t_eye_* (iris in R above floor),
    c_t_eyehigh_* (sparkles in R). DXT5 sheets (A already meaningful) pass through.
    """
    t = np.asarray(tex, dtype=np.float32)
    if t.ndim != 3:
        return t
    if t.shape[2] == 3:
        t = np.concatenate([t, np.ones((*t.shape[:2], 1), dtype=np.float32)], axis=2)
    if not _is_dxt1_style_opaque_alpha(t):
        return t
    out = t.copy()
    r = t[..., 0]
    floor = float(np.percentile(r, 10))
    span = max(1e-6, float(np.percentile(r, 99)) - floor)
    out[..., 3] = np.clip((r - floor) / span, 0.0, 1.0)
    return out


def _coverage_from_hs2_tex(tex: np.ndarray) -> np.ndarray:
    """Coverage/alpha channel. Prefer A (after normalize_hs2_addtex)."""
    if tex.shape[2] >= 4:
        return np.asarray(tex[..., 3], dtype=np.float32)
    return np.asarray(tex[..., 0], dtype=np.float32)


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
    """Bake c_m_eye layers the way ChaControl wires them:

    ChangeWhiteEyesColor → _Color on MainTex (eye white)
    ChangeEyesKind       → _Texture2 pupil AddTex + ChangeEyesColor (_Color2)
    ChangeEyesWH         → _texture2uv scale Lerp(2, 0.5, pupilW/H)
    ChangeBlackEyes*     → _Texture3 / _Color3 / _texture3uv Lerp(4, 0.4, blackW/H)
    ChangeEyesHighlight* → _Texture4 / _Color4
    """

    def pupil_layout_scale(t: float) -> float:
        # ChaControl.ChangeEyesWH
        return float(2.0 + (0.5 - 2.0) * float(np.clip(t, 0, 1)))

    def black_layout_scale(t: float) -> float:
        # ChaControl.ChangeBlackEyesWH writes _texture3uv.xy = Lerp(4, 0.4, blackW/H).
        # Stock c_m_eye also keeps _texture3uv.z = 4 (never touched by ChaControl).
        # Default before ChaControl is xy=(1,1), z=4 → effective zoom 4. After
        # ChangeBlackEyesWH the shader still uses that z with the new xy (xy*z):
        # blackW=0 → 4*4=16 (small pupil), blackW=0.83 → ~1*4=4 (iris remains).
        # Using xy alone at blackW>~0.7 soft-circle-over-blends the whole iris away.
        return float(4.0 + (0.4 - 4.0) * float(np.clip(t, 0, 1))) * 4.0

    psx, psy = pupil_layout_scale(pupil_w), pupil_layout_scale(pupil_h)
    bsx, bsy = black_layout_scale(black_w), black_layout_scale(black_h)

    white = _resize_rgb(white_tex, (size, size))
    if white.shape[2] == 3:
        white = np.concatenate([white, np.ones((*white.shape[:2], 1), dtype=np.float32)], axis=2)
    wc = np.array([float(white_color[i]) if i < len(white_color) else 1.0 for i in range(3)], dtype=np.float32)
    out = white[..., :3] * wc

    # Normalize DXT1 AddTex → A once, before any UV zoom (see normalize_hs2_addtex).
    if pupil_tex is not None:
        pupil_tex = normalize_hs2_addtex(pupil_tex)
    if black_tex is not None:
        black_tex = normalize_hs2_addtex(black_tex)
    if hl_tex is not None:
        hl_tex = normalize_hs2_addtex(hl_tex)

    yy, xx = np.mgrid[0:size, 0:size]
    u = (xx + 0.5) / size
    v = (yy + 0.5) / size  # image row already top→bottom

    def blend_layer(tex: Optional[np.ndarray], color: Sequence[float], sx: float, sy: float, strength: float = 1.0):
        nonlocal out
        if tex is None:
            return
        t = _resize_rgb(tex, (size, size))
        if t.shape[2] == 3:
            t = np.concatenate([t, np.ones((*t.shape[:2], 1), dtype=np.float32)], axis=2)
        uu = (u - 0.5) * sx + 0.5
        vv = (v - 0.5) * sy + 0.5
        # outside scaled UV → no contribute (shader clips via layout scale)
        mask = (uu >= 0) & (uu <= 1) & (vv >= 0) & (vv <= 1)
        samp = _sample_uv(t, uu, vv)
        col = np.array([float(color[i]) if i < len(color) else 1.0 for i in range(4)], dtype=np.float32)
        cov = _coverage_from_hs2_tex(samp)
        # c_m_eyelashes _Cutoff 0.1 — drop residual noise
        cov = np.where(cov > 0.1, cov, 0.0)
        a = cov * col[3] * strength
        rgb = samp[..., :3] * col[:3]
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
    """c_m_eyelashes / similar: ChangeEyelashesColor sets _Color, Kind sets _MainTex.

    Shader is Fade (_Mode 2, SrcAlpha/OneMinusSrcAlpha). With DXT5 MainTex, A is
    the strand mask. With DXT1 (HS2 stock st_eyelash), coverage lives in R — using
    RGB as color produced the opaque green almond that covered the eyeballs.
    """
    t = normalize_hs2_addtex(tex)
    col = np.array([float(color[i]) if i < len(color) else 1.0 for i in range(4)], dtype=np.float32)
    out = np.zeros_like(t, dtype=np.float32)
    coverage = _coverage_from_hs2_tex(t)
    # Match material _Cutoff 0.1 on c_m_eyelashes
    coverage = np.where(coverage > 0.1, coverage, 0.0)
    out[..., :3] = col[:3]
    out[..., 3] = np.clip(coverage * col[3] * alpha_mul, 0, 1)
    return out
