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
    bg: Tuple[float, float, float] = (0.62, 0.64, 0.68),
    extra_meshes: Optional[list] = None,
    exposure: float = 1.35,
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
        # Studio: key (cam-left-up) + fill (cam-right) + soft top
        light_key = np.array([0.35, 0.55, -0.75], dtype=np.float64)
        light_fill = np.array([-0.45, 0.25, -0.85], dtype=np.float64)
        light_rim = np.array([0.1, 0.8, 0.4], dtype=np.float64)
    else:
        xy_all = np.column_stack([all_v[:, 2], all_v[:, 1]])
        view_dir = np.array([-1.0, 0.0, 0.0])
        light_key = np.array([-0.85, 0.45, 0.25], dtype=np.float64)
        light_fill = np.array([-0.55, 0.2, -0.7], dtype=np.float64)
        light_rim = np.array([0.3, 0.7, 0.5], dtype=np.float64)
    light_key /= np.linalg.norm(light_key)
    light_fill /= np.linalg.norm(light_fill)
    light_rim /= np.linalg.norm(light_rim)

    pad = 0.06 * max(float(np.ptp(xy_all[:, 0])), float(np.ptp(xy_all[:, 1])), 1e-6)
    xlim = (xy_all[:, 0].min() - pad, xy_all[:, 0].max() + pad)
    ylim = (xy_all[:, 1].min() - pad, xy_all[:, 1].max() + pad)

    fig = plt.figure(figsize=(size / 100, size / 100), dpi=100)
    ax = fig.add_axes([0, 0, 1, 1])
    ax.set_aspect("equal")
    ax.axis("off")
    ax.set_facecolor(bg)

    def _shade(vn: np.ndarray) -> np.ndarray:
        """Bright wrap lighting so faces stay readable (not crushed black)."""
        # Prefer outward normals toward camera/lights; flip if mesh winding is inward.
        n = vn
        if float(np.mean(n @ (-view_dir))) < 0:
            n = -n
        wrap = 0.45
        key = np.clip((n @ (-light_key) + wrap) / (1.0 + wrap), 0.0, 1.0)
        fill = np.clip((n @ (-light_fill) + wrap) / (1.0 + wrap), 0.0, 1.0)
        rim = np.clip(n @ (-light_rim), 0.0, 1.0) ** 2
        ambient = 0.52
        return ambient + 0.38 * key + 0.22 * fill + 0.12 * rim

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
        if it.get("double_sided"):
            faces_v = f
        else:
            # Face toward camera (handle either winding)
            vis_a = (fn @ view_dir) < -0.01
            vis_b = (fn @ view_dir) > 0.01
            faces_v = f[vis_a] if vis_a.sum() >= vis_b.sum() else f[vis_b]
            if faces_v.size == 0:
                faces_v = f
        centroids = (v[faces_v[:, 0]] + v[faces_v[:, 1]] + v[faces_v[:, 2]]) / 3.0
        depth = centroids[:, 2] if view == "front" else -centroids[:, 0]

        alb_v = _sample_tex(alb, uv)
        rgb = alb_v[:, :3] * tint
        if alb_v.shape[1] >= 4:
            alpha_v = np.clip(alb_v[:, 3], 0, 1)
        else:
            alpha_v = np.ones(len(v), dtype=np.float64)
        if it.get("use_alpha") and float(alpha_v.max()) < 0.02:
            alpha_v = np.clip(alb_v[:, :3].max(axis=1), 0, 1)
        if occ is not None and not it.get("skip_ao"):
            ao = _sample_tex(occ, uv)[:, :3].mean(axis=1, keepdims=True)
            rgb = rgb * (0.7 + 0.3 * ao)
        if it.get("unlit"):
            col_v = np.clip(rgb * float(exposure), 0, 1)
        else:
            shade = _shade(vn)
            col_v = np.clip(rgb * shade[:, None] * float(exposure), 0, 1)
        face_rgb = (col_v[faces_v[:, 0]] + col_v[faces_v[:, 1]] + col_v[faces_v[:, 2]]) / 3.0
        face_a = (alpha_v[faces_v[:, 0]] + alpha_v[faces_v[:, 1]] + alpha_v[faces_v[:, 2]]) / 3.0
        if it.get("use_alpha"):
            keep = face_a > 0.04
            if not np.any(keep):
                continue
            faces_v = faces_v[keep]
            depth = depth[keep]
            face_rgb = face_rgb[keep]
            face_a = face_a[keep]
        else:
            face_a = np.ones(len(face_rgb))
        xy_f = xy[faces_v]
        face_cols = np.concatenate([face_rgb, face_a[:, None]], axis=1)
        batches.append((depth, xy_f, face_cols))

    if batches:
        depths = np.concatenate([b[0] for b in batches])
        polys = np.concatenate([b[1] for b in batches], axis=0)
        cols = np.concatenate([b[2] for b in batches], axis=0)
        order = np.argsort(depths)
        coll = PolyCollection(
            polys[order],
            facecolors=cols[order],
            edgecolors=cols[order],
            linewidths=0.2,
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
