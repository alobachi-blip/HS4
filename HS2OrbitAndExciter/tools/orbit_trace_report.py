#!/usr/bin/env python3
import html
import json
import sys
from collections import Counter, defaultdict
from dataclasses import dataclass
from pathlib import Path

from _assert_fsm_regression import analyze_rows


MICRO_RUN_SECONDS = 0.25


@dataclass(frozen=True)
class StageSegment:
    session: int
    family: str
    cell: str
    clip: str
    pose_id: int
    pose_name: str
    start_ut: float
    end_ut: float
    duration: float
    row_index: int


@dataclass(frozen=True)
class StageAnomaly:
    code: str
    severity: str
    row_index: int
    message: str


@dataclass(frozen=True)
class StageRun:
    session: int
    family: str
    cell: str
    clip: str
    pose_id: int
    pose_name: str
    start_ut: float
    end_ut: float
    duration: float
    samples: int
    row_index: int


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


def image_src(path):
    if not path:
        return ""
    try:
        return Path(path).resolve().as_uri()
    except Exception:
        return str(path)


def fmt_seconds(value):
    return f"{value:.2f}s"


def data_value(row, *names):
    data = row.get("data", {})
    if not isinstance(data, dict):
        return None
    for name in names:
        if name in data and data[name] not in (None, ""):
            return data[name]
    return None


def as_float(value, fallback=0.0):
    try:
        return float(value)
    except (TypeError, ValueError):
        return fallback


def as_int(value, fallback=-1):
    try:
        return int(value)
    except (TypeError, ValueError):
        return fallback


def row_data(row):
    data = row.get("data", {})
    return data if isinstance(data, dict) else {}


def is_stage_row(row):
    if row.get("id") not in {"session/start", "session/state", "SNAP", "gate", "director", "anim", "orgasm"}:
        return False
    data = row_data(row)
    return "fsmCell" in data and "clip" in data


def build_stage_segments(rows):
    state_rows = [(index, row) for index, row in enumerate(rows) if is_stage_row(row)]
    segments = []
    for pos, (index, row) in enumerate(state_rows):
        data = row_data(row)
        start = as_float(row.get("ut"), float(index))
        if pos + 1 < len(state_rows):
            end = as_float(state_rows[pos + 1][1].get("ut"), start)
        else:
            end = start
        duration = max(0.0, end - start)
        segments.append(
            StageSegment(
                session=as_int(data.get("sessionIndex")),
                family=str(data.get("sessionFamily") or "?"),
                cell=str(data.get("fsmCell") or "?"),
                clip=str(data.get("clip") or "?"),
                pose_id=as_int(data.get("nowAnimId")),
                pose_name=str(data.get("nowAnimName") or ""),
                start_ut=start,
                end_ut=end,
                duration=duration,
                row_index=index,
            )
        )
    return segments


def build_stage_runs(segments):
    runs = []
    current = None
    for seg in segments:
        if seg.family in {"?", "Unknown"} or seg.cell in {"?", "Unknown"}:
            continue
        key = (seg.session, seg.family, seg.cell, seg.clip)
        if current and current["key"] == key and abs(current["end_ut"] - seg.start_ut) <= 0.75:
            current["end_ut"] = seg.end_ut
            current["duration"] = max(0.0, current["end_ut"] - current["start_ut"])
            current["samples"] += 1
            continue
        if current:
            runs.append(StageRun(**{k: v for k, v in current.items() if k != "key"}))
        current = {
            "key": key,
            "session": seg.session,
            "family": seg.family,
            "cell": seg.cell,
            "clip": seg.clip,
            "pose_id": seg.pose_id,
            "pose_name": seg.pose_name,
            "start_ut": seg.start_ut,
            "end_ut": seg.end_ut,
            "duration": seg.duration,
            "samples": 1,
            "row_index": seg.row_index,
        }
    if current:
        runs.append(StageRun(**{k: v for k, v in current.items() if k != "key"}))
    return runs


def median(values):
    ordered = sorted(values)
    count = len(ordered)
    if count == 0:
        return 0.0
    mid = count // 2
    if count % 2:
        return ordered[mid]
    return (ordered[mid - 1] + ordered[mid]) / 2.0


