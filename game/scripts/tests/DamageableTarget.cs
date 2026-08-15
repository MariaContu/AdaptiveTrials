using AdaptiveTrials.Game.Missions.Combat.Shared;
using Godot;

namespace AdaptiveTrials.Game.Tests;

/// <summary>
/// Alvo simples usado somente para validar o disparo e o recebimento de dano.
/// </summary>
public partial class DamageableTarget : Area2D, IDamageable
{
	[Signal]
	public delegate void HealthChangedEventHandler(
		int currentHealth,
		int maximumHealth);

	[Signal]
	public delegate void DefeatedEventHandler();

	[Export]
	public int MaximumHealth { get; set; } = 3;

	public bool CanReceiveDamage => !_isDefeated;

	private Label _healthLabel = null!;
	private int _currentHealth;
	private bool _isDefeated;

	public override void _Ready()
	{
		_healthLabel = GetNode<Label>("HealthLabel");
		_currentHealth = Mathf.Max(1, MaximumHealth);
		UpdateHealthLabel();
	}

	public void ReceiveDamage(int damage, Node source)
	{
		if (!CanReceiveDamage || damage <= 0)
		{
			return;
		}

		_currentHealth = Mathf.Max(
			0,
			_currentHealth - damage);

		EmitSignal(
			SignalName.HealthChanged,
			_currentHealth,
			MaximumHealth);

		GD.Print(
			$"{Name} recebeu {damage} de dano de {source.Name}. " +
			$"Vida: {_currentHealth}/{MaximumHealth}.");

		UpdateHealthLabel();

		if (_currentHealth > 0)
		{
			return;
		}

		_isDefeated = true;
		Monitoring = false;
		Monitorable = false;
		Modulate = new Color(0.45f, 0.45f, 0.55f, 0.65f);
		_healthLabel.Text = "ALVO DERROTADO";

		EmitSignal(SignalName.Defeated);
	}

	private void UpdateHealthLabel()
	{
		_healthLabel.Text =
			$"ALVO DE TESTE\nVIDA: {_currentHealth}/{MaximumHealth}";
	}
}
