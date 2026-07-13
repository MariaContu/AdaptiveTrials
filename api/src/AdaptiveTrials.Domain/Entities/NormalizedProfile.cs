namespace AdaptiveTrials.Domain.Entities;

public class NormalizedProfile
{
    public int Id { get; set; }

    public int PlayerId { get; set; }

    public Player? Player { get; set; }

    public string Source { get; set; } = "manual";

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

    public double? AvgPlaytimePerGame { get; set; }

    public double? Diversity { get; set; }

    public double? Entropy { get; set; }

    public double? Dominance { get; set; }

    public double? SecondMax { get; set; }

    public double? Gap { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}