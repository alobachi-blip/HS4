# -*- coding: utf-8 -*-
"""Smooth textured orthographic face render using HS2 MainTex (no wireframe)."""
from __future__ import annotations

from pathlib import Path
from typing import Literal, Optional, Tuple

import numpy as np


def _face_normals(verts: np.ndarray, faces: np.ndarray) -> np.ndarray:
    v0 = verts[faces[:, 0]]
    v1 = verts[faces[:, 1]]
    v2 = verts[faces[:, 2]]
    fn = np.cross(v1 - v0, v2 - v0)
    lens = np.linalg.norm(fn, axis=1, keepdims=True)
    return fn / np.maximum(lens, 1e-12)


def _vertex_normals(verts: np.ndarray, faces: np.ndarray) -> np.ndarray:
    n = np.zeros_like(verts)
    fn = _face_normals(verts, faces)
    for i in range(3):
        np.add.at(n, faces[:, i], fn)
    lens = np.linalg.norm(n, axis=1, keepdims=True)
    return n / np.maximum(lens, 1e-12)


def _sample_tex(tex: np.ndarray, uv: np.ndarray) -> np.ndarray:
    h, w = tex.shape[:2]
    u = np.clip(uv[..., 0], 0, 1) * (w - 1)
    v = np.clip(1.0 - uv[..., 1], 0, 1) * (h - 1)
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


def render_textured(
    verts: np.ndarray,
    faces: np.ndarray,
    uvs: np.ndarray,
    *,
    albedo: np.ndarray,
    occlusion: Optional[np.ndarray] = None,
    view: Literal["front", "side"] = "front",
    size: int = 512,
    skin_tint: Tuple[float, float, float] = (1.0, 1.0, 1.0),
    bg: Tuple[float, float, float] = (0.55, 0.58, 0.62),
) -> np.ndarray:
    """Fast path: sample HS2 albedo at vertices, Gouraud-ish face colors, no edges."""
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.collections import PolyCollection

    if view == "front":
        xy = verts[:, [0, 1]].copy()
        view_dir = np.array([0.0, 0.0, -1.0])
        light = np.array([0.25, 0.45, -0.85], dtype=np.float64)
    else:
        xy = np.column_stack([verts[:, 2], verts[:, 1]])
        view_dir = np.array([-1.0, 0.0, 0.0])
        light = np.array([-0.75, 0.4, 0.3], dtype=np.float64)

    light /= np.linalg.norm(light)
    vn = _vertex_normals(verts, faces)
    fn = _face_normals(verts, faces)
    visible = (fn @ view_dir) < -0.01
    faces_v = faces[visible]
    if faces_v.size == 0:
        faces_v = faces

    # painter sort
    centroids = (verts[faces_v[:, 0]] + verts[faces_v[:, 1]] + verts[faces_v[:, 2]]) / 3.0
    if view == "front":
        order = np.argsort(centroids[:, 2])
    else:
        order = np.argsort(-centroids[:, 0])
    faces_v = faces_v[order]

    tint = np.asarray(skin_tint, dtype=np.float64)
    alb_v = _sample_tex(albedo, uvs)[:, :3] * tint
    if occlusion is not None:
        ao = _sample_tex(occlusion, uvs)[:, :3].mean(axis=1, keepdims=True)
        alb_v = alb_v * (0.55 + 0.45 * ao)

    ndl = np.clip((-vn) @ light, 0.0, 1.0)
    shade_v = 0.28 + 0.72 * ndl
    col_v = np.clip(alb_v * shade_v[:, None], 0, 1)

    # Keep only visible faces for triangulation
    from matplotlib.tri import Triangulation

    tri = Triangulation(xy[:, 0], xy[:, 1], faces_v)
    # scalar for cmap workaround: encode RGB via three passes is heavy;
    # use face-average with very soft edges by supersampling via high dpi + no edges
    face_cols = (col_v[faces_v[:, 0]] + col_v[faces_v[:, 1]] + col_v[faces_v[:, 2]]) / 3.0

    fig = plt.figure(figsize=(size / 100, size / 100), dpi=100)
    ax = fig.add_axes([0, 0, 1, 1])
    ax.set_aspect("equal")
    ax.axis("off")
    ax.set_facecolor(bg)
    # Gouraud via tripcolor on luminance then... better: PolyCollection antialiased
    coll = PolyCollection(
        xy[faces_v],
        facecolors=face_cols,
        edgecolors=face_cols,  # match fill → hide seams
        linewidths=0.35,
        antialiased=True,
        closed=True,
    )
    ax.add_collection(coll)
    pad = 0.06 * max(float(np.ptp(xy[:, 0])), float(np.ptp(xy[:, 1])), 1e-6)
    ax.set_xlim(xy[:, 0].min() - pad, xy[:, 0].max() + pad)
    ax.set_ylim(xy[:, 1].min() - pad, xy[:, 1].max() + pad)

    import io
    from PIL import Image

    buf = io.BytesIO()
    fig.savefig(buf, format="png", dpi=100, facecolor=bg)
    plt.close(fig)
    buf.seek(0)
    arr = np.asarray(Image.open(buf).convert("RGB"), dtype=np.float64) / 255.0
    return arr


def save_image(img: np.ndarray, path: str | Path) -> None:
    from PIL import Image

    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8), mode="RGB").save(path)
