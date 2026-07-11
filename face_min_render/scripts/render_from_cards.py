# -*- coding: utf-8 -*-
"""Load HS2 cards → full CmpFace non-hair draw (real skeleton + real skin
weights) + CreateFaceTexture makeup.

Uses D:\\HS2 - Copy only. Mirrors ChaControl.CreateFaceTexture / ChangeEyes* /
ChangeEyelashes* / CmpFace renderers (no hair) and ShapeHeadInfoFemale.Update()
(auto-generated, see face_min/shape_update_real.py + tools/gen_shape_update_real.py).
"""
from __future__ import annotations

import argparse
import json
import re
import shutil
import sys
from pathlib import Path
from typing import Optional

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
HS4 = Path(r"D:\HS4")
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(HS4))

from face_min.compose_face_tex import (
    compose_face_albedo,
    export_addtex_layer,
    load_rgba,
)
from face_min.extract_eyes import compose_eye_albedo, tinted_alpha_albedo
from face_min.extract_face_tex import extract_face_skin_pack
from face_min.face_core import FaceCore
from face_min.hs2_abdata import hs2_root
from read_hs2_card import read_trailing_data
from write_face_params_to_card import (
    ALL_FACE_CHA_NAMES,
    get_block_info,
    parse_block_header,
    read_face_from_trailing_messagepack,
    read_trailing_header,
)
import msgpack


def card_short_name(path: Path) -> str:
    m = re.search(r"HS2ChaF_\d+_(.+?)-[0-9a-f]{8}", path.name, re.I)
    return m.group(1) if m else path.stem[:40]


def game_dict_to_rates(face_game: dict) -> list[float]:
    rates = [0.5] * 59
    for i, name in enumerate(ALL_FACE_CHA_NAMES):
        if name in face_game:
            rates[i] = float(face_game[name]) / 100.0
    return [max(0.0, min(1.0, r)) for r in rates]


def load_card_face_bundle(card: Path):
    trailing, _ = read_trailing_data(card)
    if trailing is None:
        raise RuntimeError(f"No trailing: {card}")
    face_game = read_face_from_trailing_messagepack(trailing)
    if not face_game:
        raise RuntimeError(f"No shapeValueFace: {card}")

    bh, base, err = read_trailing_header(trailing)
    lst, err = parse_block_header(bh)
    info = get_block_info(lst, "Custom")
    blob = trailing[base + int(info["pos"]) : base + int(info["pos"]) + int(info["size"])]
    up = msgpack.Unpacker(raw=False, strict_map_key=False)
    up.feed(blob)

    head_id, skin_id = 0, 0
    skin_color = [1.0, 1.0, 1.0, 1.0]
    makeup, pupils = {}, []
    eyebrow_id, mole_id = 0, 0
    eyebrow_color = [0.2, 0.15, 0.12, 1.0]
    mole_color = [1, 1, 1, 1]
    eyelashes_id = 0
    eyelashes_color = [0.15, 0.15, 0.15, 1.0]
    hl_id = 0
    hl_color = [1, 1, 1, 0.6]
    white_shadow_scale = 0.5

    while up.tell() < len(blob):
        try:
            o = up.unpack()
        except Exception:
            break
        if not isinstance(o, dict):
            continue
        if "shapeValueFace" in o:
            head_id = int(o.get("headId", 0) or 0)
            skin_id = int(o.get("skinId", 0) or 0)
            makeup = o.get("makeup") or {}
            pupils = o.get("pupil") or []
            eyebrow_id = int(o.get("eyebrowId", 0) or 0)
            eyebrow_color = list(o.get("eyebrowColor") or eyebrow_color)
            mole_id = int(o.get("moleId", 0) or 0)
            mole_color = list(o.get("moleColor") or mole_color)
            eyelashes_id = int(o.get("eyelashesId", 0) or 0)
            eyelashes_color = list(o.get("eyelashesColor") or eyelashes_color)
            hl_id = int(o.get("hlId", 0) or 0)
            hl_color = list(o.get("hlColor") or hl_color)
            white_shadow_scale = float(o.get("whiteShadowScale", 0.5) or 0.5)
        if "skinColor" in o and isinstance(o["skinColor"], (list, tuple)):
            skin_color = list(o["skinColor"])

    return {
        "rates": game_dict_to_rates(face_game),
        "face_game": face_game,
        "head_id": head_id,
        "skin_id": skin_id,
        "skin_color": skin_color,
        "makeup": makeup,
        "pupils": pupils,
        "eyebrow_id": eyebrow_id,
        "eyebrow_color": eyebrow_color,
        "mole_id": mole_id,
        "mole_color": mole_color,
        "eyelashes_id": eyelashes_id,
        "eyelashes_color": eyelashes_color,
        "hl_id": hl_id,
        "hl_color": hl_color,
        "white_shadow_scale": white_shadow_scale,
    }


