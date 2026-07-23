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

	/// <summary>
	/// Representa a persistência demonstrada durante a execução,
	/// utilizando um valor entre zero e um.
	/// </summary>
	public double Persistence { get; init; }
}
