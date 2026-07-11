# -*- coding: utf-8 -*-
"""Build a demo asset pack from o_head.obj (synthetic bones + weights + curves).

This is NOT 1:1 HS2 ShapeAnime — it preserves the same *pipeline* and Src/Dst
naming so real exports can replace demo_pack later.
"""
from __future__ import annotations

import json
from pathlib import Path
from typing import Dict, List, Tuple

import numpy as np

from .animation_key_info import AnimationKeyInfo, KeySample
from .category_table import write_category_json
from .obj_io import load_obj
from .skeleton import Skeleton, Bone, _mat_trs


# Minimal category → SrcName (subset of real cf_customhead semantics)
DEMO_CATEGORY: Dict[int, List[dict]] = {
    0: [{"name": "cf_s_FaceBase_sx", "use": {"pos": [False, False, False], "rot": [False, False, False], "scl": [True, False, False]}}],
    1: [{"name": "cf_s_FaceUp_tz", "use": {"pos": [False, False, True], "rot": [False, False, False], "scl": [False, False, False]}}],
    2: [{"name": "cf_s_FaceUp_ty", "use": {"pos": [False, True, False], "rot": [False, False, False], "scl": [False, False, False]}}],
    3: [{"name": "cf_s_FaceLow_tz", "use": {"pos": [False, False, True], "rot": [False, False, False], "scl": [False, False, False]}}],
    4: [{"name": "cf_s_FaceLow_sx", "use": {"pos": [False, False, False], "rot": [False, False, False], "scl": [True, False, False]}}],
    5: [{"name": "cf_s_Chin_sx", "use": {"pos": [False, False, False], "rot": [False, False, False], "scl": [True, False, False]}}],
    6: [{"name": "cf_s_Chin_ty", "use": {"pos": [False, True, False], "rot": [False, False, False], "scl": [False, False, False]}}],
    7: [{"name": "cf_s_Chin_tz", "use": {"pos": [False, False, True], "rot": [False, False, False], "scl": [False, False, False]}}],
    8: [{"name": "cf_s_Chin_rx", "use": {"pos": [False, False, False], "rot": [True, False, False], "scl": [False, False, False]}}],
    9: [{"name": "cf_s_ChinLow", "use": {"pos": [False, True, False], "rot": [False, False, False], "scl": [False, False, False]}}],
    10: [{"name": "cf_s_ChinTip_sx", "use": {"pos": [False, False, False], "rot": [False, False, False], "scl": [True, True, True]}}],
    11: [{"name": "cf_s_ChinTip_ty", "use": {"pos": [False, True, False], "rot": [False, False, False], "scl": [False, False, False]}}],
    12: [{"name": "cf_s_ChinTip_tz", "use": {"pos": [False, False, True], "rot": [False, False, False], "scl": [False, False, False]}}],
    13: [{"name": "cf_s_CheekLow_ty", "use": {"pos": [False, True, False], "rot": [False, False, False], "scl": [False, False, False]}}],
    14: [{"name": "cf_s_CheekLow_tz", "use": {"pos": [False, False, True], "rot": [False, False, False], "scl": [False, False, False]}}],
    15: [
        {"name": "cf_s_CheekLow_tx_L", "use": {"pos": [True, False, False], "rot": [False, False, False], "scl": [False, False, False]}},
        {"name": "cf_s_CheekLow_tx_R", "use": {"pos": [True, False, False], "rot": [False, False, False], "scl": [False, False, False]}},
    ],
    16: [{"name": "cf_s_CheekUp_ty", "use": {"pos": [False, True, False], "rot": [False, False, False], "scl": [False, False, False]}}],
    17: [
        {"name": "cf_s_CheekUp_tz_00", "use": {"pos": [False, False, True], "rot": [False, False, False], "scl": [False, False, False]}},
        {"name": "cf_s_CheekUp_tz_01", "use": {"pos": [False, False, True], "rot": [False, False, False], "scl": [False, False, False]}},
    ],
    18: [
        {"name": "cf_s_CheekUp_tx_L", "use": {"pos": [True, False, False], "rot": [False, False, False], "scl": [False, False, False]}},
        {"name": "cf_s_CheekUp_tx_R", "use": {"pos": [True, False, False], "rot": [False, False, False], "scl": [False, False, False]}},
    ],
    32: [{"name": "cf_s_NoseBridge_ty", "use": {"pos": [False, True, False], "rot": [False, False, False], "scl": [False, False, False]}}],
    33: [
        {"name": "cf_s_NoseBridge_tz_00", "use": {"pos": [False, False, True], "rot": [False, False, False], "scl": [False, False, False]}},
        {"name": "cf_s_NoseBase_tz", "use": {"pos": [False, False, True], "rot": [False, False, False], "scl": [False, False, False]}},
    ],
    36: [{"name": "cf_s_NoseBridge_ty", "use": {"pos": [False, True, False], "rot": [False, False, False], "scl": [False, False, False]}}],
    39: [
        {"name": "cf_s_NoseWing_tx_L", "use": {"pos": [True, False, False], "rot": [False, False, False], "scl": [False, False, False]}},
        {"name": "cf_s_NoseWing_tx_R", "use": {"pos": [True, False, False], "rot": [False, False, False], "scl": [False, False, False]}},
    ],
    44: [{"name": "cf_s_Nose_tip", "use": {"pos": [False, True, True], "rot": [False, False, False], "scl": [True, True, True]}}],
    47: [{"name": "cf_s_MouthBase_ty", "use": {"pos": [False, True, False], "rot": [False, False, False], "scl": [False, False, False]}}],
    48: [{"name": "cf_s_MouthBase_sx", "use": {"pos": [False, False, False], "rot": [False, False, False], "scl": [True, False, False]}}],
    49: [{"name": "cf_s_MouthBase_sy", "use": {"pos": [False, False, False], "rot": [False, False, False], "scl": [False, True, False]}}],
    50: [{"name": "cf_s_MouthBase_tz", "use": {"pos": [False, False, True], "rot": [False, False, False], "scl": [False, False, False]}}],
}


