using AdaptiveTrials.Application.DTOs.SessionQueries;
using AdaptiveTrials.Application.DTOs.Sessions;

namespace AdaptiveTrials.Application.Interfaces;

public interface ISessionService
{
    Task<CreateSessionResponse> CreateSessionAsync(CreateSessionRequest request);

    Task<EndSessionResponse?> EndSessionAsync(int sessionId);

    Task<SessionDetailsResponse?> GetSessionByIdAsync(int sessionId);

    Task<List<SessionEventResponse>?> GetSessionEventsAsync(int sessionId);

    Task<List<SessionRecommendationResponse>?> GetSessionRecommendationsAsync(int sessionId);

    Task<List<PlayerSessionSummaryResponse>?> GetPlayerSessionsAsync(int playerId);
}