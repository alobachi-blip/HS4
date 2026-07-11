# -*- coding: utf-8 -*-
"""FaceCore: shapeValueFace → deform → HS2-textured render.

Two modes:
  - demo pack (`from_demo_pack`): synthetic o_head.obj + distance-weight LBS +
    hand-approximated Src->Dst subset. Kept only as an offline fallback when no
    HS2 Copy is available; not fidelity-accurate.
  - real head (`from_real_head`): real cf_J_* skeleton, real per-part skin
    weights, real cf_customhead category table, real cf_anmShapeHead_XX curves,
    and shape_update_real.apply_real_shape_head_update (mechanically generated
    from the decompiled ShapeHeadInfoFemale.Update()). This is the primary path.
"""
from __future__ import annotations

import json
from pathlib import Path
from typing import Dict, List, Optional, Sequence, Tuple, Union

import numpy as np

from .animation_key_info import AnimationKeyInfo
from .category_table import load_category_table
from .extract_face_tex import load_rgba
from .obj_io import load_obj
from .render import render_textured, save_image
from .shape_update import apply_src_to_dst
from .shape_update_real import apply_real_shape_head_update
from .skeleton import Bone, Skeleton, skin_mesh


def _build_skeleton(bone_names: List[str], parents: Dict[str, Optional[str]], rest_local: Dict[str, np.ndarray]) -> Skeleton:
    bones = {}
    for name in bone_names:
        bones[name] = Bone(
            name=name,
            parent=parents.get(name),
            rest_local=np.asarray(rest_local[name], dtype=np.float64),
            local=np.asarray(rest_local[name], dtype=np.float64).copy(),
        )
    order: List[str] = []
    remaining = set(bone_names)
    while remaining:
        progressed = False
        for n in list(remaining):
            p = parents.get(n)
            if p is None or p in order:
                order.append(n)
                remaining.remove(n)
                progressed = True
        if not progressed:
            order.extend(sorted(remaining))
            break
    return Skeleton(bones=bones, order=order)


