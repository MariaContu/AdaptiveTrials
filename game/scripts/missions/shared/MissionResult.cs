namespace AdaptiveTrials.Game.Missions.Shared;

/// <summary>
/// Resultado produzido ao finalizar qualquer tipo de missão.
/// </summary>
public sealed class MissionResult
{
	public int MissionId { get; init; }

	public double CompletionTime { get; init; }

	public int Failures { get; init; }

	public bool Success { get; init; }
}
