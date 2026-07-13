using AdaptiveTrials.Domain.Enums;

namespace AdaptiveTrials.Application.DTOs.Exports;

public class RecommendationExportResponse
{
    public int RecommendationId { get; set; }

    public int SessionId { get; set; }

    public int PlayerId { get; set; }

    public GameMode SessionMode { get; set; }

    public int? MissionId { get; set; }

    public string? MissionName { get; set; }

    public MissionType RecommendedType { get; set; }

    public int RecommendedDifficulty { get; set; }

    public double CombatProbability { get; set; }

    public double ExplorationProbability { get; set; }

    public double PuzzleProbability { get; set; }

    public double ProfileWeight { get; set; }

    public double BehaviorWeight { get; set; }

    public string? Reason { get; set; }

    public DateTime CreatedAt { get; set; }
}
