"""Map Steam games to combat, exploration and strategic reasoning."""
from __future__ import annotations
import argparse, csv, json, logging, math, sys
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_INPUT = ROOT / "data/raw/game_metadata_raw.csv"
DEFAULT_CONFIG = ROOT / "config/category_mapping_v2.json"
DEFAULT_OUTPUT = ROOT / "data/interim/game_category_scores_v2.csv"
DEFAULT_SUMMARY = ROOT / "reports/metrics/04b_game_category_mapping_summary.json"
CATEGORIES = ("combat", "exploration", "strategic_reasoning")
LOGGER = logging.getLogger("revised-category-mapper")
FIELDS = [
    "appid", "name", "store_status", "eligible", "combat_score",
    "exploration_score", "strategic_reasoning_score", "top_category",
    "second_category", "top_score", "second_score", "score_gap",
    "dominance_ratio", "assigned_category", "assignment_reason",
    "assigned_subgroup", "combat_subgroup", "exploration_subgroup",
    "strategic_reasoning_subgroup", "considered_tags_json",
    "matched_tags_json", "matched_genres_json"
]

def norm(v: Any) -> str:
    return "" if v is None else str(v).strip()

def fnum(v: Any) -> float:
    try: return float(v)
    except (TypeError, ValueError): return 0.0

def parse_cell(v: Any, expected: type):
    text = norm(v)
    if not text: return expected()
    try: parsed = json.loads(text)
    except json.JSONDecodeError: return expected()
    return parsed if isinstance(parsed, expected) else expected()

def lookup(d: dict[str, Any]) -> dict[str, float]:
    return {norm(k).casefold(): fnum(w) for k, w in d.items() if norm(k)}

def top_tags(tags: dict[str, Any], limit: int) -> dict[str, float]:
    items = [(norm(t), max(fnum(p), 0.0)) for t, p in tags.items() if norm(t)]
    items.sort(key=lambda x: (-x[1], x[0].casefold()))
    return dict(items[:limit] if limit > 0 else items)

def contains_any(values, excluded: list[str]) -> bool:
    src = values.keys() if isinstance(values, dict) else values
    available = {norm(v).casefold() for v in src}
    return any(norm(v).casefold() in available for v in excluded)

def score(tags: dict[str, float], genres: list[Any], cfg: dict[str, Any]):
    tw, gw = lookup(cfg.get("tags", {})), lookup(cfg.get("genres", {}))
    total, mt, mg, contrib = 0.0, [], [], {}
    for tag, pop in tags.items():
        weight = tw.get(tag.casefold())
        if weight is None: continue
        value = weight * math.log1p(pop)
        total += value; mt.append(tag); contrib[tag] = value
    for raw in genres:
        genre = norm(raw); weight = gw.get(genre.casefold())
        if weight is None: continue
        total += weight; mg.append(genre)
    return total, sorted(set(mt)), sorted(set(mg)), contrib

def subgroup(contrib: dict[str, float], cfg: dict[str, list[str]]) -> str:
    values = {k.casefold(): v for k, v in contrib.items()}
    scores = {name: sum(values.get(norm(tag).casefold(), 0.0) for tag in tags)
              for name, tags in cfg.items()}
    ordered = sorted(scores.items(), key=lambda x: (-x[1], x[0]))
    if not ordered or ordered[0][1] <= 0: return ""
    if len(ordered) > 1 and math.isclose(ordered[0][1], ordered[1][1], rel_tol=1e-9, abs_tol=1e-9):
        return "ambiguous"
    return ordered[0][0]

def classify(scores: dict[str, float], m: dict[str, Any]):
    ordered = sorted(scores.items(), key=lambda x: (-x[1], x[0]))
    top, second = ordered[0], ordered[1]
    gap = top[1] - second[1]
    ratio = top[1] / second[1] if second[1] > 0 else (float("inf") if top[1] > 0 else 0.0)
    if top[1] < fnum(m.get("minimum_category_score", 1.0)):
        assigned, reason = m.get("uncategorized_label", "uncategorized"), "insufficient_score"
    elif gap < fnum(m.get("minimum_score_gap", 0.5)):
        assigned, reason = m.get("ambiguous_label", "ambiguous"), "insufficient_gap"
    elif ratio < fnum(m.get("minimum_dominance_ratio", 1.15)):
        assigned, reason = m.get("ambiguous_label", "ambiguous"), "insufficient_dominance"
    else:
        assigned, reason = top[0], "dominant_category"
    return assigned, reason, top[0], second[0], top[1], second[1], gap, ratio

def atomic_csv(path: Path, rows: list[dict[str, Any]]):
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + ".tmp")
    with tmp.open("w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=FIELDS); w.writeheader(); w.writerows(rows)
    tmp.replace(path)

def atomic_json(path: Path, payload: dict[str, Any]):
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + ".tmp")
    with tmp.open("w", encoding="utf-8") as f:
        json.dump(payload, f, ensure_ascii=False, indent=2)
    tmp.replace(path)

