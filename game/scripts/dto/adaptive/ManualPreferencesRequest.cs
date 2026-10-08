using System.Text.Json.Serialization;

namespace AdaptiveTrials.Game.Dto.Adaptive;

public sealed class ManualPreferencesRequest
{
    [JsonPropertyName("combat")]
    public double Combat { get; set; }

    [JsonPropertyName("exploration")]
    public double Exploration { get; set; }

    [JsonPropertyName("puzzle")]
    public double Puzzle { get; set; }
}
