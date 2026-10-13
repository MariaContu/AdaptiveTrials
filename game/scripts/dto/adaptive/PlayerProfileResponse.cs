using System.Text.Json.Serialization;

namespace AdaptiveTrials.Game.Dto.Adaptive;

public sealed class PlayerProfileResponse
{
    [JsonPropertyName("playerId")]
    public int PlayerId { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("combat")]
    public double Combat { get; set; }

    [JsonPropertyName("exploration")]
    public double Exploration { get; set; }

    [JsonPropertyName("puzzle")]
    public double Puzzle { get; set; }
}
