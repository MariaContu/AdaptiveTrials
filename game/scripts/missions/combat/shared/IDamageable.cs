using Godot;

namespace AdaptiveTrials.Game.Missions.Combat.Shared;

/// <summary>
/// Contrato comum para objetos que podem receber dano durante o combate.
/// </summary>
public interface IDamageable
{
	bool CanReceiveDamage { get; }

	void ReceiveDamage(int damage, Node source);
}
