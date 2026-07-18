# -*- coding: utf-8 -*-
"""Offline: bake HS2OrbitAndExciter map_vanish caches for all maps.

Sources (no game running):
  1. Vanilla ExcelData in abdata/list/map/*.unity3d
  2. Sideloader zipmod abdata/list/map/{id}/map_col_*.csv
  3. Optional: map scene Colliders (fill gaps Excel/CSV missed)

Writes:
  BepInEx/config/HS2OrbitAndExciter/map_vanish/map_{id}.json  (cache v2)

Usage:
  python tools/bake_map_vanish_caches.py
  python tools/bake_map_vanish_caches.py --hs2 D:\\HS2 --no-scene
"""
from __future__ import annotations

import argparse
import csv
import io
import json
import re
import sys
import zipfile
from collections import defaultdict
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Set, Tuple

CACHE_VERSION = 3
COL_TYPES = (
    "BoxCollider",
    "MeshCollider",
    "CapsuleCollider",
    "SphereCollider",
    "TerrainCollider",
)


def decode_bytes(b: bytes) -> str:
    for enc in ("utf-8-sig", "utf-8", "cp932", "utf-16"):
        try:
            return b.decode(enc)
        except UnicodeDecodeError:
            continue
    return b.decode("utf-8", errors="replace")


def row_list(row) -> List[str]:
    if row is None:
        return []
    if hasattr(row, "list"):
        return [str(x) for x in (row.list or [])]
    if isinstance(row, (list, tuple)):
        return [str(x) for x in row]
    return []


def merge_entry(dst: Dict[str, List[str]], collider: str, objects: Iterable[str]) -> None:
    collider = (collider or "").strip()
    if not collider:
        return
    bucket = dst.setdefault(collider, [])
    seen = set(bucket)
    for o in objects:
        name = (o or "").strip()
        if not name or name in seen:
            continue
        bucket.append(name)
        seen.add(name)
    if not bucket:
        # at least hide something with same stem
        stem = re.sub(r"_col$", "", collider, flags=re.I)
        bucket.append(stem if stem else collider)


def parse_excel_bundle(path: Path) -> Dict[int, Dict[str, List[str]]]:
    """mapId -> {collider: [objects...]} from one list/map/*.unity3d."""
    import UnityPy

    env = UnityPy.load(str(path))
    sheets: Dict[str, List[List[str]]] = {}
    name_rows: List[List[str]] = []

    for o in env.objects:
        if o.type.name != "MonoBehaviour":
            continue
        d = o.read()
        name = getattr(d, "m_Name", "") or ""
        lst = getattr(d, "list", None)
        if lst is None:
            continue
        rows = [row_list(r) for r in lst]
        if name == "map_col_name":
            name_rows = rows
        elif name.startswith("map_col_"):
            sheets[name] = rows

    # map_col_name: [sheetName, mapId, displayName...]
    id_to_sheet: Dict[int, str] = {}
    for row in name_rows:
        if len(row) < 2:
            continue
        sheet, mid = row[0].strip(), row[1].strip()
        if not sheet or not mid.isdigit():
            continue
        id_to_sheet[int(mid)] = sheet

    out: Dict[int, Dict[str, List[str]]] = {}
    for mid, sheet in id_to_sheet.items():
        rows = sheets.get(sheet)
        if not rows:
            # sheet asset may be named exactly sheet
            continue
        entries: Dict[str, List[str]] = {}
        # skip header rows 0..1 like runtime (num2 starts at 2)
        for row in rows[2:]:
            if not row or not row[0].strip():
                continue
            merge_entry(entries, row[0], row[1:])
        out[mid] = entries
    return out


def parse_map_col_csv(text: str) -> Dict[str, List[str]]:
    entries: Dict[str, List[str]] = {}
    # strip BOM already via decode
    reader = csv.reader(io.StringIO(text))
    rows = [r for r in reader]
    # same as Excel: skip first two rows when present
    start = 0
    if rows and rows[0] and rows[0][0].startswith("map_col"):
        start = 2 if len(rows) >= 2 else 1
    for row in rows[start:]:
        if not row or not str(row[0]).strip():
            continue
        # skip header-ish
        if "判定" in row[0] or "フレーム" in "".join(row):
            continue
        merge_entry(entries, row[0], row[1:])
    return entries


