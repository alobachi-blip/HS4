#!/usr/bin/env python3
"""Regression assertions for Orbit FSM/session NDJSON traces.

This tool is intentionally conservative: it flags known bad symptoms and
future H-loop closure failures, but it does not try to infer gameplay success
from ordinary text logs.
"""

from __future__ import annotations

import argparse
import json
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable


@dataclass(frozen=True)
class TraceIssue:
    code: str
    message: str
    row_index: int | None = None
    severity: str = "error"


def load_rows(path: Path) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for line in path.read_text(encoding="utf-8-sig", errors="replace").splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            obj = json.loads(line)
        except json.JSONDecodeError:
            continue
        if isinstance(obj, dict):
            rows.append(obj)
    return rows


def _as_text(value: Any) -> str:
    return "" if value is None else str(value)


def _clip(row: dict[str, Any]) -> str:
    data = row.get("data", {})
    if isinstance(data, dict):
        return _as_text(data.get("clip"))
    return ""


def _data(row: dict[str, Any]) -> dict[str, Any]:
    data = row.get("data", {})
    return data if isinstance(data, dict) else {}


def _as_int(value: Any, fallback: int = -1) -> int:
    try:
        return int(value)
    except (TypeError, ValueError):
        return fallback


def _is_active_h_snapshot(row: dict[str, Any]) -> bool:
    data = _data(row)
    mode = _as_int(data.get("mode"))
    mode_ctrl = _as_int(data.get("modeCtrl"))
    clip = _as_text(data.get("clip"))
    now_anim = _as_text(data.get("nowAnim"))
    valid_anim = "#id-1" not in now_anim and now_anim not in ("", "?")
    valid_clip = clip not in ("", "?")
    return mode >= 0 and mode_ctrl >= 0 and (valid_anim or valid_clip)


def _ut(row: dict[str, Any], fallback: float) -> float:
    try:
        return float(row.get("ut", fallback))
    except (TypeError, ValueError):
        return fallback


LEGACY_RECOVERY_MESSAGES = {
    "force_ws_to_oloop",
    "force_oloop_to_orgasm",
    "force_insert_to_wloop",
    "force_spank_to_orgasm",
}


