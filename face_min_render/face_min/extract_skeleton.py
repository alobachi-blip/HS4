# -*- coding: utf-8 -*-
"""Extract the REAL cf_J_* face skeleton + o_head bind pose + vertex skin weights
from a fo_head bundle, for the matching p_cf_head_XX prefab.

No synthetic bones, no distance-weight guessing — this reads exactly what HS2
ships: Transform hierarchy (rest pos/quat/scale), SkinnedMeshRenderer.m_Bones
order, Mesh.m_BindPose, and the BlendWeight/BlendIndices channels packed into
Mesh.m_VertexData.
"""
from __future__ import annotations

import json
from pathlib import Path
from typing import Dict, List, Optional, Tuple

import numpy as np

from .extract_eyes import (
    DEFAULT_SKIP_PARTS,
    FACE_DRAW_PARTS,
    export_texture_from_env,
    mat_tex_map,
    _parent_prefab_name,
    resolve_fo_head_entry,
)
from .hs2_abdata import resolve_ab
from .obj_io import load_obj_from_text

# Unity VertexFormat enum (2019+): byte size per component.
_VERTEX_FORMAT_SIZE = {
    0: 4,  # Float32
    1: 2,  # Float16
    2: 1,  # UNorm8
    3: 1,  # SNorm8
    4: 2,  # UNorm16
    5: 2,  # SNorm16
    6: 1,  # UInt8
    7: 1,  # SInt8
    8: 2,  # UInt16
    9: 2,  # SInt16
    10: 4,  # UInt32
    11: 4,  # SInt32
}


def _find_root_transform(env, prefab: str, root_name: str = "cf_J_FaceRoot"):
    for obj in env.objects:
        if obj.type.name != "Transform":
            continue
        t = obj.read()
        try:
            go = t.m_GameObject.read()
            if go.m_Name != root_name:
                continue
        except Exception:
            continue
        if _parent_prefab_name(env, go) != prefab:
            continue
        return t
    return None


def _walk_transform_tree(root) -> Tuple[Dict[str, Optional[str]], Dict[str, np.ndarray]]:
    """Return (parent_map, rest_local_matrices) for root + all descendants."""
    from .skeleton import mat_trs_quat

    parents: Dict[str, Optional[str]] = {}
    rest_local: Dict[str, np.ndarray] = {}

    def walk(tr, parent_name: Optional[str]):
        try:
            go = tr.m_GameObject.read()
            name = go.m_Name
        except Exception:
            return
        if name in parents:
            return  # already visited (defensive against cycles/dupes)
        parents[name] = parent_name
        lp = tr.m_LocalPosition
        lr = tr.m_LocalRotation
        ls = tr.m_LocalScale
        pos = np.array([lp.x, lp.y, lp.z], dtype=np.float64)
        quat = np.array([lr.x, lr.y, lr.z, lr.w], dtype=np.float64)
        scl = np.array([ls.x, ls.y, ls.z], dtype=np.float64)
        rest_local[name] = mat_trs_quat(pos, quat, scl)
        for c in getattr(tr, "m_Children", None) or []:
            try:
                walk(c.read(), name)
            except Exception:
                continue

    walk(root, None)
    return parents, rest_local


