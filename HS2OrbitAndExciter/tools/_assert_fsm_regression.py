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


def _as_float(value: Any, fallback: float = 0.0) -> float:
    try:
        return float(value)
    except (TypeError, ValueError):
        return fallback


def _vector3(value: Any) -> tuple[float, float, float] | None:
    if not isinstance(value, list) or len(value) != 3:
        return None
    return (_as_float(value[0]), _as_float(value[1]), _as_float(value[2]))


def _distance3(a: tuple[float, float, float], b: tuple[float, float, float]) -> float:
    return ((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2 + (a[2] - b[2]) ** 2) ** 0.5


def _is_active_h_snapshot(row: dict[str, Any]) -> bool:
    data = _data(row)
    mode = _as_int(data.get("mode"))
    mode_ctrl = _as_int(data.get("modeCtrl"))
    clip = _as_text(data.get("clip"))
    now_anim = _as_text(data.get("nowAnim"))
    valid_anim = "#id-1" not in now_anim and now_anim not in ("", "?")
    valid_clip = clip not in ("", "?")
    return mode >= 0 and mode_ctrl >= 0 and (valid_anim or valid_clip)


def _is_orbit_enabled_snapshot(row: dict[str, Any]) -> bool:
    data = _data(row)
    return data.get("orbit") is True


def _has_slice0_fields(row: dict[str, Any]) -> bool:
    data = _data(row)
    pose_pool = data.get("posePool")
    return (
        isinstance(pose_pool, dict)
        and {"total", "afterUnlock", "afterFaintness"} <= set(pose_pool)
        and "bhsAutoFinishEnabled" in data
        and "bhsOffsetApplied" in data
        and "bhsSolverEnabled" in data
    )


def _has_session_trace_fields(row: dict[str, Any]) -> bool:
    data = _data(row)
    return (
        "fsmCell" in data
        and "sessionFamily" in data
        and "nowAnimId" in data
        and "nowAnimName" in data
        and "actionCtrl1" in data
        and "actionCtrl2" in data
        and "clipNorm" in data
        and "finishVisible" in data
    )


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

EXPECTED_COVERAGE_FAMILIES = {
    "A_Aibu",
    "B_Houshi",
    "C_Sonyu",
    "D_Masturbation",
    "E_Spnking",
    "A_Les",
}


def _split_family_sequence(value: Any) -> set[str]:
    if not isinstance(value, str):
        return set()
    return {part.strip() for part in value.split(",") if part.strip()}


def _runtime_family_evidence(row: dict[str, Any]) -> str:
    data = _data(row)
    ident = _as_text(row.get("id"))
    msg = _as_text(row.get("msg"))
    family = _as_text(data.get("sessionFamily"))
    if family in {"", "?", "Unknown", "Peeping"}:
        return ""
    if ident == "smoke" and msg == "stage_keyframe":
        return family
    if "fsmCell" in data and "clip" in data:
        return family
    return ""


def _is_action_bridge_auto_selection(row: dict[str, Any]) -> bool:
    if _as_text(row.get("id")) != "session/state":
        return False
    data = _data(row)
    if data.get("fsmCell") != "ActionBridge":
        return False
    if data.get("isAutoAction") is not True:
        return False
    if data.get("nowChangeAnim") is not True:
        return False
    now_anim_id = _as_int(data.get("nowAnimId"))
    sel_anim_id = _as_int(data.get("selAnimId"))
    if now_anim_id < 0 or sel_anim_id < 0:
        return False
    return now_anim_id != sel_anim_id


def _is_spank_finish_orgasm_evidence(row: dict[str, Any]) -> bool:
    data = _data(row)
    ident = _as_text(row.get("id"))
    msg = _as_text(row.get("msg"))
    if data.get("nowOrgasm") is True:
        return True
    if ident == "orgasm" and msg == "nowOrgasm":
        return True
    return _clip(row) in {"Orgasm", "D_Orgasm", "D_Orgasm_A"}


def _changing_focus_jump_off_live_bone(row: dict[str, Any]) -> tuple[bool, float]:
    if _as_text(row.get("id")) != "focus_jump":
        return (False, 0.0)
    data = _data(row)
    if data.get("director") != "Changing":
        return (False, 0.0)
    focus = _vector3(data.get("focusW1"))
    if focus is None:
        return (False, 0.0)
    distances: list[float] = []
    for key in ("head1", "chest1", "pelvis1"):
        bone = _vector3(data.get(key))
        if bone is not None:
            distances.append(_distance3(focus, bone))
    if not distances:
        return (False, 0.0)
    nearest = min(distances)
    return (nearest > 2.0, nearest)


def analyze_rows(
    rows: Iterable[dict[str, Any]],
    closure_seconds: float = 8.0,
    direct_h_seconds: float = 120.0,
) -> list[TraceIssue]:
    materialized = list(rows)
    issues: list[TraceIssue] = []

    if not materialized:
        return [TraceIssue("empty_trace", "Trace has no parseable NDJSON rows.")]

    coverage_start_index: int | None = None
    coverage_expected: set[str] = set()
    coverage_observed: set[str] = set()
    for index, row in enumerate(materialized):
        ident = _as_text(row.get("id"))
        msg = _as_text(row.get("msg"))
        if ident == "smoke" and msg == "family_coverage_start":
            if coverage_start_index is None:
                coverage_start_index = index
            coverage_expected.update(_split_family_sequence(_data(row).get("sequence")))
        family = _runtime_family_evidence(row)
        if family:
            coverage_observed.add(family)
    if coverage_start_index is not None and not coverage_expected:
        coverage_expected = set(EXPECTED_COVERAGE_FAMILIES)

    pending_finish: tuple[int, float, str] | None = None
    pending_pull: tuple[int, float] | None = None
    pending_spank: tuple[int, float] | None = None
    pending_spank_finish: tuple[int, float] | None = None
    pending_spank_handoff: int | None = None
    pending_direct_h: tuple[int, float] | None = None
    pending_direct_h_orbit: tuple[int, float] | None = None
    saw_snapshot = False
    saw_slice0_fields = False
    saw_session_state = False
    saw_session_trace_fields = False
    first_snapshot_index: int | None = None
    action_bridge_auto_selection_seen: set[tuple[int, int, int]] = set()

    for index, row in enumerate(materialized):
        ident = _as_text(row.get("id"))
        msg = _as_text(row.get("msg"))
        loc = _as_text(row.get("loc"))
        lower_msg = msg.lower()
        now = _ut(row, float(index))

        off_live_bone, nearest_live_bone = _changing_focus_jump_off_live_bone(row)
        if off_live_bone:
            issues.append(
                TraceIssue(
                    "focus_jump_off_live_bone",
                    f"Changing focus jump was {nearest_live_bone:.1f}m from the nearest live head/chest/pelvis bone.",
                    index,
                )
            )

        if ident == "SNAP":
            saw_snapshot = True
            if first_snapshot_index is None:
                first_snapshot_index = index
            if _has_slice0_fields(row):
                saw_slice0_fields = True

        if ident == "session/state":
            saw_session_state = True
            if _has_session_trace_fields(row):
                saw_session_trace_fields = True

        if ident == "pose_reject":
            issues.append(TraceIssue("pose_flow_issue", f"{ident}: {msg}", index))
        elif ident == "stale_sel":
            issues.append(TraceIssue("stale_selection_recovered", f"{ident}: {msg}", index, "warning"))

        if ident != "stale_sel" and ("fail" in lower_msg or "stuck" in lower_msg):
            issues.append(TraceIssue("failure_event", f"{ident}/{loc}: {msg}", index))

        if msg in LEGACY_RECOVERY_MESSAGES:
            issues.append(
                TraceIssue(
                    "legacy_setplay_recovery",
                    f"Legacy recovery still fired: {msg}. This should be diagnostic-only, not the H-loop main path.",
                    index,
                )
            )

        if _is_action_bridge_auto_selection(row):
            data = _data(row)
            key = (
                _as_int(data.get("sessionIndex")),
                _as_int(data.get("nowAnimId")),
                _as_int(data.get("selAnimId")),
            )
            if key not in action_bridge_auto_selection_seen:
                action_bridge_auto_selection_seen.add(key)
                issues.append(
                    TraceIssue(
                        "action_bridge_auto_selection",
                        "Vanilla auto-pose selection fired inside ActionBridge: "
                        f"family={_as_text(data.get('sessionFamily')) or '?'} "
                        f"clip={_as_text(data.get('clip')) or '?'} "
                        f"now=#{_as_int(data.get('nowAnimId'))} {_as_text(data.get('nowAnimName')) or '?'} "
                        f"sel=#{_as_int(data.get('selAnimId'))} {_as_text(data.get('selAnimName')) or '?'}.",
                        index,
                    )
                )

        if ident == "smoke" and msg == "direct_h_load":
            pending_direct_h = (index, now)
            pending_direct_h_orbit = (index, now)
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

        if pending_direct_h_orbit is not None:
            start_index, started_at = pending_direct_h_orbit
            if (ident == "smoke" and msg == "direct_h_orbit_on") or _is_orbit_enabled_snapshot(row):
                pending_direct_h_orbit = None
            elif now - started_at > direct_h_seconds:
                issues.append(
                    TraceIssue(
                        "direct_h_orbit_not_enabled",
                        f"Direct H smoke did not enable Orbit assist within {direct_h_seconds:g}s.",
                        start_index,
                    )
                )
                pending_direct_h_orbit = None

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

        if ident == "spank" and msg == "click":
            if pending_spank_handoff is not None:
                issues.append(
                    TraceIssue(
                        "spank_click_after_finish",
                        "Spnking wheel pulse continued after finish feel assist; expected handoff to pose pool.",
                        pending_spank_handoff,
                    )
                )
                pending_spank_handoff = None
            pending_spank = (index, now)
        elif ident == "spank" and msg == "finish_feel":
            pending_spank_finish = (index, now)
            pending_spank_handoff = index
        elif ident == "spank" and msg == "handoff_pool":
            pending_spank_handoff = None
        elif pending_spank_handoff is not None:
            family = _runtime_family_evidence(row)
            if family and family != "E_Spnking":
                pending_spank_handoff = None
        elif pending_spank is not None:
            start_index, started_at = pending_spank
            clip = _clip(row)
            if clip in {"WAction", "SAction", "D_Action"}:
                pending_spank = None
            elif now - started_at > closure_seconds:
                issues.append(
                    TraceIssue(
                        "spank_action_not_observed",
                        f"Spnking wheel pulse did not reach an Action clip within {closure_seconds:g}s.",
                        start_index,
                    )
                )
                pending_spank = None

        if pending_spank_finish is not None:
            start_index, started_at = pending_spank_finish
            if _is_spank_finish_orgasm_evidence(row):
                pending_spank_finish = None
            elif now - started_at > closure_seconds:
                issues.append(
                    TraceIssue(
                        "spank_finish_not_observed",
                        f"Spnking finish feel assist did not reach Orgasm/D_Orgasm within {closure_seconds:g}s.",
                        start_index,
                    )
                )
                pending_spank_finish = None

    if pending_finish is not None:
        start_index, _, path = pending_finish
        issues.append(
            TraceIssue("finish_not_consumed", f"Finish click path '{path or '?'}' has no consumed/Orgasm evidence.", start_index)
        )

    if pending_pull is not None:
        start_index, _ = pending_pull
        issues.append(TraceIssue("ina_pull_not_observed", "IN_A pull click has no Pull/Drop evidence.", start_index))

    if pending_spank is not None:
        start_index, _ = pending_spank
        issues.append(TraceIssue("spank_action_not_observed", "Spnking wheel pulse has no Action clip evidence.", start_index))

    if pending_spank_finish is not None:
        start_index, _ = pending_spank_finish
        issues.append(TraceIssue("spank_finish_not_observed", "Spnking finish feel assist has no Orgasm/D_Orgasm evidence.", start_index))

    if pending_direct_h is not None:
        start_index, _ = pending_direct_h
        issues.append(TraceIssue("direct_h_not_ready", "Direct H smoke has no active H animation evidence.", start_index))

    if pending_direct_h_orbit is not None:
        start_index, _ = pending_direct_h_orbit
        issues.append(TraceIssue("direct_h_orbit_not_enabled", "Direct H smoke has no Orbit assist enable evidence.", start_index))

    if saw_snapshot and not saw_slice0_fields:
        issues.append(
            TraceIssue(
                "missing_slice0_trace",
                "SNAP rows are missing Slice 0 posePool/BHS compatibility fields.",
                first_snapshot_index,
                "warning",
            )
        )

    if saw_snapshot and not saw_session_state:
        issues.append(
            TraceIssue(
                "missing_session_trace",
                "SNAP rows are present but no passive session/state trace rows were emitted.",
                first_snapshot_index,
                "warning",
            )
        )
    elif saw_session_state and not saw_session_trace_fields:
        issues.append(
            TraceIssue(
                "missing_session_trace_fields",
                "session/state rows are missing required Slice 1 fields.",
                first_snapshot_index,
                "warning",
            )
        )

    if coverage_start_index is not None:
        missing = sorted(coverage_expected - coverage_observed)
        if missing:
            issues.append(
                TraceIssue(
                    "family_coverage_missing",
                    "Smoke coverage did not observe runtime families: " + ", ".join(missing),
                    coverage_start_index,
                )
            )

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
