using AdaptiveTrials.Application.DTOs.Steam;

namespace AdaptiveTrials.Application.Interfaces;

public interface ISteamService
{
    Task<SteamProfilePreviewResponse?> GetSteamProfilePreviewAsync(
        SteamProfilePreviewRequest request
    );

    Task<SteamImportResponse?> ImportSteamProfileAsync(SteamImportRequest request);
}
