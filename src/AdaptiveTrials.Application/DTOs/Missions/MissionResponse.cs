using AdaptiveTrials.Domain.Enums;

namespace AdaptiveTrials.Application.DTOs.Missions;

public class MissionResponse
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public MissionType Type { get; set; }

    public string Template { get; set; } = string.Empty;

    public int Difficulty { get; set; }

    public string ParametersJson { get; set; } = "{}";

    public string? Description { get; set; }
}