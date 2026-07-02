using AdaptiveTrials.Application.DTOs.Sessions;

namespace AdaptiveTrials.Application.Interfaces;

public interface ISessionService
{
    Task<CreateSessionResponse> CreateSessionAsync(CreateSessionRequest request);

    Task<EndSessionResponse?> EndSessionAsync(int sessionId);
}