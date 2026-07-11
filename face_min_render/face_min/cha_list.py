# -*- coding: utf-8 -*-
"""Parse Illusion ChaListData (MessagePack) from list/characustom TextAssets.

Mirrors AIChara.ChaListControl.LoadListInfo + ChaListData (dll_decompiled).
"""
from __future__ import annotations

import struct
from pathlib import Path
from typing import Any, Dict, List, Optional

import msgpack

from .hs2_abdata import abdata, resolve_ab


def _extract_textasset_script(raw: bytes) -> bytes:
    """Unity serialized TextAsset → m_Script bytes."""
    name_len = struct.unpack_from("<i", raw, 0)[0]
    pos = 4 + name_len
    pos += (4 - (pos % 4)) % 4
    script_len = struct.unpack_from("<i", raw, pos)[0]
    pos += 4
    return raw[pos : pos + script_len]


def load_cha_list_from_textasset_object(obj) -> Dict[str, Any]:
    script = _extract_textasset_script(obj.get_raw_data())
    return msgpack.unpackb(script, raw=False, strict_map_key=False)


def load_category_list(list_asset_name: str, characustom_bundle: str = "00.unity3d") -> Dict[str, Any]:
    """Load e.g. list asset 'ft_skin_f_00' from list/characustom/00.unity3d."""
    import UnityPy

    bundle = abdata() / "list" / "characustom" / characustom_bundle
    env = UnityPy.load(str(bundle))
    for obj in env.objects:
        if obj.type.name != "TextAsset":
            continue
        name = obj.read().m_Name
        if name == list_asset_name:
            return load_cha_list_from_textasset_object(obj)
    raise FileNotFoundError(f"{list_asset_name} not in {bundle}")


def list_entry_by_id(cha_list: Dict[str, Any], entry_id: int) -> Optional[Dict[str, str]]:
    vals = cha_list.get("dictList", {}).get(entry_id)
    if vals is None:
        # msgpack may use str keys
        vals = cha_list.get("dictList", {}).get(str(entry_id))
    if vals is None:
        return None
    keys = cha_list["lstKey"]
    return {k: (vals[i] if i < len(vals) else "") for i, k in enumerate(keys)}


def resolve_face_skin(skin_id: int, head_id: int = 0) -> Dict[str, str]:
    """Follow ChaControl.CreateFaceTexture skin lookup (ft_skin_f).

    Prefer exact skin_id; if HeadID on entry mismatches card headId, still use
    the skin_id hit (card authoritatively chose that skin).
    """
    hits: List[Dict[str, str]] = []
    for bundle_name in sorted((abdata() / "list" / "characustom").glob("*.unity3d")):
        if bundle_name.name == "namelist.unity3d":
            continue
        import UnityPy

        env = UnityPy.load(str(bundle_name))
        for obj in env.objects:
            if obj.type.name != "TextAsset":
                continue
            name = obj.read().m_Name
            if not name.startswith("ft_skin_f_"):
                continue
            data = load_cha_list_from_textasset_object(obj)
            entry = list_entry_by_id(data, int(skin_id))
            if entry is not None:
                hits.append(entry)
    if hits:
        # Prefer HeadID match when available
        for e in hits:
            try:
                if int(e.get("HeadID", -1)) == int(head_id):
                    return e
            except ValueError:
                pass
        return hits[0]
    data = load_category_list("ft_skin_f_00")
    entry = list_entry_by_id(data, 0)
    if not entry:
        raise RuntimeError("No ft_skin_f entry found")
    return entry