def _part_main_tex(core: FaceCore, part_name: str) -> Optional[np.ndarray]:
    """Load material `_MainTex` exported by extract_skeleton.export_real_head_rig."""
    info = core.real_parts.get(part_name) or {}
    path = info.get("main_tex")
    if not path:
        return None
    return load_rgba(str(path))


def apply_makeup_and_eyes(core: FaceCore, card: dict, tex_dir: Path) -> list[str]:
    """Set per-part albedo+flags on `core` (real mode). Geometry/skinning is
    handled entirely by FaceCore.deform_real_all(); we only supply textures.

    Draw-order intent (FACE_DRAW_PARTS): tooth/tang → eyebase → eyeshadow → eyelashes.
    """
    pupils = card["pupils"] or []
    p0 = pupils[0] if pupils else {}
    p1 = pupils[1] if len(pupils) > 1 else p0

    # Prefer shared eye_white exported by export_real_head_rig; else eyebase MainTex.
    white = None
    for part_meshdir in tex_dir.parent.glob("_fo_head*"):
        cand = part_meshdir / "eye_white_c_t_eye_white_01.png"
        if cand.is_file():
            white = load_rgba(str(cand))
            break
    if white is None:
        white = _part_main_tex(core, "o_eyebase_L") or _part_main_tex(core, "o_eyebase_R")
    if white is None:
        white = np.ones((64, 64, 4), dtype=np.float32)

    applied: list[str] = []

    # Mouth interior: material MainTex from fo_head (c_tooth_t / c_tang_t).
    # skip_side: closed-mouth side views otherwise show tooth/tang punching through jaw.
    for part in ("o_tooth", "o_tang"):
        if part not in core.real_parts:
            continue
        tex = _part_main_tex(core, part)
        if tex is None:
            continue
        core.set_part_render(
            part,
            albedo=tex,
            skin_tint=(1.0, 1.0, 1.0),
            skip_ao=True,
            skip_side=True,
        )
        applied.append(part)

    hl_layer = export_addtex_layer("st_eye_hl_", int(card["hl_id"]), tex_dir / "eyes", label="hl")
    hl_tex = load_rgba(hl_layer.get("path") if hl_layer else None)

    for side, part in (("L", "o_eyebase_L"), ("R", "o_eyebase_R")):
        if part not in core.real_parts:
            continue
        pupil = p0 if side == "L" else p1
        pid = int(pupil.get("pupilId", 0) or 0)
        bid = int(pupil.get("blackId", 0) or 0)
        pupil_layer = export_addtex_layer("st_eye_", pid, tex_dir / "eyes", label=f"pupil_{side}_{pid}")
        black_layer = export_addtex_layer("st_eyeblack_", bid, tex_dir / "eyes", label=f"black_{side}_{bid}")
        alb = compose_eye_albedo(
            white,
            load_rgba(pupil_layer.get("path") if pupil_layer else None),
            pupil.get("pupilColor") or (0.3, 0.3, 0.3, 1),
            white_color=pupil.get("whiteColor") or (1, 1, 1, 1),
            black_tex=load_rgba(black_layer.get("path") if black_layer else None),
            black_color=pupil.get("blackColor") or (0, 0, 0, 1),
            hl_tex=hl_tex,
            hl_color=card["hl_color"],
            pupil_w=float(pupil.get("pupilW", 0.5) or 0.5),
            pupil_h=float(pupil.get("pupilH", 0.5) or 0.5),
            black_w=float(pupil.get("blackW", 0.8) or 0.8),
            black_h=float(pupil.get("blackH", 0.8) or 0.8),
        )
        core.set_part_render(part, albedo=alb, skin_tint=(1.0, 1.0, 1.0), skip_ao=True)
        applied.append(part)

    # Eyelid / eyekage mesh: MainTex * material _Color (c_m_eyekage defaults ~0.15 gray).
    # Makeup st_eyeshadow_ is a separate CreateFaceTexture layer on o_head albedo.
    if "o_eyeshadow" in core.real_parts:
        tex = _part_main_tex(core, "o_eyeshadow")
        if tex is not None:
            col = (core.real_parts["o_eyeshadow"].get("main_color") or [0.15, 0.15, 0.15, 1.0])
            alb = tinted_alpha_albedo(tex, col)
            core.set_part_render(
                "o_eyeshadow",
                albedo=alb,
                use_alpha=True,
                double_sided=True,
                skip_ao=True,
            )
            applied.append("o_eyeshadow")

    if "o_eyelashes" in core.real_parts:
        layer = export_addtex_layer("st_eyelash_", int(card["eyelashes_id"]), tex_dir / "lash", label="lash")
        tex = load_rgba(layer.get("path") if layer else None)
        if tex is None:
            # Fall back to the fo_head material MainTex if ChaList id is missing (mod).
            tex = _part_main_tex(core, "o_eyelashes")
        if tex is None:
            tex = np.zeros((64, 64, 4), dtype=np.float32)
            tex[..., 3] = 1
        alb = tinted_alpha_albedo(tex, card["eyelashes_color"])
        core.set_part_render(
            "o_eyelashes",
            albedo=alb,
            use_alpha=True,
            double_sided=True,
            unlit=True,
            skip_ao=True,
        )
        applied.append("o_eyelashes")

    return applied


