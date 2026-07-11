# -*- coding: utf-8 -*-
"""Load HS2 cards → skin+makeup+eyes from HS2 Copy → textured render.

Uses D:\\HS2 - Copy only (never writes into live D:\\HS2).
Texture paths follow ChaControl.CreateFaceTexture / ChangeEyesKind (dll_decompiled).
"""
from __future__ import annotations

import argparse
import json
import re
import shutil
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
HS4 = Path(r"D:\HS4")
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(HS4))

from face_min.compose_face_tex import (
    compose_face_albedo,
    export_addtex_layer,
    load_rgba,
)
from face_min.extract_eyes import (
    compose_eye_albedo,
    export_face_meshes_from_head,
)
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


def load_card_face_and_ids(card: Path):
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
    makeup = {}
    pupils = []
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
        if "skinColor" in o and isinstance(o["skinColor"], (list, tuple)):
            skin_color = list(o["skinColor"])
    return game_dict_to_rates(face_game), face_game, head_id, skin_id, skin_color, makeup, pupils


def _build_eye_sources(eyes_meta: dict, tex_dir: Path, pupils: list) -> list:
    """Eyebases stay in fo_head mesh space (same as exported o_head)."""
    meshes_info = eyes_meta.get("meshes") or {}
    L_info = meshes_info.get("o_eyebase_L") or {}
    R_info = meshes_info.get("o_eyebase_R") or {}
    if not L_info.get("npz") or not R_info.get("npz"):
        return []

    L = np.load(L_info["npz"])
    R = np.load(R_info["npz"])

    white = load_rgba(meshes_info.get("eye_white"))
    if white is None:
        white = np.ones((64, 64, 4), dtype=np.float32)

    p0 = pupils[0] if pupils else {}
    p1 = pupils[1] if len(pupils) > 1 else p0
    sources = []
    for side, data, pupil in (("L", L, p0), ("R", R, p1)):
        pid = int(pupil.get("pupilId", 0) or 0)
        layer = export_addtex_layer("st_eye_", pid, tex_dir / "eyes", label=f"pupil_{side}_{pid}")
        pupil_tex = load_rgba(layer.get("path") if layer else None)
        alb = compose_eye_albedo(
            white,
            pupil_tex,
            pupil.get("pupilColor") or (0.3, 0.3, 0.3, 1),
            white_color=pupil.get("whiteColor") or (1, 1, 1, 1),
        )
        sources.append(
            {
                "side": side,
                "verts": data["verts"],
                "faces": data["faces"],
                "uvs": data["uvs"],
                "albedo": alb,
                "skin_tint": (1.0, 1.0, 1.0),
            }
        )
    return sources


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("cards", nargs="*", type=Path)
    ap.add_argument("--pack", type=Path, default=ROOT / "assets" / "demo_pack")
    ap.add_argument("--out-dir", type=Path, default=ROOT / "output" / "from_cards_textured")
    ap.add_argument("--size", type=int, default=640)
    args = ap.parse_args()

    print(f"HS2 assets root (read-only): {hs2_root()}")

    if not args.cards:
        asset_dir = Path(r"C:\Users\jason\.cursor\projects\d-HS4\assets")
        args.cards = sorted(asset_dir.glob("*HS2ChaF*.png"))
    if not (args.pack / "meta.json").exists():
        raise SystemExit("Run scripts/build_demo_pack.py first")

    core = FaceCore.from_demo_pack(args.pack)
    args.out_dir.mkdir(parents=True, exist_ok=True)
    summary = []

    # Cache eyebase per headId
    eyes_cache: dict[int, dict] = {}

    for card in args.cards:
        name = card_short_name(card)
        rates, face_game, head_id, skin_id, skin_color, makeup, pupils = load_card_face_and_ids(card)
        print(f"\n=== {name} headId={head_id} skinId={skin_id} makeup={ {k: makeup.get(k) for k in ('eyeshadowId','cheekId','lipId')} } ===")

        tex_dir = args.out_dir / f"_tex_{name}"
        paths = extract_face_skin_pack(tex_dir, skin_id=skin_id, head_id=head_id)
        print(f"  MainTex ← {paths.get('main_asset')} @ {paths.get('bundle')}")

        # Makeup AddTex layers (CreateFaceTexture order: eyeshadow → cheek → lip)
        mk_dir = tex_dir / "makeup"
        lip = export_addtex_layer("st_lip_", int(makeup.get("lipId", 0) or 0), mk_dir, label="lip")
        cheek = export_addtex_layer("st_cheek_", int(makeup.get("cheekId", 0) or 0), mk_dir, label="cheek")
        eyeshadow = export_addtex_layer(
            "st_eyeshadow_", int(makeup.get("eyeshadowId", 0) or 0), mk_dir, label="eyeshadow"
        )
        for lab, layer in (("lip", lip), ("cheek", cheek), ("eyeshadow", eyeshadow)):
            if layer and layer.get("ok") and not layer.get("disabled"):
                print(f"  {lab} ← {layer.get('asset')} @ {layer.get('main_ab')}")
            else:
                print(f"  {lab} ← skip ({layer})")

        main = load_rgba(paths.get("main"))
        if main is None:
            print("  WARN: MainTex export failed, using flat fallback")
            main = np.ones((256, 256, 4), dtype=np.float32)
            main[..., 0], main[..., 1], main[..., 2] = 0.86, 0.72, 0.66
        occ = load_rgba(paths.get("occlusion"))
        composed = compose_face_albedo(
            main,
            skin_tint=skin_color,
            eyeshadow=load_rgba(eyeshadow.get("path") if eyeshadow else None),
            eyeshadow_color=makeup.get("eyeshadowColor") or (1, 1, 1, 1),
            cheek=load_rgba(cheek.get("path") if cheek else None),
            cheek_color=makeup.get("cheekColor") or (1, 1, 1, 1),
            lip=load_rgba(lip.get("path") if lip else None),
            lip_color=makeup.get("lipColor") or (1, 1, 1, 1),
            occlusion=occ,
        )
        composed_path = tex_dir / "face_composed.png"
        from PIL import Image

        Image.fromarray((np.clip(composed, 0, 1) * 255).astype(np.uint8), mode="RGBA").save(composed_path)
        core.set_composed_albedo(composed, occlusion=None, tint_already_applied=True)

        if head_id not in eyes_cache:
            mesh_dir = args.out_dir / f"_fo_head{head_id}"
            eyes_cache[head_id] = export_face_meshes_from_head(mesh_dir, head_id=head_id)
        head_info = (eyes_cache[head_id].get("meshes") or {}).get("o_head") or {}
        if not head_info.get("npz"):
            raise RuntimeError(f"fo_head o_head missing for headId={head_id}")
        extras = _build_eye_sources(eyes_cache[head_id], tex_dir, pupils)
        # Same fo_head bundle → o_head + eyebase share mesh space (CmpFace).
        core.set_fo_head_meshes(head_info["npz"], eye_sources=extras)
        print(f"  fo_head o_head + eyes: {len(extras)} (native space, no retarget)")

        core.set_shape(rates)

        front = args.out_dir / f"{name}_front.png"
        side = args.out_dir / f"{name}_side.png"
        core.render(out_front=front, out_side=side, size=args.size)

        preview = args.out_dir / f"{name}_card.png"
        shutil.copy2(card, preview)
        meta = {
            "name": name,
            "headId": head_id,
            "skinId": skin_id,
            "skinColor": skin_color,
            "makeup": {
                "eyeshadowId": makeup.get("eyeshadowId"),
                "cheekId": makeup.get("cheekId"),
                "lipId": makeup.get("lipId"),
            },
            "pupils": [{"pupilId": (p or {}).get("pupilId")} for p in (pupils or [])],
            "main_tex": paths.get("main"),
            "composed": str(composed_path),
            "occlusion": paths.get("occlusion"),
            "list_entry": paths.get("list_entry"),
            "key_rates": {
                "FaceBaseW": rates[0],
                "FaceLowW": rates[4],
                "ChinW": rates[5],
                "NoseZ": rates[33],
            },
        }
        (args.out_dir / f"{name}_meta.json").write_text(json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8")
        summary.append({"name": name, "front": str(front), "side": str(side)})
        print(f"  → {front.name}, {side.name}")

    (args.out_dir / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(f"\nDone → {args.out_dir}")


if __name__ == "__main__":
    main()