def _keys_scale_x(amp: float = 0.25) -> List[KeySample]:
    # rate 0 → narrow, 0.5 → rest, 1 → wide
    return [
        KeySample(0, np.zeros(3), np.zeros(3), np.array([1.0 - amp, 1.0, 1.0])),
        KeySample(1, np.zeros(3), np.zeros(3), np.array([1.0, 1.0, 1.0])),
        KeySample(2, np.zeros(3), np.zeros(3), np.array([1.0 + amp, 1.0, 1.0])),
    ]


def _keys_pos(axis: int, amp: float) -> List[KeySample]:
    def v(t):
        p = np.zeros(3)
        p[axis] = t
        return p

    return [
        KeySample(0, v(-amp), np.zeros(3), np.ones(3)),
        KeySample(1, v(0.0), np.zeros(3), np.ones(3)),
        KeySample(2, v(amp), np.zeros(3), np.ones(3)),
    ]


def _keys_rot_x(amp_deg: float) -> List[KeySample]:
    return [
        KeySample(0, np.zeros(3), np.array([-amp_deg, 0, 0]), np.ones(3)),
        KeySample(1, np.zeros(3), np.zeros(3), np.ones(3)),
        KeySample(2, np.zeros(3), np.array([amp_deg, 0, 0]), np.ones(3)),
    ]


