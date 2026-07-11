# -*- coding: utf-8 -*-
"""OBJ loader with UVs (v / vt / f v/vt/vn)."""
from __future__ import annotations

from pathlib import Path
from typing import Tuple

import numpy as np


def load_obj(path: str | Path) -> Tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
    """Return verts (N,3), faces (F,3) vertex indices, uvs (N,2) per-vertex, uv_faces unused.

    UVs are expanded onto vertices: if a vertex has multiple UVs, first wins
    (HS2 o_head typically 1:1 vt count with v).
    """
    path = Path(path)
    verts: list[list[float]] = []
    uvs_raw: list[list[float]] = []
    faces_v: list[list[int]] = []
    faces_t: list[list[int]] = []

    with path.open("r", encoding="utf-8", errors="ignore") as f:
        for line in f:
            if line.startswith("v "):
                p = line.split()
                verts.append([float(p[1]), float(p[2]), float(p[3])])
            elif line.startswith("vt "):
                p = line.split()
                uvs_raw.append([float(p[1]), float(p[2])])
            elif line.startswith("f "):
                parts = line.split()[1:]
                iv, it = [], []
                for p in parts:
                    sp = p.split("/")
                    iv.append(int(sp[0]) - 1)
                    if len(sp) > 1 and sp[1]:
                        it.append(int(sp[1]) - 1)
                    else:
                        it.append(int(sp[0]) - 1)
                if len(iv) >= 3:
                    for i in range(1, len(iv) - 1):
                        faces_v.append([iv[0], iv[i], iv[i + 1]])
                        faces_t.append([it[0], it[i], it[i + 1]])

    verts_a = np.asarray(verts, dtype=np.float64)
    faces_a = np.asarray(faces_v, dtype=np.int32)
    uvs = np.zeros((len(verts), 2), dtype=np.float64)
    if uvs_raw:
        uvs_a = np.asarray(uvs_raw, dtype=np.float64)
        # assign per corner then average
        acc = np.zeros((len(verts), 2), dtype=np.float64)
        cnt = np.zeros((len(verts), 1), dtype=np.float64)
        for fv, ft in zip(faces_v, faces_t):
            for vi, ti in zip(fv, ft):
                if 0 <= ti < len(uvs_a):
                    acc[vi] += uvs_a[ti]
                    cnt[vi] += 1
        mask = cnt[:, 0] > 0
        uvs[mask] = acc[mask] / cnt[mask]
    return verts_a, faces_a, uvs, np.asarray(faces_t, dtype=np.int32)
