using AdaptiveTrials.Game.Missions.Combat.Shared;
using AdaptiveTrials.Game.Player;
using Godot;

namespace AdaptiveTrials.Game.Tests;

/// <summary>
/// Coordena o teste das variantes corpo a corpo e à distância.
/// </summary>
public partial class CombatEnemyVariantsTestController : Node2D
{
    private PlayerController _player = null!;
    private CombatEnemyController _meleeEnemy = null!;
    private CombatEnemyController _rangedEnemy = null!;
    private Label _playerHealthLabel = null!;
    private Label _meleeHealthLabel = null!;
    private Label _rangedHealthLabel = null!;
    private Label _statusLabel = null!;
    private int _defeatedEnemies;

    public override void _Ready()
    {
        _player = GetNode<PlayerController>("Player");
        _meleeEnemy = GetNode<CombatEnemyController>("MeleeEnemy");
        _rangedEnemy = GetNode<CombatEnemyController>("RangedEnemy");

        _playerHealthLabel = GetNode<Label>("Interface/PlayerHealthLabel");
        _meleeHealthLabel = GetNode<Label>("Interface/MeleeHealthLabel");
        _rangedHealthLabel = GetNode<Label>("Interface/RangedHealthLabel");
        _statusLabel = GetNode<Label>("Interface/StatusLabel");

        _player.SetVisualMode(PlayerVisualMode.Combat);

        // O teste configura os modos explicitamente para não depender
        // apenas das propriedades herdadas da cena.
        _meleeEnemy.SetAttackMode(CombatEnemyAttackMode.Melee);
        _meleeEnemy.ChaseOnlyAfterDetection = false;
        _meleeEnemy.SetTarget(_player);

        _rangedEnemy.SetAttackMode(CombatEnemyAttackMode.Ranged);
        _rangedEnemy.ChaseOnlyAfterDetection = false;
        _rangedEnemy.MovementSpeed = 0.0f;
        _rangedEnemy.RangedMovementEnabled = false;
        _rangedEnemy.MaximumRangedDistance = 700.0f;
        _rangedEnemy.SetTarget(_player);

        _player.HealthChanged += OnPlayerHealthChanged;
        _player.Died += OnPlayerDied;

        _meleeEnemy.HealthChanged += OnMeleeHealthChanged;
        _meleeEnemy.EnemyDefeated += OnEnemyDefeated;

        _rangedEnemy.HealthChanged += OnRangedHealthChanged;
        _rangedEnemy.EnemyDefeated += OnEnemyDefeated;

        OnPlayerHealthChanged(_player.CurrentHealth, _player.MaximumHealth);
        OnMeleeHealthChanged(_meleeEnemy.CurrentHealth, _meleeEnemy.MaximumHealth);
        OnRangedHealthChanged(_rangedEnemy.CurrentHealth, _rangedEnemy.MaximumHealth);
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

    private void OnMeleeHealthChanged(int currentHealth, int maximumHealth)
    {
        _meleeHealthLabel.Text = $"CORPO A CORPO: {currentHealth}/{maximumHealth}";
    }

    private void OnRangedHealthChanged(int currentHealth, int maximumHealth)
    {
        _rangedHealthLabel.Text = $"À DISTÂNCIA: {currentHealth}/{maximumHealth}";
    }

    private void OnPlayerDied(Node source)
    {
        _meleeEnemy.SetCombatEnabled(false);
        _rangedEnemy.SetCombatEnabled(false);
        _statusLabel.Text = "JOGADOR DERROTADO — pressione R para reiniciar";
    }

    private void OnEnemyDefeated(CombatEnemyController enemy)
    {
        _defeatedEnemies++;

        if (_defeatedEnemies < 2)
        {
            _statusLabel.Text = $"{enemy.Name} derrotado. Falta um inimigo.";
            return;
        }

        _player.SetDamageEnabled(false);
        _statusLabel.Text = "TODOS OS INIMIGOS DERROTADOS — pressione R para reiniciar";
    }
}
