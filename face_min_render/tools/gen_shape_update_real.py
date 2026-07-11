# -*- coding: utf-8 -*-
"""Mechanically parse dll_decompiled/ShapeHeadInfoFemale.cs Update() and emit
face_min/shape_update_real.py.

Do NOT hand-transcribe the C# enums/body — always regenerate from source so
index mistakes (DstName/SrcName off-by-N) cannot silently creep in.
"""
from __future__ import annotations

import re
from pathlib import Path

SRC_CS = Path(r"D:\HS4\dll_decompiled\ShapeHeadInfoFemale.cs")
OUT_PY = Path(r"D:\HS4\face_min_render\face_min\shape_update_real.py")

AXIS = {"x": 0, "y": 1, "z": 2}
VCT_ATTR = {"vctPos": "pos", "vctRot": "rot", "vctScl": "scl"}


def parse_enum(text: str, enum_name: str) -> list[str]:
    m = re.search(rf"enum {enum_name}\s*\{{([^}}]*)\}}", text, re.S)
    return [n.strip() for n in m.group(1).split(",") if n.strip()]


def parse_expr(expr: str, src_names: list[str]) -> str:
    """Translate 'dictSrc[41].vctPos.x + dictSrc[9].vctPos.x' → python sum expr."""
    expr = expr.strip()
    if expr in ("0f", "0"):
        return "0.0"
    if expr in ("1f", "1"):
        return "1.0"
    terms = [t.strip() for t in expr.split("+")]
    py_terms = []
    for t in terms:
        m = re.match(r"dictSrc\[(\d+)\]\.(vctPos|vctRot|vctScl)\.(x|y|z)", t)
        if not m:
            raise ValueError(f"Unrecognized term: {t!r} in expr {expr!r}")
        idx, vct, axis = m.groups()
        name = src_names[int(idx)]
        attr = VCT_ATTR[vct]
        py_terms.append(f"g({name!r})[{attr!r}][{AXIS[axis]}]")
    return " + ".join(py_terms)