def analyze_rows(
    rows: Iterable[dict[str, Any]],
    closure_seconds: float = 8.0,
    direct_h_seconds: float = 120.0,
) -> list[TraceIssue]:
    materialized = list(rows)
    issues: list[TraceIssue] = []

    if not materialized:
        return [TraceIssue("empty_trace", "Trace has no parseable NDJSON rows.")]

    pending_finish: tuple[int, float, str] | None = None
    pending_pull: tuple[int, float] | None = None
    pending_direct_h: tuple[int, float] | None = None

    for index, row in enumerate(materialized):
        ident = _as_text(row.get("id"))
        msg = _as_text(row.get("msg"))
        loc = _as_text(row.get("loc"))
        lower_msg = msg.lower()
        now = _ut(row, float(index))

        if ident in {"pose_reject", "stale_sel"}:
            issues.append(TraceIssue("pose_flow_issue", f"{ident}: {msg}", index))

        if "fail" in lower_msg or "stuck" in lower_msg:
            issues.append(TraceIssue("failure_event", f"{ident}/{loc}: {msg}", index))

        if msg in LEGACY_RECOVERY_MESSAGES:
            issues.append(
                TraceIssue(
                    "legacy_setplay_recovery",
                    f"Legacy recovery still fired: {msg}. This should be diagnostic-only, not the H-loop main path.",
                    index,
                )
            )

        if ident == "smoke" and msg == "direct_h_load":
            pending_direct_h = (index, now)
        elif pending_direct_h is not None:
            start_index, started_at = pending_direct_h
            if _is_active_h_snapshot(row):
                pending_direct_h = None
            elif now - started_at > direct_h_seconds:
                issues.append(
                    TraceIssue(
                        "direct_h_not_ready",
                        f"Direct H smoke loaded HScene but did not reach an active H animation within {direct_h_seconds:g}s.",
                        start_index,
                    )
                )
                pending_direct_h = None

        if ident == "gate":
            data = row.get("data", {})
            if isinstance(data, dict) and data.get("suppress") not in (None, "", "none"):
                issues.append(TraceIssue("gate_suppressed", f"Gate suppressed: {data.get('suppress')}", index, "warning"))

        if ident == "finish" and msg == "set_click":
            data = row.get("data", {})
            path = _as_text(data.get("path") if isinstance(data, dict) else "")
            pending_finish = (index, now, path)
        elif ident == "finish" and msg == "consumed":
            pending_finish = None

        if pending_finish is not None:
            start_index, started_at, path = pending_finish
            clip = _clip(row)
            if clip.startswith("Orgasm") or "_A" in clip or msg == "consumed":
                pending_finish = None
            elif now - started_at > closure_seconds:
                issues.append(
                    TraceIssue(
                        "finish_not_consumed",
                        f"Finish click path '{path or '?'}' was not consumed within {closure_seconds:g}s.",
                        start_index,
                    )
                )
                pending_finish = None

        if ident == "ina" and msg == "pull_click":
            pending_pull = (index, now)
        elif pending_pull is not None:
            start_index, started_at = pending_pull
            clip = _clip(row)
            if clip in {"Pull", "Drop", "OrgasmM_OUT_A"} or "Drop" in clip:
                pending_pull = None
            elif clip in {"WLoop", "SLoop", "OLoop"}:
                issues.append(
                    TraceIssue(
                        "ina_resumed_loop",
                        "Orgasm_IN_A resumed loop instead of taking Pull -> Drop.",
                        index,
                    )
                )
                pending_pull = None
            elif now - started_at > closure_seconds:
                issues.append(
                    TraceIssue(
                        "ina_pull_not_observed",
                        f"IN_A pull click did not reach Pull/Drop within {closure_seconds:g}s.",
                        start_index,
                    )
                )
                pending_pull = None

    if pending_finish is not None:
        start_index, _, path = pending_finish
        issues.append(
            TraceIssue("finish_not_consumed", f"Finish click path '{path or '?'}' has no consumed/Orgasm evidence.", start_index)
        )

    if pending_pull is not None:
        start_index, _ = pending_pull
        issues.append(TraceIssue("ina_pull_not_observed", "IN_A pull click has no Pull/Drop evidence.", start_index))

    if pending_direct_h is not None:
        start_index, _ = pending_direct_h
        issues.append(TraceIssue("direct_h_not_ready", "Direct H smoke has no active H animation evidence.", start_index))

    return issues


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Assert Orbit FSM/session trace regressions.")
    parser.add_argument("trace", type=Path, help="Path to HS2OrbitAndExciter_fsm.ndjson")
    parser.add_argument("--closure-seconds", type=float, default=8.0)
    parser.add_argument(
        "--direct-h-seconds",
        type=float,
        default=120.0,
        help="Maximum allowed time from DirectH scene request to active H animation evidence.",
    )
    parser.add_argument("--json", action="store_true", help="Print machine-readable issue list.")
    args = parser.parse_args(argv)

    rows = load_rows(args.trace)
    issues = analyze_rows(rows, closure_seconds=args.closure_seconds, direct_h_seconds=args.direct_h_seconds)
    errors = [issue for issue in issues if issue.severity == "error"]

    if args.json:
        print(json.dumps([issue.__dict__ for issue in issues], ensure_ascii=False, indent=2))
    else:
        if issues:
            print(f"Orbit trace issues: {len(issues)} ({len(errors)} errors)")
            for issue in issues:
                where = "" if issue.row_index is None else f" row={issue.row_index}"
                print(f"[{issue.severity}] {issue.code}{where}: {issue.message}")
        else:
            print(f"Orbit trace OK: {len(rows)} rows, no regression issues.")

    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