def main() -> int:
    logging.basicConfig(level=logging.INFO, format="%(asctime)s | %(levelname)s | %(message)s")
    p = argparse.ArgumentParser()
    p.add_argument("--input", type=Path, default=DEFAULT_INPUT)
    p.add_argument("--config", type=Path, default=DEFAULT_CONFIG)
    p.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    p.add_argument("--summary-output", type=Path, default=DEFAULT_SUMMARY)
    a = p.parse_args()
    try:
        with a.config.open(encoding="utf-8") as f: cfg = json.load(f)
        cats, method = cfg["categories"], cfg["methodology"]
        if set(cats) != set(CATEGORIES): raise ValueError(f"Config must define {CATEGORIES}")
        exclusions = cfg.get("global_exclusions", {})
        rows, status_counts, assign_counts, reason_counts = [], Counter(), Counter(), Counter()
        subgroup_counts, eligible_count = defaultdict(Counter), 0
        with a.input.open(encoding="utf-8", newline="") as f:
            r = csv.DictReader(f)
            for row in r:
                appid, name, status = norm(row.get("appid")), norm(row.get("name")), norm(row.get("store_status"))
                status_counts[status] += 1
                tags = top_tags(parse_cell(row.get("steamspy_tags_json"), dict), int(method.get("top_tags_limit", 20)))
                genres = parse_cell(row.get("genres_json"), list)
                source_ok = status == "success"
                globally_excluded = contains_any(genres, exclusions.get("genres", [])) or contains_any(tags, exclusions.get("tags", []))
                eligible = source_ok and not globally_excluded
                scores = {c: 0.0 for c in CATEGORIES}; mts = {}; mgs = {}; subs = {c: "" for c in CATEGORIES}
                assigned = "excluded"
                reason = "global_content_exclusion" if globally_excluded and source_ok else f"store_status_{status or 'missing'}"
                top = second = ""; top_score = second_score = gap = ratio = 0.0; assigned_sub = ""
                if eligible:
                    eligible_count += 1
                    for c in CATEGORIES:
                        s, mt, mg, contrib = score(tags, genres, cats[c])
                        scores[c] = s; mts[c] = mt; mgs[c] = mg; subs[c] = subgroup(contrib, cats[c].get("subgroups", {}))
                    assigned, reason, top, second, top_score, second_score, gap, ratio = classify(scores, method)
                    if assigned in CATEGORIES:
                        assigned_sub = subs[assigned]
                        if assigned_sub: subgroup_counts[assigned][assigned_sub] += 1
                assign_counts[assigned] += 1; reason_counts[reason] += 1
                rows.append({
                    "appid": appid, "name": name, "store_status": status, "eligible": eligible,
                    "combat_score": round(scores["combat"], 6), "exploration_score": round(scores["exploration"], 6),
                    "strategic_reasoning_score": round(scores["strategic_reasoning"], 6),
                    "top_category": top, "second_category": second, "top_score": round(top_score, 6),
                    "second_score": round(second_score, 6), "score_gap": round(gap, 6),
                    "dominance_ratio": "inf" if math.isinf(ratio) else round(ratio, 6),
                    "assigned_category": assigned, "assignment_reason": reason, "assigned_subgroup": assigned_sub,
                    "combat_subgroup": subs["combat"], "exploration_subgroup": subs["exploration"],
                    "strategic_reasoning_subgroup": subs["strategic_reasoning"],
                    "considered_tags_json": json.dumps(tags, ensure_ascii=False, sort_keys=True),
                    "matched_tags_json": json.dumps(mts, ensure_ascii=False, sort_keys=True),
                    "matched_genres_json": json.dumps(mgs, ensure_ascii=False, sort_keys=True)
                })
        atomic_csv(a.output, rows)
        assigned_total = sum(assign_counts.get(c, 0) for c in CATEGORIES)
        atomic_json(a.summary_output, {
            "pipeline_stage": "04b_map_revised_categories", "mapping_version": cfg.get("version"),
            "total_rows": len(rows), "eligible_rows": eligible_count,
            "store_status_counts": dict(sorted(status_counts.items())),
            "assignment_counts": dict(sorted(assign_counts.items())),
            "assigned_category_percentages": {c: round(assign_counts.get(c, 0)/assigned_total, 6) if assigned_total else 0.0 for c in CATEGORIES},
            "assignment_reason_counts": dict(sorted(reason_counts.items())),
            "assigned_subgroup_counts": {c: dict(sorted(v.items())) for c, v in sorted(subgroup_counts.items())},
            "methodology": method
        })
    except Exception as e:
        LOGGER.exception("Revised mapping failed: %s", e); return 1
    LOGGER.info("Revised mapping completed: rows=%d, eligible=%d.", len(rows), eligible_count)
    LOGGER.info("Output: %s", a.output); LOGGER.info("Summary: %s", a.summary_output)
    return 0

if __name__ == "__main__":
    sys.exit(main())