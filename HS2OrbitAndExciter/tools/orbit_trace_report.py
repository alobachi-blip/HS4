#!/usr/bin/env python3
import html
import json
import sys
from collections import Counter, defaultdict
from pathlib import Path


def load_rows(path: Path):
    rows = []
    for line in path.read_text(encoding="utf-8-sig", errors="replace").splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            rows.append(json.loads(line))
        except json.JSONDecodeError:
            continue
    return rows


def esc(value):
    return html.escape("" if value is None else str(value))


def main():
    if len(sys.argv) < 2:
        print("usage: orbit_trace_report.py <HS2OrbitAndExciter_fsm.ndjson> [out.html]")
        return 2

    src = Path(sys.argv[1])
    out = Path(sys.argv[2]) if len(sys.argv) >= 3 else src.with_suffix(".html")
    rows = load_rows(src)

    counts = Counter(r.get("id", "?") for r in rows)
    messages = Counter((r.get("id", "?"), r.get("msg", "?")) for r in rows)
    runs = sorted({r.get("runId", "") for r in rows if r.get("runId")})
    by_run = defaultdict(list)
    for row in rows:
        by_run[row.get("runId", "")].append(row)

    failures = []
    for row in rows:
        ident = str(row.get("id", ""))
        msg = str(row.get("msg", ""))
        data = row.get("data", {})
        if ident in {"pose_reject", "stale_sel"} or "fail" in msg or "stuck" in msg:
            failures.append(row)
        elif isinstance(data, dict) and data.get("suppress") not in (None, "", "none"):
            if ident == "gate":
                failures.append(row)

    last = rows[-200:]

    parts = [
        "<!doctype html><meta charset='utf-8'>",
        "<title>Orbit Smoke Report</title>",
        "<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;line-height:1.4}"
        "table{border-collapse:collapse;margin:12px 0;width:100%}"
        "td,th{border:1px solid #ccc;padding:4px 6px;vertical-align:top}"
        "code{white-space:pre-wrap}.bad{background:#fff2f2}.ok{background:#f2fff5}</style>",
        f"<h1>Orbit Smoke Report</h1><p><b>Source:</b> {esc(src)}<br>"
        f"<b>Rows:</b> {len(rows)}<br><b>Runs:</b> {esc(', '.join(runs) or '?')}</p>",
    ]

    parts.append("<h2>Event Counts</h2><table><tr><th>id</th><th>count</th></tr>")
    for key, count in counts.most_common():
        parts.append(f"<tr><td>{esc(key)}</td><td>{count}</td></tr>")
    parts.append("</table>")

    parts.append("<h2>Top Messages</h2><table><tr><th>id</th><th>msg</th><th>count</th></tr>")
    for (ident, msg), count in messages.most_common(40):
        parts.append(f"<tr><td>{esc(ident)}</td><td>{esc(msg)}</td><td>{count}</td></tr>")
    parts.append("</table>")

    cls = "bad" if failures else "ok"
    parts.append(f"<h2 class='{cls}'>Potential Issues ({len(failures)})</h2>")
    parts.append("<table><tr><th>ut</th><th>id</th><th>loc</th><th>msg</th><th>data</th></tr>")
    for row in failures[:120]:
        parts.append("<tr><td>{}</td><td>{}</td><td>{}</td><td>{}</td><td><code>{}</code></td></tr>".format(
            esc(row.get("ut")), esc(row.get("id")), esc(row.get("loc")),
            esc(row.get("msg")), esc(json.dumps(row.get("data", {}), ensure_ascii=False))
        ))
    parts.append("</table>")

    parts.append("<h2>Last 200 Rows</h2><table><tr><th>ut</th><th>id</th><th>loc</th><th>msg</th><th>data</th></tr>")
    for row in last:
        parts.append("<tr><td>{}</td><td>{}</td><td>{}</td><td>{}</td><td><code>{}</code></td></tr>".format(
            esc(row.get("ut")), esc(row.get("id")), esc(row.get("loc")),
            esc(row.get("msg")), esc(json.dumps(row.get("data", {}), ensure_ascii=False))
        ))
    parts.append("</table>")

    out.write_text("\n".join(parts), encoding="utf-8")
    print(out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
