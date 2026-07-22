using AdaptiveTrials.Game.Interactions;
using Godot;

namespace AdaptiveTrials.Game.Missions.Exploration;

/// <summary>
/// Objeto coletável utilizado na missão Encontrar Objetos.
/// </summary>
public partial class CollectibleObject : Area2D, IInteractable
{
	[Signal]
	public delegate void CollectedEventHandler(
		CollectibleObject collectible);

	public bool CanInteract { get; private set; } = true;

	public bool WasCollected { get; private set; }

	public override void _Ready()
	{
		AddToGroup("exploration_collectible");
	}

	public void Interact()
	{
		Collect();
	}

	public void Collect()
	{
		if (!CanInteract || WasCollected)
		{
			return;
		}

		CanInteract = false;
		WasCollected = true;

		Monitoring = false;
		Monitorable = false;

		EmitSignal(
			SignalName.Collected,
			this);

		GD.Print($"Objeto coletado: {Name}");

		Hide();
	}

	public void SetAvailable(bool available)
	{
		CanInteract = available;
		WasCollected = false;
		Monitoring = available;
		Monitorable = available;
		Visible = available;

		ProcessMode = available
			? ProcessModeEnum.Inherit
			: ProcessModeEnum.Disabled;
	}
}
