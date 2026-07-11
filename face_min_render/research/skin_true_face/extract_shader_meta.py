"""Extract AIT/Skin True Face CB/texture name bindings from fo_head_00."""
from __future__ import annotations

import json
from pathlib import Path

import UnityPy
from UnityPy.export.ShaderConverter import ShaderProgram
from UnityPy.helpers import CompressionHelper
from UnityPy.streams import EndianBinaryReader

ROOT = Path(__file__).resolve().parent
BUNDLE = Path(r"D:\HS2 - Copy\abdata\chara\00\fo_head_00.unity3d")


def get_entry(arr, i):
    e = arr[i]
    return e[0] if isinstance(e, (list, tuple)) else e


def main() -> None:
    env = UnityPy.load(str(BUNDLE))
    target = None
    for obj in env.objects:
        if obj.type.name != "Shader":
            continue
        d = obj.read()
        pf = getattr(d, "m_ParsedForm", None)
        if pf and pf.m_Name == "AIT/Skin True Face":
            target = (obj, d)
            break
    assert target
    obj, d = target

    tree = obj.read_typetree()
    fp = tree["m_ParsedForm"]["m_SubShaders"][0]["m_Passes"][0]["progFragment"][
        "m_SubPrograms"
    ][0]
    lines = []
    lines.append(f"blobIndex={fp.get('m_BlobIndex')}")
    lines.append("--- CB0 vectors ---")
    for v in fp["m_ConstantBuffers"][0]["m_VectorParams"]:
        lines.append(
            f"  {v.get('m_Name')!r:30} Index={v.get('m_Index'):4} "
            f"Dim={v.get('m_Dim')}  => cb0[{v.get('m_Index')//16}]"
        )
    lines.append("--- textures ---")
    for t in fp["m_TextureParams"]:
        lines.append(
            f"  {t.get('m_Name')!r:30} t{t.get('m_Index')} "
            f"sampler={t.get('m_SamplerIndex')}"
        )

    text = "\n".join(lines)
    (ROOT / "cb_bindings_pass0_fp0.txt").write_text(text, encoding="utf-8")
    print(text)

    # also dump DXBC for blob
    compressed = bytes(d.compressedBlob)
    decompressed = CompressionHelper.decompress_lz4(
        compressed[
            get_entry(d.offsets, 0) : get_entry(d.offsets, 0)
            + get_entry(d.compressedLengths, 0)
        ],
        get_entry(d.decompressedLengths, 0),
    )
    prog = ShaderProgram(
        EndianBinaryReader(decompressed, endian="<"), obj.assets_file.version
    )
    bi = fp["m_BlobIndex"]
    code = bytes(prog.m_SubPrograms[bi].m_ProgramCode)
    dxbc = code[code.find(b"DXBC") :]
    (ROOT / "dxbc" / f"pass0_fp_{bi}.dxbc").write_bytes(dxbc)

    # Save typetree fragment for Texture3-related props
    props = []
    for p in tree["m_ParsedForm"]["m_PropInfo"]["m_Props"]:
        n = p.get("m_Name") or ""
        if any(x in n for x in ("Texture3", "Color3", "Texture2", "Color2", "Texture5")):
            props.append(p)
    (ROOT / "props_texture3.json").write_text(
        json.dumps(props, indent=2, default=str), encoding="utf-8"
    )


if __name__ == "__main__":
    main()
