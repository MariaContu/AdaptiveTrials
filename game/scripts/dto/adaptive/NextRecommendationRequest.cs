using System.Text.Json.Serialization;

namespace AdaptiveTrials.Game.Dto.Adaptive;

public sealed class NextRecommendationRequest
{
    [JsonPropertyName("playerId")]
    public int PlayerId { get; init; }

    [JsonPropertyName("sessionId")]
    public int SessionId { get; init; }
}
