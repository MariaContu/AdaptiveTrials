using System.Text.Json.Serialization;

namespace AdaptiveTrials.Game.Dto.Adaptive;

public sealed class SteamProfilePreviewRequest
{
    [JsonPropertyName("steamId")]
    public string SteamId { get; set; } = string.Empty;
}
