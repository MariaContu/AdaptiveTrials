using AdaptiveTrials.Application.DTOs.Players;

namespace AdaptiveTrials.Application.Interfaces;

public interface IPlayerService
{
    Task<PlayerProfileResponse?> RegisterManualPreferencesAsync(
        int playerId,
        ManualPreferencesRequest request
    );

    Task<PlayerProfileResponse?> GetPlayerProfileAsync(int playerId);
}