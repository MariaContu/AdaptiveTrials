using AdaptiveTrials.Domain.Enums;

namespace AdaptiveTrials.Domain.Entities;

public class BehaviorEvent
{
    public int Id { get; set; }

    public int SessionId { get; set; }

    public GameSession? Session { get; set; }

    public int MissionId { get; set; }

    public Mission? Mission { get; set; }

    public MissionType MissionType { get; set; }

    public string Template { get; set; } = string.Empty;

    public int Difficulty { get; set; }

    public double CompletionTime { get; set; }

    public int Failures { get; set; }

    public bool Success { get; set; }

    public double Persistence { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}