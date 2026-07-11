# -*- coding: utf-8 -*-
"""Load HS2 cards → extract real face MainTex from HS2 Copy → textured render.

Uses D:\\HS2 - Copy only (never writes into live D:\\HS2).
Texture paths follow ChaControl.CreateFaceTexture / ChaListDefine (dll_decompiled).
"""
from __future__ import annotations

import argparse
import json
import re
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
HS4 = Path(r"D:\HS4")
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(HS4))

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
        if "skinColor" in o and isinstance(o["skinColor"], (list, tuple)):
            skin_color = list(o["skinColor"])
    return game_dict_to_rates(face_game), face_game, head_id, skin_id, skin_color


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

    for card in args.cards:
        name = card_short_name(card)
        rates, face_game, head_id, skin_id, skin_color = load_card_face_and_ids(card)
        print(f"\n=== {name} headId={head_id} skinId={skin_id} ===")

        tex_dir = args.out_dir / f"_tex_{name}"
        paths = extract_face_skin_pack(tex_dir, skin_id=skin_id, head_id=head_id)
        print(f"  MainTex ← {paths.get('main_asset')} @ {paths.get('bundle')}")
        if not paths.get("main"):
            print("  WARN: MainTex export failed, using flat fallback")

        core.set_hs2_skin(paths.get("main"), paths.get("occlusion"), skin_tint=skin_color)
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
            "main_tex": paths.get("main"),
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
