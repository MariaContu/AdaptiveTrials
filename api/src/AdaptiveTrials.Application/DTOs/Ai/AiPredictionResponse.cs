using System.Text.Json.Serialization;

namespace AdaptiveTrials.Application.DTOs.Ai;

public class AiPredictionResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("steamId")]
    public string SteamId { get; set; } = string.Empty;

    [JsonPropertyName("steamIdentifierInput")]
    public string SteamIdentifierInput { get; set; } = string.Empty;

    [JsonPropertyName("steamIdentifierType")]
    public string SteamIdentifierType { get; set; } = string.Empty;

    [JsonPropertyName("predicted_category")]
    public string PredictedCategory { get; set; } = string.Empty;

    [JsonPropertyName("probabilities")]
    public AiProbabilitiesResponse Probabilities { get; set; } = new();

    [JsonPropertyName("profile_quality")]
    public AiProfileQualityResponse ProfileQuality { get; set; } = new();

    [JsonPropertyName("library_summary")]
    public AiLibrarySummaryResponse LibrarySummary { get; set; } = new();

    [JsonPropertyName("recent_summary")]
    public AiRecentSummaryResponse RecentSummary { get; set; } = new();

    [JsonPropertyName("feature_count")]
    public int FeatureCount { get; set; }

    [JsonPropertyName("feature_set")]
    public string FeatureSet { get; set; } = string.Empty;

    [JsonPropertyName("model_version")]
    public string ModelVersion { get; set; } = string.Empty;
}
