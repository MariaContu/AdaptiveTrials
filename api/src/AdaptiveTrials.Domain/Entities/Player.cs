namespace AdaptiveTrials.Domain.Entities;

public class Player
{
    public int Id { get; set; }

    public string? SteamId { get; set; }

    // ex: {"combat":0.4,"exploration":0.35,"puzzle":0.25}
    public string? ManualPreferencesJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<GameSession> Sessions { get; set; } = new();

    public NormalizedProfile? NormalizedProfile { get; set; }
}