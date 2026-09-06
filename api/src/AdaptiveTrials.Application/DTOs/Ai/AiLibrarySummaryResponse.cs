using System.Text.Json.Serialization;

namespace AdaptiveTrials.Application.DTOs.Ai;

public class AiLibrarySummaryResponse
{
    [JsonPropertyName("num_games")]
    public int NumGames { get; set; }

    [JsonPropertyName("num_played_games")]
    public int NumPlayedGames { get; set; }

    [JsonPropertyName("total_playtime_hours")]
    public double TotalPlaytimeHours { get; set; }

    [JsonPropertyName("games_combat")]
    public int GamesCombat { get; set; }

    [JsonPropertyName("games_exploration")]
    public int GamesExploration { get; set; }

    [JsonPropertyName("games_strategic_reasoning")]
    public int GamesStrategicReasoning { get; set; }

    [JsonPropertyName("hours_combat")]
    public double HoursCombat { get; set; }

    [JsonPropertyName("hours_exploration")]
    public double HoursExploration { get; set; }

    [JsonPropertyName("hours_strategic_reasoning")]
    public double HoursStrategicReasoning { get; set; }
}
