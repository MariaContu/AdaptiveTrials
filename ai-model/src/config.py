from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parent.parent

DATA_DIR = PROJECT_ROOT / "data"
RAW_DATA_DIR = DATA_DIR / "raw"
PROCESSED_DATA_DIR = DATA_DIR / "processed"

MODELS_DIR = PROJECT_ROOT / "models"

REPORTS_DIR = PROJECT_ROOT / "reports"
FIGURES_DIR = REPORTS_DIR / "figures"
METRICS_DIR = REPORTS_DIR / "metrics"

RANDOM_STATE = 42
TEST_SIZE = 0.20

FEATURES = [
    "combat",
    "exploration",
    "puzzle",
    "hours_combat",
    "hours_exploration",
    "hours_puzzle",
    "games_combat",
    "games_exploration",
    "games_puzzle",
    "total_playtime",
    "num_games",
    "avg_playtime_per_game",
    "diversity",
    "entropy",
    "dominance",
    "second_max",
    "gap",
]

TARGET_COLUMN = "target"
TARGET_CLASSES = ["combat", "exploration", "puzzle"]