def build_demo_curves() -> AnimationKeyInfo:
    curves = {
        "cf_s_FaceBase_sx": _keys_scale_x(0.22),
        "cf_s_FaceLow_sx": _keys_scale_x(0.28),
        "cf_s_FaceLow_tz": _keys_pos(2, 0.004),
        "cf_s_FaceUp_ty": _keys_pos(1, 0.003),
        "cf_s_FaceUp_tz": _keys_pos(2, 0.003),
        "cf_s_Chin_sx": _keys_scale_x(0.35),
        "cf_s_Chin_ty": _keys_pos(1, 0.004),
        "cf_s_Chin_tz": _keys_pos(2, 0.005),
        "cf_s_Chin_rx": _keys_rot_x(8.0),
        "cf_s_ChinLow": _keys_pos(1, 0.003),
        "cf_s_ChinTip_sx": _keys_scale_x(0.3),
        "cf_s_ChinTip_ty": _keys_pos(1, 0.004),
        "cf_s_ChinTip_tz": _keys_pos(2, 0.004),
        "cf_s_CheekLow_tx_L": _keys_pos(0, -0.004),
        "cf_s_CheekLow_tx_R": _keys_pos(0, 0.004),
        "cf_s_CheekLow_ty": _keys_pos(1, 0.002),
        "cf_s_CheekLow_tz": _keys_pos(2, 0.003),
        "cf_s_CheekUp_tx_L": _keys_pos(0, -0.003),
        "cf_s_CheekUp_tx_R": _keys_pos(0, 0.003),
        "cf_s_CheekUp_ty": _keys_pos(1, 0.002),
        "cf_s_CheekUp_tz_00": _keys_pos(2, 0.002),
        "cf_s_CheekUp_tz_01": _keys_pos(2, 0.001),
        "cf_s_NoseBridge_ty": _keys_pos(1, 0.002),
        "cf_s_NoseBridge_tz_00": _keys_pos(2, 0.004),
        "cf_s_NoseBridge_tz_01": _keys_pos(2, 0.002),
        "cf_s_NoseBase_tz": _keys_pos(2, 0.003),
        "cf_s_NoseBase_ty": _keys_pos(1, 0.001),
        "cf_s_NoseBase_rx": _keys_rot_x(6.0),
        "cf_s_Nose_tip": [
            KeySample(0, np.array([0, -0.002, -0.003]), np.zeros(3), np.array([0.85, 0.85, 0.85])),
            KeySample(1, np.zeros(3), np.zeros(3), np.ones(3)),
            KeySample(2, np.array([0, 0.002, 0.005]), np.zeros(3), np.array([1.15, 1.15, 1.15])),
        ],
        "cf_s_NoseWing_tx_L": _keys_pos(0, -0.003),
        "cf_s_NoseWing_tx_R": _keys_pos(0, 0.003),
        "cf_s_NoseWing_ty": _keys_pos(1, 0.001),
        "cf_s_NoseWing_tz": _keys_pos(2, 0.002),
        "cf_s_MouthBase_ty": _keys_pos(1, 0.003),
        "cf_s_MouthBase_tz": _keys_pos(2, 0.002),
        "cf_s_MouthBase_sx": _keys_scale_x(0.25),
        "cf_s_MouthBase_sy": [
            KeySample(0, np.zeros(3), np.zeros(3), np.array([1.0, 0.8, 1.0])),
            KeySample(1, np.zeros(3), np.zeros(3), np.ones(3)),
            KeySample(2, np.zeros(3), np.zeros(3), np.array([1.0, 1.2, 1.0])),
        ],
    }
    return AnimationKeyInfo(curves=curves)


