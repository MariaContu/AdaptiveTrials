namespace AdaptiveTrials.Application.DTOs.Players;

public class PlayerProfileResponse
{
    public int PlayerId { get; set; }

    public string Source { get; set; } = string.Empty;

    public double Combat { get; set; }

    public double Exploration { get; set; }

    public double Puzzle { get; set; }

    public DateTime CreatedAt { get; set; }
}