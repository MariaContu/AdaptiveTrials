namespace AdaptiveTrials.Models;

public sealed class MissionResult
{
	public required string MissionId { get; init; }

	public required MissionCategory Category { get; init; }

	public required int Difficulty { get; init; }

	public required MissionOutcome Outcome { get; init; }

	public required double CompletionTimeSeconds { get; init; }

	public required int FailureCount { get; init; }

	public required double Persistence { get; init; }

	public bool WasSuccessful => Outcome == MissionOutcome.Success;
}
