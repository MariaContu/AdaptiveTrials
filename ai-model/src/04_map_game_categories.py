"""Map Steam games to Adaptive Trials categories.

Version 1.1 applies the configured top-tag limit, preserves category scores and
counts subgroups only inside the game's assigned macro category.
"""

from __future__ import annotations

import argparse
import csv
import json
import logging
import math
import sys
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any

PROJECT_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_INPUT = PROJECT_ROOT / "data" / "raw" / "game_metadata_raw.csv"
DEFAULT_CONFIG = PROJECT_ROOT / "config" / "category_mapping.json"
DEFAULT_OUTPUT = PROJECT_ROOT / "data" / "interim" / "game_category_scores.csv"
DEFAULT_SUMMARY = PROJECT_ROOT / "reports" / "metrics" / "04_game_category_mapping_summary.json"

LOGGER = logging.getLogger("game-category-mapper")

OUTPUT_FIELDS = [
    "appid", "name", "store_status", "eligible",
    "combat_score", "exploration_score", "puzzle_score",
    "top_category", "second_category", "top_score", "second_score",
    "score_gap", "dominance_ratio", "assigned_category",
    "assignment_reason", "assigned_subgroup",
    "combat_subgroup", "exploration_subgroup", "puzzle_subgroup",
    "considered_tags_json", "matched_tags_json", "matched_genres_json",
]


def configure_logging() -> None:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s | %(levelname)s | %(message)s",
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Map Steam games to combat, exploration and puzzle."
    )
    parser.add_argument("--input", type=Path, default=DEFAULT_INPUT)
    parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--summary-output", type=Path, default=DEFAULT_SUMMARY)
    parser.add_argument(
        "--include-not-found",
        action="store_true",
        help="Evaluate not_found rows using complementary metadata when possible.",
    )
    return parser.parse_args()


def normalize(value: Any) -> str:
    return "" if value is None else str(value).strip()


def safe_float(value: Any) -> float:
    try:
        return float(value)
    except (TypeError, ValueError):
        return 0.0


def load_json(path: Path) -> dict[str, Any]:
    if not path.exists():
        raise FileNotFoundError(path)
    with path.open("r", encoding="utf-8") as file:
        payload = json.load(file)
    if not isinstance(payload, dict):
        raise ValueError(f"JSON root must be an object: {path}")
    return payload


def parse_json_cell(value: Any, expected: type) -> Any:
    text = normalize(value)
    if not text:
        return expected()
    try:
        parsed = json.loads(text)
    except json.JSONDecodeError:
        return expected()
    return parsed if isinstance(parsed, expected) else expected()


def normalize_lookup(mapping: dict[str, Any]) -> dict[str, float]:
    return {
        normalize(key).casefold(): safe_float(weight)
        for key, weight in mapping.items()
        if normalize(key)
    }


def select_top_tags(tags: dict[str, Any], limit: int) -> dict[str, float]:
    normalized = [
        (normalize(tag), max(safe_float(popularity), 0.0))
        for tag, popularity in tags.items()
        if normalize(tag)
    ]
    normalized.sort(key=lambda item: (-item[1], item[0].casefold()))
    if limit > 0:
        normalized = normalized[:limit]
    return dict(normalized)


def score_category(
    tags: dict[str, float],
    genres: list[Any],
    category_config: dict[str, Any],
) -> tuple[float, list[str], list[str], dict[str, float]]:
    tag_weights = normalize_lookup(category_config.get("tags", {}))
    genre_weights = normalize_lookup(category_config.get("genres", {}))

    total = 0.0
    matched_tags: list[str] = []
    matched_genres: list[str] = []
    tag_contributions: dict[str, float] = {}

    for tag, popularity in tags.items():
        weight = tag_weights.get(tag.casefold())
        if weight is None:
            continue
        contribution = weight * math.log1p(popularity)
        total += contribution
        matched_tags.append(tag)
        tag_contributions[tag] = contribution

    for raw_genre in genres:
        genre = normalize(raw_genre)
        weight = genre_weights.get(genre.casefold())
        if weight is None:
            continue
        total += weight
        matched_genres.append(genre)

    return (
        total,
        sorted(set(matched_tags)),
        sorted(set(matched_genres)),
        tag_contributions,
    )