def main() -> None:
    text = SRC_CS.read_text(encoding="utf-8")
    dst_names = parse_enum(text, "DstName")
    src_names = parse_enum(text, "SrcName")
    print(f"DstName={len(dst_names)} SrcName={len(src_names)}")

    body = re.search(r"public override void Update\(\)\s*\{(.*?)\n\t\}", text, re.S).group(1)
    stmt_re = re.compile(
        r"dictDst\[(\d+)\]\.trfBone\.(SetLocalPositionX|SetLocalPositionY|SetLocalPositionZ|SetLocalRotation|SetLocalScale)\(([^;]*)\);"
    )
    stmts = stmt_re.findall(body)
    print(f"parsed statements: {len(stmts)}")

    # Aggregate per dst bone: pos_expr[3], rot_expr[3], scl_expr[3], masks
    per_bone: dict[int, dict] = {}

    def bone(idx: int) -> dict:
        return per_bone.setdefault(
            idx,
            {
                "pos": [None, None, None],
                "rot": [None, None, None],
                "scl": [None, None, None],
            },
        )

    def split_args(argstr: str) -> list[str]:
        # args are simple sums, no nested parens/commas inside a single arg here
        depth = 0
        parts, cur = [], ""
        for ch in argstr:
            if ch == "(":
                depth += 1
            elif ch == ")":
                depth -= 1
            if ch == "," and depth == 0:
                parts.append(cur)
                cur = ""
            else:
                cur += ch
        parts.append(cur)
        return [p.strip() for p in parts]

    for dst_idx_s, method, argstr in stmts:
        dst_idx = int(dst_idx_s)
        b = bone(dst_idx)
        if method in ("SetLocalPositionX", "SetLocalPositionY", "SetLocalPositionZ"):
            axis = {"SetLocalPositionX": 0, "SetLocalPositionY": 1, "SetLocalPositionZ": 2}[method]
            b["pos"][axis] = parse_expr(argstr, src_names)
        elif method == "SetLocalRotation":
            args = split_args(argstr)
            assert len(args) == 3, (dst_idx, argstr)
            for axis, a in enumerate(args):
                b["rot"][axis] = parse_expr(a, src_names)
        elif method == "SetLocalScale":
            args = split_args(argstr)
            assert len(args) == 3, (dst_idx, argstr)
            for axis, a in enumerate(args):
                b["scl"][axis] = parse_expr(a, src_names)

    lines = []
    lines.append("# -*- coding: utf-8 -*-")
    lines.append('"""AUTO-GENERATED from dll_decompiled/ShapeHeadInfoFemale.cs Update().')
    lines.append("")
    lines.append("Do NOT hand-edit. Regenerate with tools/gen_shape_update_real.py whenever")
    lines.append("the decompiled source changes. This mirrors ShapeHeadInfoFemale.Update()")
    lines.append("statement-for-statement (dictDst[N] -> real cf_J_* bone name, dictSrc[M] ->")
    lines.append('real cf_s_* AnimationKeyInfo curve name)."""')
    lines.append("from __future__ import annotations")
    lines.append("")
    lines.append("from typing import Dict")
    lines.append("")
    lines.append("import numpy as np")
    lines.append("")
    lines.append("from .skeleton import Skeleton")
    lines.append("")
    lines.append("")
    lines.append("_ZERO = {\"pos\": np.zeros(3), \"rot\": np.zeros(3), \"scl\": np.ones(3)}")
    lines.append("")
    lines.append("")
    lines.append("def apply_real_shape_head_update(src: Dict[str, dict], skeleton: Skeleton) -> None:")
    lines.append('    """src[name] = {"pos": np.ndarray(3), "rot": np.ndarray(3), "scl": np.ndarray(3)}.')
    lines.append("")
    lines.append("    name is a real cf_s_* SrcName from AnimationKeyInfo.get_prs(name, rate).")
    lines.append("")
    lines.append("    Uses Skeleton.set_local_absolute: every value here is the literal absolute")
    lines.append("    local pos/rot/scl HS2 writes via Transform.SetLocalPositionX/Y/Z /")
    lines.append("    SetLocalRotation / SetLocalScale — NOT added/multiplied onto rest. Axes never")
    lines.append("    touched by Update() keep the bone's real rest value from the extracted rig.")
    lines.append('    """')
    lines.append("")
    lines.append("    def g(name: str) -> dict:")
    lines.append("        return src.get(name, _ZERO)")
    lines.append("")

    for dst_idx in sorted(per_bone.keys()):
        b = per_bone[dst_idx]
        name = dst_names[dst_idx]
        pos_mask = tuple(v is not None for v in b["pos"])
        rot_mask = tuple(v is not None for v in b["rot"])
        scl_mask = tuple(v is not None for v in b["scl"])
        pos_vals = [v if v is not None else "0.0" for v in b["pos"]]
        rot_vals = [v if v is not None else "0.0" for v in b["rot"]]
        scl_vals = [v if v is not None else "1.0" for v in b["scl"]]
        lines.append(f"    # DstName[{dst_idx}] = {name}")
        lines.append(f"    skeleton.set_local_absolute(")
        lines.append(f"        {name!r},")
        if any(pos_mask):
            lines.append(f"        pos=np.array([{pos_vals[0]}, {pos_vals[1]}, {pos_vals[2]}]),")
            lines.append(f"        mask_pos={pos_mask!r},")
        if any(rot_mask):
            lines.append(f"        rot_deg=np.array([{rot_vals[0]}, {rot_vals[1]}, {rot_vals[2]}]),")
            lines.append(f"        mask_rot={rot_mask!r},")
        if any(scl_mask):
            lines.append(f"        scl=np.array([{scl_vals[0]}, {scl_vals[1]}, {scl_vals[2]}]),")
            lines.append(f"        mask_scl={scl_mask!r},")
        lines.append(f"    )")
        lines.append("")

    OUT_PY.write_text("\n".join(lines), encoding="utf-8")
    print(f"wrote {OUT_PY} ({len(per_bone)} bones)")


if __name__ == "__main__":
    main()
