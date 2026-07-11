# -*- coding: utf-8 -*-
"""Skeleton + linear blend skinning."""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Dict, List, Optional, Tuple

import numpy as np


def _euler_to_matrix(rot_deg: np.ndarray) -> np.ndarray:
    """Rotation-only 3x3. rot_deg is XYZ Euler degrees (Unity Quaternion.Euler order: Z*X*Y intrinsic ~ Rz*Rx*Ry for the extension SetLocalRotation(x,y,z) used by ShapeHeadInfoFemale)."""
    rx, ry, rz = np.deg2rad(rot_deg)
    cx, sx = np.cos(rx), np.sin(rx)
    cy, sy = np.cos(ry), np.sin(ry)
    cz, sz = np.cos(rz), np.sin(rz)
    R = np.array(
        [
            [cy * cz, cz * sx * sy - cx * sz, sx * sz + cx * cz * sy],
            [cy * sz, cx * cz + sx * sy * sz, cx * sy * sz - cz * sx],
            [-sy, cy * sx, cx * cy],
        ],
        dtype=np.float64,
    )
    return R


def _mat_trs(pos: np.ndarray, rot_deg: np.ndarray, scl: np.ndarray) -> np.ndarray:
    """Local TRS matrix. rot_deg is XYZ Euler degrees (approx Unity order for small angles)."""
    M = np.eye(4, dtype=np.float64)
    M[:3, :3] = _euler_to_matrix(rot_deg) @ np.diag(scl)
    M[:3, 3] = pos
    return M


def quat_to_matrix(quat_xyzw: np.ndarray) -> np.ndarray:
    """Unity quaternion (x,y,z,w) → 3x3 rotation matrix. Use for REAL rig rest poses —
    never round-trip real rest rotations through Euler, that loses/misorders data."""
    x, y, z, w = quat_xyzw
    n = x * x + y * y + z * z + w * w
    s = 2.0 / n if n > 1e-12 else 0.0
    xs, ys, zs = x * s, y * s, z * s
    wx, wy, wz = w * xs, w * ys, w * zs
    xx, xy, xz = x * xs, x * ys, x * zs
    yy, yz, zz = y * ys, y * zs, z * zs
    return np.array(
        [
            [1.0 - (yy + zz), xy - wz, xz + wy],
            [xy + wz, 1.0 - (xx + zz), yz - wx],
            [xz - wy, yz + wx, 1.0 - (xx + yy)],
        ],
        dtype=np.float64,
    )


def mat_trs_quat(pos: np.ndarray, quat_xyzw: np.ndarray, scl: np.ndarray) -> np.ndarray:
    """Build a real rest_local 4x4 from Unity Transform local pos/quaternion/scale."""
    M = np.eye(4, dtype=np.float64)
    M[:3, :3] = quat_to_matrix(quat_xyzw) @ np.diag(scl)
    M[:3, 3] = pos
    return M


def decompose_rest(rest_local: np.ndarray) -> Tuple[np.ndarray, np.ndarray, np.ndarray]:
    """rest_local → (pos, pure_rotation_3x3, scale). Assumes no shear."""
    pos = rest_local[:3, 3].copy()
    cols = rest_local[:3, :3]
    scale = np.linalg.norm(cols, axis=0)
    scale_safe = np.where(scale < 1e-9, 1.0, scale)
    rot = cols / scale_safe[None, :]
    return pos, rot, scale


@dataclass
class Bone:
    name: str
    parent: Optional[str]
    rest_local: np.ndarray  # 4x4
    local: np.ndarray = field(default_factory=lambda: np.eye(4))
    world: np.ndarray = field(default_factory=lambda: np.eye(4))


