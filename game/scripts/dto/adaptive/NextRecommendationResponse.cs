using System.Text.Json.Serialization;
using AdaptiveTrials.Game.Enums;

namespace AdaptiveTrials.Game.Dto.Adaptive;

public sealed class NextRecommendationResponse
{
    [JsonPropertyName("recommendationId")]
    public int RecommendationId { get; set; }

    [JsonPropertyName("recommendedMissionId")]
    public int RecommendedMissionId { get; set; }

    [JsonPropertyName("recommendedType")]
    public MissionType RecommendedType { get; set; }

    [JsonPropertyName("missionName")]
    public string MissionName { get; set; } = string.Empty;

    [JsonPropertyName("difficulty")]
    public int Difficulty { get; set; }

    [JsonPropertyName("targetDifficulty")]
    public int TargetDifficulty { get; set; }
}
