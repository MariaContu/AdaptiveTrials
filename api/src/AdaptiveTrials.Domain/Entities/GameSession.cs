using AdaptiveTrials.Domain.Enums;

namespace AdaptiveTrials.Domain.Entities;

public class GameSession
{
    public int Id { get; set; }

    public int PlayerId { get; set; }

    public Player? Player { get; set; }

    public GameMode Mode { get; set; }

    public SessionStatus Status { get; set; } = SessionStatus.Started;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? EndedAt { get; set; }

    public List<BehaviorEvent> BehaviorEvents { get; set; } = new();

    public List<Recommendation> Recommendations { get; set; } = new();
}