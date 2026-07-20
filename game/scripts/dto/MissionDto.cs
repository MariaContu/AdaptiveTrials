using System.Text.Json.Serialization;

namespace AdaptiveTrials.Game.Dto;

/// <summary>
/// Representa uma missão retornada pelo catálogo da API.
/// </summary>
public sealed class MissionDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("template")]
    public string Template { get; set; } = string.Empty;

    [JsonPropertyName("difficulty")]
    public int Difficulty { get; set; }

    [JsonPropertyName("parametersJson")]
    public string ParametersJson { get; set; } = "{}";

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}