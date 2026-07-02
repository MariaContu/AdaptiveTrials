using AdaptiveTrials.Domain.Enums;

namespace AdaptiveTrials.Domain.Entities;

public class Recommendation
{
    public int Id { get; set; }

    public int SessionId { get; set; }

    public GameSession? Session { get; set; }

    public int? MissionId { get; set; }

    public Mission? Mission { get; set; }

    public MissionType RecommendedType { get; set; }

    public int RecommendedDifficulty { get; set; }

    public double CombatProbability { get; set; }

    public double ExplorationProbability { get; set; }

    public double PuzzleProbability { get; set; }

    public double ProfileWeight { get; set; }

    public double BehaviorWeight { get; set; }

    public string? Reason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}