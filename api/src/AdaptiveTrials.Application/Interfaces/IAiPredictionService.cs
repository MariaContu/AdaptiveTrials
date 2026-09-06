using AdaptiveTrials.Application.DTOs.Ai;

namespace AdaptiveTrials.Application.Interfaces;

public interface IAiPredictionService
{
    Task<AiPredictionResponse?> PredictFromSteamIdAsync(string steamId);
}
