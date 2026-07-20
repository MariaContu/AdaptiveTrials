using AdaptiveTrials.Models;
using Godot;

namespace AdaptiveTrials.Core;

public partial class Bootstrap : Node
{
	public override void _Ready()
	{
		GD.Print("[Bootstrap] Inicializando Adaptive Trials.");

		var mission = new MissionDefinition
		{
			Id = "test_mission",
			Name = "Missão de teste",
			Description = "Objeto temporário para validar os contratos.",
			Category = MissionCategory.Combat,
			Difficulty = 1,
			ScenePath = "res://Scenes/Main/Main.tscn"
		};

		GD.Print($"[Bootstrap] Contrato criado: {mission}");
		GD.Print($"[Bootstrap] Caminho válido: {mission.HasValidScenePath()}");
	}
}
