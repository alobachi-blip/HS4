# -*- coding: utf-8 -*-
"""AnimationKeyInfo / ShapeAnime: rate → (pos, rot, scl).

Binary layout matches HS2 AnimationKeyInfo.LoadInfo(Stream):
  int32 N
  repeat N:
    string name (Unity length-prefixed UTF-8: int32 len + bytes)
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
        rot = (1 - t) * a.rot + t * b.rot  # game uses LerpAngle; fine for small demo ranges
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
        """Load game ShapeAnime TextAsset.bytes."""
        data = Path(path).read_bytes()
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

        def read_string() -> str:
            nonlocal offset
            n = read_i32()
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
        return cls(curves=curves)