class FaceCore:
    def __init__(
        self,
        *,
        verts: np.ndarray,
        faces: np.ndarray,
        uvs: np.ndarray,
        bone_names: List[str],
        parents: Dict[str, Optional[str]],
        rest_local: Dict[str, np.ndarray],
        bind_world_inv: Dict[str, np.ndarray],
        bone_indices: np.ndarray,
        bone_weights: np.ndarray,
        category_table: Dict[int, list],
        anime: AnimationKeyInfo,
        albedo: Optional[np.ndarray] = None,
        occlusion: Optional[np.ndarray] = None,
    ):
        self.mode = "demo"
        self.rest_verts = np.asarray(verts, dtype=np.float64)
        self.faces = np.asarray(faces, dtype=np.int32)
        self.uvs = np.asarray(uvs, dtype=np.float64)
        self.bone_names = list(bone_names)
        self.bind_world_inv = {k: np.asarray(v, dtype=np.float64) for k, v in bind_world_inv.items()}
        self.bone_indices = np.asarray(bone_indices, dtype=np.int32)
        self.bone_weights = np.asarray(bone_weights, dtype=np.float64)
        self.category_table = category_table
        self.anime = anime
        self.albedo = albedo
        self.occlusion = occlusion
        self.skin_tint = (1.0, 1.0, 1.0)
        self.extra_meshes: List[dict] = []
        self.shape = np.full(59, 0.5, dtype=np.float64)
        self.skeleton = _build_skeleton(bone_names, parents, rest_local)

        # Real-mode state (unused in demo mode)
        self.real_skeleton: Optional[Skeleton] = None
        self.real_parts: Dict[str, dict] = {}
        self.real_part_render: Dict[str, dict] = {}

    @classmethod
    def from_demo_pack(cls, pack_dir: Union[str, Path]) -> "FaceCore":
        pack_dir = Path(pack_dir)
        npz_path = pack_dir / "head_skinned.npz"
        json_path = pack_dir / "head_skinned.json"
        data = np.load(npz_path, allow_pickle=True)
        meta = json.loads(json_path.read_text(encoding="utf-8"))
        verts = data["verts"]
        faces = data["faces"]
        uvs = data["uvs"] if "uvs" in data.files else np.zeros((len(verts), 2))
        bone_indices = data["bone_indices"]
        bone_weights = data["bone_weights"]
        bone_names = [str(x) for x in data["bone_names"].tolist()]
        parents = meta["parents"]
        rest_local = {k: np.asarray(v, dtype=np.float64) for k, v in meta["rest_local"].items()}
        bind_inv = {k: np.asarray(v, dtype=np.float64) for k, v in meta["bind_world_inv"].items()}
        category = load_category_table(pack_dir / "cf_customhead.json")
        anime = AnimationKeyInfo.load_json(pack_dir / "shape_anime.json")
        return cls(
            verts=verts,
            faces=faces,
            uvs=uvs,
            bone_names=bone_names,
            parents=parents,
            rest_local=rest_local,
            bind_world_inv=bind_inv,
            bone_indices=bone_indices,
            bone_weights=bone_weights,
            category_table=category,
            anime=anime,
        )

    @classmethod
    def from_real_head(cls, head_id: int, *, cache_dir: Optional[Union[str, Path]] = None) -> "FaceCore":
        """Real cf_J_* skeleton + real per-part skin weights + real cf_customhead
        category table + real cf_anmShapeHead_XX curves. No synthetic geometry,
        no synthetic bones — every number here is read out of the HS2 Copy.
        """
        from .extract_skeleton import (
            export_real_head_rig,
            load_real_category_table,
            load_real_shape_anime,
        )

        cache_dir = Path(cache_dir) if cache_dir else Path(f"_real_rig_head{head_id}")
        rig_meta = export_real_head_rig(cache_dir, head_id=head_id)

        self = cls.__new__(cls)
        self.mode = "real"
        self.skin_tint = (1.0, 1.0, 1.0)
        self.shape = np.full(59, 0.5, dtype=np.float64)
        self.extra_meshes = []
        self.albedo = None
        self.occlusion = None

        parents = rig_meta["parents"]
        rest_local = {k: np.asarray(v, dtype=np.float64) for k, v in rig_meta["rest_local"].items()}
        self.real_skeleton = _build_skeleton(list(parents.keys()), parents, rest_local)
        self.real_category_table = load_real_category_table()
        self.real_anime = load_real_shape_anime(head_id)

        self.real_parts = {}
        for part_name, info in rig_meta["parts"].items():
            if "error" in info:
                continue
            data = np.load(info["npz"])
            self.real_parts[part_name] = {
                "rest_verts": np.asarray(data["verts"], dtype=np.float64),
                "faces": np.asarray(data["faces"], dtype=np.int32),
                "uvs": np.asarray(data["uvs"], dtype=np.float64),
                "bone_names": [str(x) for x in data["bone_names"].tolist()],
                "bone_indices": np.asarray(data["bone_indices"], dtype=np.int32),
                "bone_weights": np.asarray(data["bone_weights"], dtype=np.float64),
                "bind_world_inv": {k: np.asarray(v, dtype=np.float64) for k, v in info["bind_world_inv"].items()},
            }
        if "o_head" not in self.real_parts:
            raise RuntimeError(f"o_head missing from real rig for headId={head_id}")

        head = self.real_parts["o_head"]
        self.rest_verts = head["rest_verts"]
        self.faces = head["faces"]
        self.uvs = head["uvs"]
        self.real_part_render = {}
        return self

    def set_hs2_skin(
        self,
        albedo_path: Optional[str],
        occlusion_path: Optional[str] = None,
        skin_tint: Optional[Sequence[float]] = None,
    ) -> None:
        self.albedo = load_rgba(albedo_path)
        self.occlusion = load_rgba(occlusion_path)
        if skin_tint is not None and len(skin_tint) >= 3:
            self.skin_tint = (float(skin_tint[0]), float(skin_tint[1]), float(skin_tint[2]))

    def set_composed_albedo(
        self,
        albedo: np.ndarray,
        *,
        occlusion: Optional[np.ndarray] = None,
        tint_already_applied: bool = True,
    ) -> None:
        self.albedo = np.asarray(albedo, dtype=np.float32)
        if occlusion is not None:
            self.occlusion = np.asarray(occlusion, dtype=np.float32)
        if tint_already_applied:
            self.skin_tint = (1.0, 1.0, 1.0)

    def set_part_render(self, part_name: str, *, albedo: np.ndarray, **flags) -> None:
        """Real-mode only: set albedo + render flags (use_alpha/unlit/double_sided/
        skip_ao/skin_tint) for a non-head CmpFace part (eyebase/eyelashes/eyeshadow/
        tooth/tang). Geometry is re-skinned every render() call from the SAME
        skeleton pose as o_head — no static/rest-pose overlay."""
        if part_name not in self.real_parts:
            return
        self.real_part_render[part_name] = {"albedo": albedo, **flags}

    def set_extra_meshes(self, meshes: Optional[List[dict]]) -> None:
        """Demo-mode overlays already in the same space as current head verts."""
        self.extra_meshes = list(meshes or [])

    def set_shape(self, values: Sequence[float]) -> None:
        arr = np.asarray(list(values), dtype=np.float64)
        if arr.size < 59:
            full = np.full(59, 0.5)
            full[: arr.size] = arr
            arr = full
        self.shape = np.clip(arr[:59], 0.0, 1.0)

    def set_index(self, index: int, value: float) -> None:
        self.shape[int(index)] = float(np.clip(value, 0.0, 1.0))

    def _gather_src(self, category_table: Dict[int, list], anime: AnimationKeyInfo) -> Dict[str, dict]:
        """Mirrors ShapeInfoBase.ChangeValue: for every category, overwrite only the
        masked axes of its Src bone(s) from the curve sampled at this category's
        shapeValueFace rate. Unmasked axes / untouched names keep identity."""
        src: Dict[str, dict] = {}
        for entries in category_table.values():
            for e in entries:
                name = e["name"]
                if name not in src:
                    src[name] = {"pos": np.zeros(3), "rot": np.zeros(3), "scl": np.ones(3)}
        for cat, entries in category_table.items():
            rate = float(self.shape[int(cat)]) if 0 <= int(cat) < 59 else 0.5
            for e in entries:
                name = e["name"]
                use = e.get("use", {})
                pos, rot, scl = anime.get_prs(name, rate)
                cur = src[name]
                up = use.get("pos", [True, True, True])
                ur = use.get("rot", [True, True, True])
                us = use.get("scl", [True, True, True])
                for i in range(3):
                    if up[i]:
                        cur["pos"][i] = pos[i]
                    if ur[i]:
                        cur["rot"][i] = rot[i]
                    if us[i]:
                        cur["scl"][i] = scl[i]
        return src

    def deform(self) -> np.ndarray:
        """Demo-mode single-mesh deform (kept for from_demo_pack)."""
        self.skeleton.reset_to_rest()
        apply_src_to_dst(self._gather_src(self.category_table, self.anime), self.skeleton)
        self.skeleton.update_world()
        return skin_mesh(
            self.rest_verts,
            self.bone_indices,
            self.bone_weights,
            self.bone_names,
            self.skeleton,
            self.bind_world_inv,
        )

    def deform_real_all(self) -> Dict[str, np.ndarray]:
        """Real-mode: pose the shared cf_J_* skeleton once from shapeValueFace,
        then LBS-skin every loaded part (o_head + eyebase/eyelashes/eyeshadow/
        tooth/tang) against that ONE pose — they move together by construction."""
        sk = self.real_skeleton
        sk.reset_to_rest()
        src = self._gather_src(self.real_category_table, self.real_anime)
        apply_real_shape_head_update(src, sk)
        sk.update_world()
        out: Dict[str, np.ndarray] = {}
        for name, part in self.real_parts.items():
            out[name] = skin_mesh(
                part["rest_verts"],
                part["bone_indices"],
                part["bone_weights"],
                part["bone_names"],
                sk,
                part["bind_world_inv"],
            )
        return out

    def render(
        self,
        out_front: Optional[Union[str, Path]] = None,
        out_side: Optional[Union[str, Path]] = None,
        *,
        size: int = 512,
        fast: bool = True,
    ) -> Dict[str, object]:
        if self.albedo is None:
            alb = np.ones((64, 64, 4), dtype=np.float32)
            alb[..., 0], alb[..., 1], alb[..., 2] = 0.86, 0.72, 0.66
            albedo = alb
        else:
            albedo = self.albedo

        extras: List[dict] = []
        if self.mode == "real":
            deformed = self.deform_real_all()
            verts = deformed["o_head"]
            faces = self.real_parts["o_head"]["faces"]
            uvs = self.real_parts["o_head"]["uvs"]
            for part_name, render_info in self.real_part_render.items():
                if part_name not in deformed:
                    continue
                part = self.real_parts[part_name]
                item = {
                    "verts": deformed[part_name],
                    "faces": part["faces"],
                    "uvs": part["uvs"],
                    "albedo": render_info["albedo"],
                    "skin_tint": render_info.get("skin_tint", (1.0, 1.0, 1.0)),
                }
                for k in ("use_alpha", "double_sided", "unlit", "skip_ao", "occlusion"):
                    if k in render_info:
                        item[k] = render_info[k]
                extras.append(item)
        else:
            verts = self.deform()
            faces = self.faces
            uvs = self.uvs
            extras = list(self.extra_meshes)

        result: Dict[str, object] = {"verts": verts}
        for view, path in (("front", out_front), ("side", out_side)):
            if path is None:
                continue
            img = render_textured(
                verts,
                faces,
                uvs,
                albedo=albedo,
                occlusion=self.occlusion,
                view=view,  # type: ignore[arg-type]
                size=size,
                skin_tint=self.skin_tint,
                extra_meshes=extras or None,
            )
            save_image(img, path)
            result[view] = str(path)
        return result
