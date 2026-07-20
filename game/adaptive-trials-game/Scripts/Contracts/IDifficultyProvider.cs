using AdaptiveTrials.Models;

namespace AdaptiveTrials.Contracts;

public interface IDifficultyProvider
{
	Task<int> GetDifficultyAsync(
		DifficultyRequestContext context,
		CancellationToken cancellationToken = default);
}
