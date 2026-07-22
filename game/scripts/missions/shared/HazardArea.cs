using Godot;

namespace AdaptiveTrials.Game.Missions.Shared;

/// <summary>
/// Área perigosa que registra uma falha ao ser tocada.
/// </summary>
public partial class HazardArea : Area2D
{
	[Signal]
	public delegate void PlayerHitEventHandler();

	private bool _canTrigger = true;

	public override void _Ready()
	{
		BodyEntered += OnBodyEntered;
	}

	public void ResetTrigger()
	{
		_canTrigger = true;
	}

	private void OnBodyEntered(Node2D body)
	{
		if (!_canTrigger)
		{
			return;
		}

		if (body is not Player.PlayerController)
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
}
