using System.Text.Json.Serialization;

namespace AdaptiveTrials.Game.Dto.Adaptive;

public sealed class SteamImportResponse
{
    [JsonPropertyName("playerId")]
    public int PlayerId { get; set; }

    [JsonPropertyName("steamId")]
    public string SteamId { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("predictedCategory")]
    public string PredictedCategory { get; set; } = string.Empty;

    [JsonPropertyName("combat")]
    public double Combat { get; set; }

    [JsonPropertyName("exploration")]
    public double Exploration { get; set; }

    [JsonPropertyName("puzzle")]
    public double Puzzle { get; set; }
}
