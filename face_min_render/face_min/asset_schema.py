# -*- coding: utf-8 -*-
"""Schema notes for plugging real HS2 head exports into FaceCore."""

HEAD_SKINNED_JSON = {
    "mode": "hs2",
    "bone_names": ["cf_J_FaceBase", "..."],
    "parents": {"cf_J_FaceBase": "root", "root": None},
    "rest_local": {"cf_J_FaceBase": "4x4 row-major list"},
    "bind_world_inv": {"cf_J_FaceBase": "4x4 row-major list"},
    "geometry": "head_skinned.npz  # verts, faces, bone_indices[N,4], bone_weights[N,4], bone_names",
}

REQUIRED_FILES = [
    "cf_customhead.txt or cf_customhead.json",
    "shape_anime.json or cf_anmShapeFace.bin",
    "head_skinned.json + head_skinned.npz",
]
