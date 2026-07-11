# -*- coding: utf-8 -*-
"""Subset of ShapeHeadInfoFemale.Update: Src dict → Dst bone locals.

Full game Update writes dozens of dictDst indices. Demo pack only creates the
bones we need; rules below match the decompiled assignments for those bones.
"""
from __future__ import annotations

from typing import Dict

import numpy as np

from .skeleton import Skeleton


def apply_src_to_dst(src: Dict[str, dict], skeleton: Skeleton) -> None:
    """src[name] = {pos, rot, scl} numpy arrays from AnimationKeyInfo."""

    def g(name: str):
        d = src.get(name)
        if d is None:
            return np.zeros(3), np.zeros(3), np.ones(3)
        return d["pos"], d["rot"], d["scl"]

    # FaceBase scale X  (dst FaceBase / cf_J_FaceBase) — game: dictDst[31] scl.x ← src FaceBase_sx
    p, r, s = g("cf_s_FaceBase_sx")
    skeleton.set_local_components("cf_J_FaceBase", scl=s, mask_scl=(True, False, False), pos=np.zeros(3), mask_pos=(False, False, False))

    # FaceLow width + depth
    p, r, s = g("cf_s_FaceLow_sx")
    skeleton.set_local_components("cf_J_FaceLow_s", scl=s, mask_scl=(True, False, False))
    p, r, s = g("cf_s_FaceLow_tz")
    skeleton.set_local_components(
        "cf_J_FaceLowBase",
        pos=p,
        mask_pos=(False, False, True),
    )

    # Face up Y/Z
    p, r, s = g("cf_s_FaceUp_ty")
    skeleton.set_local_components("cf_J_FaceUp_ty", pos=p, mask_pos=(False, True, False))
    p, r, s = g("cf_s_FaceUp_tz")
    skeleton.set_local_components("cf_J_FaceUp_tz", pos=p, mask_pos=(False, False, True))

    # Chin
    p_sx, r_sx, s_sx = g("cf_s_Chin_sx")
    p_ty, r_ty, s_ty = g("cf_s_Chin_ty")
    p_tz, r_tz, s_tz = g("cf_s_Chin_tz")
    p_rx, r_rx, s_rx = g("cf_s_Chin_rx")
    # game: Chin_rs pos.y = Chin_ty + Chin_rx.pos.y; pos.z = Chin_tz + Chin_rx.pos.z; rot.x = Chin_rx; scl.x = Chin_sx
    chin_pos = np.array([0.0, p_ty[1] + p_rx[1], p_tz[2] + p_rx[2]])
    skeleton.set_local_components(
        "cf_J_Chin_rs",
        pos=chin_pos,
        rot_deg=np.array([r_rx[0], 0.0, 0.0]),
        scl=s_sx,
        mask_pos=(False, True, True),
        mask_rot=(True, False, False),
        mask_scl=(True, False, False),
    )
    p, r, s = g("cf_s_ChinLow")
    skeleton.set_local_components("cf_J_ChinLow", pos=p, mask_pos=(False, True, False))

    # Chin tip
    p_sx, _, s_sx = g("cf_s_ChinTip_sx")
    p_ty, _, _ = g("cf_s_ChinTip_ty")
    p_tz, _, _ = g("cf_s_ChinTip_tz")
    skeleton.set_local_components(
        "cf_J_ChinTip_s",
        pos=np.array([0.0, p_ty[1], p_tz[2]]),
        scl=s_sx,
        mask_pos=(False, True, True),
        mask_scl=(True, True, True),
    )

    # Cheeks
    for side, src_tx, dst in (
        ("L", "cf_s_CheekLow_tx_L", "cf_J_CheekLow_L"),
        ("R", "cf_s_CheekLow_tx_R", "cf_J_CheekLow_R"),
    ):
        p, r, s = g(src_tx)
        p_ty, _, _ = g("cf_s_CheekLow_ty")
        p_tz, _, _ = g("cf_s_CheekLow_tz")
        skeleton.set_local_components(
            dst,
            pos=np.array([p[0], p_ty[1], p_tz[2]]),
            mask_pos=(True, True, True),
        )
    for src_tx, dst in (
        ("cf_s_CheekUp_tx_L", "cf_J_CheekUp_L"),
        ("cf_s_CheekUp_tx_R", "cf_J_CheekUp_R"),
    ):
        p, _, _ = g(src_tx)
        p_ty, _, _ = g("cf_s_CheekUp_ty")
        p_tz0, _, _ = g("cf_s_CheekUp_tz_00")
        p_tz1, _, _ = g("cf_s_CheekUp_tz_01")
        skeleton.set_local_components(
            dst,
            pos=np.array([p[0], p_ty[1], p_tz0[2] + p_tz1[2]]),
            mask_pos=(True, True, True),
        )

    # Nose
    p, r, s = g("cf_s_NoseBridge_tz_00")
    p1, _, _ = g("cf_s_NoseBridge_tz_01")
    p_ty, _, _ = g("cf_s_NoseBridge_ty")
    skeleton.set_local_components(
        "cf_J_NoseBridge_t",
        pos=np.array([0.0, p_ty[1], p[2] + p1[2]]),
        mask_pos=(False, True, True),
    )
    p, r, s = g("cf_s_Nose_tip")
    skeleton.set_local_components(
        "cf_J_Nose_tip",
        pos=p,
        scl=s,
        mask_pos=(False, True, True),
        mask_scl=(True, True, True),
    )
    p, r, s = g("cf_s_NoseBase_tz")
    p_ty, r_ty, _ = g("cf_s_NoseBase_ty")
    p_rx, r_rx, s_rx = g("cf_s_NoseBase_rx")
    skeleton.set_local_components(
        "cf_J_NoseBase_trs",
        pos=np.array([0.0, p_ty[1] + p[1], p[2] + p_ty[2]]),
        rot_deg=np.array([r_rx[0] + r_ty[0], 0.0, 0.0]),
        mask_pos=(False, True, True),
        mask_rot=(True, False, False),
    )
    for src_n, dst in (
        ("cf_s_NoseWing_tx_L", "cf_J_NoseWing_tx_L"),
        ("cf_s_NoseWing_tx_R", "cf_J_NoseWing_tx_R"),
    ):
        p, _, _ = g(src_n)
        p_ty, _, _ = g("cf_s_NoseWing_ty")
        p_tz, _, _ = g("cf_s_NoseWing_tz")
        skeleton.set_local_components(
            dst,
            pos=np.array([p[0], p_ty[1], p_tz[2]]),
            mask_pos=(True, True, True),
        )

    # Mouth base
    p, _, s = g("cf_s_MouthBase_ty")
    p_tz, _, _ = g("cf_s_MouthBase_tz")
    skeleton.set_local_components(
        "cf_J_MouthBase_tr",
        pos=np.array([0.0, p[1], p[2] + p_tz[2]]),
        mask_pos=(False, True, True),
    )
    _, _, s = g("cf_s_MouthBase_sx")
    _, _, sy = g("cf_s_MouthBase_sy")
    skeleton.set_local_components(
        "cf_J_MouthBase_s",
        scl=np.array([s[0], sy[1], 1.0]),
        mask_scl=(True, True, False),
    )