def parse_map_col_name_csv(text: str) -> List[Tuple[str, int]]:
    """Return [(sheet_or_file_stem, mapId), ...]."""
    out: List[Tuple[str, int]] = []
    reader = csv.reader(io.StringIO(text))
    for row in reader:
        if len(row) < 2:
            continue
        sheet, mid = row[0].strip(), row[1].strip()
        if not mid.isdigit():
            continue
        if sheet.startswith("map_col") or mid:
            out.append((sheet, int(mid)))
    return out


def load_vanilla_excel(abdata: Path) -> Dict[int, Dict[str, List[str]]]:
    merged: Dict[int, Dict[str, List[str]]] = {}
    folder = abdata / "list" / "map"
    if not folder.is_dir():
        return merged
    for path in sorted(folder.glob("*.unity3d")):
        try:
            part = parse_excel_bundle(path)
        except Exception as e:
            print(f"[warn] excel {path.name}: {e}", file=sys.stderr)
            continue
        for mid, entries in part.items():
            bucket = merged.setdefault(mid, {})
            for col, objs in entries.items():
                merge_entry(bucket, col, objs)
        print(f"[excel] {path.name}: maps {sorted(part.keys())}")
    return merged


def iter_map_zipmods(mods_root: Path) -> Iterable[Path]:
    # Prefer map packs; also scan MyMods / Exclusive; skip huge unrelated packs by name.
    prefer = [
        mods_root / "Sideloader Modpack - Maps (HS2 Game)",
        mods_root / "Sideloader Modpack - Exclusive HS2",
        mods_root / "MyMods",
        mods_root / "Sideloader Modpack",
    ]
    seen: Set[Path] = set()
    for root in prefer:
        if not root.is_dir():
            continue
        for z in root.rglob("*.zipmod"):
            rp = z.resolve()
            if rp in seen:
                continue
            seen.add(rp)
            yield z


def zip_has_map_col(zf: zipfile.ZipFile) -> bool:
    for n in zf.namelist():
        ln = n.lower().replace("\\", "/")
        if "list/map" in ln and "map_col" in ln and ln.endswith(".csv"):
            return True
    return False


def _zipmod_map_ids_and_csv(
    zf: zipfile.ZipFile,
) -> Tuple[Dict[int, Dict[str, List[str]]], Set[int]]:
    """Parse map_col CSVs inside one zip; return (entries_by_map, all_map_ids_seen)."""
    merged: Dict[int, Dict[str, List[str]]] = {}
    ids: Set[int] = set()
    names = {n.replace("\\", "/"): n for n in zf.namelist()}
    by_dir: Dict[str, Dict[str, str]] = defaultdict(dict)
    for norm, orig in names.items():
        ln = norm.lower()
        if "list/map/" not in ln or not ln.endswith(".csv"):
            continue
        if "map_col" not in ln:
            continue
        parent = norm.rsplit("/", 1)[0]
        base = norm.rsplit("/", 1)[-1]
        by_dir[parent][base.lower()] = orig

    for parent, files in by_dir.items():
        name_csv = files.get("map_col_name.csv")
        id_sheet: List[Tuple[str, int]] = []
        if name_csv:
            id_sheet = parse_map_col_name_csv(decode_bytes(zf.read(name_csv)))
        m = re.search(r"/list/map/(\d+)$", parent.replace("\\", "/"), re.I)
        folder_id = int(m.group(1)) if m else None

        targets: List[Tuple[int, str]] = []
        if id_sheet:
            for sheet, mid in id_sheet:
                ids.add(mid)
                fname = sheet.lower() + ("" if sheet.lower().endswith(".csv") else ".csv")
                orig = files.get(fname) or files.get(f"map_col_{mid}.csv")
                if orig:
                    targets.append((mid, orig))
        elif folder_id is not None:
            ids.add(folder_id)
            for k, orig in files.items():
                if k == "map_col_name.csv":
                    continue
                if k.startswith("map_col_"):
                    targets.append((folder_id, orig))

        for mid, orig in targets:
            ids.add(mid)
            entries = parse_map_col_csv(decode_bytes(zf.read(orig)))
            bucket = merged.setdefault(mid, {})
            for col, objs in entries.items():
                merge_entry(bucket, col, objs)
    return merged, ids


