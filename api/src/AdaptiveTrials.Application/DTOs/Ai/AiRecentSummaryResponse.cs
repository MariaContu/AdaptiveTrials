using System.Text.Json.Serialization;

namespace AdaptiveTrials.Application.DTOs.Ai;

public class AiRecentSummaryResponse
{
    [JsonPropertyName("has_recent_activity")]
    public bool HasRecentActivity { get; set; }

    [JsonPropertyName("recent_total_playtime_minutes")]
    public double RecentTotalPlaytimeMinutes { get; set; }

    [JsonPropertyName("recent_active_games")]
    public int RecentActiveGames { get; set; }
}
