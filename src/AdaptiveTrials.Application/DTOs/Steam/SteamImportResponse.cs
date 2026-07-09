namespace AdaptiveTrials.Application.DTOs.Steam;

public class SteamImportResponse
{
    public int PlayerId { get; set; }

    public string SteamId { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public double Combat { get; set; }

    public double Exploration { get; set; }

    public double Puzzle { get; set; }

    public double TotalPlaytime { get; set; }

    public int NumGames { get; set; }

    public int GamesCombat { get; set; }

    public int GamesExploration { get; set; }

    public int GamesPuzzle { get; set; }

    public double HoursCombat { get; set; }

    public double HoursExploration { get; set; }

    public double HoursPuzzle { get; set; }

    public DateTime CreatedAt { get; set; }
}