using AdaptiveTrials.Domain.Enums;

namespace AdaptiveTrials.Application.DTOs.BehaviorEvents;

public class RegisterBehaviorEventResponse
{
    public int EventId { get; set; }

    public int SessionId { get; set; }

    public int MissionId { get; set; }

    public MissionType MissionType { get; set; }

    public string Template { get; set; } = string.Empty;

    public int Difficulty { get; set; }

    public double CompletionTime { get; set; }

    public int Failures { get; set; }

    public bool Success { get; set; }

    public double Persistence { get; set; }

    public DateTime CreatedAt { get; set; }
}