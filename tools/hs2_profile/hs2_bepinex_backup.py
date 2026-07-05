# -*- coding: utf-8 -*-
"""
HS2 BepInEx 設定一鍵備份 / 還原（比照 hs2_photo_to_card_config.py 的 timestamp 備份思路）。

子命令：
  backup   將 BepInEx\\config\\*.cfg 複製到 config\\_hs4_backups\\<timestamp>\\
  restore  從最新一份備份還原全部 cfg（還原前會再備份現況）
  list     列出已有備份

範例：
  python tools/hs2_profile/hs2_bepinex_backup.py backup --hs2-root D:\\HS2
  python tools/hs2_profile/hs2_bepinex_backup.py restore --hs2-root D:\\HS2
  python tools/hs2_profile/hs2_bepinex_backup.py list --hs2-root D:\\HS2
"""
from __future__ import annotations

import argparse
import shutil
import sys
from datetime import datetime
from pathlib import Path
from typing import Iterable, Optional

BACKUP_DIR_NAME = "_hs4_backups"


def _timestamp() -> str:
    return datetime.now().strftime("%Y%m%d_%H%M%S")


def _config_dir(hs2_root: Path) -> Path:
    return hs2_root / "BepInEx" / "config"


def _backup_root(hs2_root: Path) -> Path:
    return _config_dir(hs2_root) / BACKUP_DIR_NAME


def _list_cfg_files(config_dir: Path) -> list[Path]:
    if not config_dir.is_dir():
        return []
    return sorted(p for p in config_dir.glob("*.cfg") if p.is_file())


def _list_snapshots(hs2_root: Path) -> list[Path]:
    root = _backup_root(hs2_root)
    if not root.is_dir():
        return []
    snaps = [p for p in root.iterdir() if p.is_dir()]
    snaps.sort(key=lambda p: p.name, reverse=True)
    return snaps


def _copy_cfgs(src_dir: Path, dst_dir: Path, files: Iterable[Path]) -> int:
    dst_dir.mkdir(parents=True, exist_ok=True)
    count = 0
    for src in files:
        shutil.copy2(src, dst_dir / src.name)
        count += 1
    return count


def cmd_backup(hs2_root: Path, label: Optional[str]) -> None:
    config_dir = _config_dir(hs2_root)
    if not config_dir.is_dir():
        print("Config dir not found:", config_dir, file=sys.stderr)
        sys.exit(1)

    cfgs = _list_cfg_files(config_dir)
    if not cfgs:
        print("No *.cfg files to backup in", config_dir)
        return

    snap_name = _timestamp() + (f"_{label}" if label else "")
    snap_dir = _backup_root(hs2_root) / snap_name
    n = _copy_cfgs(config_dir, snap_dir, cfgs)
    print(f"Backed up {n} cfg file(s) to {snap_dir}")


def cmd_restore(hs2_root: Path, snapshot: Optional[str]) -> None:
    config_dir = _config_dir(hs2_root)
    snaps = _list_snapshots(hs2_root)
    if not snaps:
        print("No backups found under", _backup_root(hs2_root), file=sys.stderr)
        sys.exit(1)

    if snapshot:
        snap_dir = _backup_root(hs2_root) / snapshot
        if not snap_dir.is_dir():
            print("Snapshot not found:", snap_dir, file=sys.stderr)
            sys.exit(1)
    else:
        snap_dir = snaps[0]

    cfgs = sorted(p for p in snap_dir.glob("*.cfg") if p.is_file())
    if not cfgs:
        print("No *.cfg in snapshot:", snap_dir, file=sys.stderr)
        sys.exit(1)

    existing = _list_cfg_files(config_dir)
    if existing:
        pre_dir = _backup_root(hs2_root) / (_timestamp() + "_pre_restore")
        _copy_cfgs(config_dir, pre_dir, existing)
        print("Pre-restore backup:", pre_dir)

    config_dir.mkdir(parents=True, exist_ok=True)
    for src in cfgs:
        dst = config_dir / src.name
        shutil.copy2(src, dst)
        print("Restored", dst.name, "<-", snap_dir.name)

    print("Done. Restored from", snap_dir)


def cmd_list(hs2_root: Path) -> None:
    snaps = _list_snapshots(hs2_root)
    if not snaps:
        print("No backups under", _backup_root(hs2_root))
        return
    for snap in snaps:
        n = len(list(snap.glob("*.cfg")))
        print(f"{snap.name}  ({n} cfg)")


def main() -> None:
    ap = argparse.ArgumentParser(description="Backup/restore HS2 BepInEx config (*.cfg).")
    sub = ap.add_subparsers(dest="command", required=True)

    bak = sub.add_parser("backup", help="Snapshot all BepInEx/config/*.cfg")
    bak.add_argument("--hs2-root", type=Path, required=True, help="HS2 game root (e.g. D:\\HS2)")
    bak.add_argument("--label", type=str, default=None, help="Optional suffix on snapshot folder name")

    rest = sub.add_parser("restore", help="Restore cfg from latest or named snapshot")
    rest.add_argument("--hs2-root", type=Path, required=True)
    rest.add_argument("--snapshot", type=str, default=None, help="Snapshot folder name (default: latest)")

    lst = sub.add_parser("list", help="List available snapshots")
    lst.add_argument("--hs2-root", type=Path, required=True)

    args = ap.parse_args()
    if args.command == "backup":
        cmd_backup(args.hs2_root, args.label)
    elif args.command == "restore":
        cmd_restore(args.hs2_root, args.snapshot)
    elif args.command == "list":
        cmd_list(args.hs2_root)


if __name__ == "__main__":
    main()
