import sys
import tempfile
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))

import _assert_fsm_regression as reg  # noqa: E402
import orbit_trace_report as report  # noqa: E402


def row(ident, msg, ut=0.0, data=None, loc="event"):
    return {
        "runId": "test",
        "dll": "test.dll",
        "ts": 1,
        "ut": ut,
        "id": ident,
        "loc": loc,
        "msg": msg,
        "data": data or {},
    }


def slice0_snapshot_data(**extra):
    data = {
        "mode": 0,
        "modeCtrl": 0,
        "clip": "WLoop",
        "nowAnim": "Idle#id0;down0",
        "posePool": {"total": 10, "afterUnlock": 9, "afterFaintness": 8},
        "bhsAutoFinishEnabled": False,
        "bhsOffsetApplied": True,
        "bhsSolverEnabled": True,
    }
    data.update(extra)
    return data


def session_state_data(**extra):
    data = slice0_snapshot_data(
        fsmCell="ActionBridge",
        sessionFamily="A_Aibu",
        nowAnimId=2,
        nowAnimName="pose",
        actionCtrl1=0,
        actionCtrl2=0,
        clipNorm=1.25,
        finishVisible=[False, False, False, False, False],
    )
    data.update(extra)
    return data


class TraceRegressionTests(unittest.TestCase):
    def test_empty_trace_fails(self):
        issues = reg.analyze_rows([])
        self.assertEqual("empty_trace", issues[0].code)

    def test_legacy_recovery_is_error(self):
        issues = reg.analyze_rows([row("feel", "force_oloop_to_orgasm")])
        self.assertTrue(any(issue.code == "legacy_setplay_recovery" for issue in issues))

    def test_legacy_setplay_recovery_messages_are_not_in_csharp_sources(self):
        plugin_root = TOOLS.parent
        hits = []
        for path in plugin_root.rglob("*.cs"):
            if any(part in {"bin", "obj"} for part in path.parts):
                continue
            text = path.read_text(encoding="utf-8-sig", errors="ignore")
            for message in reg.LEGACY_RECOVERY_MESSAGES:
                if message in text:
                    hits.append(f"{path.relative_to(plugin_root)}:{message}")
        self.assertFalse(hits)

    def test_stale_selection_is_recovery_warning(self):
        issues = reg.analyze_rows([row("stale_sel", "cleared_nowChange_stuck")])
        self.assertTrue(any(issue.code == "stale_selection_recovered" and issue.severity == "warning" for issue in issues))
        self.assertFalse([issue for issue in issues if issue.severity == "error"])

    def test_action_bridge_vanilla_auto_selection_is_error(self):
        rows = [
            row(
                "session/state",
                "state",
                ut=1.0,
                data=session_state_data(
                    sessionIndex=3,
                    sessionFamily="A_Aibu",
                    clip="WLoop",
                    nowAnimId=18,
                    nowAnimName="A pose",
                    selAnimId=32,
                    selAnimName="C pose",
                    nowChangeAnim=True,
                    isAutoAction=True,
                ),
                loc="trace",
            )
        ]
        issues = reg.analyze_rows(rows)
        self.assertTrue(any(issue.code == "action_bridge_auto_selection" for issue in issues))

    def test_action_bridge_orbit_selection_without_auto_flag_is_allowed(self):
        rows = [
            row(
                "session/state",
                "state",
                ut=1.0,
                data=session_state_data(
                    clip="WLoop",
                    nowAnimId=18,
                    selAnimId=32,
                    nowChangeAnim=True,
                    isAutoAction=False,
                ),
                loc="trace",
            )
        ]
        issues = reg.analyze_rows(rows)
        self.assertFalse(any(issue.code == "action_bridge_auto_selection" for issue in issues))

    def test_sources_do_not_push_vanilla_auto_action_for_orbit_flow(self):
        plugin_root = TOOLS.parent
        forbidden = ("isAutoActionChange = true", 'Field("initiative").SetValue(1)')
        hits = []
        for path in plugin_root.rglob("*.cs"):
            if any(part in {"bin", "obj"} for part in path.parts):
                continue
            text = path.read_text(encoding="utf-8-sig", errors="ignore")
            for marker in forbidden:
                if marker in text:
                    hits.append(f"{path.relative_to(plugin_root)}:{marker}")
        self.assertFalse(hits)

    def test_finish_click_must_close(self):
        rows = [
            row("finish", "set_click", ut=1.0, data={"path": "drink"}),
            row("SNAP", "snapshot", ut=10.0, data={"clip": "OLoop"}),
        ]
        issues = reg.analyze_rows(rows, closure_seconds=4.0)
        self.assertTrue(any(issue.code == "finish_not_consumed" for issue in issues))

    def test_finish_click_closes_on_orgasm_clip(self):
        rows = [
            row("finish", "set_click", ut=1.0, data={"path": "drink"}),
            row("SNAP", "snapshot", ut=2.0, data={"clip": "Orgasm_IN"}),
        ]
        issues = reg.analyze_rows(rows)
        self.assertFalse([issue for issue in issues if issue.severity == "error"])

    def test_ina_pull_rejects_resume_loop(self):
        rows = [
            row("ina", "pull_click", ut=1.0),
            row("SNAP", "snapshot", ut=2.0, data={"clip": "WLoop"}),
        ]
        issues = reg.analyze_rows(rows)
        self.assertTrue(any(issue.code == "ina_resumed_loop" for issue in issues))

    def test_spanking_pulse_must_reach_action(self):
        rows = [
            row("spank", "click", ut=1.0),
            row("SNAP", "snapshot", ut=10.0, data={"clip": "WIdle"}),
        ]
        issues = reg.analyze_rows(rows, closure_seconds=4.0)
        self.assertTrue(any(issue.code == "spank_action_not_observed" for issue in issues))

    def test_spanking_pulse_closes_on_action_clip(self):
        rows = [
            row("spank", "click", ut=1.0),
            row("SNAP", "snapshot", ut=1.5, data={"clip": "WAction"}),
        ]
        issues = reg.analyze_rows(rows)
        self.assertFalse([issue for issue in issues if issue.severity == "error"])

    def test_spanking_finish_feel_must_reach_orgasm(self):
        rows = [
            row("spank", "finish_feel", ut=1.0),
            row("SNAP", "snapshot", ut=10.0, data={"clip": "SAction"}),
        ]
        issues = reg.analyze_rows(rows, closure_seconds=4.0)
        self.assertTrue(any(issue.code == "spank_finish_not_observed" for issue in issues))

    def test_spanking_finish_feel_closes_on_orgasm(self):
        rows = [
            row("spank", "finish_feel", ut=1.0),
            row("SNAP", "snapshot", ut=2.0, data={"clip": "Orgasm"}),
        ]
        issues = reg.analyze_rows(rows)
        self.assertFalse([issue for issue in issues if issue.severity == "error"])

    def test_spanking_finish_feel_closes_on_hash_now_orgasm(self):
        rows = [
            row("spank", "finish_feel", ut=1.0),
            row(
                "orgasm",
                "nowOrgasm",
                ut=2.0,
                data={"clip": "h=387472779;norm=0", "nowOrgasm": True},
            ),
        ]
        issues = reg.analyze_rows(rows)
        self.assertFalse([issue for issue in issues if issue.severity == "error"])

    def test_spanking_finish_feel_must_not_keep_clicking(self):
        rows = [
            row("spank", "finish_feel", ut=1.0),
            row("orgasm", "nowOrgasm", ut=2.0, data={"clip": "h=387472779;norm=0", "nowOrgasm": True}),
            row("spank", "click", ut=8.0, data={"clip": "WIdle"}),
        ]
        issues = reg.analyze_rows(rows)
        self.assertTrue(any(issue.code == "spank_click_after_finish" for issue in issues))

    def test_spanking_finish_handoff_allows_no_more_clicks(self):
        rows = [
            row("spank", "finish_feel", ut=1.0),
            row("orgasm", "nowOrgasm", ut=2.0, data={"clip": "h=387472779;norm=0", "nowOrgasm": True}),
            row("spank", "handoff_pool", ut=8.0, data={"clip": "WIdle"}),
        ]
        issues = reg.analyze_rows(rows)
        self.assertFalse([issue for issue in issues if issue.severity == "error"])

    def test_direct_h_load_must_reach_active_animation(self):
        rows = [
            row("smoke", "direct_h_load", ut=1.0),
            row("SNAP", "snapshot", ut=10.0, data={"mode": -1, "modeCtrl": -1, "clip": "?", "nowAnim": "#id-1;down0"}),
        ]
        issues = reg.analyze_rows(rows, closure_seconds=4.0, direct_h_seconds=4.0)
        self.assertTrue(any(issue.code == "direct_h_not_ready" for issue in issues))

    def test_direct_h_load_closes_on_active_animation(self):
        rows = [
            row("smoke", "direct_h_load", ut=1.0),
            row("smoke", "direct_h_orbit_on", ut=1.5),
            row("SNAP", "snapshot", ut=2.0, data=slice0_snapshot_data(orbit=True)),
            row("session/state", "state", ut=2.0, data=session_state_data(), loc="trace"),
        ]
        issues = reg.analyze_rows(rows)
        self.assertFalse([issue for issue in issues if issue.severity == "error"])

    def test_direct_h_load_requires_orbit_assist(self):
        rows = [
            row("smoke", "direct_h_load", ut=1.0),
            row("SNAP", "snapshot", ut=2.0, data=slice0_snapshot_data(orbit=False)),
            row("session/state", "state", ut=2.0, data=session_state_data(), loc="trace"),
        ]
        issues = reg.analyze_rows(rows, direct_h_seconds=4.0)
        self.assertTrue(any(issue.code == "direct_h_orbit_not_enabled" for issue in issues))

    def test_snapshot_without_slice0_fields_warns(self):
        rows = [
            row("SNAP", "snapshot", data={"mode": 0, "modeCtrl": 0, "clip": "WLoop", "nowAnim": "Idle#id0;down0"}),
        ]
        issues = reg.analyze_rows(rows)
        self.assertTrue(any(issue.code == "missing_slice0_trace" and issue.severity == "warning" for issue in issues))

    def test_snapshot_with_slice0_fields_does_not_warn(self):
        issues = reg.analyze_rows([
            row("SNAP", "snapshot", data=slice0_snapshot_data()),
            row("session/state", "state", data=session_state_data(), loc="trace"),
        ])
        self.assertFalse(any(issue.code == "missing_slice0_trace" for issue in issues))

    def test_snapshot_without_session_trace_warns(self):
        issues = reg.analyze_rows([row("SNAP", "snapshot", data=slice0_snapshot_data())])
        self.assertTrue(any(issue.code == "missing_session_trace" and issue.severity == "warning" for issue in issues))

    def test_session_trace_with_required_fields_does_not_warn(self):
        issues = reg.analyze_rows([
            row("SNAP", "snapshot", data=slice0_snapshot_data()),
            row("session/state", "state", data=session_state_data(), loc="trace"),
        ])
        self.assertFalse(any(issue.code.startswith("missing_session_trace") for issue in issues))

    def test_stage_duration_summary_and_anomaly_detection(self):
        rows = [
            row("session/state", "state", ut=1.0, data=session_state_data(sessionIndex=1, clip="Idle", fsmCell="Idle")),
            row("session/state", "state", ut=15.0, data=session_state_data(sessionIndex=1, clip="Idle", fsmCell="Idle")),
            row("session/state", "state", ut=16.0, data=session_state_data(sessionIndex=1, clip="WLoop", fsmCell="ActionBridge")),
        ]
        segments = report.build_stage_segments(rows)
        self.assertEqual(3, len(segments))
        self.assertAlmostEqual(14.0, segments[0].duration)
        summary = report.summarize_stage_segments(segments)
        idle = [item for item in summary if item["clip"] == "Idle"][0]
        self.assertAlmostEqual(15.0, idle["total"])
        anomalies = report.detect_stage_anomalies(rows, segments)
        self.assertTrue(any(item.code == "idle_long" for item in anomalies))

    def test_stage_runs_merge_consecutive_samples_for_duration_summary(self):
        rows = [
            row("session/state", "state", ut=1.0, data=session_state_data(sessionIndex=1, clip="WLoop", fsmCell="ActionBridge")),
            row("session/state", "state", ut=1.5, data=session_state_data(sessionIndex=1, clip="WLoop", fsmCell="ActionBridge")),
            row("session/state", "state", ut=2.0, data=session_state_data(sessionIndex=1, clip="SLoop", fsmCell="ActionBridge")),
            row("session/state", "state", ut=5.0, data=session_state_data(sessionIndex=1, clip="WLoop", fsmCell="ActionBridge")),
            row("session/state", "state", ut=5.5, data=session_state_data(sessionIndex=1, clip="WLoop", fsmCell="ActionBridge")),
        ]
        runs = report.build_stage_runs(report.build_stage_segments(rows))
        wloop_runs = [run for run in runs if run.clip == "WLoop"]
        self.assertEqual(2, len(wloop_runs))
        summary = report.summarize_stage_runs(runs)
        wloop = [item for item in summary if item["clip"] == "WLoop"][0]
        self.assertEqual(2, wloop["runs"])
        self.assertAlmostEqual(0.75, wloop["typical"])
        self.assertAlmostEqual(0.5, wloop["min"])
        self.assertAlmostEqual(1.0, wloop["max"])
        self.assertEqual(0, wloop["short_runs"])

    def test_stage_run_summary_flags_short_vs_long_mix(self):
        rows = [
            row("session/state", "state", ut=1.0, data=session_state_data(sessionIndex=1, clip="WLoop", fsmCell="ActionBridge")),
            row("session/state", "state", ut=1.05, data=session_state_data(sessionIndex=1, clip="SLoop", fsmCell="ActionBridge")),
            row("session/state", "state", ut=2.0, data=session_state_data(sessionIndex=1, clip="WLoop", fsmCell="ActionBridge")),
            row("session/state", "state", ut=7.0, data=session_state_data(sessionIndex=1, clip="SLoop", fsmCell="ActionBridge")),
        ]
        summary = report.summarize_stage_runs(report.build_stage_runs(report.build_stage_segments(rows)))
        wloop = [item for item in summary if item["clip"] == "WLoop"][0]
        self.assertEqual(2, wloop["runs"])
        self.assertEqual(1, wloop["short_runs"])
        self.assertAlmostEqual(0.05, wloop["min"])
        self.assertAlmostEqual(5.0, wloop["max"])
        self.assertEqual("short_vs_long", wloop["flag"])

    def test_coverage_counts_finish_paths_and_missing_families(self):
        rows = [
            row("session/state", "state", ut=1.0, data=session_state_data(sessionIndex=1, sessionFamily="C_Sonyu")),
            row("finish", "candidate", ut=2.0, data={"candidates": [{"path": "maleInside"}]}),
            row("finish", "set_click", ut=2.1, data={"path": "maleInside"}),
            row("finish", "consumed", ut=2.2, data={"path": "maleInside"}),
            row("ina", "pull_click", ut=3.0),
        ]
        coverage = report.build_coverage(rows, report.build_stage_segments(rows))
        self.assertIn("C_Sonyu", coverage["families"])
        self.assertIn("A_Aibu", coverage["missing_families"])
        self.assertEqual(1, coverage["finish_consumed"]["maleInside"])
        self.assertEqual(1, coverage["special"]["ina/pull_click"])

    def test_family_coverage_missing_is_error_when_coverage_started(self):
        rows = [
            row("smoke", "family_coverage_start", ut=0.0, data={"sequence": "A_Aibu,B_Houshi,C_Sonyu"}),
            row("session/state", "state", ut=1.0, data=session_state_data(sessionFamily="A_Aibu"), loc="trace"),
            row("session/state", "state", ut=2.0, data=session_state_data(sessionFamily="B_Houshi"), loc="trace"),
        ]
        issues = reg.analyze_rows(rows)
        self.assertTrue(any(issue.code == "family_coverage_missing" for issue in issues))

    def test_family_coverage_passes_with_runtime_family_evidence(self):
        rows = [
            row("smoke", "family_coverage_start", ut=0.0, data={"sequence": "A_Aibu,B_Houshi,C_Sonyu"}),
            row("session/state", "state", ut=1.0, data=session_state_data(sessionFamily="A_Aibu"), loc="trace"),
            row("session/state", "state", ut=2.0, data=session_state_data(sessionFamily="B_Houshi"), loc="trace"),
            row("session/state", "state", ut=3.0, data=session_state_data(sessionFamily="C_Sonyu"), loc="trace"),
        ]
        issues = reg.analyze_rows(rows)
        self.assertFalse(any(issue.code == "family_coverage_missing" for issue in issues))
        coverage = report.build_coverage(rows, report.build_stage_segments(rows))
        self.assertTrue(coverage["coverage_active"])
        self.assertEqual(1.0, coverage["family_timing"]["A_Aibu"]["first"])
        self.assertEqual(1, coverage["family_timing"]["A_Aibu"]["count"])

    def test_report_load_ignores_bad_json(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "trace.ndjson"
            path.write_text('{"id":"boot","msg":"ok"}\nnot-json\n', encoding="utf-8")
            rows = report.load_rows(path)
            self.assertEqual(1, len(rows))
            self.assertEqual("boot", rows[0]["id"])


if __name__ == "__main__":
    unittest.main()
