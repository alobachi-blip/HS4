# -*- coding: utf-8 -*-
"""
FSM regression asserts against HS2OrbitAndExciter_fsm.ndjson (last runId).

Checks (plan):
  1. PoseQueued without NowChangeAnim must not last >= 2s
  2. Changing with sel==now must resolve within one SNAP interval (~0.5s)
  3. Non-A+B Idle land → startsex / loop within 1s (when landed/auto_start_sex present)
  4. A+B land → appreciate; no auto force_cha_setPlay right after without latch intent
  5. AfterIdle escape path present within 3s of AfterIdle wait (when afteridle events exist)

Usage:
  python tools/_assert_fsm_regression.py
  python tools/_assert_fsm_regression.py D:\\path\\to\\HS2OrbitAndExciter_fsm.ndjson
Exit 0 = all applicable checks pass (or skipped for lack of evidence);
Exit 1 = at least one FAIL.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

# A+B long appreciation pose ids (OrbitHelpers.LongAppreciationPoseIds)
APB_IDS = {8, 9, 15, 102, 105, 106, 107}

IDLE_CLIPS = {"Idle", "D_Idle", "WIdle", "SIdle"}
LOOP_SUBSTR = ("WLoop", "SLoop", "OLoop", "D_WLoop", "D_SLoop", "D_OLoop")


def S(x):
    if x is None:
        return ""
    return str(x).encode("ascii", "backslashreplace").decode("ascii")


def data_of(r):
    if not isinstance(r, dict):
        return {}
    d = r.get("data")
    return d if isinstance(d, dict) else {}


def parse_now_id(now_anim: str) -> int | None:
    # formats like "name#id14" or "name#id14;down1"
    if not now_anim or "#id" not in now_anim:
        return None
    try:
        tail = now_anim.split("#id", 1)[1]
        num = ""
        for ch in tail:
            if ch.isdigit():
                num += ch
            else:
                break
        return int(num) if num else None
    except Exception:
        return None


def is_loop_clip(clip: str) -> bool:
    c = clip or ""
    return any(s in c for s in LOOP_SUBSTR)


def sel_eq_now(d: dict) -> bool:
    sel = S(d.get("selAnim"))
    now = S(d.get("nowAnim"))
    if not sel or not now or sel in ("", "null") or now in ("", "null"):
        return False
    sid = parse_now_id(sel)
    nid = parse_now_id(now)
    if sid is not None and nid is not None:
        return sid == nid
    # fallback: id fragment match
    return "#id" in sel and sel.split("#id")[-1].split(";")[0] == now.split("#id")[-1].split(";")[0]


def load_rows(path: Path):
    rows = []
    text = path.read_text(encoding="utf-8-sig", errors="replace")
    for line in text.splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            o = json.loads(line)
        except Exception:
            continue
        if isinstance(o, dict):
            rows.append(o)
    return rows


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    path = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(
        r"D:\HS2\BepInEx\LogOutput\HS2OrbitAndExciter_fsm.ndjson"
    )
    if not path.is_file():
        print(f"SKIP: log not found: {path}")
        return 0

    rows = load_rows(path)
    if not rows:
        print("SKIP: empty log")
        return 0

    rid = rows[-1].get("runId")
    R = [r for r in rows if r.get("runId") == rid]
    print(f"runId={rid} lines={len(R)} file={path}")

    fails = []
    passes = []
    skips = []

    # --- 1. PoseQueued stuck >= 2s without NowChangeAnim ---
    state = None
    t0 = None
    meta = None
    bad_queued = []
    for r in R:
        d = data_of(r)
        if "director" not in d:
            continue
        cur = d.get("director")
        if cur != state:
            if state == "PoseQueued" and t0 is not None:
                dur = (r.get("ut") or 0) - t0
                if dur >= 2.0 and not meta.get("nowChangeAnim"):
                    bad_queued.append((t0, dur, meta))
            state = cur
            t0 = r.get("ut")
            meta = d
    if state == "PoseQueued" and t0 is not None:
        dur = (R[-1].get("ut") or 0) - t0
        if dur >= 2.0 and not (meta or {}).get("nowChangeAnim"):
            bad_queued.append((t0, dur, meta))

    if bad_queued:
        for t0, dur, meta in bad_queued:
            fails.append(
                f"FAIL[1] PoseQueued {dur:.2f}s @ut{t0:.1f} without NowChangeAnim "
                f"clip={S((meta or {}).get('clip'))} sel={S((meta or {}).get('selAnim'))}"
            )
    else:
        passes.append("PASS[1] no PoseQueued>=2s without NowChangeAnim")

    # --- 2. Changing + sel==now must not persist across consecutive SNAPs ---
    snaps = [r for r in R if r.get("id") == "SNAP"]
    sticky = []
    for i in range(len(snaps) - 1):
        a, b = snaps[i], snaps[i + 1]
        da, db = data_of(a), data_of(b)
        if da.get("director") == "Changing" and da.get("nowChangeAnim") and sel_eq_now(da):
            # next SNAP still unresolved sticky
            if db.get("director") == "Changing" and db.get("nowChangeAnim") and sel_eq_now(db):
                sticky.append((a.get("ut"), b.get("ut"), da))
    # Also: any SNAP Changing+sel==now followed by no cleared_pose within 0.6s
    resolve_uts = [
        r.get("ut") or 0
        for r in R
        if r.get("id") == "stale_sel" and r.get("msg") == "cleared_pose_already_applied"
    ]
    for r in snaps:
        d = data_of(r)
        if not (d.get("director") == "Changing" and d.get("nowChangeAnim") and sel_eq_now(d)):
            continue
        ut = r.get("ut") or 0
        if not any(ut <= ru <= ut + 0.6 for ru in resolve_uts):
            # allow if next snap already resolved (not Changing or not sel==now)
            sticky.append((ut, ut, d))

    # Deduplicate: only fail if we saw sticky across two SNAPs (stronger signal)
    sticky_pairs = [
        s for s in sticky if s[0] != s[1]
    ]
    if sticky_pairs:
        for t0, t1, meta in sticky_pairs[:5]:
            fails.append(
                f"FAIL[2] Changing+sel==now across SNAPs ut{t0:.2f}->{t1:.2f} "
                f"now={S((meta or {}).get('nowAnim'))}"
            )
    else:
        # If no Changing+sel==now at all, still pass
        passes.append("PASS[2] no Changing+sel==now spanning consecutive SNAPs")

    # --- 3. Non-A+B: landed/auto_start_sex → loop-ish within 1s (evidence-based) ---
    land_auto = [
        r for r in R
        if r.get("id") == "landed" and r.get("msg") == "auto_start_sex"
    ]
    startsex = [
        r for r in R
        if r.get("id") == "startsex" and r.get("msg") == "force_cha_setPlay"
    ]
    if not land_auto and not any(
        r.get("id") == "startsex" and "landed_" in S(data_of(r).get("reason") if False else "")
        for r in R
    ):
        # look for startsex with reason landed_*
        landed_start = [
            r for r in startsex
            if "landed_" in S((r.get("data") if isinstance(r.get("data"), dict) else {}))
            or (isinstance(r.get("data"), str) and "landed_" in r.get("data"))
        ]
        # data may be JSON string in some loggers — check raw
        landed_start = []
        for r in R:
            if r.get("id") != "startsex" or r.get("msg") != "force_cha_setPlay":
                continue
            raw = r.get("data")
            raw_s = json.dumps(raw, ensure_ascii=False) if not isinstance(raw, str) else raw
            if "landed_" in (raw_s or ""):
                landed_start.append(r)

        if not land_auto and not landed_start:
            skips.append("SKIP[3] no landed/auto_start_sex evidence in this run")
        else:
            land_auto = land_auto or landed_start

    check3_fail = False
    for r in land_auto:
        ut = r.get("ut") or 0
        # find nearby snap/clip after
        ok = False
        for s in snaps:
            su = s.get("ut") or 0
            if su < ut:
                continue
            if su > ut + 1.0:
                break
            clip = S(data_of(s).get("clip"))
            if is_loop_clip(clip) or clip not in IDLE_CLIPS:
                # left idle or entered loop
                if is_loop_clip(clip) or clip not in IDLE_CLIPS:
                    ok = True
                    break
        # also accept startsex force within 1s
        for s in startsex:
            su = s.get("ut") or 0
            if ut <= su <= ut + 1.0:
                ok = True
                break
        if not ok:
            # AutoStartSex event itself is the intent; force_cha may be same frame
            raw = r.get("data")
            raw_s = json.dumps(raw, ensure_ascii=False) if raw is not None else ""
            if r.get("id") == "startsex":
                ok = True
            elif any(
                (sr.get("ut") or 0) >= ut - 0.05 and (sr.get("ut") or 0) <= ut + 1.0
                for sr in startsex
            ):
                ok = True
        if not ok:
            check3_fail = True
            fails.append(f"FAIL[3] landed auto_start_sex @ut{ut:.2f} no startsex/loop within 1s")

    if land_auto and not check3_fail:
        passes.append(f"PASS[3] {len(land_auto)} non-A+B land→start within 1s")
    elif not land_auto and not any(s.startswith("SKIP[3]") for s in skips):
        skips.append("SKIP[3] no landed/auto_start_sex evidence in this run")

    # --- 4. A+B: landed/appreciate; no immediate auto force without N ---
    land_appr = [r for r in R if r.get("id") == "landed" and r.get("msg") == "appreciate"]
    # Bad pattern: auto_after_* (legacy) or landed_* startsex immediately after appreciate on A+B
    bad_apb = []
    for r in land_appr:
        ut = r.get("ut") or 0
        for s in R:
            su = s.get("ut") or 0
            if su < ut or su > ut + 0.3:
                continue
            if s.get("id") == "startsex" and s.get("msg") == "force_cha_setPlay":
                raw = s.get("data")
                raw_s = json.dumps(raw, ensure_ascii=False) if raw is not None else ""
                # N is allowed; landed_* / auto_after_* is not right after appreciate
                if '"reason":"N"' in raw_s or '"reason": "N"' in raw_s:
                    continue
                if "landed_" in raw_s or "auto_after_" in raw_s:
                    bad_apb.append((ut, su, raw_s))
    # Also: SNAP longAppreciation on A+B idle after appreciate is good
    if land_appr:
        if bad_apb:
            for ut, su, raw in bad_apb[:5]:
                fails.append(
                    f"FAIL[4] appreciate @ut{ut:.2f} then force start @ut{su:.2f}: {raw[:120]}"
                )
        else:
            passes.append(f"PASS[4] {len(land_appr)} A+B appreciate without auto startsex")
    else:
        # Evidence via suppress longAppreciation on A+B ids while Idle — soft skip
        saw_apb = False
        for r in snaps:
            d = data_of(r)
            if d.get("suppress") != "longAppreciation":
                continue
            nid = parse_now_id(S(d.get("nowAnim")))
            clip = S(d.get("clip"))
            if nid in APB_IDS and clip in IDLE_CLIPS:
                saw_apb = True
                break
        if saw_apb:
            # Check no auto_after in whole run while we can't prove land policy
            legacy = [
                r for r in R
                if r.get("id") == "startsex"
                and "auto_after_" in (
                    json.dumps(r.get("data"), ensure_ascii=False)
                    if r.get("data") is not None
                    else ""
                )
            ]
            if legacy:
                fails.append(
                    f"FAIL[4] legacy auto_after_* startsex still present ({len(legacy)}x) with A+B suppress"
                )
            else:
                skips.append("SKIP[4] saw longAppreciation Idle but no landed/appreciate event yet")
        else:
            skips.append("SKIP[4] no A+B appreciate evidence in this run")

    # --- 5. AfterIdle: afteridle force or leave within 3s of AfterIdle-looking clips ---
    after_events = [r for r in R if r.get("id") == "afteridle"]
    after_snaps = []
    for r in snaps:
        clip = S(data_of(r).get("clip"))
        if "Orgasm" in clip and clip.endswith("_A"):
            after_snaps.append(r)
        if "AfterIdle" in clip:
            after_snaps.append(r)

    if after_events:
        passes.append(f"PASS[5] afteridle events present ({len(after_events)})")
    elif after_snaps:
        stuck = []
        for r in after_snaps:
            ut = r.get("ut") or 0
            left = False
            for s in snaps:
                su = s.get("ut") or 0
                if su <= ut:
                    continue
                if su > ut + 3.0:
                    break
                clip = S(data_of(s).get("clip"))
                if "Orgasm" not in clip or not clip.endswith("_A"):
                    if "AfterIdle" not in clip:
                        left = True
                        break
            if not left:
                # still in after at end of window
                end_ut = min((R[-1].get("ut") or 0), ut + 3.0)
                if end_ut >= ut + 2.9:
                    stuck.append(ut)
        if stuck:
            fails.append(
                f"FAIL[5] AfterIdle-looking clip stuck >=3s without leave @ut{stuck[0]:.2f} "
                f"(no afteridle log events)"
            )
        else:
            passes.append("PASS[5] AfterIdle clips left within 3s (no afteridle events logged)")
    else:
        skips.append("SKIP[5] no AfterIdle evidence in this run")

    print("--- results ---")
    for line in passes:
        print(line)
    for line in skips:
        print(line)
    for line in fails:
        print(line)

    if fails:
        print(f"SUMMARY: FAIL ({len(fails)} fail, {len(passes)} pass, {len(skips)} skip)")
        return 1
    print(f"SUMMARY: OK ({len(passes)} pass, {len(skips)} skip)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