def determine_subgroup(
    tag_contributions: dict[str, float],
    subgroup_config: dict[str, list[str]],
) -> str:
    contribution_lookup = {
        tag.casefold(): value for tag, value in tag_contributions.items()
    }
    subgroup_scores: dict[str, float] = {}

    for subgroup, subgroup_tags in subgroup_config.items():
        subgroup_scores[subgroup] = sum(
            contribution_lookup.get(normalize(tag).casefold(), 0.0)
            for tag in subgroup_tags
        )

    if not subgroup_scores:
        return ""

    ordered = sorted(
        subgroup_scores.items(),
        key=lambda item: (-item[1], item[0]),
    )
    if ordered[0][1] <= 0:
        return ""

    if len(ordered) > 1 and math.isclose(
        ordered[0][1], ordered[1][1], rel_tol=1e-9, abs_tol=1e-9
    ):
        return "ambiguous"

    return ordered[0][0]


def classify(
    scores: dict[str, float],
    methodology: dict[str, Any],
) -> tuple[str, str, str, str, float, float, float, float]:
    ordered = sorted(scores.items(), key=lambda item: (-item[1], item[0]))
    top_category, top_score = ordered[0]
    second_category, second_score = ordered[1]

    gap = top_score - second_score
    dominance = (
        top_score / second_score
        if second_score > 0
        else (float("inf") if top_score > 0 else 0.0)
    )

    minimum_score = safe_float(methodology.get("minimum_category_score", 1.0))
    minimum_gap = safe_float(methodology.get("minimum_score_gap", 0.5))
    minimum_ratio = safe_float(methodology.get("minimum_dominance_ratio", 1.15))

    if top_score < minimum_score:
        return (
            normalize(methodology.get("uncategorized_label")) or "uncategorized",
            "insufficient_score",
            top_category, second_category,
            top_score, second_score, gap, dominance,
        )
    if gap < minimum_gap:
        return (
            normalize(methodology.get("ambiguous_label")) or "ambiguous",
            "insufficient_gap",
            top_category, second_category,
            top_score, second_score, gap, dominance,
        )
    if dominance < minimum_ratio:
        return (
            normalize(methodology.get("ambiguous_label")) or "ambiguous",
            "insufficient_dominance",
            top_category, second_category,
            top_score, second_score, gap, dominance,
        )

    return (
        top_category, "dominant_category", top_category, second_category,
        top_score, second_score, gap, dominance,
    )


