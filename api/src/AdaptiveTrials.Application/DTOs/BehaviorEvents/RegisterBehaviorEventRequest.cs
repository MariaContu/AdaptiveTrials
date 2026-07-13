namespace AdaptiveTrials.Application.DTOs.BehaviorEvents;

public class RegisterBehaviorEventRequest
{
    public int MissionId { get; set; }

    public double CompletionTime { get; set; }

    public int Failures { get; set; }

    public bool Success { get; set; }

    public double Persistence { get; set; }
}