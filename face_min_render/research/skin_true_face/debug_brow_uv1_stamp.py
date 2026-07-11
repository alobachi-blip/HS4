"""Step C: brow-ridge UV1 debug viz (Kawamoto layout → stamp UV).

See EYEBROW_UV_SPEC.md. Writes debug_kawamoto_brow_uv1_stamp.png — green hits
must be a brow-shaped cluster on the sheet, not a full-width horizontal band.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))

from face_min.compose_face_tex import (  # noqa: E402
    eyebrow_shader_uv_params,
    export_addtex_layer,
    load_rgba,
)
from face_min.extract_eyes import resolve_fo_head_entry  # noqa: E402
from face_min.extract_skeleton import _decode_vertex_channel  # noqa: E402
from face_min.hs2_abdata import hs2_root, resolve_ab  # noqa: E402

OUT = Path(__file__).resolve().parent

# From card PNG (scripts/render_from_cards.load_card_face_bundle)
KAWAMOTO_LAYOUT = (0.5400005578994751, 0.3811250627040863, 0.5288600921630859, 0.6679108738899231)
KAWAMOTO_TILT = 0.5979048013687134
KAWAMOTO_BROW_ID = 17


def vanilla_stamp_uv(
    uv1: np.ndarray, tx: float, ty: float, sx: float, sy: float, rot: float
) -> np.ndarray:
    u = uv1[:, 0] + tx
    v = uv1[:, 1] + ty
    c = float(np.cos(rot * np.pi))
    s = float(np.sin(rot * np.pi))
    px, py = u - 0.3, v - 0.4
    rx = px * c + py * s
    ry = -px * s + py * c
    u, v = rx + 0.3, ry + 0.4
    u = u * sx + (sx - 1.0) * (-0.1)
    v = v * sy + (sy - 1.0) * (-0.43)
    return np.stack([u, v], axis=1)


def load_o_head_uvs(head_id: int = 0):
    import UnityPy

    entry = resolve_fo_head_entry(head_id)
    bundle_rel = entry.get("MainAB") or "chara/00/fo_head_00.unity3d"
    env = UnityPy.load(str(resolve_ab(bundle_rel)))
    for o in env.objects:
        if o.type.name != "Mesh":
            continue
        d = o.read()
        if getattr(d, "m_Name", None) != "o_head":
            continue
        vd = d.m_VertexData
        n = int(vd.m_VertexCount)
        return _decode_vertex_channel(vd, 4, n), _decode_vertex_channel(vd, 5, n)
    raise RuntimeError("o_head not found")


def main() -> None:
    print("HS2 root", hs2_root())
    tx, ty, sx, sy, rot = eyebrow_shader_uv_params(KAWAMOTO_LAYOUT, KAWAMOTO_TILT)
    print(f"Texture3UV=({tx:.5f},{ty:.5f},{sx:.5f},{sy:.5f}) Rotator={rot:.5f}")

    uv0, uv1 = load_o_head_uvs(0)
    # UV1 stamp island (authored brow loci) — not UV0 brow-band alone
    brow = (
        (uv1[:, 0] > 0.05)
        & (uv1[:, 0] < 0.95)
        & (uv1[:, 1] > 0.05)
        & (uv1[:, 1] < 0.85)
    )
    stamp = vanilla_stamp_uv(uv1[brow], tx, ty, sx, sy, rot)
    in_unit = (
        (stamp[:, 0] >= 0)
        & (stamp[:, 0] <= 1)
        & (stamp[:, 1] >= 0)
        & (stamp[:, 1] <= 1)
    )
    print(
        f"brow_verts={int(brow.sum())} stamp_bbox="
        f"[{stamp[:,0].min():.3f},{stamp[:,1].min():.3f}]-"
        f"[{stamp[:,0].max():.3f},{stamp[:,1].max():.3f}] "
        f"in_unit={float(in_unit.mean()):.3f}"
    )

    mk = OUT / "_debug_tex"
    mk.mkdir(exist_ok=True)
    layer = export_addtex_layer("st_eyebrow_", KAWAMOTO_BROW_ID, mk, label="eyebrow")
    tex = load_rgba(layer.get("path")) if layer and layer.get("path") else None
    if tex is None:
        raise SystemExit(f"failed to load eyebrow id {KAWAMOTO_BROW_ID}: {layer}")

    size = 512
    sheet = np.clip(np.asarray(tex[..., :3], dtype=np.float32), 0, 1)
    base = Image.fromarray((sheet * 255).astype(np.uint8)).resize(
        (size, size), Image.BILINEAR
    )
    canvas = np.asarray(base, dtype=np.float32) / 255.0
    for (su, sv), ok in zip(stamp, in_unit):
        if not ok:
            continue
        x = int(np.clip(su, 0, 1) * (size - 1))
        y = int(np.clip(1.0 - sv, 0, 1) * (size - 1))
        canvas[max(0, y - 1) : y + 2, max(0, x - 1) : x + 2] = (0.05, 1.0, 0.15)

    uv1_img = np.zeros((size, size, 3), dtype=np.float32)
    for u, v in uv1[brow]:
        x = int(np.clip(u, 0, 1) * (size - 1))
        y = int(np.clip(1.0 - v, 0, 1) * (size - 1))
        uv1_img[max(0, y - 1) : y + 2, max(0, x - 1) : x + 2] = (1, 1, 1)

    # Contrast: old wrong UV0-centered formula on same verts → wide band risk
    wrong = np.zeros((size, size, 3), dtype=np.float32)
    # old: rotate about 0.5, centered scale, subtract offset, on UV0
    u0 = uv0[brow, 0]
    v0 = uv0[brow, 1]
    c = float(np.cos(rot))
    s = float(np.sin(rot))
    cu, cv = u0 - 0.5, v0 - 0.5
    ru = cu * c - cv * s
    rv = cu * s + cv * c
    su = ru * sx + 0.5 - tx
    sv = rv * sy + 0.5 - ty
    for su_i, sv_i in zip(su, sv):
        if 0 <= su_i <= 1 and 0 <= sv_i <= 1:
            x = int(su_i * (size - 1))
            y = int((1.0 - sv_i) * (size - 1))
            wrong[max(0, y - 1) : y + 2, max(0, x - 1) : x + 2] = (1.0, 0.2, 0.1)

    sheet_bg = np.asarray(
        Image.fromarray((sheet * 255).astype(np.uint8)).resize((size, size)),
        dtype=np.float32,
    ) / 255.0
    wrong_panel = sheet_bg * 0.35 + wrong
    panel = np.concatenate([canvas, uv1_img, np.clip(wrong_panel, 0, 1)], axis=1)
    out_png = OUT / "debug_kawamoto_brow_uv1_stamp.png"
    Image.fromarray((np.clip(panel, 0, 1) * 255).astype(np.uint8)).save(out_png)
    meta = {
        "Texture3UV": [tx, ty, sx, sy],
        "Texture3Rotator": rot,
        "brow_verts": int(brow.sum()),
        "stamp_in_unit_fraction": float(in_unit.mean()),
        "stamp_uv_bbox": [float(x) for x in (
            stamp[:, 0].min(), stamp[:, 1].min(), stamp[:, 0].max(), stamp[:, 1].max()
        )],
        "panels": "L=vanilla UV1 formula on sheet (green); M=brow verts in UV1; "
        "R=OLD UV0-centered formula (red) — band risk",
        "spec": "EYEBROW_UV_SPEC.md",
    }
    (OUT / "debug_kawamoto_brow_uv1_stamp.json").write_text(
        json.dumps(meta, indent=2), encoding="utf-8"
    )
    print("wrote", out_png)


if __name__ == "__main__":
    main()
