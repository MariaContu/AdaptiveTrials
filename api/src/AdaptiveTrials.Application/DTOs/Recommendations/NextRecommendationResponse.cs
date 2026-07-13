using AdaptiveTrials.Domain.Enums;

namespace AdaptiveTrials.Application.DTOs.Recommendations;

public class NextRecommendationResponse
{
    public int RecommendationId { get; set; }

    public int RecommendedMissionId { get; set; }

    public string MissionName { get; set; } = string.Empty;

    public MissionType RecommendedType { get; set; }

    public string Template { get; set; } = string.Empty;

    public int Difficulty { get; set; }

    public int TargetDifficulty { get; set; }

    public RecommendationProbabilitiesResponse ProfileProbabilities { get; set; } = new();

    public RecommendationProbabilitiesResponse BehaviorProbabilities { get; set; } = new();

    public RecommendationProbabilitiesResponse FinalProbabilities { get; set; } = new();

    public RecommendationWeightsResponse Weights { get; set; } = new();

    public string? Reason { get; set; }
}