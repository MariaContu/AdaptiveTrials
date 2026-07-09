namespace AdaptiveTrials.Application.DTOs.Recommendations;

public class BehaviorDistributionResponse
{
    public double Combat { get; set; }

    public double Exploration { get; set; }

    public double Puzzle { get; set; }

    public int TotalEvents { get; set; }
}