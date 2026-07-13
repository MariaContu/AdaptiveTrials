using AdaptiveTrials.Application.DTOs.Exports;

namespace AdaptiveTrials.Application.Interfaces;

public interface IExportService
{
    Task<List<SessionExportResponse>> GetSessionsExportAsync();

    Task<List<BehaviorEventExportResponse>> GetBehaviorEventsExportAsync();

    Task<List<RecommendationExportResponse>> GetRecommendationsExportAsync();
}