def _zipmod_scene_paths(zf: zipfile.ZipFile) -> List[str]:
    """Candidate map scene bundles inside zip (skip thumbs)."""
    out: List[str] = []
    for n in zf.namelist():
        ln = n.replace("\\", "/").lower()
        if not ln.endswith(".unity3d"):
            continue
        if "thumb" in ln:
            continue
        # common layouts
        if "/maps/" in ln or ln.endswith("_map.unity3d") or "/map_" in ln:
            if "mapinfo" in ln:
                continue
            out.append(n.replace("\\", "/"))
            continue
        # author folders e.g. abdata/kky/*.unity3d (not thumbs)
        if ln.startswith("abdata/") and ln.count("/") >= 2:
            leaf = ln.rsplit("/", 1)[-1]
            if leaf.startswith("map_") or "map" in leaf:
                if "list/" in ln or "thumbnail" in ln or "eventcg" in ln:
                    continue
                if "mapinfo" in ln:
                    continue
                out.append(n.replace("\\", "/"))
    # unique preserve order
    seen: Set[str] = set()
    uniq: List[str] = []
    for p in out:
        if p in seen:
            continue
        seen.add(p)
        uniq.append(p)
    return uniq


def load_zipmod_data(mods_root: Path, *, do_scene: bool) -> Dict[int, Dict[str, List[str]]]:
    """CSV vanish lists + optional Collider scan of zipmod scene bundles."""
    merged: Dict[int, Dict[str, List[str]]] = {}
    n_csv = 0
    for zpath in iter_map_zipmods(mods_root):
        try:
            zf = zipfile.ZipFile(zpath)
        except Exception:
            continue
        with zf:
            if not zip_has_map_col(zf) and not any(
                n.lower().endswith(".unity3d") and "map" in n.lower() for n in zf.namelist()
            ):
                continue

            csv_part, map_ids = _zipmod_map_ids_and_csv(zf)
            for mid, entries in csv_part.items():
                bucket = merged.setdefault(mid, {})
                for col, objs in entries.items():
                    merge_entry(bucket, col, objs)
                n_csv += 1

            if not do_scene:
                print(f"[zip] {zpath.name} csv_maps={sorted(map_ids) or '-'}")
                continue

            scenes = _zipmod_scene_paths(zf)
            if not scenes:
                print(f"[zip] {zpath.name} csv_maps={sorted(map_ids) or '-'} (no scene)")
                continue

            if not map_ids:
                print(f"[zip] {zpath.name}: scene-only, skip (no map id)")
                continue

            scene_entries: Dict[str, List[str]] = {}
            for sn in scenes:
                try:
                    data = zf.read(sn)
                    part = extract_scene_colliders_from_bytes(data, label=f"{zpath.name}:{Path(sn).name}")
                except Exception as e:
                    print(f"[warn] zip scene {zpath.name}/{sn}: {e}", file=sys.stderr)
                    continue
                for col, objs in part.items():
                    merge_entry(scene_entries, col, objs)

            for mid in map_ids:
                bucket = merged.setdefault(mid, {})
                before = len(bucket)
                for col, objs in scene_entries.items():
                    merge_entry(bucket, col, objs)
                print(
                    f"[zip] {zpath.name} map={mid} "
                    f"csv={len(csv_part.get(mid, {}))} "
                    f"scene+={len(bucket) - before} total={len(bucket)} "
                    f"scenes={len(scenes)}"
                )
    print(f"[zip] done → {len(merged)} maps ({n_csv} csv sheets)")
    return merged


def load_mapinfo(abdata: Path) -> Dict[int, Tuple[str, str]]:
    """mapId -> (AssetBundleName, AssetName)."""
    import UnityPy

    out: Dict[int, Tuple[str, str]] = {}
    folder = abdata / "map" / "list" / "mapinfo"
    if not folder.is_dir():
        return out
    for path in sorted(folder.rglob("*.unity3d")):
        try:
            env = UnityPy.load(str(path))
        except Exception as e:
            print(f"[warn] mapinfo {path}: {e}", file=sys.stderr)
            continue
        for o in env.objects:
            if o.type.name != "MonoBehaviour":
                continue
            d = o.read()
            param = getattr(d, "param", None)
            if not param:
                continue
            for row in param:
                mid = getattr(row, "No", None)
                ab = getattr(row, "AssetBundleName", None)
                an = getattr(row, "AssetName", None)
                if mid is None or not ab:
                    continue
                out[int(mid)] = (str(ab).replace("\\", "/"), str(an or ""))
    return out


