from pathlib import Path

from dotenv import load_dotenv
import os


PROJECT_ROOT = Path(__file__).resolve().parents[2]

load_dotenv(PROJECT_ROOT / ".env")

STEAM_API_KEY = os.getenv("STEAM_API_KEY")

RAW_DATA_DIR = PROJECT_ROOT / "data" / "raw"
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