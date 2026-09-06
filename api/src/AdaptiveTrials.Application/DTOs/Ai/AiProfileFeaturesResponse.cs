namespace AdaptiveTrials.Application.DTOs.Ai;

public class AiProfileFeaturesResponse
{
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
}
