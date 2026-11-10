namespace AdaptiveTrials.Application.DTOs.Steam;

public class SteamProfilePreviewResponse
{
    public string SteamId { get; set; } = string.Empty;

    public string PersonaName { get; set; } = string.Empty;

    public string AvatarUrl { get; set; } = string.Empty;

    public string CountryCode { get; set; } = string.Empty;

    public string ProfileUrl { get; set; } = string.Empty;

    public bool IsCommunityProfilePublic { get; set; }
}
