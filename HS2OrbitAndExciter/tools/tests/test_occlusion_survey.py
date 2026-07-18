import sys
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

    def test_empty_rows_are_supported(self):
        result = survey.analyze_rows([])
        self.assertEqual([], result["ranking"])
        self.assertEqual(0, result["issueSamples"])


if __name__ == "__main__":
    unittest.main()
