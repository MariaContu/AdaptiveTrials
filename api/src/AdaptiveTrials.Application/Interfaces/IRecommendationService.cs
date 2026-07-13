using AdaptiveTrials.Application.DTOs.Recommendations;

namespace AdaptiveTrials.Application.Interfaces;

public interface IRecommendationService
{
    Task<NextRecommendationResponse?> GetNextRecommendationAsync(
        NextRecommendationRequest request
    );

    Task<BehaviorDistributionResponse?> GetBehaviorDistributionAsync(int sessionId);
}