def _bone_rest_positions(verts: np.ndarray) -> Dict[str, np.ndarray]:
    mn, mx = verts.min(axis=0), verts.max(axis=0)
    c = 0.5 * (mn + mx)
    ext = mx - mn
    # Heuristic landmarks on AABB (good enough for visible morph demo)
    return {
        "root": c.copy(),
        "cf_J_FaceBase": c + np.array([0.0, 0.05 * ext[1], 0.0]),
        "cf_J_FaceLow_s": c + np.array([0.0, -0.05 * ext[1], 0.05 * ext[2]]),
        "cf_J_FaceLowBase": c + np.array([0.0, -0.08 * ext[1], 0.02 * ext[2]]),
        "cf_J_FaceUp_ty": c + np.array([0.0, 0.18 * ext[1], 0.0]),
        "cf_J_FaceUp_tz": c + np.array([0.0, 0.12 * ext[1], 0.08 * ext[2]]),
        "cf_J_Chin_rs": c + np.array([0.0, -0.22 * ext[1], 0.05 * ext[2]]),
        "cf_J_ChinLow": c + np.array([0.0, -0.28 * ext[1], 0.02 * ext[2]]),
        "cf_J_ChinTip_s": c + np.array([0.0, -0.32 * ext[1], 0.08 * ext[2]]),
        "cf_J_CheekLow_L": c + np.array([-0.22 * ext[0], -0.05 * ext[1], 0.1 * ext[2]]),
        "cf_J_CheekLow_R": c + np.array([0.22 * ext[0], -0.05 * ext[1], 0.1 * ext[2]]),
        "cf_J_CheekUp_L": c + np.array([-0.2 * ext[0], 0.05 * ext[1], 0.12 * ext[2]]),
        "cf_J_CheekUp_R": c + np.array([0.2 * ext[0], 0.05 * ext[1], 0.12 * ext[2]]),
        "cf_J_NoseBridge_t": c + np.array([0.0, 0.08 * ext[1], 0.2 * ext[2]]),
        "cf_J_NoseBase_trs": c + np.array([0.0, 0.02 * ext[1], 0.22 * ext[2]]),
        "cf_J_Nose_tip": c + np.array([0.0, 0.0, 0.28 * ext[2]]),
        "cf_J_NoseWing_tx_L": c + np.array([-0.06 * ext[0], 0.0, 0.22 * ext[2]]),
        "cf_J_NoseWing_tx_R": c + np.array([0.06 * ext[0], 0.0, 0.22 * ext[2]]),
        "cf_J_MouthBase_tr": c + np.array([0.0, -0.12 * ext[1], 0.18 * ext[2]]),
        "cf_J_MouthBase_s": c + np.array([0.0, -0.12 * ext[1], 0.18 * ext[2]]),
    }


def _parents() -> Dict[str, str | None]:
    return {
        "root": None,
        "cf_J_FaceBase": "root",
        "cf_J_FaceUp_ty": "cf_J_FaceBase",
        "cf_J_FaceUp_tz": "cf_J_FaceBase",
        "cf_J_FaceLow_s": "cf_J_FaceBase",
        "cf_J_FaceLowBase": "cf_J_FaceLow_s",
        "cf_J_Chin_rs": "cf_J_FaceLowBase",
        "cf_J_ChinLow": "cf_J_Chin_rs",
        "cf_J_ChinTip_s": "cf_J_Chin_rs",
        "cf_J_CheekLow_L": "cf_J_FaceLowBase",
        "cf_J_CheekLow_R": "cf_J_FaceLowBase",
        "cf_J_CheekUp_L": "cf_J_FaceBase",
        "cf_J_CheekUp_R": "cf_J_FaceBase",
        "cf_J_NoseBridge_t": "cf_J_FaceBase",
        "cf_J_NoseBase_trs": "cf_J_NoseBridge_t",
        "cf_J_Nose_tip": "cf_J_NoseBase_trs",
        "cf_J_NoseWing_tx_L": "cf_J_NoseBase_trs",
        "cf_J_NoseWing_tx_R": "cf_J_NoseBase_trs",
        "cf_J_MouthBase_tr": "cf_J_FaceLowBase",
        "cf_J_MouthBase_s": "cf_J_MouthBase_tr",
    }


def build_skeleton(verts: np.ndarray) -> Tuple[Skeleton, List[str]]:
    rests = _bone_rest_positions(verts)
    parents = _parents()
    # topological order
    order = []
    remaining = set(rests)
    while remaining:
        progress = False
        for n in list(remaining):
            p = parents[n]
            if p is None or p in order:
                order.append(n)
                remaining.remove(n)
                progress = True
        if not progress:
            raise RuntimeError("bone parent cycle")

    bones: Dict[str, Bone] = {}
    world_rest: Dict[str, np.ndarray] = {}
    for name in order:
        p = parents[name]
        world_pos = rests[name]
        if p is None:
            local_pos = world_pos
            rest_local = _mat_trs(local_pos, np.zeros(3), np.ones(3))
            world_rest[name] = rest_local.copy()
        else:
            parent_world = world_rest[p]
            parent_inv = np.linalg.inv(parent_world)
            local_h = parent_inv @ np.array([*world_pos, 1.0])
            rest_local = _mat_trs(local_h[:3], np.zeros(3), np.ones(3))
            world_rest[name] = parent_world @ rest_local
        bones[name] = Bone(name=name, parent=p, rest_local=rest_local, local=rest_local.copy(), world=world_rest[name].copy())
    return Skeleton(bones=bones, order=order), order


