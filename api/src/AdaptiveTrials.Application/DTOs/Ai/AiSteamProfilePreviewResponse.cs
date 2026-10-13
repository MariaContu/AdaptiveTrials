using System.Text.Json.Serialization;

namespace AdaptiveTrials.Application.DTOs.Ai;

public class AiSteamProfilePreviewResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("steamId")]
    public string SteamId { get; set; } = string.Empty;

    [JsonPropertyName("personaName")]
    public string PersonaName { get; set; } = string.Empty;

    [JsonPropertyName("avatarFull")]
    public string AvatarFull { get; set; } = string.Empty;

    [JsonPropertyName("countryCode")]
    public string CountryCode { get; set; } = string.Empty;

    [JsonPropertyName("profileUrl")]
    public string ProfileUrl { get; set; } = string.Empty;

    [JsonPropertyName("communityVisibilityState")]
    public int CommunityVisibilityState { get; set; }
}