def summarize_stage_runs(runs):
    summary = {}
    for run in runs:
        if run.duration <= 0:
            continue
        key = (run.family, run.cell, run.clip)
        item = summary.setdefault(key, {"durations": [], "total": 0.0})
        item["durations"].append(run.duration)
        item["total"] += run.duration
    rows = []
    for (family, cell, clip), item in summary.items():
        durations = item["durations"]
        short_runs = sum(1 for value in durations if value < MICRO_RUN_SECONDS)
        min_value = min(durations)
        max_value = max(durations)
        avg_value = item["total"] / len(durations)
        flag = ""
        if short_runs and len(durations) > 1 and max_value >= 1.0 and max_value / max(min_value, 0.01) >= 5.0:
            flag = "short_vs_long"
        rows.append(
            {
                "family": family,
                "cell": cell,
                "clip": clip,
                "runs": len(durations),
                "short_runs": short_runs,
                "short_ratio": short_runs / len(durations),
                "typical": median(durations),
                "min": min_value,
                "max": max_value,
                "avg": avg_value,
                "total": item["total"],
                "flag": flag,
            }
        )
    rows.sort(key=lambda r: (r["family"], r["cell"], r["clip"]))
    return rows


def summarize_stage_segments(segments):
    summary = {}
    for seg in segments:
        key = (seg.family, seg.cell, seg.clip)
        item = summary.setdefault(
            key,
            {"count": 0, "total": 0.0, "max": 0.0, "min": None, "sessions": set(), "pose_ids": set()},
        )
        item["count"] += 1
        item["total"] += seg.duration
        item["max"] = max(item["max"], seg.duration)
        item["min"] = seg.duration if item["min"] is None else min(item["min"], seg.duration)
        if seg.session >= 0:
            item["sessions"].add(seg.session)
        if seg.pose_id >= 0:
            item["pose_ids"].add(seg.pose_id)
    rows = []
    for (family, cell, clip), item in summary.items():
        count = item["count"] or 1
        rows.append(
            {
                "family": family,
                "cell": cell,
                "clip": clip,
                "count": item["count"],
                "total": item["total"],
                "avg": item["total"] / count,
                "max": item["max"],
                "min": item["min"] or 0.0,
                "sessions": len(item["sessions"]),
                "poses": len(item["pose_ids"]),
            }
        )
    rows.sort(key=lambda r: (r["family"], r["cell"], -r["total"], r["clip"]))
    return rows


def summarize_sessions(segments):
    by_session = defaultdict(list)
    for seg in segments:
        by_session[seg.session].append(seg)
    out = []
    for session, items in sorted(by_session.items()):
        if session < 0 or not items:
            continue
        start = min(seg.start_ut for seg in items)
        end = max(seg.end_ut for seg in items)
        cells = []
        clips = []
        for seg in items:
            if not cells or cells[-1] != seg.cell:
                cells.append(seg.cell)
            if not clips or clips[-1] != seg.clip:
                clips.append(seg.clip)
        first = items[0]
        out.append(
            {
                "session": session,
                "family": first.family,
                "pose_id": first.pose_id,
                "pose_name": first.pose_name,
                "start": start,
                "end": end,
                "duration": max(0.0, end - start),
                "cells": " > ".join(cells[:12]),
                "clips": " > ".join(clips[:18]),
            }
        )
    return out


def event_times(rows, predicate):
    times = []
    for index, row in enumerate(rows):
        if predicate(row):
            times.append((index, as_float(row.get("ut"), float(index))))
    return times


def has_near_event(times, start, end, pad=1.5):
    lo = start - pad
    hi = end + pad
    return any(lo <= t <= hi for _, t in times)


