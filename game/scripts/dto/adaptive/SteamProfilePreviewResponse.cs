using System.Text.Json.Serialization;

namespace AdaptiveTrials.Game.Dto.Adaptive;

public sealed class SteamProfilePreviewResponse
{
    [JsonPropertyName("steamId")]
    public string SteamId { get; set; } = string.Empty;

    [JsonPropertyName("personaName")]
    public string PersonaName { get; set; } = string.Empty;

    [JsonPropertyName("avatarUrl")]
    public string AvatarUrl { get; set; } = string.Empty;

    [JsonPropertyName("countryCode")]
    public string CountryCode { get; set; } = string.Empty;

    [JsonPropertyName("profileUrl")]
    public string ProfileUrl { get; set; } = string.Empty;

    [JsonPropertyName("isCommunityProfilePublic")]
    public bool IsCommunityProfilePublic { get; set; }
}
