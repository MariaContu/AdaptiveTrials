namespace AdaptiveTrials.Application.DTOs.Recommendations;

public class NextRecommendationRequest
{
    public int PlayerId { get; set; }

    public int SessionId { get; set; }
}