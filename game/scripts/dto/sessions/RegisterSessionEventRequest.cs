using System.Text.Json.Serialization;

namespace AdaptiveTrials.Game.Dto.Sessions;

/// <summary>
/// Dados comportamentais enviados ao finalizar uma missão.
/// </summary>
public sealed class RegisterSessionEventRequest
{
	[JsonPropertyName("missionId")]
	public int MissionId { get; init; }

	[JsonPropertyName("completionTime")]
	public double CompletionTime { get; init; }

	[JsonPropertyName("failures")]
	public int Failures { get; init; }

	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("persistence")]
	public double Persistence { get; init; }
}
