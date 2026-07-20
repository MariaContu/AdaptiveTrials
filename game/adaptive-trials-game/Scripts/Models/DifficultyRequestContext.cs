namespace AdaptiveTrials.Models;

public sealed class DifficultyRequestContext
{
	public required string SessionId { get; init; }

	public required MissionCategory Category { get; init; }

	public required int CompletedMissionCount { get; init; }

	public MissionDefinition? PreviousMission { get; init; }

	public IReadOnlyList<MissionResult> PreviousResults { get; init; }
		= Array.Empty<MissionResult>();
}
