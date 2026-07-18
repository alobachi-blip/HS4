#!/usr/bin/env python3
"""Aggregate diagnostic-only non-character occlusion survey traces."""

from __future__ import annotations

import argparse
import json
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any, Iterable


NON_OCCLUDING_NAME_PARTS = ("shadow",)


def load_rows(paths: Iterable[Path]) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for path in paths:
        with path.open("r", encoding="utf-8-sig", errors="replace") as stream:
            for line_no, line in enumerate(stream, 1):
                if not line.strip():
                    continue
                try:
                    row = json.loads(line)
                except json.JSONDecodeError:
                    # The shared FSM trace has a few legacy non-JSON diagnostic
                    # events. They are unrelated to this survey and must not
                    # invalidate otherwise complete occlusion evidence.
                    continue
                if row.get("id") == "occlusion_survey":
                    data = row.get("data", {})
                    if isinstance(data, str):
                        data = json.loads(data)
                    if isinstance(data, dict):
                        rows.append({**row, "data": data, "trace": str(path)})
    return rows


def analyze_rows(rows: Iterable[dict[str, Any]]) -> dict[str, Any]:
    rows = list(rows)
    sample_rows = [row for row in rows if row.get("msg") == "sample"]
    primary_categories: Counter[str] = Counter()
    signals: Counter[str] = Counter()
    maps: dict[int, Counter[str]] = defaultdict(Counter)
    blockers: dict[str, Counter[str]] = defaultdict(Counter)
    issue_samples_by_map: Counter[int] = Counter()
    origin_deltas: list[float] = []
    included_samples = 0

    total_samples_by_trace: Counter[str] = Counter()
    for row in rows:
        if row.get("msg") != "summary":
            continue
        data = row.get("data") or {}
        trace = str(row.get("trace", row.get("runId", "unknown")))
        try:
            total_samples_by_trace[trace] = max(
                total_samples_by_trace[trace], int(data.get("samples", 0))
            )
        except (TypeError, ValueError):
            pass

    for row in sample_rows:
        data = row.get("data") or {}
        map_id = int(data.get("mapId", -1))
        row_categories = [str(value) for value in data.get("categories", [])]
        primary = str(data.get("primary") or (row_categories[0] if row_categories else "unknown"))
        if primary == "camera_inside_geometry":
            name = data.get("insideFirst") or "unknown"
        elif primary == "renderer_without_collider":
            name = data.get("rendererOnly") or "unknown"
        else:
            name = data.get("afterFirst") or data.get("finalFirst") or "unknown"
        if any(part in str(name).lower() for part in NON_OCCLUDING_NAME_PARTS):
            continue

        included_samples += 1
        issue_samples_by_map[map_id] += 1
        try:
            origin_deltas.append(float(data.get("originDelta", 0)))
        except (TypeError, ValueError):
            pass
        for category in row_categories:
            signals[category] += 1
        if primary != "unknown":
            primary_categories[primary] += 1
            maps[map_id][primary] += 1
            blockers[primary][str(name)] += 1

    ranking = [
        {
            "rank": rank,
            "category": category,
            "samples": count,
            "shareOfIssueSamples": round(count / included_samples, 6) if included_samples else 0.0,
            "mapsAffected": sum(1 for value in maps.values() if value[category]),
            "topBlockers": [
                {"name": name, "samples": blocker_count}
                for name, blocker_count in blockers[category].most_common(10)
            ],
        }
        for rank, (category, count) in enumerate(primary_categories.most_common(), 1)
    ]
    return {
        "scope": {
            "charactersExcluded": True,
            "smallPartialEdgesExcluded": True,
            "method": "single center ray from camera to focus",
        },
        "totalSamples": sum(total_samples_by_trace.values()) or len(sample_rows),
        "issueSamples": included_samples,
        "issueRate": round(
            included_samples / (sum(total_samples_by_trace.values()) or included_samples), 6
        ) if included_samples else 0.0,
        "signalCounts": dict(signals.most_common()),
        "maps": {
            str(map_id): {
                "issueSamples": issue_samples_by_map[map_id],
                "categories": dict(counter.most_common()),
            }
            for map_id, counter in sorted(maps.items())
        },
        "ranking": ranking,
        "originDelta": {
            "max": max(origin_deltas, default=0.0),
            "mean": round(sum(origin_deltas) / len(origin_deltas), 6) if origin_deltas else 0.0,
        },
    }


def render_markdown(result: dict[str, Any], trace_count: int) -> str:
    lines = [
        "# Non-character occlusion survey",
        "",
        f"Traces: {trace_count}; samples: {result['totalSamples']}; "
        f"issue samples: {result['issueSamples']} ({result['issueRate']:.1%}).",
        "Characters and tiny partial-edge overlaps are excluded; a center ray is used.",
        "",
        "| Rank | Category | Samples | Issue share | Maps |",
        "|---:|---|---:|---:|---:|",
    ]
    for item in result["ranking"]:
        lines.append(
            f"| {item['rank']} | `{item['category']}` | {item['samples']} | "
            f"{item['shareOfIssueSamples']:.1%} | {item['mapsAffected']} |"
        )
    lines.extend(["", "## Per map", ""])
    for map_id, item in result["maps"].items():
        counts = ", ".join(f"`{key}`={value}" for key, value in item["categories"].items())
        lines.append(f"- Map {map_id}: {item['issueSamples']} issue samples; {counts}")
    lines.append("")
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("traces", nargs="+", type=Path)
    parser.add_argument("--json", dest="json_path", type=Path)
    parser.add_argument("--markdown", dest="markdown_path", type=Path)
    args = parser.parse_args()

    paths = [path for path in args.traces if path.is_file()]
    if not paths:
        parser.error("no trace files found")
    result = analyze_rows(load_rows(paths))
    output = json.dumps(result, ensure_ascii=False, indent=2)
    print(output)
    if args.json_path:
        args.json_path.parent.mkdir(parents=True, exist_ok=True)
        args.json_path.write_text(output + "\n", encoding="utf-8")
    if args.markdown_path:
        args.markdown_path.parent.mkdir(parents=True, exist_ok=True)
        args.markdown_path.write_text(render_markdown(result, len(paths)), encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
