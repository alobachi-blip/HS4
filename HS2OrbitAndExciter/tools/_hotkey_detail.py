# -*- coding: utf-8 -*-
import json
from pathlib import Path
rows = []
for l in Path(r"D:\HS2\BepInEx\LogOutput\HS2OrbitAndExciter_fsm.ndjson").read_text(encoding="utf-8-sig", errors="replace").splitlines():
    l = l.strip()
    if not l:
        continue
    try:
        o = json.loads(l)
    except Exception:
        continue
    if isinstance(o, dict):
        rows.append(o)
R = [r for r in rows if r.get("runId") == "cd3f291e"]
print("hotkey outcomes:")
for r in R:
    if r.get("id") != "hotkey":
        continue
    d = r.get("data") if isinstance(r.get("data"), dict) else {}
    print("  ut={:.2f} {} {} {}".format(r.get("ut") or 0, r.get("loc"), r.get("msg"), d))
print("last status summary:")
last = [x for x in R if x.get("id") == "SNAP"][-1]
d = last.get("data") if isinstance(last.get("data"), dict) else {}
print("  ut={:.2f} dir={} sup={} chg={} clip={} now={} sel={}".format(
    last.get("ut") or 0, d.get("director"), d.get("suppress"), d.get("nowChangeAnim"),
    d.get("clip"), d.get("nowAnim"), d.get("selAnim")))
