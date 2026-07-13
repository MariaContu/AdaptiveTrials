using AdaptiveTrials.Domain.Enums;

namespace AdaptiveTrials.Application.DTOs.SessionQueries;

public class SessionDetailsResponse
{
    public int SessionId { get; set; }

    public int PlayerId { get; set; }

    public GameMode Mode { get; set; }

    public SessionStatus Status { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? EndedAt { get; set; }

    public int TotalEvents { get; set; }

    public int TotalRecommendations { get; set; }
}