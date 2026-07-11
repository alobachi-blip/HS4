# -*- coding: utf-8 -*-
"""Export o_head + o_eyebase_* from the same fo_head prefab variant.

CmpFace binds rendHead / rendEyes under one p_cf_head_XX tree. A single
fo_head*.unity3d contains headId 0/1/2 variants — must pick the SMR whose
parent chain matches p_cf_head_{headId:02d}, not the first Mesh named o_head.
"""
from __future__ import annotations

import json
from pathlib import Path
from typing import Dict, Optional, Sequence, Tuple

import numpy as np

from .cha_list import list_entry_by_id, load_category_list, load_cha_list_from_textasset_object
from .hs2_abdata import abdata, resolve_ab


def _mesh_to_numpy(mesh) -> Tuple[np.ndarray, np.ndarray, np.ndarray]:
    from .obj_io import load_obj_from_text

    obj_text = mesh.export()
    if not obj_text or not isinstance(obj_text, str):
        raise RuntimeError("Mesh.export() returned empty")
    return load_obj_from_text(obj_text)


def resolve_fo_head_entry(head_id: int) -> Dict[str, str]:
    for prefix_bundle in sorted((abdata() / "list" / "characustom").glob("*.unity3d")):
        if prefix_bundle.name == "namelist.unity3d":
            continue
        import UnityPy

        env = UnityPy.load(str(prefix_bundle))
        for obj in env.objects:
            if obj.type.name != "TextAsset":
                continue
            name = obj.read().m_Name
            if not name.startswith("fo_head_"):
                continue
            data = load_cha_list_from_textasset_object(obj)
            entry = list_entry_by_id(data, int(head_id))
            if entry:
                return entry
    data = load_category_list("fo_head_00")
    return list_entry_by_id(data, 0) or {}


def compose_eye_albedo(
    white_tex: np.ndarray,
    pupil_tex: Optional[np.ndarray],
    pupil_color: Sequence[float],
    *,
    white_color: Sequence[float] = (1, 1, 1, 1),
) -> np.ndarray:
    """Approximate ChangeEyesKind: white * whiteColor + st_eye AddTex * pupilColor."""
    from .compose_face_tex import blend_addtex

    base = white_tex.copy()
    if base.shape[2] == 3:
        base = np.concatenate([base, np.ones((*base.shape[:2], 1), dtype=np.float32)], axis=2)
    wc = np.array([float(white_color[i]) if i < len(white_color) else 1.0 for i in range(3)], dtype=np.float32)
    base[..., :3] *= wc
    if pupil_tex is not None:
        base = blend_addtex(base, pupil_tex, pupil_color, strength=1.0)
    return base


def _transform_for_go(env, go) -> Optional[object]:
    go_id = go.object_reader.path_id
    for tobj in env.objects:
        if tobj.type.name != "Transform":
            continue
        t = tobj.read()
        try:
            if t.m_GameObject.path_id == go_id:
                return t
        except Exception:
            continue
    return None


def _parent_prefab_name(env, go) -> Optional[str]:
    """Return p_cf_head_XX (non-hit) ancestor name, if any."""
    cur = _transform_for_go(env, go)
    for _ in range(12):
        if cur is None:
            return None
        try:
            name = cur.m_GameObject.read().m_Name
        except Exception:
            return None
        if name.startswith("p_cf_head_") and not name.endswith("_hit"):
            return name
        try:
            if not cur.m_Father:
                return None
            cur = cur.m_Father.read()
        except Exception:
            return None
    return None


def _export_smr_mesh(env, go_name: str, prefab: str, out_npz: Path) -> Optional[Dict[str, object]]:
    """Export Mesh referenced by SkinnedMeshRenderer under the given prefab."""
    for obj in env.objects:
        if obj.type.name != "SkinnedMeshRenderer":
            continue
        data = obj.read()
        try:
            go = data.m_GameObject.read()
            if go.m_Name != go_name:
                continue
        except Exception:
            continue
        if _parent_prefab_name(env, go) != prefab:
            continue
        if not data.m_Mesh:
            continue
        mesh = data.m_Mesh.read()
        verts, faces, uvs = _mesh_to_numpy(mesh)
        out_npz.parent.mkdir(parents=True, exist_ok=True)
        np.savez_compressed(out_npz, verts=verts, faces=faces, uvs=uvs)
        return {
            "npz": str(out_npz),
            "n_verts": int(verts.shape[0]),
            "n_faces": int(faces.shape[0]),
            "center": verts.mean(axis=0).tolist(),
            "aabb_min": verts.min(axis=0).tolist(),
            "aabb_max": verts.max(axis=0).tolist(),
            "prefab": prefab,
            "mesh_name": getattr(mesh, "m_Name", go_name),
            "mesh_path_id": int(data.m_Mesh.path_id),
        }
    return None


def export_face_meshes_from_head(
    out_dir: Path,
    *,
    head_id: int = 0,
) -> Dict[str, object]:
    """Export o_head + eye meshes for p_cf_head_{head_id:02d} only."""
    import UnityPy

    out_dir = Path(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    entry = resolve_fo_head_entry(head_id)
    bundle_rel = entry.get("MainAB") or "chara/00/fo_head_00.unity3d"
    if bundle_rel in ("0", ""):
        bundle_rel = "chara/00/fo_head_00.unity3d"
    bundle = resolve_ab(bundle_rel)
    env = UnityPy.load(str(bundle))

    prefab = f"p_cf_head_{int(head_id):02d}"
    exported: Dict[str, object] = {}
    for want in ("o_head", "o_eyebase_L", "o_eyebase_R", "o_eyelashes", "o_eyeshadow"):
        info = _export_smr_mesh(env, want, prefab, out_dir / f"{want}.npz")
        if info is None:
            exported[want] = {"error": f"no SMR under {prefab}"}
        else:
            exported[want] = info

    for tex_name, label in (
        ("c_t_eye_white_01", "eye_white"),
        ("c_t_eye_o_01", "eye_occlusion"),
        ("c_t_eye_n", "eye_normal"),
    ):
        for obj in env.objects:
            if obj.type.name != "Texture2D":
                continue
            data = obj.read()
            if getattr(data, "m_Name", None) != tex_name:
                continue
            dest = out_dir / f"{label}_{tex_name}.png"
            data.image.save(dest)
            exported[label] = str(dest)
            break

    meta = {
        "head_id": head_id,
        "prefab": prefab,
        "bundle": str(bundle),
        "list_entry": entry,
        "note": "Meshes taken from SMR under matching p_cf_head_XX (same space as CmpFace)",
        "meshes": exported,
    }
    (out_dir / "face_meshes_meta.json").write_text(json.dumps(meta, indent=2), encoding="utf-8")
    return meta


def export_eyebase_from_head(out_dir: Path, *, head_id: int = 0) -> Dict[str, object]:
    return export_face_meshes_from_head(out_dir, head_id=head_id)
