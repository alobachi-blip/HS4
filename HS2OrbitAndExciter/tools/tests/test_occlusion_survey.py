import sys
import tempfile
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))

import analyze_occlusion_survey as survey  # noqa: E402


def sample(map_id, categories, **extra):
    data = {"mapId": map_id, "categories": categories, "originDelta": 0.1}
    data.update(extra)
    return {"id": "occlusion_survey", "msg": "sample", "data": data}


class OcclusionSurveyTests(unittest.TestCase):
    def test_ranking_counts_categories_and_maps(self):
        result = survey.analyze_rows(
            [
                sample(3, ["multi_layer_remaining", "false_hidden_mapping"], afterFirst="wall"),
                sample(5, ["multi_layer_remaining"], afterFirst="door"),
                sample(5, ["transition_stale_origin"], afterFirst="ceiling"),
            ]
        )
        self.assertEqual("multi_layer_remaining", result["ranking"][0]["category"])
        self.assertEqual(2, result["ranking"][0]["samples"])
        self.assertEqual(2, result["ranking"][0]["mapsAffected"])
        self.assertEqual(3, result["issueSamples"])

    def test_summary_supplies_total_sample_denominator(self):
        result = survey.analyze_rows(
            [
                sample(3, ["multi_layer_remaining"]),
                {
                    "runId": "a",
                    "id": "occlusion_survey",
                    "msg": "summary",
                    "data": {"samples": 100},
                },
            ]
        )
        self.assertEqual(100, result["totalSamples"])
        self.assertEqual(0.01, result["issueRate"])

    def test_shadow_nodes_are_not_counted_as_visual_occlusion(self):
        result = survey.analyze_rows(
            [
                sample(
                    8,
                    ["false_hidden_mapping"],
                    primary="false_hidden_mapping",
                    afterFirst="o_h2_mi_office00_00shadow",
                )
            ]
        )
        self.assertEqual(0, result["issueSamples"])
        self.assertEqual([], result["ranking"])

    def test_top_three_runtime_paths_are_present(self):
        plugin_root = TOOLS.parent
        vanish = (plugin_root / "OrbitMapVanishAssist.cs").read_text(
            encoding="utf-8-sig", errors="ignore"
        )
        production = (plugin_root / "OrbitOcclusion20Test.cs").read_text(
            encoding="utf-8-sig", errors="ignore"
        )
        self.assertIn(
            "HideDirectRendererOccluders(origin, direction, distance, camera ?? Camera.main);",
            vanish,
        )
        self.assertIn("QueryTriggerInteraction.Collide", vanish)
        self.assertIn("HideContainingGeometry(origin);", vanish)
        self.assertIn("PredictFinalCameraPosition(ctrl)", production)

    def test_empty_rows_are_supported(self):
        result = survey.analyze_rows([])
        self.assertEqual([], result["ranking"])
        self.assertEqual(0, result["issueSamples"])

    def test_loader_skips_unrelated_malformed_trace_rows(self):
        with tempfile.TemporaryDirectory() as tmp:
            trace = Path(tmp) / "trace.ndjson"
            trace.write_text(
                '{"id":"other","data":not-json}\n'
                '{"id":"occlusion_survey","msg":"sample",'
                '"data":{"mapId":3,"categories":["camera_inside_geometry"]}}\n',
                encoding="utf-8",
            )
            rows = survey.load_rows([trace])
        self.assertEqual(1, len(rows))


if __name__ == "__main__":
    unittest.main()
