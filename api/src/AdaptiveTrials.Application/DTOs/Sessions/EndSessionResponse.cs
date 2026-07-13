using AdaptiveTrials.Domain.Enums;

namespace AdaptiveTrials.Application.DTOs.Sessions;

public class EndSessionResponse
{
    public int SessionId { get; set; }

    public SessionStatus Status { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? EndedAt { get; set; }
}