@dataclass
class Skeleton:
    bones: Dict[str, Bone]
    order: List[str]  # parents before children

    def reset_to_rest(self) -> None:
        for b in self.bones.values():
            b.local = b.rest_local.copy()

    def set_local_prs(self, name: str, pos: np.ndarray, rot_deg: np.ndarray, scl: np.ndarray) -> None:
        if name not in self.bones:
            return
        self.bones[name].local = _mat_trs(pos, rot_deg, scl)

    def set_local_components(
        self,
        name: str,
        *,
        pos: Optional[np.ndarray] = None,
        rot_deg: Optional[np.ndarray] = None,
        scl: Optional[np.ndarray] = None,
        mask_pos=(True, True, True),
        mask_rot=(True, True, True),
        mask_scl=(True, True, True),
    ) -> None:
        """Merge into current local by decomposing rest+delta style for demo.

        Demo bones store rest_local; we rebuild from rest PRS then override masked axes.
        """
        b = self.bones.get(name)
        if b is None:
            return
        # Decompose rest roughly: pos from translation, scl from column norms, rot~0 for demo
        rest_pos = b.rest_local[:3, 3].copy()
        rest_scl = np.array(
            [
                np.linalg.norm(b.rest_local[:3, 0]),
                np.linalg.norm(b.rest_local[:3, 1]),
                np.linalg.norm(b.rest_local[:3, 2]),
            ]
        )
        rest_scl = np.where(rest_scl < 1e-8, 1.0, rest_scl)
        p = rest_pos.copy() if pos is None else rest_pos.copy()
        r = np.zeros(3) if rot_deg is None else np.zeros(3)
        s = rest_scl.copy() if scl is None else rest_scl.copy()
        if pos is not None:
            for i in range(3):
                if mask_pos[i]:
                    p[i] = pos[i]
        if rot_deg is not None:
            for i in range(3):
                if mask_rot[i]:
                    r[i] = rot_deg[i]
        if scl is not None:
            for i in range(3):
                if mask_scl[i]:
                    s[i] = scl[i]
        # For demo Update: often *add* src deltas onto rest translation
        if pos is not None:
            p = rest_pos.copy()
            for i in range(3):
                if mask_pos[i]:
                    p[i] = rest_pos[i] + pos[i]
        if scl is not None:
            s = rest_scl.copy()
            for i in range(3):
                if mask_scl[i]:
                    s[i] = rest_scl[i] * scl[i]
        if rot_deg is not None:
            r = np.zeros(3)
            for i in range(3):
                if mask_rot[i]:
                    r[i] = rot_deg[i]
        b.local = _mat_trs(p, r, s)

    def set_local_absolute(
        self,
        name: str,
        *,
        pos: Optional[np.ndarray] = None,
        rot_deg: Optional[np.ndarray] = None,
        scl: Optional[np.ndarray] = None,
        mask_pos=(False, False, False),
        mask_rot=(False, False, False),
        mask_scl=(False, False, False),
    ) -> None:
        """Faithful port of Transform.SetLocalPositionX/Y/Z / SetLocalRotation / SetLocalScale.

        Masked axes get the LITERAL value passed in (absolute overwrite, matching the
        real HS2 call) — never added/multiplied onto rest. Unmasked axes keep the bone's
        real rest value (from the extracted rig), not zero/identity.
        """
        b = self.bones.get(name)
        if b is None:
            return
        rest_pos, rest_rot, rest_scale = decompose_rest(b.rest_local)

        new_pos = rest_pos.copy()
        if pos is not None:
            for i in range(3):
                if mask_pos[i]:
                    new_pos[i] = pos[i]

        if rot_deg is not None and any(mask_rot):
            R = _euler_to_matrix(np.asarray(rot_deg, dtype=np.float64))
        else:
            R = rest_rot

        new_scale = rest_scale.copy()
        if scl is not None:
            for i in range(3):
                if mask_scl[i]:
                    new_scale[i] = scl[i]

        M = np.eye(4, dtype=np.float64)
        M[:3, :3] = R @ np.diag(new_scale)
        M[:3, 3] = new_pos
        b.local = M

    def update_world(self) -> None:
        for name in self.order:
            b = self.bones[name]
            if b.parent is None:
                b.world = b.local.copy()
            else:
                b.world = self.bones[b.parent].world @ b.local


def skin_mesh(
    rest_verts: np.ndarray,
    bone_indices: np.ndarray,
    bone_weights: np.ndarray,
    bone_names: List[str],
    skeleton: Skeleton,
    bind_world_inv: Dict[str, np.ndarray],
) -> np.ndarray:
    """Linear blend skinning. rest_verts (N,3), indices/weights (N,4)."""
    n = rest_verts.shape[0]
    out = np.zeros((n, 3), dtype=np.float64)
    ones = np.ones((n, 1))
    rest_h = np.hstack([rest_verts, ones])
    for j in range(4):
        bi = bone_indices[:, j]
        w = bone_weights[:, j][:, None]
        # group by bone id for speed
        for bone_id in np.unique(bi):
            if bone_id < 0:
                continue
            mask = bi == bone_id
            name = bone_names[int(bone_id)]
            if name not in skeleton.bones:
                continue
            M = skeleton.bones[name].world @ bind_world_inv[name]
            transformed = (M @ rest_h[mask].T).T[:, :3]
            out[mask] += w[mask] * transformed
    return out
