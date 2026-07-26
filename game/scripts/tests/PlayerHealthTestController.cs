using AdaptiveTrials.Game.Player;
using Godot;

namespace AdaptiveTrials.Game.Tests;

/// <summary>
/// Atualiza a interface da cena isolada de vida e permite reiniciar o teste.
/// </summary>
public partial class PlayerHealthTestController : Node2D
{
    private PlayerController _player = null!;
    private Label _healthLabel = null!;
    private Label _statusLabel = null!;

    public override void _Ready()
    {
        _player = GetNode<PlayerController>("Player");
        _healthLabel = GetNode<Label>("CanvasLayer/Panel/VBoxContainer/HealthLabel");
        _statusLabel = GetNode<Label>("CanvasLayer/Panel/VBoxContainer/StatusLabel");

        _player.HealthChanged += OnHealthChanged;
        _player.Damaged += OnPlayerDamaged;
        _player.Died += OnPlayerDied;

        OnHealthChanged(_player.CurrentHealth, _player.MaximumHealth);
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey keyEvent ||
            !keyEvent.Pressed ||
            keyEvent.Echo ||
            keyEvent.Keycode != Key.R)
        {
            return;
        }

        _player.RestoreFullHealth();
        _player.SetDamageEnabled(true);
        _player.SetMovementEnabled(true);
        _player.GlobalPosition = new Vector2(300, 360);
        _statusLabel.Text = "Atravesse a área rosa para receber dano.";
        GetViewport().SetInputAsHandled();
    }

    private void OnHealthChanged(int currentHealth, int maximumHealth)
    {
        _healthLabel.Text = $"VIDA: {currentHealth}/{maximumHealth}";
    }

    private void OnPlayerDamaged(int damage, Node source)
    {
        _statusLabel.Text = $"Dano recebido: {damage}. Invulnerabilidade temporária ativa.";
    }

    private void OnPlayerDied(Node source)
    {
        _statusLabel.Text = "SEM VIDA — pressione R para reiniciar o teste.";
    }
}