def detect_stage_anomalies(rows, segments):
    anomalies = []
    finish_or_orgasm = event_times(
        rows,
        lambda r: (
            (r.get("id") == "finish" and r.get("msg") == "consumed")
            or r.get("id") == "ina"
            or r.get("id") == "spank"
            or r.get("id") == "orgasm"
        ),
    )

    for seg in segments:
        if seg.cell == "Unknown" and seg.duration > 8.0:
            anomalies.append(StageAnomaly("unknown_long", "warning", seg.row_index, f"Unknown state for {seg.duration:.1f}s."))
        elif seg.cell == "Idle" and seg.duration > 12.0:
            anomalies.append(StageAnomaly("idle_long", "warning", seg.row_index, f"Idle persisted for {seg.duration:.1f}s."))
        elif seg.cell == "ActionBridge" and seg.duration > 45.0:
            anomalies.append(StageAnomaly("action_clip_long", "warning", seg.row_index, f"{seg.clip} persisted for {seg.duration:.1f}s."))
        elif seg.cell == "AfterIdle" and seg.duration > 18.0:
            anomalies.append(StageAnomaly("afteridle_long", "warning", seg.row_index, f"AfterIdle persisted for {seg.duration:.1f}s."))

    by_session = defaultdict(list)
    for seg in segments:
        by_session[seg.session].append(seg)
    for session, items in by_session.items():
        if session < 0:
            continue
        now_change = [seg for seg in items if "changing" in seg.clip.lower()]
        _ = now_change
        cells = []
        for seg in items:
            if seg.cell == "Unknown":
                continue
            if not cells or cells[-1] != seg.cell:
                cells.append(seg.cell)
        total = max(seg.end_ut for seg in items) - min(seg.start_ut for seg in items) if items else 0.0
        if total > 90.0:
            anomalies.append(StageAnomaly("session_long", "warning", items[0].row_index, f"Session {session} lasted {total:.1f}s."))
        for prev, cur in zip(items, items[1:]):
            if prev.cell == "ActionBridge" and cur.cell == "AfterIdle":
                if not has_near_event(finish_or_orgasm, prev.start_ut, cur.end_ut):
                    anomalies.append(
                        StageAnomaly(
                            "skip_to_afteridle",
                            "warning",
                            cur.row_index,
                            f"Session {session} moved ActionBridge -> AfterIdle without nearby finish/orgasm marker.",
                        )
                    )
                    break
    return anomalies


def build_coverage(rows, segments):
    family_set = {seg.family for seg in segments if seg.family and seg.family not in {"?", "Unknown", "Peeping"}}
    family_timing = {}
    for seg in segments:
        if not seg.family or seg.family in {"?", "Unknown", "Peeping"}:
            continue
        item = family_timing.setdefault(
            seg.family,
            {"first": seg.start_ut, "last": seg.end_ut, "total": 0.0, "count": 0, "sessions": set()},
        )
        item["first"] = min(item["first"], seg.start_ut)
        item["last"] = max(item["last"], seg.end_ut)
        item["total"] += seg.duration
        item["count"] += 1
        if seg.session >= 0:
            item["sessions"].add(seg.session)
    coverage_active = False
    expected_families = []
    for row in rows:
        data = row_data(row)
        if row.get("id") == "smoke" and row.get("msg") == "family_coverage_start":
            coverage_active = True
            sequence = str(data.get("sequence") or "")
            expected_families.extend(part.strip() for part in sequence.split(",") if part.strip())
        if row.get("id") == "smoke" and row.get("msg") == "stage_keyframe":
            family = str(data.get("sessionFamily") or "")
            if family and family not in {"?", "Unknown", "Peeping"}:
                family_set.add(family)
                ut = as_float(row.get("ut"), 0.0)
                item = family_timing.setdefault(
                    family,
                    {"first": ut, "last": ut, "total": 0.0, "count": 0, "sessions": set()},
                )
                item["first"] = min(item["first"], ut)
                item["last"] = max(item["last"], ut)
    if not expected_families:
        expected_families = ["A_Aibu", "B_Houshi", "C_Sonyu", "D_Masturbation", "E_Spnking", "A_Les"]
    expected_families = list(dict.fromkeys(expected_families))
    families = sorted(family_set)
    finish_candidates = Counter()
    finish_set = Counter()
    finish_consumed = Counter()
    for row in rows:
        data = row_data(row)
        if row.get("id") == "finish":
            path = str(data.get("path") or "?")
            if row.get("msg") == "candidate":
                for item in data.get("candidates", []) if isinstance(data.get("candidates"), list) else []:
                    if isinstance(item, dict):
                        finish_candidates[str(item.get("path") or "?")] += 1
            elif row.get("msg") == "set_click":
                finish_set[path] += 1
            elif row.get("msg") == "consumed":
                finish_consumed[path] += 1
    special = Counter()
    for row in rows:
        ident = row.get("id")
        msg = row.get("msg")
        if ident in {"ina", "spank"}:
            special[f"{ident}/{msg}"] += 1
        if ident == "ws" and msg == "script_step":
            special["ws/script_step"] += 1
    return {
        "coverage_active": coverage_active,
        "expected_families": expected_families,
        "families": families,
        "family_timing": {
            family: {
                "first": item["first"],
                "last": item["last"],
                "total": item["total"],
                "count": item["count"],
                "sessions": len(item["sessions"]),
            }
            for family, item in family_timing.items()
        },
        "missing_families": [f for f in expected_families if f not in families],
        "finish_candidates": finish_candidates,
        "finish_set": finish_set,
        "finish_consumed": finish_consumed,
        "special": special,
    }


