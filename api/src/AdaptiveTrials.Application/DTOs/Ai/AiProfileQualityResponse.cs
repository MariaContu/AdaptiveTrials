using System.Text.Json.Serialization;

namespace AdaptiveTrials.Application.DTOs.Ai;

public class AiProfileQualityResponse
{
    [JsonPropertyName("category_game_coverage")]
    public double CategoryGameCoverage { get; set; }

    [JsonPropertyName("category_playtime_coverage")]
    public double CategoryPlaytimeCoverage { get; set; }
}
