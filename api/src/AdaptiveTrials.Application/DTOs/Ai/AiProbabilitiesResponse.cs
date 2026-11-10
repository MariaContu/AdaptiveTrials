using System.Text.Json.Serialization;

namespace AdaptiveTrials.Application.DTOs.Ai;

public class AiProbabilitiesResponse
{
    [JsonPropertyName("combat")]
    public double Combat { get; set; }

    [JsonPropertyName("exploration")]
    public double Exploration { get; set; }

    [JsonPropertyName("strategic_reasoning")]
    public double StrategicReasoning { get; set; }
}
