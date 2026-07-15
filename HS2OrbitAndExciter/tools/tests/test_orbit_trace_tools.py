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


class TraceRegressionTests(unittest.TestCase):
    def test_empty_trace_fails(self):
        issues = reg.analyze_rows([])
        self.assertEqual("empty_trace", issues[0].code)

    def test_legacy_recovery_is_error(self):
        issues = reg.analyze_rows([row("feel", "force_oloop_to_orgasm")])
        self.assertTrue(any(issue.code == "legacy_setplay_recovery" for issue in issues))

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
            row("SNAP", "snapshot", ut=2.0, data={"mode": 0, "modeCtrl": 0, "clip": "WLoop", "nowAnim": "Idle#id0;down0"}),
        ]
        issues = reg.analyze_rows(rows)
        self.assertFalse([issue for issue in issues if issue.severity == "error"])

    def test_report_load_ignores_bad_json(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "trace.ndjson"
            path.write_text('{"id":"boot","msg":"ok"}\nnot-json\n', encoding="utf-8")
            rows = report.load_rows(path)
            self.assertEqual(1, len(rows))
            self.assertEqual("boot", rows[0]["id"])


if __name__ == "__main__":
    unittest.main()
