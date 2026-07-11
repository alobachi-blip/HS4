# -*- coding: utf-8 -*-
import json
import collections
import sys
from pathlib import Path

def S(x):
    if x is None:
        return ""
    return str(x).encode("ascii", "backslashreplace").decode("ascii")

def data_of(r):
    if not isinstance(r, dict):
        return {}
    d = r.get("data")
    return d if isinstance(d, dict) else {}

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
p = Path(r"D:\HS2\BepInEx\LogOutput\HS2OrbitAndExciter_fsm.ndjson")
rows = []
for l in p.read_text(encoding="utf-8-sig", errors="replace").splitlines():
    l = l.strip()
    if not l:
        continue
    try:
        o = json.loads(l)
    except Exception:
        continue
    if isinstance(o, dict):
        rows.append(o)

rid = rows[-1].get("runId")
R = [r for r in rows if r.get("runId") == rid]
print("runId", rid, "dll", R[0].get("dll"), "lines", len(R), "ut", R[0].get("ut"), "->", R[-1].get("ut"))

c = collections.Counter((r.get("id"), r.get("msg")) for r in R)
print("--- top ---")
for k, v in c.most_common(35):
    print(v, k)

print("--- pose_kick / stale / escape clear / director reset ---")
for r in R:
    if r.get("id") in ("pose_kick", "stale_sel", "escape", "director") or (
        r.get("id") == "gate" and r.get("msg") in ("longAppreciation", "changing", "poseQueued", "rebinding")
    ):
        if r.get("id") == "director" and r.get("msg") not in (
            "PoseQueued", "Changing", "Rebinding", "Orbitting", "reset_phantom_changing"
        ):
            continue
        d = data_of(r)
        print(
            "ut={:.2f} {}/{} chg={} clip={} now={} sel={} raw={}".format(
                r.get("ut") or 0,
                r.get("id"),
                r.get("msg"),
                d.get("nowChangeAnim"),
                S(d.get("clip")),
                S(d.get("nowAnim")),
                S(d.get("selAnim")),
                r.get("data") if r.get("id") in ("pose_kick", "stale_sel", "escape") else "",
            )
        )

print("--- longAppreciation spans ---")
on = False
t0 = None
info = None
for r in R:
    d = data_of(r)
    if "suppress" not in d:
        continue
    sup = d.get("suppress")
    if sup == "longAppreciation" and not on:
        on = True
        t0 = r.get("ut")
        info = d
    elif on and sup != "longAppreciation":
        print("longAppreciation {:.2f}s @ut{:.1f} clip={} now={}".format((r.get("ut") or 0) - t0, t0, S(info.get("clip")), S(info.get("nowAnim"))))
        on = False
if on:
    print("longAppreciation OPEN {:.2f}s clip={} now={}".format((R[-1].get("ut") or 0) - t0, S(info.get("clip")), S(info.get("nowAnim"))))

print("--- Changing / PoseQueued long ---")
state = None
t0 = None
meta = None
for r in R:
    d = data_of(r)
    if "director" not in d:
        continue
    cur = d.get("director")
    if cur != state:
        if state in ("PoseQueued", "Changing", "Rebinding", "PosePending") and t0 is not None:
            dur = (r.get("ut") or 0) - t0
            if dur >= 1.0:
                print("{} {:.2f}s @ut{:.1f} chg={} clip={} now={} sel={}".format(
                    state, dur, t0, meta.get("nowChangeAnim"), S(meta.get("clip")), S(meta.get("nowAnim")), S(meta.get("selAnim"))))
        state = cur
        t0 = r.get("ut")
        meta = d
if state in ("PoseQueued", "Changing") and t0 is not None:
    print("{} OPEN {:.2f}s chg={} clip={}".format(state, (R[-1].get("ut") or 0) - t0, meta.get("nowChangeAnim"), S(meta.get("clip"))))

print("--- last 12 SNAPs ---")
for r in [x for x in R if x.get("id") == "SNAP"][-12:]:
    d = data_of(r)
    print("ut={:.2f} dir={} sup={} chg={} faint={} clip={} now={} sel={} auto={}".format(
        r.get("ut") or 0, d.get("director"), d.get("suppress"), d.get("nowChangeAnim"),
        d.get("faint"), S(d.get("clip")), S(d.get("nowAnim")), S(d.get("selAnim")), d.get("isAutoAction")))

print("--- last 30 non-SNAP ---")
for r in [x for x in R if x.get("id") != "SNAP"][-30:]:
    d = data_of(r)
    print("ut={:.2f} {}/{} dir={} sup={} chg={} clip={} now={} sel={}".format(
        r.get("ut") or 0, r.get("id"), r.get("msg"), d.get("director"), d.get("suppress"),
        d.get("nowChangeAnim"), S(d.get("clip")), S(d.get("nowAnim")), S(d.get("selAnim"))))
