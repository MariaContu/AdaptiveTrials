using Godot;

namespace AdaptiveTrials.Core;

public partial class Bootstrap : Node
{
	public override void _Ready()
	{
		GD.Print("[Bootstrap] Inicializando Adaptive Trials.");
		GD.Print("[Bootstrap] Fundação do Game carregada com sucesso.");
	}
}
