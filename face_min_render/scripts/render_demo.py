# -*- coding: utf-8 -*-
"""Render front/side face images from shapeValueFace-like params."""
from __future__ import annotations

import argparse
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from face_min.face_core import FaceCore


def main():
    ap = argparse.ArgumentParser(description="Minimal HS2-like face renderer (offline)")
    ap.add_argument("--pack", type=Path, default=ROOT / "assets" / "demo_pack")
    ap.add_argument("--out-dir", type=Path, default=ROOT / "output")
    ap.add_argument("--size", type=int, default=512)
    ap.add_argument("--face-base-w", type=float, default=None, help="index 0, 0..1")
    ap.add_argument("--face-low-w", type=float, default=None, help="index 4")
    ap.add_argument("--chin-w", type=float, default=None, help="index 5")
    ap.add_argument("--chin-z", type=float, default=None, help="index 7")
    ap.add_argument("--nose-z", type=float, default=None, help="index 33")
    ap.add_argument("--cheek-w", type=float, default=None, help="index 15")
    ap.add_argument("--prefix", type=str, default="face")
    args = ap.parse_args()

    if not (args.pack / "meta.json").exists():
        raise SystemExit(f"Demo pack missing. Run: python scripts/build_demo_pack.py\n  looked in {args.pack}")

    core = FaceCore.from_demo_pack(args.pack)
    shape = [0.5] * 59
    overrides = {
        0: args.face_base_w,
        4: args.face_low_w,
        5: args.chin_w,
        7: args.chin_z,
        15: args.cheek_w,
        33: args.nose_z,
    }
    for i, v in overrides.items():
        if v is not None:
            shape[i] = float(v)
    core.set_shape(shape)

    args.out_dir.mkdir(parents=True, exist_ok=True)
    front = args.out_dir / f"{args.prefix}_front.png"
    side = args.out_dir / f"{args.prefix}_side.png"

    t0 = time.perf_counter()
    core.render(out_front=front, out_side=side, size=args.size, fast=True)
    dt = time.perf_counter() - t0
    print(f"Wrote {front}")
    print(f"Wrote {side}")
    print(f"deform+render {dt*1000:.1f} ms")

    # Also emit a default / wide / side-depth comparison if no overrides
    if all(v is None for v in overrides.values()):
        for name, vals in (
            ("default", shape),
            ("wide", _with(shape, {0: 0.9, 4: 0.85, 5: 0.7, 15: 0.8})),
            ("narrow", _with(shape, {0: 0.15, 4: 0.2, 5: 0.25})),
            ("nose_out", _with(shape, {33: 0.95, 7: 0.8})),
        ):
            core.set_shape(vals)
            core.render(
                out_front=args.out_dir / f"{name}_front.png",
                out_side=args.out_dir / f"{name}_side.png",
                size=args.size,
                fast=True,
            )
            print(f"Wrote comparison set: {name}")


def _with(base, upd):
    out = list(base)
    for k, v in upd.items():
        out[k] = v
    return out


if __name__ == "__main__":
    main()
