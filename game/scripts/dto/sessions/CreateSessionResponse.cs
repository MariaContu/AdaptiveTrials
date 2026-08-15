using System;
using System.Text.Json.Serialization;
using AdaptiveTrials.Game.Enums;

namespace AdaptiveTrials.Game.Dto.Sessions;

public sealed class CreateSessionResponse
{
	[JsonPropertyName("sessionId")]
	public int SessionId { get; set; }

	[JsonPropertyName("playerId")]
	public int PlayerId { get; set; }

	[JsonPropertyName("mode")]
	public GameMode Mode { get; set; }

	[JsonPropertyName("status")]
	public int Status { get; set; }

	[JsonPropertyName("startedAt")]
	public DateTime StartedAt { get; set; }
}
