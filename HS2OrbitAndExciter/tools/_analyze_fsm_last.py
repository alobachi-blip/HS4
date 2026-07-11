# -*- coding: utf-8 -*-
import json
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
R = [r for r in rows if isinstance(r, dict) and r.get("runId") == rid]
print("runId", rid, "lines", len(R), "ut", R[0].get("ut"), "->", R[-1].get("ut"))
print("dll", R[0].get("dll"))

nons = [x for x in R if x.get("id") != "SNAP"]
print("non-SNAP", len(nons), "types", set(type(x).__name__ for x in nons[-40:]))

print("\n--- last 50 non-SNAP (id/msg only + key fields) ---")
for r in nons[-50:]:
    d = data_of(r)
    print(
        "ut={:.2f} {}/{} dir={} sup={} chg={} clip={} now={} sel={} auto={}".format(
            r.get("ut") or 0,
            r.get("id"),
            r.get("msg"),
            d.get("director"),
            d.get("suppress"),
            d.get("nowChangeAnim"),
            S(d.get("clip")),
            S(d.get("nowAnim")),
            S(d.get("selAnim")),
            d.get("isAutoAction"),
        )
    )

print("\n--- pose_kick / stale_sel ---")
for r in R:
    if r.get("id") not in ("pose_kick", "stale_sel"):
        continue
    print("ut={:.2f} {}/{} data={}".format(r.get("ut") or 0, r.get("id"), r.get("msg"), r.get("data")))

print("\n--- last 10 SNAPs ---")
for r in [x for x in R if x.get("id") == "SNAP"][-10:]:
    d = data_of(r)
    print(
        "ut={:.2f} dir={} sup={} chg={} faint={} clip={} now={} sel={} auto={}".format(
            r.get("ut") or 0,
            d.get("director"),
            d.get("suppress"),
            d.get("nowChangeAnim"),
            d.get("faint"),
            S(d.get("clip")),
            S(d.get("nowAnim")),
            S(d.get("selAnim")),
            d.get("isAutoAction"),
        )
    )

print("\n--- Changing windows ---")
state = None
t0 = None
meta = None
for r in R:
    d = data_of(r)
    if "director" not in d:
        continue
    cur = d.get("director")
    if cur != state:
        if state == "Changing" and t0 is not None:
            print(
                "Changing {:.2f}s @ut{:.1f} start_chg={} end_ut={:.1f} clip={} now={} sel={}".format(
                    (r.get("ut") or 0) - t0,
                    t0,
                    meta.get("nowChangeAnim"),
                    r.get("ut") or 0,
                    S(meta.get("clip")),
                    S(meta.get("nowAnim")),
                    S(meta.get("selAnim")),
                )
            )
        state = cur
        t0 = r.get("ut")
        meta = d
if state == "Changing":
    print("Changing OPEN {:.2f}s".format((R[-1].get("ut") or 0) - t0))

print("\n--- suppress last 30s ---")
t_end = R[-1].get("ut") or 0
last_sup = None
for r in R:
    if (r.get("ut") or 0) < t_end - 30:
        continue
    d = data_of(r)
    if "suppress" not in d:
        continue
    if d.get("suppress") != last_sup:
        print("ut={:.2f} suppress={} dir={} clip={}".format(r.get("ut") or 0, d.get("suppress"), d.get("director"), S(d.get("clip"))))
        last_sup = d.get("suppress")