def write_csv_atomic(path: Path, rows: list[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")
    with temp.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=OUTPUT_FIELDS)
        writer.writeheader()
        writer.writerows(rows)
    temp.replace(path)


def write_json_atomic(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")
    with temp.open("w", encoding="utf-8") as file:
        json.dump(payload, file, ensure_ascii=False, indent=2)
    temp.replace(path)


def main() -> int:
    configure_logging()
    args = parse_args()

    try:
        config = load_json(args.config)
        categories = config.get("categories")
        methodology = config.get("methodology")

        if not isinstance(categories, dict) or set(categories) != {
            "combat", "exploration", "puzzle"
        }:
            raise ValueError(
                "Config must define exactly combat, exploration and puzzle."
            )
        if not isinstance(methodology, dict):
            raise ValueError("Config must contain methodology.")

        top_tags_limit = int(methodology.get("top_tags_limit", 20))

        rows: list[dict[str, Any]] = []
        status_counts: Counter[str] = Counter()
        assignment_counts: Counter[str] = Counter()
        reason_counts: Counter[str] = Counter()
        assigned_subgroup_counts: dict[str, Counter[str]] = defaultdict(Counter)
        eligible_count = 0

        with args.input.open("r", encoding="utf-8", newline="") as file:
            reader = csv.DictReader(file)
            required = {
                "appid", "name", "store_status",
                "genres_json", "steamspy_tags_json"
            }
            if reader.fieldnames is None or not required.issubset(reader.fieldnames):
                raise ValueError(f"Input CSV must contain: {sorted(required)}")

            for row in reader:
                appid = normalize(row.get("appid"))
                name = normalize(row.get("name"))
                store_status = normalize(row.get("store_status"))
                status_counts[store_status] += 1

                eligible = store_status == "success" or (
                    args.include_not_found and store_status == "not_found"
                )

                scores = {"combat": 0.0, "exploration": 0.0, "puzzle": 0.0}
                matched_tags_by_category: dict[str, list[str]] = {}
                matched_genres_by_category: dict[str, list[str]] = {}
                subgroups = {"combat": "", "exploration": "", "puzzle": ""}
                considered_tags: dict[str, float] = {}

                assigned = "excluded"
                reason = f"store_status_{store_status or 'missing'}"
                top_category = ""
                second_category = ""
                top_score = second_score = gap = dominance = 0.0
                assigned_subgroup = ""

                if eligible:
                    eligible_count += 1
                    raw_tags = parse_json_cell(
                        row.get("steamspy_tags_json"), dict
                    )
                    considered_tags = select_top_tags(raw_tags, top_tags_limit)
                    genres = parse_json_cell(row.get("genres_json"), list)

                    for category, category_config in categories.items():
                        (
                            score,
                            matched_tags,
                            matched_genres,
                            tag_contributions,
                        ) = score_category(
                            considered_tags, genres, category_config
                        )
                        scores[category] = score
                        matched_tags_by_category[category] = matched_tags
                        matched_genres_by_category[category] = matched_genres
                        subgroups[category] = determine_subgroup(
                            tag_contributions,
                            category_config.get("subgroups", {}),
                        )

                    (
                        assigned,
                        reason,
                        top_category,
                        second_category,
                        top_score,
                        second_score,
                        gap,
                        dominance,
                    ) = classify(scores, methodology)

                    if assigned in categories:
                        assigned_subgroup = subgroups[assigned]
                        if assigned_subgroup:
                            assigned_subgroup_counts[assigned][
                                assigned_subgroup
                            ] += 1

                assignment_counts[assigned] += 1
                reason_counts[reason] += 1

                rows.append({
                    "appid": appid,
                    "name": name,
                    "store_status": store_status,
                    "eligible": eligible,
                    "combat_score": round(scores["combat"], 6),
                    "exploration_score": round(scores["exploration"], 6),
                    "puzzle_score": round(scores["puzzle"], 6),
                    "top_category": top_category,
                    "second_category": second_category,
                    "top_score": round(top_score, 6),
                    "second_score": round(second_score, 6),
                    "score_gap": round(gap, 6),
                    "dominance_ratio": (
                        "inf" if math.isinf(dominance)
                        else round(dominance, 6)
                    ),
                    "assigned_category": assigned,
                    "assignment_reason": reason,
                    "assigned_subgroup": assigned_subgroup,
                    "combat_subgroup": subgroups["combat"],
                    "exploration_subgroup": subgroups["exploration"],
                    "puzzle_subgroup": subgroups["puzzle"],
                    "considered_tags_json": json.dumps(
                        considered_tags,
                        ensure_ascii=False,
                        sort_keys=True,
                    ),
                    "matched_tags_json": json.dumps(
                        matched_tags_by_category,
                        ensure_ascii=False,
                        sort_keys=True,
                    ),
                    "matched_genres_json": json.dumps(
                        matched_genres_by_category,
                        ensure_ascii=False,
                        sort_keys=True,
                    ),
                })

        write_csv_atomic(args.output, rows)

        assigned_total = sum(
            assignment_counts.get(category, 0)
            for category in categories
        )
        assignment_percentages = {
            label: round(count / len(rows), 6) if rows else 0.0
            for label, count in sorted(assignment_counts.items())
        }
        assigned_category_percentages = {
            category: round(
                assignment_counts.get(category, 0) / assigned_total, 6
            ) if assigned_total else 0.0
            for category in sorted(categories)
        }

        summary = {
            "pipeline_stage": "04_map_game_categories",
            "mapping_version": config.get("version"),
            "total_rows": len(rows),
            "eligible_rows": eligible_count,
            "store_status_counts": dict(sorted(status_counts.items())),
            "assignment_counts": dict(sorted(assignment_counts.items())),
            "assignment_percentages_total": assignment_percentages,
            "assigned_category_percentages": assigned_category_percentages,
            "assignment_reason_counts": dict(sorted(reason_counts.items())),
            "assigned_subgroup_counts": {
                category: dict(sorted(counter.items()))
                for category, counter in sorted(
                    assigned_subgroup_counts.items()
                )
            },
            "methodology": methodology,
            "important_note": (
                "The mapping is rule-based and versioned. Only the configured "
                "top SteamSpy tags are scored, and subgroup totals refer only "
                "to games assigned to the corresponding macro category."
            ),
        }
        write_json_atomic(args.summary_output, summary)

    except (OSError, ValueError, json.JSONDecodeError) as error:
        LOGGER.exception("Category mapping failed: %s", error)
        return 1

    LOGGER.info(
        "Category mapping completed: rows=%d, eligible=%d.",
        len(rows),
        eligible_count,
    )
    LOGGER.info("Output: %s", args.output)
    LOGGER.info("Summary: %s", args.summary_output)
    return 0


if __name__ == "__main__":
    sys.exit(main())