using AdaptiveTrials.Game.Dto;
using AdaptiveTrials.Game.Missions.Shared;

namespace AdaptiveTrials.Game.Session;

/// <summary>
/// Associa os dados de uma missão ao resultado
/// obtido durante sua execução.
/// </summary>
public sealed class CompletedMissionRecord
{
	public required MissionDto Mission { get; init; }

	public required MissionResult Result { get; init; }
}
