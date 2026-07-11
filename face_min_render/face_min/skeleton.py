# -*- coding: utf-8 -*-
"""Skeleton + linear blend skinning."""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Dict, List, Optional

import numpy as np


def _mat_trs(pos: np.ndarray, rot_deg: np.ndarray, scl: np.ndarray) -> np.ndarray:
    """Local TRS matrix. rot_deg is XYZ Euler degrees (approx Unity order for small angles)."""
    rx, ry, rz = np.deg2rad(rot_deg)
    cx, sx = np.cos(rx), np.sin(rx)
    cy, sy = np.cos(ry), np.sin(ry)
    cz, sz = np.cos(rz), np.sin(rz)
    # Rz * Ry * Rx
    R = np.array(
        [
            [cy * cz, cz * sx * sy - cx * sz, sx * sz + cx * cz * sy],
            [cy * sz, cx * cz + sx * sy * sz, cx * sy * sz - cz * sx],
            [-sy, cy * sx, cx * cy],
        ],
        dtype=np.float64,
    )
    M = np.eye(4, dtype=np.float64)
    M[:3, :3] = R @ np.diag(scl)
    M[:3, 3] = pos
    return M


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
