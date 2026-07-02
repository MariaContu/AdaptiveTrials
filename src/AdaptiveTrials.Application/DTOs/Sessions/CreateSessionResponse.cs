using AdaptiveTrials.Domain.Enums;

namespace AdaptiveTrials.Application.DTOs.Sessions;

public class CreateSessionResponse
{
    public int SessionId { get; set; }

    public int PlayerId { get; set; }

    public GameMode Mode { get; set; }

    public SessionStatus Status { get; set; }

    public DateTime StartedAt { get; set; }
}