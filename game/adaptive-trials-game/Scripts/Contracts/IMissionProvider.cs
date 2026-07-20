using AdaptiveTrials.Models;

namespace AdaptiveTrials.Contracts;

public interface IMissionProvider
{
	Task<MissionDefinition?> GetNextMissionAsync(
		MissionRequestContext context,
		CancellationToken cancellationToken = default);
}
