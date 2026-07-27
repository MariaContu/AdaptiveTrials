using System.Collections.Generic;
using AdaptiveTrials.Game.Missions.Combat.Shared;
using AdaptiveTrials.Game.Player;
using Godot;

namespace AdaptiveTrials.Game.Tests;

/// <summary>
/// Valida o componente de ondas sem depender de uma missão ou da API.
/// </summary>
public partial class CombatWaveManagerTestController : Node2D
{
    private PlayerController _player = null!;
    private CombatWaveManager _waveManager = null!;
    private Node2D _dynamicEnemies = null!;
    private Node2D _spawnPoints = null!;
    private Label _waveLabel = null!;
    private Label _enemyLabel = null!;
    private Label _healthLabel = null!;
    private Label _statusLabel = null!;
    private WaveAnnouncement _waveAnnouncement = null!;

    public override void _Ready()
    {
        _player = GetNode<PlayerController>("Player");
        _waveManager = GetNode<CombatWaveManager>("CombatWaveManager");
        _dynamicEnemies = GetNode<Node2D>("DynamicEnemies");
        _spawnPoints = GetNode<Node2D>("SpawnPoints");
        _waveLabel = GetNode<Label>("Interface/Panel/Margin/Content/WaveLabel");
        _enemyLabel = GetNode<Label>("Interface/Panel/Margin/Content/EnemyLabel");
        _healthLabel = GetNode<Label>("Interface/Panel/Margin/Content/HealthLabel");
        _statusLabel = GetNode<Label>("Interface/Panel/Margin/Content/StatusLabel");
        _waveAnnouncement = GetNode<WaveAnnouncement>("Interface/WaveAnnouncement");

        _waveManager.WaveStarted += OnWaveStarted;
        _waveManager.WaveCompleted += OnWaveCompleted;
        _waveManager.ActiveEnemyCountChanged += OnActiveEnemyCountChanged;
        _waveManager.AllWavesCompleted += OnAllWavesCompleted;

        _player.HealthChanged += OnPlayerHealthChanged;
        _player.Died += OnPlayerDied;

        ConfigurePlayer();
        ConfigureWaves();

        CallDeferred(nameof(StartTest));
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

    private void ConfigurePlayer()
    {
        _player.SetVisualMode(PlayerVisualMode.Combat);
        _player.RestoreFullHealth();
        _player.SetDamageEnabled(true);
        _player.SetMovementEnabled(true);
        OnPlayerHealthChanged(_player.CurrentHealth, _player.MaximumHealth);
    }

    private void ConfigureWaves()
    {
        List<Marker2D> markers = new();
        foreach (Node child in _spawnPoints.GetChildren())
        {
            if (child is Marker2D marker)
            {
                markers.Add(marker);
            }
        }

        CombatWaveDefinition[] waves =
        {
            new()
            {
                MeleeCount = 2,
                EnemyHealth = 1,
                MeleeSpeed = 65.0f
            },
            new()
            {
                MeleeCount = 2,
                RangedCount = 1,
                EnemyHealth = 2,
                MeleeSpeed = 70.0f,
                RangedCooldownSeconds = 2.2f,
                RangedOrbSpeed = 270.0f
            },
            new()
            {
                MeleeCount = 2,
                RangedCount = 2,
                EnemyHealth = 2,
                MeleeSpeed = 78.0f,
                RangedCooldownSeconds = 2.0f,
                RangedOrbSpeed = 300.0f
            }
        };

        _waveManager.Configure(
            _player,
            _dynamicEnemies,
            markers,
            waves);
    }

    private void StartTest()
    {
        _statusLabel.Text = "Defeat all three waves.";
        _waveManager.StartWaves();
    }

    private void OnWaveStarted(
        int currentWave,
        int totalWaves,
        int enemyCount)
    {
        _waveLabel.Text = $"Wave: {currentWave}/{totalWaves}";
        _statusLabel.Text = $"Wave {currentWave} incoming...";
        _waveAnnouncement.ShowWave(currentWave, totalWaves, enemyCount);
    }

    private void OnWaveCompleted(int completedWave, int totalWaves)
    {
        _statusLabel.Text =
            completedWave < totalWaves
                ? $"Wave {completedWave} complete. Next wave incoming..."
                : $"Wave {completedWave} complete.";
    }

    private void OnActiveEnemyCountChanged(int activeEnemies)
    {
        _enemyLabel.Text = $"Enemies: {activeEnemies}";
    }

    private void OnPlayerHealthChanged(int currentHealth, int maximumHealth)
    {
        _healthLabel.Text = $"Health: {currentHealth}/{maximumHealth}";
    }

    private void OnPlayerDied(Node source)
    {
        _waveManager.StopAndClear();
        _player.SetMovementEnabled(false);
        _statusLabel.Text = "Player defeated. Press R to restart.";
    }

    private void OnAllWavesCompleted()
    {
        _player.SetDamageEnabled(false);
        _statusLabel.Text = "All waves complete. Press R to restart.";
    }
}
