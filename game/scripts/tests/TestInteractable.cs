using AdaptiveTrials.Game.Interactions;
using Godot;

namespace AdaptiveTrials.Game.Tests;

/// <summary>
/// Objeto simples utilizado para validar a interação do jogador.
/// </summary>
public partial class TestInteractable : StaticBody2D, IInteractable
{
	public bool CanInteract { get; private set; } = true;

	public void Interact()
	{
		if (!CanInteract)
		{
			return;
		}

		CanInteract = false;

		GD.Print("Objeto de teste ativado.");

		Modulate = new Color(
			0.55f,
			0.95f,
			0.65f,
			1.0f);
	}
}
