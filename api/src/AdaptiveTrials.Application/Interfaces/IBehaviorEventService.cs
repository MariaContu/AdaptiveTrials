using AdaptiveTrials.Application.DTOs.BehaviorEvents;

namespace AdaptiveTrials.Application.Interfaces;

public interface IBehaviorEventService
{
    Task<RegisterBehaviorEventResponse?> RegisterEventAsync(
        int sessionId,
        RegisterBehaviorEventRequest request
    );
}