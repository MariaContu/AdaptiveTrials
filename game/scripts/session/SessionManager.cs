using AdaptiveTrials.Game.Enums;
using Godot;

namespace AdaptiveTrials.Game.Session;

/// <summary>
/// Mantém o estado da sessão durante a troca de cenas.
/// </summary>
public partial class SessionManager : Node
{
	public int? SessionId { get; private set; }

	public int? PlayerId { get; private set; }

	public GameMode? Mode { get; private set; }

	public bool HasActiveSession =>
		SessionId.HasValue &&
		PlayerId.HasValue &&
		Mode.HasValue;

	public void InitializeSession(
		int sessionId,
		int playerId,
		GameMode mode)
	{
		SessionId = sessionId;
		PlayerId = playerId;
		Mode = mode;

		GD.Print(
			$"SessionManager inicializado: " +
			$"SessionId={SessionId}, " +
			$"PlayerId={PlayerId}, " +
			$"Mode={Mode}");
	}

	public void ClearSession()
	{
		SessionId = null;
		PlayerId = null;
		Mode = null;

		GD.Print("Estado da sessão removido.");
	}
}
