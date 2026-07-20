using System.Text.Json.Serialization;

namespace AdaptiveTrials.Game.Api;

public sealed class ApiSettings
{
    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; set; } =
        "http://localhost:5277";

    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 10;

    public string GetNormalizedBaseUrl()
    {
        return BaseUrl.TrimEnd('/');
    }
}