def resolve_ab(hs2: Path, rel: str) -> Optional[Path]:
    rel = rel.replace("\\", "/").lstrip("/")
    if rel.startswith("abdata/"):
        rel = rel[len("abdata/") :]
    candidates = [hs2 / "abdata" / Path(*rel.split("/"))]
    # DLC addXX folders sometimes mirror paths
    abdata = hs2 / "abdata"
    if abdata.is_dir():
        for add in abdata.glob("add*"):
            if add.is_dir():
                candidates.append(add / Path(*rel.split("/")))
    for c in candidates:
        if c.is_file():
            return c
    return None


def looks_like_character_or_clothes(name: str) -> bool:
    n = (name or "").strip()
    if not n:
        return False
    low = n.lower()
    if low.startswith("ct_"):
        return True
    if "clothes" in low or "cloth" in low:
        return True
    # 角色骨骼／身體常見前綴（地圖傢俱很少用）
    if low.startswith("cf_o_") or low.startswith("cm_o_"):
        return True
    if low.startswith("cf_j_") or low.startswith("cm_j_"):
        return True
    return False


def extract_scene_colliders_from_bytes(data: bytes, label: str = "") -> Dict[str, List[str]]:
    """Collider names + hide targets (same GO / stem / sibling renderers under prop root)."""
    import UnityPy

    env = UnityPy.load(data)
    go_name: Dict[int, str] = {}
    go_components: Dict[int, List[int]] = {}
    comp_type: Dict[int, str] = {}
    # transform path_id -> (go_path_id, father_transform_path_id or 0)
    tf_go: Dict[int, int] = {}
    tf_father: Dict[int, int] = {}
    go_tf: Dict[int, int] = {}

    for o in env.objects:
        try:
            tname = o.type.name
            if tname == "GameObject":
                d = o.read()
                go_name[o.path_id] = d.m_Name
                comps = []
                for c in getattr(d, "m_Component", []) or []:
                    ptr = getattr(c, "component", c)
                    pid = getattr(ptr, "path_id", None)
                    if pid is not None:
                        comps.append(int(pid))
                go_components[o.path_id] = comps
            elif tname == "Transform":
                d = o.read()
                go_ptr = getattr(d, "m_GameObject", None)
                gid = int(getattr(go_ptr, "path_id", 0) or 0) if go_ptr is not None else 0
                father = getattr(d, "m_Father", None)
                fid = int(getattr(father, "path_id", 0) or 0) if father is not None else 0
                tf_go[o.path_id] = gid
                tf_father[o.path_id] = fid
                if gid:
                    go_tf[gid] = o.path_id
            elif tname in COL_TYPES:
                comp_type[o.path_id] = tname
            elif tname in ("MeshRenderer", "SkinnedMeshRenderer"):
                comp_type[o.path_id] = tname
        except Exception:
            continue

    comp_to_go: Dict[int, int] = {}
    renderer_gos: Set[int] = set()
    for gid, comps in go_components.items():
        for cid in comps:
            comp_to_go[cid] = gid
            if comp_type.get(cid) in ("MeshRenderer", "SkinnedMeshRenderer"):
                renderer_gos.add(gid)

    # children: father_tf -> [child_tf]
    children: Dict[int, List[int]] = defaultdict(list)
    for tid, fid in tf_father.items():
        if fid:
            children[fid].append(tid)

    def collect_descendant_gos(root_tf: int) -> List[int]:
        out: List[int] = []
        stack = [root_tf]
        seen: Set[int] = set()
        while stack:
            tid = stack.pop()
            if tid in seen:
                continue
            seen.add(tid)
            gid = tf_go.get(tid, 0)
            if gid:
                out.append(gid)
            for ch in children.get(tid, []):
                stack.append(ch)
        return out

    def prop_root_tf(tid: int) -> int:
        """Walk up a few levels; stop before a very wide parent (scene container)."""
        cur = tid
        for depth in range(8):
            fid = tf_father.get(cur, 0)
            if not fid:
                break
            sibs = children.get(fid, [])
            if len(sibs) > 24 and depth >= 1:
                break
            cur = fid
        return cur

    entries: Dict[str, List[str]] = {}
    for cid, ctype in comp_type.items():
        if ctype not in COL_TYPES:
            continue
        gid = comp_to_go.get(cid)
        if gid is None:
            continue
        cname = go_name.get(gid, "")
        if not cname or looks_like_character_or_clothes(cname):
            continue

        hide: List[str] = []
        stem = re.sub(r"_col$", "", cname, flags=re.I)
        if stem and stem != cname and not looks_like_character_or_clothes(stem):
            hide.append(stem)
        if gid in renderer_gos:
            hide.append(cname)

        tid = go_tf.get(gid)
        if tid:
            root = prop_root_tf(tid)
            for og in collect_descendant_gos(root):
                oname = go_name.get(og, "")
                if not oname or looks_like_character_or_clothes(oname):
                    continue
                if og in renderer_gos and oname not in hide:
                    hide.append(oname)

        merge_entry(entries, cname, hide)

    if label:
        n_hide = sum(len(v) for v in entries.values())
        print(f"  [scene-bytes] {label}: {len(entries)} colliders, {n_hide} hide-names")
    return entries


