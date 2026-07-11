# -*- coding: utf-8 -*-
"""AUTO-GENERATED from dll_decompiled/ShapeHeadInfoFemale.cs Update().

Do NOT hand-edit. Regenerate with tools/gen_shape_update_real.py whenever
the decompiled source changes. This mirrors ShapeHeadInfoFemale.Update()
statement-for-statement (dictDst[N] -> real cf_J_* bone name, dictSrc[M] ->
real cf_s_* AnimationKeyInfo curve name)."""
from __future__ import annotations

from typing import Dict

import numpy as np

from .skeleton import Skeleton


_ZERO = {"pos": np.zeros(3), "rot": np.zeros(3), "scl": np.ones(3)}


def apply_real_shape_head_update(src: Dict[str, dict], skeleton: Skeleton) -> None:
    """src[name] = {"pos": np.ndarray(3), "rot": np.ndarray(3), "scl": np.ndarray(3)}.

    name is a real cf_s_* SrcName from AnimationKeyInfo.get_prs(name, rate).

    Uses Skeleton.set_local_absolute: every value here is the literal absolute
    local pos/rot/scl HS2 writes via Transform.SetLocalPositionX/Y/Z /
    SetLocalRotation / SetLocalScale — NOT added/multiplied onto rest. Axes never
    touched by Update() keep the bone's real rest value from the extracted rig.
    """

    def g(name: str) -> dict:
        return src.get(name, _ZERO)

    # DstName[0] = cf_J_CheekLow_L
    skeleton.set_local_absolute(
        'cf_J_CheekLow_L',
        pos=np.array([g('cf_s_CheekLow_tx_L')['pos'][0], g('cf_s_CheekLow_ty')['pos'][1], g('cf_s_CheekLow_tz')['pos'][2]]),
        mask_pos=(True, True, True),
    )

    # DstName[1] = cf_J_CheekLow_R
    skeleton.set_local_absolute(
        'cf_J_CheekLow_R',
        pos=np.array([g('cf_s_CheekLow_tx_R')['pos'][0], g('cf_s_CheekLow_ty')['pos'][1], g('cf_s_CheekLow_tz')['pos'][2]]),
        mask_pos=(True, True, True),
    )

    # DstName[2] = cf_J_CheekUp_L
    skeleton.set_local_absolute(
        'cf_J_CheekUp_L',
        pos=np.array([g('cf_s_CheekUp_tx_L')['pos'][0], g('cf_s_CheekUp_ty')['pos'][1], g('cf_s_CheekUp_tz_00')['pos'][2] + g('cf_s_CheekUp_tz_01')['pos'][2]]),
        mask_pos=(True, True, True),
    )

    # DstName[3] = cf_J_CheekUp_R
    skeleton.set_local_absolute(
        'cf_J_CheekUp_R',
        pos=np.array([g('cf_s_CheekUp_tx_R')['pos'][0], g('cf_s_CheekUp_ty')['pos'][1], g('cf_s_CheekUp_tz_00')['pos'][2] + g('cf_s_CheekUp_tz_01')['pos'][2]]),
        mask_pos=(True, True, True),
    )

    # DstName[4] = cf_J_Chin_rs
    skeleton.set_local_absolute(
        'cf_J_Chin_rs',
        pos=np.array([0.0, g('cf_s_Chin_ty')['pos'][1] + g('cf_s_Chin_rx')['pos'][1], g('cf_s_Chin_tz')['pos'][2] + g('cf_s_Chin_rx')['pos'][2]]),
        mask_pos=(False, True, True),
        rot_deg=np.array([g('cf_s_Chin_rx')['rot'][0], 0.0, 0.0]),
        mask_rot=(True, True, True),
        scl=np.array([g('cf_s_Chin_sx')['scl'][0], 1.0, 1.0]),
        mask_scl=(True, True, True),
    )

    # DstName[5] = cf_J_ChinLow
    skeleton.set_local_absolute(
        'cf_J_ChinLow',
        pos=np.array([0.0, g('cf_s_ChinLow')['pos'][1], 0.0]),
        mask_pos=(False, True, False),
    )

    # DstName[6] = cf_J_ChinTip_s
    skeleton.set_local_absolute(
        'cf_J_ChinTip_s',
        pos=np.array([0.0, g('cf_s_ChinTip_ty')['pos'][1], g('cf_s_ChinTip_tz')['pos'][2]]),
        mask_pos=(False, True, True),
        scl=np.array([g('cf_s_ChinTip_sx')['scl'][0], g('cf_s_ChinTip_sx')['scl'][1], g('cf_s_ChinTip_sx')['scl'][2]]),
        mask_scl=(True, True, True),
    )

    # DstName[7] = cf_J_EarBase_s_L
    skeleton.set_local_absolute(
        'cf_J_EarBase_s_L',
        rot_deg=np.array([0.0, g('cf_s_EarBase_ry_L')['rot'][1], g('cf_s_EarBase_rz_L')['rot'][2]]),
        mask_rot=(True, True, True),
        scl=np.array([g('cf_s_EarBase_s_L')['scl'][0], g('cf_s_EarBase_s_L')['scl'][1], g('cf_s_EarBase_s_L')['scl'][2]]),
        mask_scl=(True, True, True),
    )

    # DstName[8] = cf_J_EarBase_s_R
    skeleton.set_local_absolute(
        'cf_J_EarBase_s_R',
        rot_deg=np.array([0.0, g('cf_s_EarBase_ry_R')['rot'][1], g('cf_s_EarBase_rz_R')['rot'][2]]),
        mask_rot=(True, True, True),
        scl=np.array([g('cf_s_EarBase_s_R')['scl'][0], g('cf_s_EarBase_s_R')['scl'][1], g('cf_s_EarBase_s_R')['scl'][2]]),
        mask_scl=(True, True, True),
    )

    # DstName[9] = cf_J_EarLow_L
    skeleton.set_local_absolute(
        'cf_J_EarLow_L',
        pos=np.array([0.0, g('cf_s_EarLow_L')['pos'][1], g('cf_s_EarLow_L')['pos'][2]]),
        mask_pos=(False, True, True),
        scl=np.array([g('cf_s_EarLow_L')['scl'][0], g('cf_s_EarLow_L')['scl'][1], g('cf_s_EarLow_L')['scl'][2]]),
        mask_scl=(True, True, True),
    )

    # DstName[10] = cf_J_EarLow_R
    skeleton.set_local_absolute(
        'cf_J_EarLow_R',
        pos=np.array([0.0, g('cf_s_EarLow_R')['pos'][1], g('cf_s_EarLow_R')['pos'][2]]),
        mask_pos=(False, True, True),
        scl=np.array([g('cf_s_EarLow_R')['scl'][0], g('cf_s_EarLow_R')['scl'][1], g('cf_s_EarLow_R')['scl'][2]]),
        mask_scl=(True, True, True),
    )

    # DstName[11] = cf_J_EarRing_L
    skeleton.set_local_absolute(
        'cf_J_EarRing_L',
        pos=np.array([0.0, g('cf_s_EarRing_L')['pos'][1], 0.0]),
        mask_pos=(False, True, False),
        rot_deg=np.array([0.0, 0.0, g('cf_s_EarRing_rz_L')['rot'][2]]),
        mask_rot=(True, True, True),
        scl=np.array([g('cf_s_EarRing_s_L')['scl'][0], g('cf_s_EarRing_s_L')['scl'][1], g('cf_s_EarRing_s_L')['scl'][2]]),
        mask_scl=(True, True, True),
    )

    # DstName[12] = cf_J_EarRing_R
    skeleton.set_local_absolute(
        'cf_J_EarRing_R',
        pos=np.array([0.0, g('cf_s_EarRing_R')['pos'][1], 0.0]),
        mask_pos=(False, True, False),
        rot_deg=np.array([0.0, 0.0, g('cf_s_EarRing_rz_R')['rot'][2]]),
        mask_rot=(True, True, True),
        scl=np.array([g('cf_s_EarRing_s_R')['scl'][0], g('cf_s_EarRing_s_R')['scl'][1], g('cf_s_EarRing_s_R')['scl'][2]]),
        mask_scl=(True, True, True),
    )

    # DstName[13] = cf_J_EarUp_L
    skeleton.set_local_absolute(
        'cf_J_EarUp_L',
        pos=np.array([g('cf_s_EarUp_L')['pos'][0], g('cf_s_EarUp_L')['pos'][1], g('cf_s_EarUp_L')['pos'][2]]),
        mask_pos=(True, True, True),
        rot_deg=np.array([g('cf_s_EarUp_L')['rot'][0], g('cf_s_EarUp_L')['rot'][1], 0.0]),
        mask_rot=(True, True, True),
        scl=np.array([g('cf_s_EarUp_L')['scl'][0], g('cf_s_EarUp_L')['scl'][1], g('cf_s_EarUp_L')['scl'][2]]),
        mask_scl=(True, True, True),
    )

    # DstName[14] = cf_J_EarUp_R
    skeleton.set_local_absolute(
        'cf_J_EarUp_R',
        pos=np.array([g('cf_s_EarUp_R')['pos'][0], g('cf_s_EarUp_R')['pos'][1], g('cf_s_EarUp_R')['pos'][2]]),
        mask_pos=(True, True, True),
        rot_deg=np.array([g('cf_s_EarUp_R')['rot'][0], g('cf_s_EarUp_R')['rot'][1], 0.0]),
        mask_rot=(True, True, True),
        scl=np.array([g('cf_s_EarUp_R')['scl'][0], g('cf_s_EarUp_R')['scl'][1], g('cf_s_EarUp_R')['scl'][2]]),
        mask_scl=(True, True, True),
    )

    # DstName[15] = cf_J_Eye_r_L
    skeleton.set_local_absolute(
        'cf_J_Eye_r_L',
        rot_deg=np.array([0.0, g('cf_s_Eye_ry_L')['rot'][1], 0.0]),
        mask_rot=(True, True, True),
    )

    # DstName[16] = cf_J_Eye_r_R
    skeleton.set_local_absolute(
        'cf_J_Eye_r_R',
        rot_deg=np.array([0.0, g('cf_s_Eye_ry_R')['rot'][1], 0.0]),
        mask_rot=(True, True, True),
    )

    # DstName[17] = cf_J_Eye_s_L
    skeleton.set_local_absolute(
        'cf_J_Eye_s_L',
        scl=np.array([g('cf_s_Eye_sx_L')['scl'][0], g('cf_s_Eye_sy_L')['scl'][1], 1.0]),
        mask_scl=(True, True, True),
    )

    # DstName[18] = cf_J_Eye_s_R
    skeleton.set_local_absolute(
        'cf_J_Eye_s_R',
        scl=np.array([g('cf_s_Eye_sx_R')['scl'][0], g('cf_s_Eye_sy_R')['scl'][1], 1.0]),
        mask_scl=(True, True, True),
    )

    # DstName[19] = cf_J_Eye_t_L
    skeleton.set_local_absolute(
        'cf_J_Eye_t_L',
        pos=np.array([g('cf_s_Eye_tx_L')['pos'][0], g('cf_s_Eye_ty')['pos'][1], g('cf_s_Eye_tz')['pos'][2]]),
        mask_pos=(True, True, True),
        rot_deg=np.array([0.0, 0.0, g('cf_s_Eye_rz_L')['rot'][2]]),
        mask_rot=(True, True, True),
    )

    # DstName[20] = cf_J_Eye_t_R
    skeleton.set_local_absolute(
        'cf_J_Eye_t_R',
        pos=np.array([g('cf_s_Eye_tx_R')['pos'][0], g('cf_s_Eye_ty')['pos'][1], g('cf_s_Eye_tz')['pos'][2]]),
        mask_pos=(True, True, True),
        rot_deg=np.array([0.0, 0.0, g('cf_s_Eye_rz_R')['rot'][2]]),
        mask_rot=(True, True, True),
    )

    # DstName[21] = cf_J_Eye01_L
    skeleton.set_local_absolute(
        'cf_J_Eye01_L',
        rot_deg=np.array([g('cf_s_Eye01_rx_L')['rot'][0], g('cf_s_Eye01_L')['rot'][1] + g('cf_s_Eye01_rx_L')['rot'][1], 0.0]),
        mask_rot=(True, True, True),
    )

    # DstName[22] = cf_J_Eye01_R
    skeleton.set_local_absolute(
        'cf_J_Eye01_R',
        rot_deg=np.array([g('cf_s_Eye01_rx_R')['rot'][0], g('cf_s_Eye01_R')['rot'][1] + g('cf_s_Eye01_rx_R')['rot'][1], 0.0]),
        mask_rot=(True, True, True),
    )

    # DstName[23] = cf_J_Eye02_L
    skeleton.set_local_absolute(
        'cf_J_Eye02_L',
        rot_deg=np.array([g('cf_s_Eye02_L')['rot'][0], g('cf_s_Eye02_ry_L')['rot'][1], g('cf_s_Eye02_ry_L')['rot'][2]]),
        mask_rot=(True, True, True),
    )

    # DstName[24] = cf_J_Eye02_R
    skeleton.set_local_absolute(
        'cf_J_Eye02_R',
        rot_deg=np.array([g('cf_s_Eye02_R')['rot'][0], g('cf_s_Eye02_ry_R')['rot'][1], g('cf_s_Eye02_ry_R')['rot'][2]]),
        mask_rot=(True, True, True),
    )

    # DstName[25] = cf_J_Eye03_L
    skeleton.set_local_absolute(
        'cf_J_Eye03_L',
        pos=np.array([g('cf_s_Eye03_L')['pos'][0], 0.0, 0.0]),
        mask_pos=(True, False, False),
        rot_deg=np.array([g('cf_s_Eye03_rx_L')['rot'][0], g('cf_s_Eye03_L')['rot'][1], 0.0]),
        mask_rot=(True, True, True),
    )

    # DstName[26] = cf_J_Eye03_R
    skeleton.set_local_absolute(
        'cf_J_Eye03_R',
        pos=np.array([g('cf_s_Eye03_R')['pos'][0], 0.0, 0.0]),
        mask_pos=(True, False, False),
        rot_deg=np.array([g('cf_s_Eye03_rx_R')['rot'][0], g('cf_s_Eye03_R')['rot'][1], 0.0]),
        mask_rot=(True, True, True),
    )

    # DstName[27] = cf_J_Eye04_L
    skeleton.set_local_absolute(
        'cf_J_Eye04_L',
        rot_deg=np.array([g('cf_s_Eye04_L')['rot'][0], g('cf_s_Eye04_ry_L')['rot'][1], g('cf_s_Eye04_ry_L')['rot'][2]]),
        mask_rot=(True, True, True),
    )

    # DstName[28] = cf_J_Eye04_R
    skeleton.set_local_absolute(
        'cf_J_Eye04_R',
        rot_deg=np.array([g('cf_s_Eye04_R')['rot'][0], g('cf_s_Eye04_ry_R')['rot'][1], g('cf_s_Eye04_ry_R')['rot'][2]]),
        mask_rot=(True, True, True),
    )

    # DstName[29] = cf_J_EyePos_rz_L
    skeleton.set_local_absolute(
        'cf_J_EyePos_rz_L',
        rot_deg=np.array([0.0, 0.0, g('cf_s_EyePos_rz_L')['rot'][2]]),
        mask_rot=(True, True, True),
    )

    # DstName[30] = cf_J_EyePos_rz_R
    skeleton.set_local_absolute(
        'cf_J_EyePos_rz_R',
        rot_deg=np.array([0.0, 0.0, g('cf_s_EyePos_rz_R')['rot'][2]]),
        mask_rot=(True, True, True),
    )

    # DstName[31] = cf_J_FaceBase
    skeleton.set_local_absolute(
        'cf_J_FaceBase',
        scl=np.array([g('cf_s_FaceBase_sx')['scl'][0], 1.0, 1.0]),
        mask_scl=(True, True, True),
    )

    # DstName[32] = cf_J_FaceLow_s
    skeleton.set_local_absolute(
        'cf_J_FaceLow_s',
        scl=np.array([g('cf_s_FaceLow_sx')['scl'][0], 1.0, 1.0]),
        mask_scl=(True, True, True),
    )

    # DstName[33] = cf_J_FaceLowBase
    skeleton.set_local_absolute(
        'cf_J_FaceLowBase',
        pos=np.array([0.0, 0.0, g('cf_s_FaceLow_tz')['pos'][2]]),
        mask_pos=(False, False, True),
    )

    # DstName[34] = cf_J_FaceUp_ty
    skeleton.set_local_absolute(
        'cf_J_FaceUp_ty',
        pos=np.array([0.0, g('cf_s_FaceUp_ty')['pos'][1], 0.0]),
        mask_pos=(False, True, False),
    )

    # DstName[35] = cf_J_FaceUp_tz
    skeleton.set_local_absolute(
        'cf_J_FaceUp_tz',
        pos=np.array([0.0, 0.0, g('cf_s_FaceUp_tz')['pos'][2]]),
        mask_pos=(False, False, True),
    )

    # DstName[36] = cf_J_megane
    skeleton.set_local_absolute(
        'cf_J_megane',
        pos=np.array([0.0, g('cf_s_megane_ty_nose')['pos'][1] + g('cf_s_megane_rx_nosebridge')['pos'][1] + g('cf_s_megane_ty_eye')['pos'][1], g('cf_s_megane_ty_nose')['pos'][2] + g('cf_s_megane_tz_nosebridge')['pos'][2] + g('cf_s_megane_ty_eye')['pos'][2]]),
        mask_pos=(False, True, True),
        rot_deg=np.array([g('cf_s_megane_ty_nose')['rot'][0] + g('cf_s_megane_rx_nosebridge')['rot'][0] + g('cf_s_megane_ty_eye')['rot'][0], 0.0, 0.0]),
        mask_rot=(True, True, True),
    )

    # DstName[37] = cf_J_Mouth_L
    skeleton.set_local_absolute(
        'cf_J_Mouth_L',
        pos=np.array([0.0, g('cf_s_Mouth_L')['pos'][1], 0.0]),
        mask_pos=(False, True, False),
        rot_deg=np.array([0.0, 0.0, g('cf_s_Mouth_L')['rot'][2]]),
        mask_rot=(True, True, True),
    )

    # DstName[38] = cf_J_Mouth_R
    skeleton.set_local_absolute(
        'cf_J_Mouth_R',
        pos=np.array([0.0, g('cf_s_Mouth_R')['pos'][1], 0.0]),
        mask_pos=(False, True, False),
        rot_deg=np.array([0.0, 0.0, g('cf_s_Mouth_R')['rot'][2]]),
        mask_rot=(True, True, True),
    )

    # DstName[39] = cf_J_MouthLow
    skeleton.set_local_absolute(
        'cf_J_MouthLow',
        pos=np.array([0.0, g('cf_s_MouthLow')['pos'][1], g('cf_s_MouthLow')['pos'][2]]),
        mask_pos=(False, True, True),
        scl=np.array([g('cf_s_MouthLow')['scl'][0], 1.0, 1.0]),
        mask_scl=(True, True, True),
    )

    # DstName[40] = cf_J_Mouthup
    skeleton.set_local_absolute(
        'cf_J_Mouthup',
        pos=np.array([0.0, g('cf_s_Mouthup')['pos'][1], 0.0]),
        mask_pos=(False, True, False),
    )

    # DstName[41] = cf_J_MouthBase_s
    skeleton.set_local_absolute(
        'cf_J_MouthBase_s',
        scl=np.array([g('cf_s_MouthBase_sx')['scl'][0], g('cf_s_MouthBase_sy')['scl'][1], 1.0]),
        mask_scl=(True, True, True),
    )

    # DstName[42] = cf_J_MouthBase_tr
    skeleton.set_local_absolute(
        'cf_J_MouthBase_tr',
        pos=np.array([0.0, g('cf_s_MouthBase_ty')['pos'][1], g('cf_s_MouthBase_ty')['pos'][2] + g('cf_s_MouthBase_tz')['pos'][2]]),
        mask_pos=(False, True, True),
    )

    # DstName[43] = cf_J_Nose_t
    skeleton.set_local_absolute(
        'cf_J_Nose_t',
        pos=np.array([0.0, g('cf_s_Nose_rx')['pos'][1], g('cf_s_Nose_tz')['pos'][2]]),
        mask_pos=(False, True, True),
        rot_deg=np.array([g('cf_s_Nose_rx')['rot'][0], 0.0, 0.0]),
        mask_rot=(True, True, True),
    )

    # DstName[44] = cf_J_Nose_tip
    skeleton.set_local_absolute(
        'cf_J_Nose_tip',
        pos=np.array([0.0, g('cf_s_Nose_tip')['pos'][1], g('cf_s_Nose_tip')['pos'][2]]),
        mask_pos=(False, True, True),
        scl=np.array([g('cf_s_Nose_tip')['scl'][0], g('cf_s_Nose_tip')['scl'][1], g('cf_s_Nose_tip')['scl'][2]]),
        mask_scl=(True, True, True),
    )

    # DstName[45] = cf_J_NoseBase_s
    skeleton.set_local_absolute(
        'cf_J_NoseBase_s',
        rot_deg=np.array([g('cf_s_NoseBase_rx')['rot'][0] + g('cf_s_NoseBase')['rot'][0], 0.0, 0.0]),
        mask_rot=(True, True, True),
        scl=np.array([g('cf_s_NoseBase_sx')['scl'][0], g('cf_s_NoseBase_sx')['scl'][1], g('cf_s_NoseBase_sx')['scl'][2]]),
        mask_scl=(True, True, True),
    )

    # DstName[46] = cf_J_NoseBase_trs
    skeleton.set_local_absolute(
        'cf_J_NoseBase_trs',
        pos=np.array([0.0, g('cf_s_NoseBase_rx')['pos'][1] + g('cf_s_NoseBase_ty')['pos'][1] + g('cf_s_NoseBase')['pos'][1], g('cf_s_NoseBase_rx')['pos'][2] + g('cf_s_NoseBase_tz')['pos'][2] + g('cf_s_NoseBase')['pos'][2]]),
        mask_pos=(False, True, True),
    )

    # DstName[47] = cf_J_NoseBridge_s
    skeleton.set_local_absolute(
        'cf_J_NoseBridge_s',
        scl=np.array([g('cf_s_NoseBridge_sx')['scl'][0], 1.0, 1.0]),
        mask_scl=(True, True, True),
    )

    # DstName[48] = cf_J_NoseBridge_t
    skeleton.set_local_absolute(
        'cf_J_NoseBridge_t',
        pos=np.array([0.0, g('cf_s_NoseBridge_ty')['pos'][1], g('cf_s_NoseBridge_tz_00')['pos'][2] + g('cf_s_NoseBridge_tz_01')['pos'][2] + g('cf_s_NoseBridge_ty')['pos'][2] + g('cf_s_NoseBridge_rx')['pos'][2]]),
        mask_pos=(False, True, True),
        rot_deg=np.array([g('cf_s_NoseBridge_rx')['rot'][0], 0.0, 0.0]),
        mask_rot=(True, True, True),
    )

    # DstName[49] = cf_J_NoseWing_tx_L
    skeleton.set_local_absolute(
        'cf_J_NoseWing_tx_L',
        pos=np.array([g('cf_s_NoseWing_tx_L')['pos'][0], g('cf_s_NoseWing_ty')['pos'][1], g('cf_s_NoseWing_tz')['pos'][2]]),
        mask_pos=(True, True, True),
        rot_deg=np.array([g('cf_s_NoseWing_rx')['rot'][0], 0.0, g('cf_s_NoseWing_rz_L')['rot'][2]]),
        mask_rot=(True, True, True),
    )

    # DstName[50] = cf_J_NoseWing_tx_R
    skeleton.set_local_absolute(
        'cf_J_NoseWing_tx_R',
        pos=np.array([g('cf_s_NoseWing_tx_R')['pos'][0], g('cf_s_NoseWing_ty')['pos'][1], g('cf_s_NoseWing_tz')['pos'][2]]),
        mask_pos=(True, True, True),
        rot_deg=np.array([g('cf_s_NoseWing_rx')['rot'][0], 0.0, g('cf_s_NoseWing_rz_R')['rot'][2]]),
        mask_rot=(True, True, True),
    )

    # DstName[51] = cf_J_MouthCavity
    skeleton.set_local_absolute(
        'cf_J_MouthCavity',
        pos=np.array([0.0, g('cf_s_MouthC_ty')['pos'][1], g('cf_s_MouthC_tz')['pos'][2] + g('cf_s_MouthC_ty')['pos'][2]]),
        mask_pos=(False, True, True),
    )
