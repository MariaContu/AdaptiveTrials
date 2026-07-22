namespace AdaptiveTrials.Game.Interactions;

/// <summary>
/// Contrato utilizado por objetos que aceitam interação do jogador.
/// </summary>
public interface IInteractable
{
	bool CanInteract { get; }

	void Interact();
}