def compute_weights(
    verts: np.ndarray,
    bone_names: List[str],
    rests: Dict[str, np.ndarray],
    *,
    k: int = 4,
    falloff: float = None,
) -> Tuple[np.ndarray, np.ndarray]:
    """Distance-based weights to nearest bones (exclude root from influence)."""
    influence = [n for n in bone_names if n != "root"]
    centers = np.stack([rests[n] for n in influence], axis=0)
    # pairwise distances
    d = np.linalg.norm(verts[:, None, :] - centers[None, :, :], axis=2)
    if falloff is None:
        falloff = 0.35 * float(np.linalg.norm(verts.max(0) - verts.min(0)))
    # softmin weights for all, then keep top-k
    soft = np.exp(-(d / max(falloff, 1e-6)) ** 2)
    soft /= np.maximum(soft.sum(axis=1, keepdims=True), 1e-12)
    top = np.argpartition(-soft, kth=min(k, soft.shape[1] - 1), axis=1)[:, :k]
    weights = np.take_along_axis(soft, top, axis=1)
    weights /= np.maximum(weights.sum(axis=1, keepdims=True), 1e-12)
    name_to_id = {n: i for i, n in enumerate(bone_names)}
    influence_ids = np.array([name_to_id[n] for n in influence], dtype=np.int32)
    indices = influence_ids[top]
    return indices.astype(np.int32), weights.astype(np.float64)


def build_demo_pack(obj_path: str | Path, out_dir: str | Path) -> Path:
    out_dir = Path(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    verts, faces, uvs, _uv_faces = load_obj(obj_path)
    skeleton, order = build_skeleton(verts)
    rests = {n: skeleton.bones[n].world[:3, 3].copy() for n in order}
    # Recompute rests from bone world after rest setup
    skeleton.reset_to_rest()
    skeleton.update_world()
    rests = {n: skeleton.bones[n].world[:3, 3].copy() for n in order}
    bone_names = order
    indices, weights = compute_weights(verts, bone_names, rests)

    bind_inv = {}
    for n in bone_names:
        bind_inv[n] = np.linalg.inv(skeleton.bones[n].world)

    pack = {
        "mode": "demo",
        "note": "Synthetic HS2-like pack from o_head.obj; replace with real exports for fidelity.",
        "bone_names": bone_names,
        "parents": {n: skeleton.bones[n].parent for n in bone_names},
        "rest_local": {n: skeleton.bones[n].rest_local.tolist() for n in bone_names},
        "bind_world_inv": {n: bind_inv[n].tolist() for n in bone_names},
        "geometry": "head_skinned.npz",
    }
    (out_dir / "head_skinned.json").write_text(json.dumps(pack, indent=2), encoding="utf-8")
    np.savez_compressed(
        out_dir / "head_skinned.npz",
        verts=verts,
        faces=faces,
        uvs=uvs,
        bone_indices=indices,
        bone_weights=weights,
        bone_names=np.array(bone_names),
    )
    # also save rest locals / bind inv as npz sidecar via json for matrices
    write_category_json(DEMO_CATEGORY, out_dir / "cf_customhead.json")
    build_demo_curves().save_json(out_dir / "shape_anime.json")
    meta = {
        "obj_source": str(obj_path),
        "n_verts": int(verts.shape[0]),
        "n_faces": int(faces.shape[0]),
        "n_bones": len(bone_names),
        "categories": sorted(DEMO_CATEGORY.keys()),
    }
    (out_dir / "meta.json").write_text(json.dumps(meta, indent=2), encoding="utf-8")
    return out_dir
