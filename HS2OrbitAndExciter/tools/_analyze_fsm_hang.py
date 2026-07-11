# -*- coding: utf-8 -*-
import json
import collections
import sys
from pathlib import Path

def S(x):
    if x is None:
        return ""
    return str(x).encode("ascii", "backslashreplace").decode("ascii")

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

p = Path(r"D:\HS2\BepInEx\LogOutput\HS2OrbitAndExciter_fsm.ndjson")
rows = []
for line in p.read_text(encoding="utf-8", errors="replace").splitlines():
    line = line.strip()
    if not line:
        continue
    try:
        rows.append(json.loads(line))
    except Exception:
        pass

print("total", len(rows))
rid = rows[-1].get("runId")
print("last runId", rid)
R = [r for r in rows if r.get("runId") == rid]
print("run lines", len(R), "ut", R[0].get("ut"), "->", R[-1].get("ut"))

c = collections.Counter((r.get("id"), r.get("msg")) for r in R)
print("--- top events ---")
for k, v in c.most_common(40):
    print(v, k)

print("--- director long spells (>=2s) ---")
state = None
start = None
meta = None
for r in R:
    d = r.get("data") or {}
    dirn = d.get("director")
    if dirn is None:
        continue
    if dirn != state:
        if state in ("PoseQueued", "Changing", "Rebinding", "PosePending") and start is not None:
            dur = (r.get("ut") or 0) - start
            if dur >= 2.0:
                print(
                    f"{state} {dur:.2f}s @ut{start:.1f} suppress={meta.get('suppress')} "
                    f"change={meta.get('nowChangeAnim')} clip={S(meta.get('clip'))} "
                    f"now={S(meta.get('nowAnim'))} sel={S(meta.get('selAnim'))}"
                )
        state = dirn
        start = r.get("ut")
        meta = d

print("--- key events ---")
for r in R:
    rid_ = r.get("id")
    msg = r.get("msg")
    d = r.get("data") or {}
    if rid_ not in ("stale_sel", "afteridle", "idle", "checkpoint", "assist", "escape", "hotkey", "pose_reject", "anim", "orgasm", "gate", "director"):
        continue
    if rid_ == "assist":
        continue  # too noisy
    if rid_ == "escape" and msg == "request":
        print(f"ut={r.get('ut'):.2f} escape/{d.get('reason') if isinstance(d, dict) else d}")
        # escape data may be nested differently
        if isinstance(d, dict) and "reason" in d:
            pass
        continue
    if rid_ == "gate" and msg not in ("longAppreciation", "poseQueued", "changing", "rebinding", "nowOrgasm"):
        continue
    if rid_ == "director" and msg not in ("PoseQueued", "Changing", "Rebinding", "PosePending"):
        continue
    slim = {
        "sup": d.get("suppress"),
        "dir": d.get("director"),
        "chg": d.get("nowChangeAnim"),
        "clip": S(d.get("clip")),
        "now": S(d.get("nowAnim")),
        "sel": S(d.get("selAnim")),
        "auto": d.get("isAutoAction"),
        "ok": d.get("ok"),
        "detail": d.get("detail"),
        "id": d.get("id"),
        "down": d.get("down"),
        "reason": d.get("reason"),
    }
    slim = {k: v for k, v in slim.items() if v is not None and v != ""}
    print(f"ut={r.get('ut'):.2f} {rid_}/{msg} {slim}")

print("--- longAppreciation spans ---")
on = False
t0 = None
info = None
for r in R:
    d = r.get("data") or {}
    if "suppress" not in d:
        continue
    sup = d.get("suppress")
    if sup == "longAppreciation" and not on:
        on = True
        t0 = r.get("ut")
        info = d
    elif on and sup != "longAppreciation":
        print(f"longAppreciation {r.get('ut')-t0:.2f}s @ut{t0:.1f} clip={S(info.get('clip'))} now={S(info.get('nowAnim'))}")
        on = False
if on:
    print(f"longAppreciation OPEN {(R[-1].get('ut') or 0)-t0:.2f}s clip={S(info.get('clip'))} now={S(info.get('nowAnim'))}")

print("--- PoseQueued stuck no NowChangeAnim (>=3s) ---")
pq_start = None
pq_meta = None
for r in R:
    d = r.get("data") or {}
    if "director" not in d:
        continue
    if d.get("director") != "PoseQueued":
        if pq_start is not None:
            dur = (r.get("ut") or 0) - pq_start
            if dur >= 3.0:
                print(
                    f"stuck PoseQueued {dur:.2f}s @ut{pq_start:.1f} "
                    f"sel={S(pq_meta.get('selAnim'))} now={S(pq_meta.get('nowAnim'))} auto={pq_meta.get('isAutoAction')}"
                )
            pq_start = None
        continue
    if not d.get("nowChangeAnim"):
        if pq_start is None:
            pq_start = r.get("ut")
            pq_meta = d
    else:
        pq_start = None

print("--- escape request reasons ---")
er = collections.Counter()
for r in R:
    if r.get("id") == "escape":
        d = r.get("data") or {}
        er[d.get("reason") if isinstance(d, dict) else "?"] += 1
print(dict(er))

print("--- hotkey L outcomes ---")
for r in R:
    if r.get("id") == "hotkey" and r.get("loc") == "L":
        d = r.get("data") or {}
        print(f"ut={r.get('ut'):.2f} L {r.get('msg')} detail={d.get('detail')}")
