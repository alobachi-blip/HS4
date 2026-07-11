# -*- coding: utf-8 -*-
"""FaceCore: shapeValueFace → deform → HS2-textured render."""
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
from .skeleton import Bone, Skeleton, skin_mesh


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
        self._eye_sources: List[dict] = []
        self._use_rest_only = False  # True when verts come from fo_head (no demo LBS)
        self.shape = np.full(59, 0.5, dtype=np.float64)

        bones = {}
        for name in bone_names:
            bones[name] = Bone(
                name=name,
                parent=parents.get(name),
                rest_local=np.asarray(rest_local[name], dtype=np.float64),
                local=np.asarray(rest_local[name], dtype=np.float64).copy(),
            )
        order = []
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
        self.skeleton = Skeleton(bones=bones, order=order)

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

    def set_fo_head_meshes(
        self,
        head_npz: Union[str, Path],
        *,
        eye_sources: Optional[List[dict]] = None,
    ) -> None:
        """Replace demo geometry with fo_head o_head (+ same-space eyebases).

        Eyes must already be in the same mesh space as o_head (CmpFace prefab).
        Demo ShapeAnime LBS is disabled until real bone weights are exported.
        """
        data = np.load(head_npz)
        self.rest_verts = np.asarray(data["verts"], dtype=np.float64)
        self.faces = np.asarray(data["faces"], dtype=np.int32)
        self.uvs = np.asarray(data["uvs"], dtype=np.float64) if "uvs" in data.files else np.zeros((len(self.rest_verts), 2))
        self._use_rest_only = True
        self.extra_meshes = []
        self._eye_sources = list(eye_sources or [])

    def set_extra_meshes(self, meshes: Optional[List[dict]]) -> None:
        """Overlays already in the same space as current head verts."""
        self.extra_meshes = list(meshes or [])
        self._eye_sources = []

    def set_eye_sources(self, sources: Optional[List[dict]]) -> None:
        """Eyebases in fo_head mesh space (same as o_head) — no retarget."""
        self._eye_sources = list(sources or [])
        self.extra_meshes = []

    def set_shape(self, values: Sequence[float]) -> None:
        arr = np.asarray(list(values), dtype=np.float64)
        if arr.size < 59:
            full = np.full(59, 0.5)
            full[: arr.size] = arr
            arr = full
        self.shape = np.clip(arr[:59], 0.0, 1.0)

    def set_index(self, index: int, value: float) -> None:
        self.shape[int(index)] = float(np.clip(value, 0.0, 1.0))

    def _gather_src(self) -> Dict[str, dict]:
        src: Dict[str, dict] = {}
        for entries in self.category_table.values():
            for e in entries:
                name = e["name"]
                if name not in src:
                    src[name] = {"pos": np.zeros(3), "rot": np.zeros(3), "scl": np.ones(3)}
        for cat, entries in self.category_table.items():
            rate = float(self.shape[int(cat)]) if 0 <= int(cat) < 59 else 0.5
            for e in entries:
                name = e["name"]
                use = e.get("use", {})
                pos, rot, scl = self.anime.get_prs(name, rate)
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
        if self._use_rest_only:
            return self.rest_verts.copy()
        self.skeleton.reset_to_rest()
        apply_src_to_dst(self._gather_src(), self.skeleton)
        self.skeleton.update_world()
        return skin_mesh(
            self.rest_verts,
            self.bone_indices,
            self.bone_weights,
            self.bone_names,
            self.skeleton,
            self.bind_world_inv,
        )

    def render(
        self,
        out_front: Optional[Union[str, Path]] = None,
        out_side: Optional[Union[str, Path]] = None,
        *,
        size: int = 512,
        fast: bool = True,
    ) -> Dict[str, object]:
        verts = self.deform()
        result: Dict[str, object] = {"verts": verts}
        if self.albedo is None:
            alb = np.ones((64, 64, 4), dtype=np.float32)
            alb[..., 0], alb[..., 1], alb[..., 2] = 0.86, 0.72, 0.66
            albedo = alb
        else:
            albedo = self.albedo

        # Parts already share o_head space (fo_head prefab) — no retarget.
        extras = list(self.extra_meshes)
        for src in self._eye_sources:
            item = {
                "verts": np.asarray(src["verts"], dtype=np.float64),
                "faces": src["faces"],
                "uvs": src["uvs"],
                "albedo": src["albedo"],
                "skin_tint": src.get("skin_tint", (1.0, 1.0, 1.0)),
            }
            for k in ("use_alpha", "double_sided", "unlit", "skip_ao", "occlusion", "name"):
                if k in src:
                    item[k] = src[k]
            extras.append(item)

        for view, path in (("front", out_front), ("side", out_side)):
            if path is None:
                continue
            img = render_textured(
                verts,
                self.faces,
                self.uvs,
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
