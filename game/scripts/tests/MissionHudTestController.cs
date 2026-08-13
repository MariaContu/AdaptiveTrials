using AdaptiveTrials.Game.Components;
using AdaptiveTrials.Game.Enums;
using Godot;

namespace AdaptiveTrials.Game.Tests;

public partial class MissionHudTestController : Node
{
	private MissionHud _hud = null!;
	private double _elapsed;

	public override void _Ready()
	{
		_hud = GetNode<MissionHud>("../MissionHud");
		_hud.Configure("Furtividade Intermediária", MissionType.Exploration, "Alcance o destino sem ser detectado pelas patrulhas.", 4);
		_hud.SetProgress(1, 4, "Rota");
		_hud.SetAttempts(2, 3);
	}

	public override void _Process(double delta)
	{
		_elapsed += delta;
		_hud.SetElapsedTime(_elapsed);
	}
}