def extract_scene_colliders(scene_path: Path) -> Dict[str, List[str]]:
    return extract_scene_colliders_from_bytes(scene_path.read_bytes(), label=scene_path.name)


def supplement_from_scenes(
    hs2: Path,
    maps: Dict[int, Dict[str, List[str]]],
    mapinfo: Dict[int, Tuple[str, str]],
) -> None:
    for mid, (ab, _an) in sorted(mapinfo.items()):
        path = resolve_ab(hs2, ab)
        if path is None:
            continue
        try:
            scene_entries = extract_scene_colliders(path)
        except Exception as e:
            print(f"[warn] scene {mid} {ab}: {e}", file=sys.stderr)
            continue
        bucket = maps.setdefault(mid, {})
        before = len(bucket)
        for col, objs in scene_entries.items():
            merge_entry(bucket, col, objs)
        added = len(bucket) - before
        if added:
            print(f"[scene] map {mid}: +{added} colliders from {path.name}")


def write_caches(out_dir: Path, maps: Dict[int, Dict[str, List[str]]]) -> None:
    out_dir.mkdir(parents=True, exist_ok=True)
    for mid, entries in sorted(maps.items()):
        payload = {
            "mapId": mid,
            "version": CACHE_VERSION,
            "entries": [
                {"collider": col, "objects": objs}
                for col, objs in sorted(entries.items(), key=lambda x: x[0].lower())
            ],
        }
        path = out_dir / f"map_{mid}.json"
        path.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"[write] {len(maps)} files → {out_dir}")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--hs2", default=r"D:\HS2", help="HS2 game root")
    ap.add_argument(
        "--out",
        default=None,
        help="Output dir (default: <hs2>/BepInEx/config/HS2OrbitAndExciter/map_vanish)",
    )
    ap.add_argument("--no-scene", action="store_true", help="Skip scene collider scan")
    ap.add_argument("--no-zip", action="store_true", help="Skip zipmod CSV")
    args = ap.parse_args()

    hs2 = Path(args.hs2)
    abdata = hs2 / "abdata"
    if not abdata.is_dir():
        print(f"abdata missing: {abdata}", file=sys.stderr)
        return 1

    out = Path(args.out) if args.out else (
        hs2 / "BepInEx" / "config" / "HS2OrbitAndExciter" / "map_vanish"
    )

    maps: Dict[int, Dict[str, List[str]]] = {}

    vanilla = load_vanilla_excel(abdata)
    for mid, entries in vanilla.items():
        bucket = maps.setdefault(mid, {})
        for col, objs in entries.items():
            merge_entry(bucket, col, objs)

    if not args.no_zip:
        mods = hs2 / "mods"
        if mods.is_dir():
            z = load_zipmod_data(mods, do_scene=not args.no_scene)
            for mid, entries in z.items():
                bucket = maps.setdefault(mid, {})
                for col, objs in entries.items():
                    merge_entry(bucket, col, objs)

    if not args.no_scene:
        info = load_mapinfo(abdata)
        print(f"[mapinfo] {len(info)} vanilla maps")
        supplement_from_scenes(hs2, maps, info)

    write_caches(out, maps)

    # summary
    empty = [mid for mid, e in maps.items() if not e]
    print(f"[summary] maps={len(maps)} empty={len(empty)}")
    if empty:
        print(f"  empty ids: {sorted(empty)}")
    for mid in (1, 10, 12, 692101, 692111, 692115, 692121, 282802):
        e = maps.get(mid)
        if e is None:
            print(f"  map {mid}: (no data)")
        else:
            hides = sum(len(v) for v in e.values())
            print(f"  map {mid}: {len(e)} colliders, {hides} hide-names")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