def main():
    if len(sys.argv) < 2:
        print("usage: orbit_trace_report.py <HS2OrbitAndExciter_fsm.ndjson> [out.html]")
        return 2

    src = Path(sys.argv[1])
    out = Path(sys.argv[2]) if len(sys.argv) >= 3 else src.with_suffix(".html")
    rows = load_rows(src)
    issues = analyze_rows(rows)
    stage_segments = build_stage_segments(rows)
    stage_runs = build_stage_runs(stage_segments)
    stage_run_summary = summarize_stage_runs(stage_runs)
    stage_summary = summarize_stage_segments(stage_segments)
    session_summary = summarize_sessions(stage_segments)
    stage_anomalies = detect_stage_anomalies(rows, stage_segments)
    coverage = build_coverage(rows, stage_segments)

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

    keyframes = []
    for row in rows:
        screenshot = data_value(row, "screenshot", "screenshotPath", "keyframePath", "image")
        keyframe = data_value(row, "keyframe", "event", "marker")
        if screenshot or keyframe:
            keyframes.append((row, screenshot, keyframe))

    last = rows[-200:]

    parts = [
        "<!doctype html><meta charset='utf-8'>",
        "<title>Orbit Smoke Report</title>",
        "<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;line-height:1.4}"
        "table{border-collapse:collapse;margin:12px 0;width:100%}"
        "td,th{border:1px solid #ccc;padding:4px 6px;vertical-align:top}"
        "code{white-space:pre-wrap}.bad{background:#fff2f2}.warn{background:#fff9e8}.ok{background:#f2fff5}"
        ".muted{color:#666}.num{text-align:right;font-variant-numeric:tabular-nums}"
        "img.keyframe{max-width:360px;max-height:240px;border:1px solid #bbb}</style>",
        f"<h1>Orbit Smoke Report</h1><p><b>Source:</b> {esc(src)}<br>"
        f"<b>Rows:</b> {len(rows)}<br><b>Runs:</b> {esc(', '.join(runs) or '?')}</p>",
    ]

    error_count = sum(1 for issue in issues if issue.severity == "error")
    cls = "bad" if error_count else ("warn" if issues else "ok")
    parts.append(f"<h2 class='{cls}'>Regression Assertions ({len(issues)})</h2>")
    parts.append("<table><tr><th>severity</th><th>code</th><th>row</th><th>message</th></tr>")
    for issue in issues:
        parts.append(
            f"<tr><td>{esc(issue.severity)}</td><td>{esc(issue.code)}</td>"
            f"<td>{'' if issue.row_index is None else issue.row_index}</td><td>{esc(issue.message)}</td></tr>"
        )
    if not issues:
        parts.append("<tr><td colspan='4'>No regression issues detected.</td></tr>")
    parts.append("</table>")

    latest_snapshot = {}
    for row in reversed(rows):
        data = row.get("data", {})
        if row.get("id") == "SNAP" and isinstance(data, dict):
            latest_snapshot = data
            break
    pose_pool = latest_snapshot.get("posePool", {}) if isinstance(latest_snapshot, dict) else {}
    if not isinstance(pose_pool, dict):
        pose_pool = {}
    parts.append("<h2>Slice 0 Quality Layer</h2>")
    parts.append("<table><tr><th>field</th><th>value</th></tr>")
    parts.append(f"<tr><td>posePool.total</td><td>{esc(pose_pool.get('total', '?'))}</td></tr>")
    parts.append(f"<tr><td>posePool.afterUnlock</td><td>{esc(pose_pool.get('afterUnlock', '?'))}</td></tr>")
    parts.append(f"<tr><td>posePool.afterFaintness</td><td>{esc(pose_pool.get('afterFaintness', '?'))}</td></tr>")
    for key in ("bhsInstalled", "bhsConfigFound", "bhsAutoFinishEnabled", "bhsOffsetApplied", "bhsSolverEnabled"):
        parts.append(f"<tr><td>{esc(key)}</td><td>{esc(latest_snapshot.get(key, '?'))}</td></tr>")
    parts.append("</table>")

    session_rows = [row for row in rows if row.get("id") in {"session/start", "session/state"}]
    latest_session = {}
    for row in reversed(session_rows):
        data = row.get("data", {})
        if row.get("id") == "session/state" and isinstance(data, dict):
            latest_session = data
            break
    parts.append(f"<h2>Session Trace ({len(session_rows)})</h2>")
    parts.append("<table><tr><th>field</th><th>value</th></tr>")
    for key in (
        "sessionIndex", "sessionFamily", "fsmCell", "mode", "modeCtrl",
        "actionCtrl1", "actionCtrl2", "nowAnimId", "nowAnimName",
        "selAnimId", "clip", "clipNorm", "speed", "feel_f", "feel_m",
        "click", "nowOrgasm", "faint", "finishVisible",
    ):
        parts.append(f"<tr><td>{esc(key)}</td><td>{esc(latest_session.get(key, '?'))}</td></tr>")
    parts.append("</table>")

    cov_cls = "bad" if coverage["coverage_active"] and coverage["missing_families"] else ("warn" if coverage["missing_families"] else "ok")
    parts.append(f"<h2 class='{cov_cls}'>Coverage</h2>")
    parts.append("<table><tr><th>item</th><th>observed</th><th>missing / counts</th></tr>")
    parts.append(
        f"<tr><td>coverage mode</td><td>{'active' if coverage['coverage_active'] else 'passive'}</td>"
        f"<td>expected: {esc(', '.join(coverage['expected_families']))}</td></tr>"
    )
    parts.append(
        f"<tr><td>families</td><td>{esc(', '.join(coverage['families']) or '?')}</td>"
        f"<td>{esc(', '.join(coverage['missing_families']) or 'none')}</td></tr>"
    )
    for family in coverage["expected_families"]:
        timing = coverage["family_timing"].get(family)
        if timing:
            observed = (
                f"first {timing['first']:.2f}s / last {timing['last']:.2f}s / "
                f"total {fmt_seconds(timing['total'])}"
            )
            detail = f"segments {timing['count']} / sessions {timing['sessions']}"
        else:
            observed = "not observed"
            detail = "missing"
        parts.append(f"<tr><td>family:{esc(family)}</td><td>{esc(observed)}</td><td>{esc(detail)}</td></tr>")
    all_paths = sorted(set(coverage["finish_candidates"]) | set(coverage["finish_set"]) | set(coverage["finish_consumed"]))
    if all_paths:
        for path in all_paths:
            parts.append(
                "<tr><td>finish:{}</td><td>candidate {} / set {} / consumed {}</td><td></td></tr>".format(
                    esc(path),
                    coverage["finish_candidates"].get(path, 0),
                    coverage["finish_set"].get(path, 0),
                    coverage["finish_consumed"].get(path, 0),
                )
            )
    else:
        parts.append("<tr><td>finish paths</td><td>none</td><td>no B/C finish consumed in this run</td></tr>")
    for key, count in sorted(coverage["special"].items()):
        parts.append(f"<tr><td>{esc(key)}</td><td>{count}</td><td></td></tr>")
    parts.append("</table>")

    anomaly_cls = "bad" if any(a.severity == "error" for a in stage_anomalies) else ("warn" if stage_anomalies else "ok")
    parts.append(f"<h2 class='{anomaly_cls}'>Stage Anomalies ({len(stage_anomalies)})</h2>")
    parts.append("<table><tr><th>severity</th><th>code</th><th>row</th><th>message</th></tr>")
    for anomaly in stage_anomalies:
        parts.append(
            f"<tr><td>{esc(anomaly.severity)}</td><td>{esc(anomaly.code)}</td>"
            f"<td>{anomaly.row_index}</td><td>{esc(anomaly.message)}</td></tr>"
        )
    if not stage_anomalies:
        parts.append("<tr><td colspan='4'>No stage timing anomalies detected.</td></tr>")
    parts.append("</table>")

    parts.append(f"<h2>Stage Duration Summary ({len(stage_summary)})</h2>")
    parts.append("<table><tr><th>family</th><th>cell</th><th>clip</th><th>count</th><th>total</th><th>avg</th><th>max</th><th>sessions</th><th>poses</th></tr>")
    for item in sorted(stage_summary, key=lambda r: r["total"], reverse=True)[:160]:
        parts.append(
            "<tr><td>{}</td><td>{}</td><td>{}</td><td class='num'>{}</td>"
            "<td class='num'>{}</td><td class='num'>{}</td><td class='num'>{}</td>"
            "<td class='num'>{}</td><td class='num'>{}</td></tr>".format(
                esc(item["family"]), esc(item["cell"]), esc(item["clip"]), item["count"],
                fmt_seconds(item["total"]), fmt_seconds(item["avg"]), fmt_seconds(item["max"]),
                item["sessions"], item["poses"],
            )
        )
    parts.append("</table>")

    parts.append(f"<h2>Stage Run Duration Summary ({len(stage_run_summary)})</h2>")
    parts.append(
        f"<p class='muted'>Consecutive identical session/family/cell/clip samples are merged into one run. "
        f"Typical is median duration. Short runs (&lt;{MICRO_RUN_SECONDS:.2f}s) are retained and flagged, not filtered out.</p>"
    )
    parts.append(
        "<table><tr><th>family</th><th>cell</th><th>clip</th><th>runs</th>"
        "<th>typical</th><th>min</th><th>mean</th><th>max</th><th>short runs</th><th>flag</th><th>total</th></tr>"
    )
    for item in stage_run_summary:
        row_cls = " class='warn'" if item["flag"] else ""
        parts.append(
            "<tr{}><td>{}</td><td>{}</td><td>{}</td><td class='num'>{}</td>"
            "<td class='num'>{}</td><td class='num'>{}</td><td class='num'>{}</td><td class='num'>{}</td>"
            "<td class='num'>{} ({:.0%})</td><td>{}</td><td class='num'>{}</td></tr>".format(
                row_cls,
                esc(item["family"]), esc(item["cell"]), esc(item["clip"]), item["runs"],
                fmt_seconds(item["typical"]), fmt_seconds(item["min"]), fmt_seconds(item["avg"]),
                fmt_seconds(item["max"]), item["short_runs"], item["short_ratio"],
                esc(item["flag"] or ""), fmt_seconds(item["total"]),
            )
        )
    if not stage_run_summary:
        parts.append("<tr><td colspan='11'>No stage runs detected.</td></tr>")
    parts.append("</table>")

    parts.append(f"<h2>Session Timeline ({len(session_summary)})</h2>")
    parts.append("<table><tr><th>session</th><th>family</th><th>pose</th><th>start</th><th>end</th><th>duration</th><th>cells</th><th>clips</th></tr>")
    for item in session_summary[:120]:
        parts.append(
            "<tr><td class='num'>{}</td><td>{}</td><td>#{} {}</td>"
            "<td class='num'>{:.2f}</td><td class='num'>{:.2f}</td><td class='num'>{}</td>"
            "<td>{}</td><td><code>{}</code></td></tr>".format(
                item["session"], esc(item["family"]), item["pose_id"], esc(item["pose_name"]),
                item["start"], item["end"], fmt_seconds(item["duration"]),
                esc(item["cells"]), esc(item["clips"]),
            )
        )
    parts.append("</table>")

    longest_segments = sorted(stage_segments, key=lambda seg: seg.duration, reverse=True)[:80]
    parts.append(f"<h2>Longest Stage Segments ({len(longest_segments)})</h2>")
    parts.append("<table><tr><th>duration</th><th>start</th><th>end</th><th>row</th><th>session</th><th>family</th><th>cell</th><th>clip</th><th>pose</th></tr>")
    for seg in longest_segments:
        parts.append(
            "<tr><td class='num'>{}</td><td class='num'>{:.2f}</td><td class='num'>{:.2f}</td>"
            "<td class='num'>{}</td><td class='num'>{}</td><td>{}</td><td>{}</td><td>{}</td><td>#{} {}</td></tr>".format(
                fmt_seconds(seg.duration), seg.start_ut, seg.end_ut, seg.row_index, seg.session,
                esc(seg.family), esc(seg.cell), esc(seg.clip), seg.pose_id, esc(seg.pose_name),
            )
        )
    if not longest_segments:
        parts.append("<tr><td colspan='9'>No stage segments detected.</td></tr>")
    parts.append("</table>")

    longest_runs = sorted(stage_runs, key=lambda run: run.duration, reverse=True)[:40]
    parts.append(f"<h2>Longest Stage Runs ({len(longest_runs)})</h2>")
    parts.append("<table><tr><th>duration</th><th>start</th><th>end</th><th>row</th><th>session</th><th>family</th><th>cell</th><th>clip</th><th>pose</th><th>samples</th></tr>")
    for run in longest_runs:
        parts.append(
            "<tr><td class='num'>{}</td><td class='num'>{:.2f}</td><td class='num'>{:.2f}</td>"
            "<td class='num'>{}</td><td class='num'>{}</td><td>{}</td><td>{}</td><td>{}</td>"
            "<td>#{} {}</td><td class='num'>{}</td></tr>".format(
                fmt_seconds(run.duration), run.start_ut, run.end_ut, run.row_index, run.session,
                esc(run.family), esc(run.cell), esc(run.clip), run.pose_id, esc(run.pose_name), run.samples,
            )
        )
    if not longest_runs:
        parts.append("<tr><td colspan='10'>No stage runs detected.</td></tr>")
    parts.append("</table>")

    timeline_segments = stage_segments
    timeline_note = ""
    if len(stage_segments) > 400:
        timeline_segments = stage_segments[:200] + stage_segments[-200:]
        timeline_note = "first 200 + last 200"
    title_note = f" - {timeline_note}" if timeline_note else ""
    parts.append(f"<h2>Stage Timeline Detail ({len(stage_segments)}{title_note})</h2>")
    parts.append("<table><tr><th>row</th><th>session</th><th>family</th><th>cell</th><th>clip</th><th>start</th><th>end</th><th>duration</th><th>pose</th></tr>")
    for seg in timeline_segments:
        parts.append(
            "<tr><td class='num'>{}</td><td class='num'>{}</td><td>{}</td><td>{}</td><td>{}</td>"
            "<td class='num'>{:.2f}</td><td class='num'>{:.2f}</td><td class='num'>{}</td><td>#{} {}</td></tr>".format(
                seg.row_index, seg.session, esc(seg.family), esc(seg.cell), esc(seg.clip),
                seg.start_ut, seg.end_ut, fmt_seconds(seg.duration), seg.pose_id, esc(seg.pose_name),
            )
        )
    if not timeline_segments:
        parts.append("<tr><td colspan='9'>No stage segments detected.</td></tr>")
    parts.append("</table>")

    parts.append("<h2>Event Counts</h2><table><tr><th>id</th><th>count</th></tr>")
    for key, count in counts.most_common():
        parts.append(f"<tr><td>{esc(key)}</td><td>{count}</td></tr>")
    parts.append("</table>")

    director_rows = [
        row for row in rows
        if row.get("id") in {"finish", "ws", "ina", "spank"}
        or (row.get("id") == "smoke" and str(row.get("msg", "")).startswith("direct_h_orbit"))
    ]
    parts.append(f"<h2>H-Loop Director Events ({len(director_rows)})</h2>")
    parts.append("<table><tr><th>ut</th><th>id</th><th>msg</th><th>clip</th><th>path/step</th><th>data</th></tr>")
    for row in director_rows[-120:]:
        data = row.get("data", {})
        clip = data.get("clip", "") if isinstance(data, dict) else ""
        path = ""
        if isinstance(data, dict):
            path = data.get("path") or data.get("step") or data.get("input") or ""
        parts.append("<tr><td>{}</td><td>{}</td><td>{}</td><td>{}</td><td>{}</td><td><code>{}</code></td></tr>".format(
            esc(row.get("ut")), esc(row.get("id")), esc(row.get("msg")), esc(clip), esc(path),
            esc(json.dumps(data, ensure_ascii=False))
        ))
    parts.append("</table>")

    parts.append(f"<h2>Keyframes ({len(keyframes)})</h2>")
    parts.append("<table><tr><th>ut</th><th>id</th><th>msg</th><th>marker</th><th>screenshot</th><th>data</th></tr>")
    for row, screenshot, keyframe in keyframes[:120]:
        shot = ""
        if screenshot:
            shot = f"<img class='keyframe' src='{esc(image_src(screenshot))}'><br><span class='muted'>{esc(screenshot)}</span>"
        parts.append("<tr><td>{}</td><td>{}</td><td>{}</td><td>{}</td><td>{}</td><td><code>{}</code></td></tr>".format(
            esc(row.get("ut")), esc(row.get("id")), esc(row.get("msg")), esc(keyframe), shot,
            esc(json.dumps(row.get("data", {}), ensure_ascii=False))
        ))
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
