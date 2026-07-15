from pathlib import Path

from dotenv import load_dotenv
import os


PROJECT_ROOT = Path(__file__).resolve().parents[2]

load_dotenv(PROJECT_ROOT / ".env")

STEAM_API_KEY = os.getenv("STEAM_API_KEY")

RAW_DATA_DIR = PROJECT_ROOT / "data" / "raw"
PROCESSED_DATA_DIR = PROJECT_ROOT / "data" / "processed"
METRICS_DIR = PROJECT_ROOT / "reports" / "metrics"

OWNED_GAMES_FILE = (
    RAW_DATA_DIR
    / "steam_api_owned_games.csv"
)

COLLECTION_SUMMARY_FILE = (
    METRICS_DIR
    / "steam_api_collection_summary.json"
)

TARGET_VALID_PROFILES = 200

REVIEWS_PER_PAGE = 100

REQUEST_TIMEOUT_SECONDS = 30
REQUEST_DELAY_SECONDS = 0.25

MAX_REQUEST_RETRIES = 5

SEED_APP_IDS = [
    620,
    730,
    570,
    1245620,
    1174180,
    413150,
    105600,
    367520,
]

MISSION_CATEGORY_TAGS = {
    "combat": [
        "action",
        "fps",
        "shooter",
        "fighting",
        "hack and slash",
        "beat em up",
        "battle royale",
        "action roguelike",
        "twin stick shooter",
        "top-down shooter",
        "third-person shooter",
        "hero shooter",
        "looter shooter",
        "combat",
        "moba",
    ],
    "exploration": [
        "adventure",
        "rpg",
        "open world",
        "exploration",
        "walking simulator",
        "sandbox",
    ],
    "puzzle": [
        "puzzle",
        "strategy",
        "logic",
        "casual",
        "hidden object",
        "turn based",
    ],
}

PROFILE_COLUMNS = [
    "combat",
    "exploration",
    "puzzle",
]

MODEL_FEATURES = [
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

TARGET_CLASSES = PROFILE_COLUMNS

FINAL_DATASET_FILE = (
    PROCESSED_DATA_DIR
    / "steam_api_mission_profiles.csv"
)

FINAL_DATASET_SUMMARY_FILE = (
    METRICS_DIR
    / "steam_api_dataset_summary.json"
)

GRANULAR_CATEGORY_TAGS = {
    "combat": {
        "combat_shooter": [
            "fps",
            "shooter",
            "battle royale",
            "twin stick shooter",
            "top-down shooter",
            "third-person shooter",
            "hero shooter",
            "looter shooter",
        ],
        "combat_fighting": [
            "fighting",
            "hack and slash",
            "beat em up",
            "souls-like",
            "swordplay",
            "martial arts",
        ],
        "combat_action": [
            "action",
            "action roguelike",
            "moba",
            "combat",
        ],
    },
    "exploration": {
        "exploration_rpg": [
            "rpg",
        ],
        "exploration_open_world": [
            "open world",
            "exploration",
            "sandbox",
        ],
        "exploration_adventure": [
            "adventure",
            "walking simulator",
        ],
    },
    "puzzle": {
        "puzzle_logic": [
            "puzzle",
            "logic",
            "hidden object",
        ],
        "puzzle_strategy": [
            "strategy",
            "turn based",
        ],
        "puzzle_casual": [
            "casual",
        ],
    },
}