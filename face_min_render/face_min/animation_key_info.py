# -*- coding: utf-8 -*-
"""AnimationKeyInfo / ShapeAnime: rate → (pos, rot, scl).

Binary layout matches HS2 AnimationKeyInfo.LoadInfo(Stream) (C# BinaryReader):
  int32 N
  repeat N:
    string name (C# BinaryWriter.Write(string): 7-bit-encoded length prefix + UTF-8 bytes,
                 NOT a raw int32 length — verified against real cf_anmShapeHead_XX assets)
    int32 K
    repeat K: int32 no, float pos[3], rot[3], scl[3]
"""
from __future__ import annotations

import json
import struct
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Tuple

import numpy as np


def _lerp_angle_deg(a: float, b: float, t: float) -> float:
    """Match Unity Mathf.LerpAngle: shortest-path circular interpolation in degrees.

    Real HS2 rotation curves are stored as raw 0..360 degree values that wrap
    (e.g. ...358.33, 0.0, 1.67...). A plain lerp between 0.0 and 358.33 gives
    ~179 (a bogus half-turn) instead of the intended ~-1 (359). This bit every
    eye/chin rotation category once real (non-demo) curves were wired in."""
    diff = (b - a + 180.0) % 360.0 - 180.0
    return a + diff * t


@dataclass
class KeySample:
    no: int
    pos: np.ndarray
    rot: np.ndarray
    scl: np.ndarray


@dataclass
class AnimationKeyInfo:
    curves: Dict[str, List[KeySample]] = field(default_factory=dict)

    def get_prs(self, name: str, rate: float) -> Tuple[np.ndarray, np.ndarray, np.ndarray]:
        keys = self.curves.get(name)
        if not keys:
            return np.zeros(3), np.zeros(3), np.ones(3)
        rate = float(np.clip(rate, 0.0, 1.0))
        if len(keys) == 1:
            k = keys[0]
            return k.pos.copy(), k.rot.copy(), k.scl.copy()
        idx = (len(keys) - 1) * rate
        i0 = int(np.floor(idx))
        i1 = min(i0 + 1, len(keys) - 1)
        t = idx - i0
        a, b = keys[i0], keys[i1]
        pos = (1 - t) * a.pos + t * b.pos
        rot = np.array(
            [_lerp_angle_deg(float(a.rot[i]), float(b.rot[i]), t) for i in range(3)],
            dtype=np.float64,
        )
        scl = (1 - t) * a.scl + t * b.scl
        return pos, rot, scl

    def to_json_dict(self) -> dict:
        out = {}
        for name, keys in self.curves.items():
            out[name] = [
                {
                    "no": k.no,
                    "pos": k.pos.tolist(),
                    "rot": k.rot.tolist(),
                    "scl": k.scl.tolist(),
                }
                for k in keys
            ]
        return out

    @classmethod
    def from_json_dict(cls, data: dict) -> "AnimationKeyInfo":
        curves: Dict[str, List[KeySample]] = {}
        for name, keys in data.items():
            curves[name] = [
                KeySample(
                    no=int(k["no"]),
                    pos=np.asarray(k["pos"], dtype=np.float64),
                    rot=np.asarray(k["rot"], dtype=np.float64),
                    scl=np.asarray(k["scl"], dtype=np.float64),
                )
                for k in keys
            ]
        return cls(curves=curves)

    def save_json(self, path: str | Path) -> None:
        Path(path).write_text(json.dumps(self.to_json_dict(), indent=2), encoding="utf-8")

    @classmethod
    def load_json(cls, path: str | Path) -> "AnimationKeyInfo":
        return cls.from_json_dict(json.loads(Path(path).read_text(encoding="utf-8")))

    @classmethod
    def load_binary(cls, path: str | Path) -> "AnimationKeyInfo":
        """Load game ShapeAnime TextAsset.bytes (e.g. cf_anmShapeHead_XX)."""
        return cls.from_bytes(Path(path).read_bytes())

    @classmethod
    def from_bytes(cls, data: bytes) -> "AnimationKeyInfo":
        offset = 0

        def read_i32() -> int:
            nonlocal offset
            (v,) = struct.unpack_from("<i", data, offset)
            offset += 4
            return v

        def read_f32() -> float:
            nonlocal offset
            (v,) = struct.unpack_from("<f", data, offset)
            offset += 4
            return v

        def read_vec3() -> np.ndarray:
            return np.array([read_f32(), read_f32(), read_f32()], dtype=np.float64)

        def read_7bit_len() -> int:
            """C# BinaryWriter.Write(string) length prefix (7-bit encoded, NOT int32)."""
            nonlocal offset
            result = 0
            shift = 0
            while True:
                b = data[offset]
                offset += 1
                result |= (b & 0x7F) << shift
                shift += 7
                if not (b & 0x80):
                    break
            return result

        def read_string() -> str:
            nonlocal offset
            n = read_7bit_len()
            s = data[offset : offset + n].decode("utf-8", errors="replace")
            offset += n
            return s

        n_names = read_i32()
        curves: Dict[str, List[KeySample]] = {}
        for _ in range(n_names):
            name = read_string()
            k = read_i32()
            keys: List[KeySample] = []
            for _j in range(k):
                no = read_i32()
                pos = read_vec3()
                rot = read_vec3()
                scl = read_vec3()
                keys.append(KeySample(no=no, pos=pos, rot=rot, scl=scl))
            curves[name] = keys
        if offset != len(data):
            raise ValueError(f"AnimationKeyInfo parse left {len(data) - offset} trailing bytes")
        return cls(curves=curves)
