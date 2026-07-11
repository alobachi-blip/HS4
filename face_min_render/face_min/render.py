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
    extra_meshes: Optional[list] = None,
) -> np.ndarray:
    """Fast path: sample HS2 albedo at vertices; optional extra_meshes for eyes.

    extra_meshes: list of dicts with keys verts, faces, uvs, albedo (optional).
    """
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.collections import PolyCollection

    # Merge extras into one draw list
    draw_items = [{"verts": verts, "faces": faces, "uvs": uvs, "albedo": albedo, "occlusion": occlusion}]
    if extra_meshes:
        for em in extra_meshes:
            draw_items.append(em)

    # Combined bounds for camera framing
    all_v = np.vstack([it["verts"] for it in draw_items])
    if view == "front":
        xy_all = all_v[:, [0, 1]]
        view_dir = np.array([0.0, 0.0, -1.0])
        light = np.array([0.25, 0.45, -0.85], dtype=np.float64)
    else:
        xy_all = np.column_stack([all_v[:, 2], all_v[:, 1]])
        view_dir = np.array([-1.0, 0.0, 0.0])
        light = np.array([-0.75, 0.4, 0.3], dtype=np.float64)
    light /= np.linalg.norm(light)

    pad = 0.06 * max(float(np.ptp(xy_all[:, 0])), float(np.ptp(xy_all[:, 1])), 1e-6)
    xlim = (xy_all[:, 0].min() - pad, xy_all[:, 0].max() + pad)
    ylim = (xy_all[:, 1].min() - pad, xy_all[:, 1].max() + pad)

    fig = plt.figure(figsize=(size / 100, size / 100), dpi=100)
    ax = fig.add_axes([0, 0, 1, 1])
    ax.set_aspect("equal")
    ax.axis("off")
    ax.set_facecolor(bg)

    # Depth-sort faces across all meshes
    batches = []
    for it in draw_items:
        v = it["verts"]
        f = it["faces"]
        if f.size == 0:
            continue
        uv = it.get("uvs")
        if uv is None or len(uv) != len(v):
            uv = np.zeros((len(v), 2))
        alb = it.get("albedo", albedo)
        occ = it.get("occlusion", None)
        tint = np.asarray(it.get("skin_tint", skin_tint), dtype=np.float64)

        if view == "front":
            xy = v[:, [0, 1]].copy()
        else:
            xy = np.column_stack([v[:, 2], v[:, 1]])

        vn = _vertex_normals(v, f)
        fn = _face_normals(v, f)
        visible = (fn @ view_dir) < -0.01
        faces_v = f[visible] if visible.any() else f
        centroids = (v[faces_v[:, 0]] + v[faces_v[:, 1]] + v[faces_v[:, 2]]) / 3.0
        depth = centroids[:, 2] if view == "front" else -centroids[:, 0]

        alb_v = _sample_tex(alb, uv)[:, :3] * tint
        if occ is not None:
            ao = _sample_tex(occ, uv)[:, :3].mean(axis=1, keepdims=True)
            alb_v = alb_v * (0.55 + 0.45 * ao)
        ndl = np.clip((-vn) @ light, 0.0, 1.0)
        col_v = np.clip(alb_v * (0.28 + 0.72 * ndl)[:, None], 0, 1)
        face_cols = (col_v[faces_v[:, 0]] + col_v[faces_v[:, 1]] + col_v[faces_v[:, 2]]) / 3.0
        batches.append((depth, xy[faces_v], face_cols))

    if batches:
        depths = np.concatenate([b[0] for b in batches])
        polys = np.concatenate([b[1] for b in batches], axis=0)
        cols = np.concatenate([b[2] for b in batches], axis=0)
        order = np.argsort(depths)
        coll = PolyCollection(
            polys[order],
            facecolors=cols[order],
            edgecolors=cols[order],
            linewidths=0.3,
            antialiased=True,
            closed=True,
        )
        ax.add_collection(coll)

    ax.set_xlim(*xlim)
    ax.set_ylim(*ylim)

    import io
    from PIL import Image

    buf = io.BytesIO()
    fig.savefig(buf, format="png", dpi=100, facecolor=bg)
    plt.close(fig)
    buf.seek(0)
    return np.asarray(Image.open(buf).convert("RGB"), dtype=np.float64) / 255.0


def save_image(img: np.ndarray, path: str | Path) -> None:
    from PIL import Image

    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8), mode="RGB").save(path)
