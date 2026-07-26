using AdaptiveTrials.Game.Missions.Combat.Shared;
using AdaptiveTrials.Game.Player;
using Godot;

namespace AdaptiveTrials.Game.Tests;

/// <summary>
/// Coordena o teste isolado do inimigo de combate.
/// </summary>
public partial class CombatEnemyTestController : Node2D
{
    private PlayerController _player = null!;
    private CombatEnemyController _enemy = null!;
    private Label _playerHealthLabel = null!;
    private Label _enemyHealthLabel = null!;
    private Label _statusLabel = null!;

    public override void _Ready()
    {
        _player = GetNode<PlayerController>("Player");
        _enemy = GetNode<CombatEnemyController>("CombatEnemy");

        // A cena de teste deve iniciar diretamente com o ataque habilitado.
        _player.SetVisualMode(PlayerVisualMode.Combat);
        _playerHealthLabel = GetNode<Label>("Interface/PlayerHealthLabel");
        _enemyHealthLabel = GetNode<Label>("Interface/EnemyHealthLabel");
        _statusLabel = GetNode<Label>("Interface/StatusLabel");

        _player.HealthChanged += OnPlayerHealthChanged;
        _player.Died += OnPlayerDied;
        _enemy.HealthChanged += OnEnemyHealthChanged;
        _enemy.EnemyDefeated += OnEnemyDefeated;

        OnPlayerHealthChanged(_player.CurrentHealth, _player.MaximumHealth);
        OnEnemyHealthChanged(_enemy.CurrentHealth, _enemy.MaximumHealth);
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey keyEvent &&
            keyEvent.Pressed &&
            !keyEvent.Echo &&
            keyEvent.Keycode == Key.R)
        {
            GetTree().ReloadCurrentScene();
            GetViewport().SetInputAsHandled();
        }
    }

    private void OnPlayerHealthChanged(int currentHealth, int maximumHealth)
    {
        _playerHealthLabel.Text = $"PLAYER: {currentHealth}/{maximumHealth}";
    }

    private void OnEnemyHealthChanged(int currentHealth, int maximumHealth)
    {
        _enemyHealthLabel.Text = $"INIMIGO: {currentHealth}/{maximumHealth}";
    }

    private void OnPlayerDied(Node source)
    {
        _enemy.SetCombatEnabled(false);
        _statusLabel.Text = "JOGADOR DERROTADO — pressione R para reiniciar";
    }

    private void OnEnemyDefeated(CombatEnemyController enemy)
    {
        _player.SetDamageEnabled(false);
        _statusLabel.Text = "INIMIGO DERROTADO — pressione R para reiniciar";
    }
}
