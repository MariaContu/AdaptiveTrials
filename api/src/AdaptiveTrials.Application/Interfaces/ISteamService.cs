using AdaptiveTrials.Application.DTOs.Steam;

namespace AdaptiveTrials.Application.Interfaces;

public interface ISteamService
{
    Task<SteamImportResponse?> ImportSteamProfileAsync(SteamImportRequest request);
}