def compose_card_face_tex(paths: dict, card: dict, tex_dir: Path) -> np.ndarray:
    mk = card["makeup"]
    mk_dir = tex_dir / "makeup"

    def layer(prefix: str, eid, label: str):
        return export_addtex_layer(prefix, int(eid or 0), mk_dir, label=label)

    lip = layer("st_lip_", mk.get("lipId", 0), "lip")
    cheek = layer("st_cheek_", mk.get("cheekId", 0), "cheek")
    eyeshadow = layer("st_eyeshadow_", mk.get("eyeshadowId", 0), "eyeshadow")
    mole = layer("st_mole_", card["mole_id"], "mole")
    # Eyebrow uses _Texture3 + eyebrowLayout/Tilt on face matDraw (ChaControl.ChangeEyebrow*).
    # Without that UV layout, the AddTex is a near-opaque sheet and would paint the whole face.
    # Skip until layout bake is wired; still list-resolve for meta.
    eyebrow = layer("st_eyebrow_", card["eyebrow_id"], "eyebrow")
    paints = mk.get("paintInfo") or []
    paint0 = layer("st_paint_", (paints[0] or {}).get("id", 0) if paints else 0, "paint0")
    paint1 = layer("st_paint_", (paints[1] or {}).get("id", 0) if len(paints) > 1 else 0, "paint1")

    for lab, L in (
        ("lip", lip),
        ("cheek", cheek),
        ("eyeshadow", eyeshadow),
        ("mole", mole),
        ("eyebrow", eyebrow),
    ):
        if L and L.get("ok") and not L.get("disabled"):
            print(f"  {lab} ← {L.get('asset')} @ {L.get('main_ab')}")
        else:
            print(f"  {lab} ← skip")

    main = load_rgba(paths.get("main"))
    if main is None:
        main = np.ones((256, 256, 4), dtype=np.float32)
        main[..., 0], main[..., 1], main[..., 2] = 0.86, 0.72, 0.66

    return compose_face_albedo(
        main,
        skin_tint=card["skin_color"],
        eyeshadow=load_rgba(eyeshadow.get("path") if eyeshadow else None),
        eyeshadow_color=mk.get("eyeshadowColor") or (1, 1, 1, 1),
        cheek=load_rgba(cheek.get("path") if cheek else None),
        cheek_color=mk.get("cheekColor") or (1, 1, 1, 1),
        lip=load_rgba(lip.get("path") if lip else None),
        lip_color=mk.get("lipColor") or (1, 1, 1, 1),
        mole=load_rgba(mole.get("path") if mole else None),
        mole_color=card["mole_color"],
        eyebrow=None,  # needs ChangeEyebrowLayout UV; see above
        eyebrow_color=card["eyebrow_color"],
        paint0=load_rgba(paint0.get("path") if paint0 else None),
        paint0_color=(paints[0] or {}).get("color", (1, 0, 0, 1)) if paints else (1, 0, 0, 1),
        paint1=load_rgba(paint1.get("path") if paint1 else None),
        paint1_color=(paints[1] or {}).get("color", (1, 0, 0, 1)) if len(paints) > 1 else (1, 0, 0, 1),
        occlusion=load_rgba(paths.get("occlusion")),
    )


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("cards", nargs="*", type=Path)
    ap.add_argument("--out-dir", type=Path, default=ROOT / "output" / "from_cards_textured")
    ap.add_argument("--size", type=int, default=640)
    args = ap.parse_args()

    print(f"HS2 assets root (read-only): {hs2_root()}")
    if not args.cards:
        asset_dir = Path(r"C:\Users\jason\.cursor\projects\d-HS4\assets")
        args.cards = sorted(asset_dir.glob("*HS2ChaF*.png"))

    args.out_dir.mkdir(parents=True, exist_ok=True)
    summary = []
    core_cache: dict[int, FaceCore] = {}

    for card_path in args.cards:
        name = card_short_name(card_path)
        card = load_card_face_bundle(card_path)
        head_id, skin_id = card["head_id"], card["skin_id"]
        mk = card["makeup"]
        print(
            f"\n=== {name} headId={head_id} skinId={skin_id} "
            f"makeup={{lip={mk.get('lipId')}, cheek={mk.get('cheekId')}, shadow={mk.get('eyeshadowId')}}} "
            f"lash={card['eyelashes_id']} brow={card['eyebrow_id']} ==="
        )

        tex_dir = args.out_dir / f"_tex_{name}"
        paths = extract_face_skin_pack(tex_dir, skin_id=skin_id, head_id=head_id)
        print(f"  MainTex ← {paths.get('main_asset')} @ {paths.get('bundle')}")

        composed = compose_card_face_tex(paths, card, tex_dir)
        composed_path = tex_dir / "face_composed.png"
        Image.fromarray((np.clip(composed, 0, 1) * 255).astype(np.uint8), mode="RGBA").save(composed_path)

        if head_id not in core_cache:
            mesh_dir = args.out_dir / f"_fo_head{head_id}"
            core_cache[head_id] = FaceCore.from_real_head(head_id, cache_dir=mesh_dir)
        core = core_cache[head_id]
        core.set_composed_albedo(composed, occlusion=None, tint_already_applied=True)

        applied_parts = apply_makeup_and_eyes(core, card, tex_dir)
        print(f"  real-skinned parts (excl head): {applied_parts}")

        core.set_shape(card["rates"])
        front = args.out_dir / f"{name}_front.png"
        side = args.out_dir / f"{name}_side.png"
        core.render(out_front=front, out_side=side, size=args.size)

        shutil.copy2(card_path, args.out_dir / f"{name}_card.png")
        meta_out = {
            "name": name,
            "headId": head_id,
            "skinId": skin_id,
            "skinColor": card["skin_color"],
            "makeup": {k: mk.get(k) for k in ("eyeshadowId", "cheekId", "lipId")},
            "eyebrowId": card["eyebrow_id"],
            "eyelashesId": card["eyelashes_id"],
            "moleId": card["mole_id"],
            "pupils": [{"pupilId": (p or {}).get("pupilId")} for p in card["pupils"]],
            "composed": str(composed_path),
            "parts": applied_parts,
            "mode": core.mode,
        }
        (args.out_dir / f"{name}_meta.json").write_text(json.dumps(meta_out, ensure_ascii=False, indent=2), encoding="utf-8")
        summary.append({"name": name, "front": str(front), "side": str(side)})
        print(f"  → {front.name}, {side.name}")

    (args.out_dir / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(f"\nDone → {args.out_dir}")


if __name__ == "__main__":
    main()
