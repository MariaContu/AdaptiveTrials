using AdaptiveTrials.Game.Player;
using Godot;

namespace AdaptiveTrials.Game.Missions.Shared;

/// <summary>
/// Área perigosa que registra uma falha ao tocar
/// a área de detecção do jogador.
/// </summary>
public partial class HazardArea : Area2D
{
	[Signal]
	public delegate void PlayerHitEventHandler();

	private const string PlayerDetectionAreaName =
		"DetectionHitbox";

	private bool _canTrigger = true;

	public override void _Ready()
	{
		AreaEntered += OnAreaEntered;
	}

	public void ResetTrigger()
	{
		_canTrigger = true;
	}

	private void OnAreaEntered(
		Area2D area)
	{
		if (!_canTrigger)
		{
			return;
		}

		if (!IsPlayerDetectionArea(area))
		{
			return;
		}

		_canTrigger = false;

		EmitSignal(
			SignalName.PlayerHit);

		GetTree()
			.CreateTimer(0.75)
			.Timeout += ResetTrigger;
	}

	private static bool IsPlayerDetectionArea(
		Area2D area)
	{
		if (area.Name !=
			PlayerDetectionAreaName)
		{
			return false;
		}

		return area.GetParentOrNull<PlayerController>()
			is not null;
	}
}
