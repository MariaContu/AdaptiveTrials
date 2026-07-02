using AdaptiveTrials.Domain.Enums;

namespace AdaptiveTrials.Domain.Entities;

public class Mission
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public MissionType Type { get; set; }

    public string Template { get; set; } = string.Empty;

    public int Difficulty { get; set; }

    // ex: {"enemies":3}, {"items":6}, {"sequenceSize":5}
    public string ParametersJson { get; set; } = "{}";

    public string? Description { get; set; }

    public List<BehaviorEvent> BehaviorEvents { get; set; } = new();

    public List<Recommendation> Recommendations { get; set; } = new();
}