def _decode_vertex_channel(
    vertex_data,
    channel_idx: int,
    vertex_count: int,
) -> Optional[np.ndarray]:
    """Decode one m_VertexData channel into a (vertex_count, dim) float/int array.

    Streams are packed as contiguous blocks (all vertices of stream0, then
    stream1, ...), each block padded up to a 16-byte boundary — this is how
    Unity's VertexData buffer is laid out (verified against o_head: computed
    offsets landed exactly on m_DataSize length).
    """
    channels = vertex_data.m_Channels
    ch = channels[channel_idx]
    if ch.dimension == 0:
        return None

    # stride per stream = sum of component byte sizes of channels using it
    n_streams = max(c.stream for c in channels) + 1
    stream_stride = [0] * n_streams
    for c in channels:
        if c.dimension == 0:
            continue
        size = _VERTEX_FORMAT_SIZE.get(c.format, 4) * c.dimension
        end = c.offset + size
        stream_stride[c.stream] = max(stream_stride[c.stream], end)

    stream_offset = [0] * n_streams
    acc = 0
    for s in range(n_streams):
        stream_offset[s] = acc
        block = stream_stride[s] * vertex_count
        block = ((block + 15) // 16) * 16
        acc += block

    raw = vertex_data.m_DataSize
    if isinstance(raw, str):
        raw = raw.encode("latin-1")
    base = stream_offset[ch.stream]
    stride = stream_stride[ch.stream]
    fmt_size = _VERTEX_FORMAT_SIZE.get(ch.format, 4)
    dtype = {
        0: np.float32,
        11: np.int32,
        10: np.uint32,
        6: np.uint8,
        7: np.int8,
        8: np.uint16,
        9: np.int16,
    }.get(ch.format, np.float32)

    out = np.zeros((vertex_count, ch.dimension), dtype=dtype)
    for v in range(vertex_count):
        start = base + v * stride + ch.offset
        chunk = raw[start : start + fmt_size * ch.dimension]
        out[v] = np.frombuffer(chunk, dtype=dtype, count=ch.dimension)
    return out


def decode_blend_weights(mesh) -> Tuple[np.ndarray, np.ndarray]:
    """Return (bone_indices[N,4] int32, bone_weights[N,4] float64) for a Mesh.

    Channel 12 = BlendWeight (4x float32), channel 13 = BlendIndices (4x SInt32)
    — this is the fixed Unity VertexChannel order for 2018.2+ (verified against
    o_head: 14 channels total, matches kShaderChannel enum layout exactly).
    """
    vd = mesh.m_VertexData
    n = vd.m_VertexCount
    weights = _decode_vertex_channel(vd, 12, n)
    indices = _decode_vertex_channel(vd, 13, n)
    if weights is None or indices is None:
        raise RuntimeError(f"{getattr(mesh, 'm_Name', '?')}: no BlendWeight/BlendIndices channel")
    return indices.astype(np.int32), weights.astype(np.float64)


def _mesh_verts_faces_uvs(mesh) -> Tuple[np.ndarray, np.ndarray, np.ndarray]:
    obj_text = mesh.export()
    return load_obj_from_text(obj_text)


def _bind_pose_dict(bone_names: List[str], bind_pose_raw) -> Dict[str, np.ndarray]:
    out: Dict[str, np.ndarray] = {}
    for name, bp in zip(bone_names, bind_pose_raw):
        out[name] = np.array(
            [
                [bp.e00, bp.e01, bp.e02, bp.e03],
                [bp.e10, bp.e11, bp.e12, bp.e13],
                [bp.e20, bp.e21, bp.e22, bp.e23],
                [bp.e30, bp.e31, bp.e32, bp.e33],
            ],
            dtype=np.float64,
        )
    return out


def _mat_color_map(mat) -> Dict[str, list]:
    out: Dict[str, list] = {}
    props = getattr(mat, "m_SavedProperties", None)
    if not props:
        return out
    for c in props.m_Colors or []:
        try:
            pname, col = c[0], c[1]
            out[str(pname)] = [float(col.r), float(col.g), float(col.b), float(col.a)]
        except Exception:
            continue
    return out


def _find_smr(env, go_name: str, prefab: str):
    for obj in env.objects:
        if obj.type.name != "SkinnedMeshRenderer":
            continue
        d = obj.read()
        try:
            go = d.m_GameObject.read()
            if go.m_Name != go_name:
                continue
        except Exception:
            continue
        if _parent_prefab_name(env, go) != prefab:
            continue
        return d
    return None


# Non-hair CmpFace parts with real skin weights (same source list as FACE_DRAW_PARTS,
# minus DEFAULT_SKIP_PARTS e.g. o_namida). Single source of truth — do not fork.
REAL_RIG_PARTS: Tuple[str, ...] = tuple(p for p in FACE_DRAW_PARTS if p not in DEFAULT_SKIP_PARTS)


def export_real_head_rig(out_dir: Path, *, head_id: int = 0) -> Dict[str, object]:
    """Export the real cf_J_* skeleton + per-part skin weights for every non-hair
    CmpFace mesh (o_head, eyebase L/R, eyelashes, eyeshadow, tooth, tang), all
    sharing ONE skeleton pose so shapeValueFace deforms them together — no
    per-mesh retargeting, no synthetic bones.
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

    root_tr = _find_root_transform(env, prefab)
    if root_tr is None:
        raise RuntimeError(f"cf_J_FaceRoot not found under {prefab} in {bundle}")
    parents, rest_local = _walk_transform_tree(root_tr)

    parts: Dict[str, object] = {}
    for part_name in REAL_RIG_PARTS:
        smr = _find_smr(env, part_name, prefab)
        if smr is None:
            parts[part_name] = {"error": f"no SMR under {prefab}"}
            continue
        bone_names: List[str] = []
        for bp in smr.m_Bones:
            bt = bp.read()
            bone_names.append(bt.m_GameObject.read().m_Name)

        mesh = smr.m_Mesh.read()
        verts, faces, uvs = _mesh_verts_faces_uvs(mesh)
        bone_indices, bone_weights = decode_blend_weights(mesh)
        bind_world_inv = _bind_pose_dict(bone_names, mesh.m_BindPose)

        mat_name = None
        tex_map: Dict[str, str] = {}
        tex_paths: Dict[str, str] = {}
        color_map: Dict[str, list] = {}
        mats = getattr(smr, "m_Materials", None) or []
        if mats:
            try:
                mat = mats[0].read()
                mat_name = mat.m_Name
                tex_map = mat_tex_map(mat)
                color_map = _mat_color_map(mat)
            except Exception:
                pass
        for prop, tex_name in tex_map.items():
            safe = prop.replace(" ", "_").lstrip("_")
            dest = out_dir / f"{part_name}_{safe}_{tex_name}.png"
            if export_texture_from_env(env, tex_name, dest):
                tex_paths[prop] = str(dest)

        npz_path = out_dir / f"{part_name}_real.npz"
        np.savez_compressed(
            npz_path,
            verts=verts,
            faces=faces,
            uvs=uvs,
            bone_indices=bone_indices,
            bone_weights=bone_weights,
            bone_names=np.array(bone_names),
        )
        parts[part_name] = {
            "npz": str(npz_path),
            "n_verts": int(verts.shape[0]),
            "n_bones_skin": len(bone_names),
            "bind_world_inv": {k: v.tolist() for k, v in bind_world_inv.items()},
            "weight_sum_check": float(bone_weights.sum(axis=1).mean()),
            "material": mat_name,
            "tex_map": tex_map,
            "tex_paths": tex_paths,
            "color_map": color_map,
            "main_tex": tex_paths.get("_MainTex"),
            "main_color": color_map.get("_Color") or [1.0, 1.0, 1.0, 1.0],
        }

    # Shared eye textures used by compose_eye_albedo (also live on eyebase mats).
    shared_tex: Dict[str, str] = {}
    for tex_name, label in (
        ("c_t_eye_white_01", "eye_white"),
        ("c_t_eye_o_01", "eye_occlusion"),
        ("c_t_eye_n", "eye_normal"),
        ("c_t_eyeblack_00", "eye_black"),
    ):
        dest = out_dir / f"{label}_{tex_name}.png"
        if export_texture_from_env(env, tex_name, dest):
            shared_tex[label] = str(dest)

    meta = {
        "head_id": head_id,
        "prefab": prefab,
        "bundle": str(bundle),
        "n_joints_total": len(parents),
        "parents": parents,
        "rest_local": {k: v.tolist() for k, v in rest_local.items()},
        "parts": parts,
        "shared_tex": shared_tex,
    }
    (out_dir / "real_head_rig_meta.json").write_text(json.dumps(meta, indent=2), encoding="utf-8")
    return meta


def load_real_category_table() -> Dict[int, List[dict]]:
    """cf_customhead (real, 59 categories) from abdata/list/customshape.unity3d."""
    import UnityPy

    from .category_table import parse_cf_customhead_text
    from .hs2_abdata import abdata

    env = UnityPy.load(str(abdata() / "list" / "customshape.unity3d"))
    for obj in env.objects:
        if obj.type.name != "TextAsset":
            continue
        d = obj.read()
        if d.m_Name == "cf_customhead":
            return parse_cf_customhead_text(d.m_Script)
    raise RuntimeError("cf_customhead TextAsset not found in list/customshape.unity3d")


def load_real_shape_anime(head_id: int):
    """cf_anmShapeHead_{id:02d} (real ShapeAnime binary) from the fo_head bundle."""
    import UnityPy

    from .animation_key_info import AnimationKeyInfo

    entry = resolve_fo_head_entry(head_id)
    bundle_rel = entry.get("MainAB") or "chara/00/fo_head_00.unity3d"
    env = UnityPy.load(str(resolve_ab(bundle_rel)))
    asset_name = f"cf_anmShapeHead_{int(head_id):02d}"
    for obj in env.objects:
        if obj.type.name != "TextAsset":
            continue
        d = obj.read()
        if d.m_Name == asset_name:
            raw = d.m_Script
            raw_bytes = raw.encode("utf-8", "surrogateescape") if isinstance(raw, str) else raw
            return AnimationKeyInfo.from_bytes(raw_bytes)
    raise RuntimeError(f"{asset_name} TextAsset not found in {bundle_rel}")
