using System.Text.Json.Serialization;
using AdaptiveTrials.Game.Enums;

namespace AdaptiveTrials.Game.Dto.Sessions;

public sealed class CreateSessionRequest
{
	[JsonPropertyName("playerId")]
	public int? PlayerId { get; set; }

	[JsonPropertyName("mode")]
	public GameMode Mode { get; set; }
}
