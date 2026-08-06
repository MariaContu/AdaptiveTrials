using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AdaptiveTrials.Game.Player;
using Godot;

namespace AdaptiveTrials.Game.Missions.Combat.Shared;

/// <summary>
/// Cria ondas sequenciais e informa o progresso aos controladores de missão.
/// </summary>
public partial class CombatWaveManager : Node
{
    [Signal]
    public delegate void WaveStartedEventHandler(
        int currentWave,
        int totalWaves,
        int enemyCount);

    [Signal]
    public delegate void WaveCompletedEventHandler(
        int completedWave,
        int totalWaves);

    [Signal]
    public delegate void ActiveEnemyCountChangedEventHandler(
        int activeEnemies);

    [Signal]
    public delegate void AllWavesCompletedEventHandler();

    [ExportGroup("Scenes")]
    [Export]
    public PackedScene? EnemyScene { get; set; }

    [ExportGroup("Timing")]
    [Export]
    public float SpawnIntervalSeconds { get; set; } = 0.25f;

    [Export]
    public float IntervalBetweenWavesSeconds { get; set; } = 1.4f;

    [Export]
    public float WaveStartDelaySeconds { get; set; } = 2.0f;

    [ExportGroup("Progression")]
    [Export]
    public bool AdvanceWhenEnemiesDefeated { get; set; } = true;

    [ExportGroup("Targeting")]
    [Export]
    public bool LockEnemiesToConfiguredTarget { get; set; }

    [Export]
    public float TimedWaveDurationSeconds { get; set; } = 8.0f;

    [Export]
    public bool ClearEnemiesOnTimedAdvance { get; set; } = true;

    private readonly List<CombatWaveDefinition> _waves = new();
    private readonly List<Marker2D> _spawnPoints = new();
    private readonly HashSet<CombatEnemyController> _activeEnemies = new();

    private Node2D? _target;
    private Node2D? _enemyContainer;
    private RandomNumberGenerator _random = new();
    private bool _isRunning;
    private int _runVersion;

    public int CurrentWaveNumber { get; private set; }

    public int TotalWaves => _waves.Count;

    public int ActiveEnemyCount => _activeEnemies.Count;

    public bool IsRunning => _isRunning;

    public override void _Ready()
    {
        EnemyScene ??= GD.Load<PackedScene>(
            "res://scenes/missions/combat/shared/CombatEnemy.tscn");

        _random.Randomize();
    }

    public void Configure(
        Node2D target,
        Node2D enemyContainer,
        IEnumerable<Marker2D> spawnPoints,
        IEnumerable<CombatWaveDefinition> waves)
    {
        StopAndClear();

        _target = target;
        _enemyContainer = enemyContainer;

        _spawnPoints.Clear();
        _spawnPoints.AddRange(spawnPoints.Where(IsInstanceValid));

        _waves.Clear();
        _waves.AddRange(waves.Where(wave => wave.TotalEnemies > 0));

        CurrentWaveNumber = 0;
    }

    public void StartWaves()
    {
        if (_isRunning)
        {
            GD.PushWarning("O gerenciador de ondas já está em execução.");
            return;
        }

        if (!ValidateConfiguration())
        {
            return;
        }

        _isRunning = true;
        int runVersion = ++_runVersion;
        _ = RunWavesAsync(runVersion);
    }

    public void StopAndClear()
    {
        _isRunning = false;
        _runVersion++;
        CurrentWaveNumber = 0;

        foreach (CombatEnemyController enemy in _activeEnemies.ToArray())
        {
            if (!IsInstanceValid(enemy))
            {
                continue;
            }

            enemy.SetCombatEnabled(false);
            enemy.QueueFree();
        }

        _activeEnemies.Clear();
        EmitSignal(SignalName.ActiveEnemyCountChanged, 0);
    }

    private async Task RunWavesAsync(int runVersion)
    {
        for (int waveIndex = 0; waveIndex < _waves.Count; waveIndex++)
        {
            if (!CanContinue(runVersion))
            {
                return;
            }

            CurrentWaveNumber = waveIndex + 1;
            CombatWaveDefinition wave = _waves[waveIndex];

            EmitSignal(
                SignalName.WaveStarted,
                CurrentWaveNumber,
                TotalWaves,
                wave.TotalEnemies);

            GD.Print(
                $"Onda {CurrentWaveNumber}/{TotalWaves} anunciada com " +
                $"{wave.TotalEnemies} inimigos.");

            if (WaveStartDelaySeconds > 0.0f)
            {
                await WaitSecondsAsync(WaveStartDelaySeconds, runVersion);
            }

            if (!CanContinue(runVersion))
            {
                return;
            }

            await SpawnWaveAsync(wave, runVersion);

            if (!CanContinue(runVersion))
            {
                return;
            }

            if (AdvanceWhenEnemiesDefeated)
            {
                await WaitForWaveCompletionAsync(runVersion);
            }
            else
            {
                await WaitSecondsAsync(
                    Mathf.Max(0.1f, TimedWaveDurationSeconds),
                    runVersion);

                if (CanContinue(runVersion) && ClearEnemiesOnTimedAdvance)
                {
                    ClearActiveEnemies();
                }
            }

            if (!CanContinue(runVersion))
            {
                return;
            }

            EmitSignal(
                SignalName.WaveCompleted,
                CurrentWaveNumber,
                TotalWaves);

            GD.Print($"Onda {CurrentWaveNumber}/{TotalWaves} concluída.");

            bool hasNextWave = CurrentWaveNumber < TotalWaves;
            if (hasNextWave && IntervalBetweenWavesSeconds > 0.0f)
            {
                await WaitSecondsAsync(
                    IntervalBetweenWavesSeconds,
                    runVersion);
            }
        }

        if (!CanContinue(runVersion))
        {
            return;
        }

        _isRunning = false;
        EmitSignal(SignalName.AllWavesCompleted);
        GD.Print("Todas as ondas de combate foram concluídas.");
    }

    private async Task SpawnWaveAsync(
        CombatWaveDefinition wave,
        int runVersion)
    {
        List<Marker2D> shuffledSpawns = new(_spawnPoints);
        Shuffle(shuffledSpawns);

        int totalEnemies = wave.TotalEnemies;
        for (int enemyIndex = 0; enemyIndex < totalEnemies; enemyIndex++)
        {
            if (!CanContinue(runVersion))
            {
                return;
            }

            bool ranged = enemyIndex >= wave.MeleeCount;
            Marker2D spawnPoint = shuffledSpawns[enemyIndex % shuffledSpawns.Count];
            SpawnEnemy(wave, ranged, enemyIndex, spawnPoint.GlobalPosition);

            bool hasAnotherEnemy = enemyIndex + 1 < totalEnemies;
            if (hasAnotherEnemy && SpawnIntervalSeconds > 0.0f)
            {
                await WaitSecondsAsync(SpawnIntervalSeconds, runVersion);
            }
        }
    }

    private void SpawnEnemy(
        CombatWaveDefinition wave,
        bool ranged,
        int enemyIndex,
        Vector2 position)
    {
        if (EnemyScene is null || _enemyContainer is null || _target is null)
        {
            return;
        }

        CombatEnemyController enemy =
            EnemyScene.Instantiate<CombatEnemyController>();

        enemy.Name =
            $"Wave{CurrentWaveNumber:00}_" +
            $"{(ranged ? "Ranged" : "Melee")}_{enemyIndex + 1:00}";

        _enemyContainer.AddChild(enemy);
        enemy.GlobalPosition = position;

        enemy.SetAttackMode(
            ranged
                ? CombatEnemyAttackMode.Ranged
                : CombatEnemyAttackMode.Melee);

        enemy.Configure(
            Mathf.Max(1, wave.EnemyHealth),
            ranged ? wave.MeleeSpeed : Mathf.Max(0.0f, wave.MeleeSpeed),
            Mathf.Max(1, wave.EnemyDamage),
            ranged
                ? Mathf.Max(0.1f, wave.RangedCooldownSeconds)
                : Mathf.Max(0.1f, wave.MeleeCooldownSeconds));

        enemy.ChaseOnlyAfterDetection = false;
        enemy.DetectionCanOverrideTarget =
            !LockEnemiesToConfiguredTarget;
        enemy.RangedMovementEnabled =
            ranged && wave.RangedMovementEnabled;
        enemy.MaximumRangedDistance = 1200.0f;
        enemy.EnemyOrbSpeed = Mathf.Max(1.0f, wave.RangedOrbSpeed);
        enemy.SetTarget(_target);
        enemy.EnemyDefeated += OnEnemyDefeated;

        _activeEnemies.Add(enemy);
        EmitSignal(
            SignalName.ActiveEnemyCountChanged,
            ActiveEnemyCount);
    }

    private async Task WaitForWaveCompletionAsync(int runVersion)
    {
        while (CanContinue(runVersion) && ActiveEnemyCount > 0)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private async Task WaitSecondsAsync(float seconds, int runVersion)
    {
        if (seconds <= 0.0f || !CanContinue(runVersion))
        {
            return;
        }

        SceneTreeTimer timer = GetTree().CreateTimer(seconds);
        await ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
    }

    private void ClearActiveEnemies()
    {
        foreach (CombatEnemyController enemy in _activeEnemies.ToArray())
        {
            if (!IsInstanceValid(enemy))
            {
                continue;
            }

            enemy.EnemyDefeated -= OnEnemyDefeated;
            enemy.SetCombatEnabled(false);
            enemy.QueueFree();
        }

        _activeEnemies.Clear();
        EmitSignal(SignalName.ActiveEnemyCountChanged, 0);
    }

    private void OnEnemyDefeated(CombatEnemyController enemy)
    {
        if (!_activeEnemies.Remove(enemy))
        {
            return;
        }

        enemy.EnemyDefeated -= OnEnemyDefeated;
        enemy.CallDeferred(Node.MethodName.QueueFree);

        EmitSignal(
            SignalName.ActiveEnemyCountChanged,
            ActiveEnemyCount);
    }

    private bool ValidateConfiguration()
    {
        if (EnemyScene is null)
        {
            GD.PushError("A cena do inimigo não foi configurada no gerenciador de ondas.");
            return false;
        }

        if (!IsInstanceValid(_target))
        {
            GD.PushError("O alvo das ondas não foi configurado.");
            return false;
        }

        if (!IsInstanceValid(_enemyContainer))
        {
            GD.PushError("O contêiner de inimigos não foi configurado.");
            return false;
        }

        if (_spawnPoints.Count == 0)
        {
            GD.PushError("Nenhum ponto de spawn foi informado ao gerenciador de ondas.");
            return false;
        }

        if (_waves.Count == 0)
        {
            GD.PushError("Nenhuma onda válida foi configurada.");
            return false;
        }

        return true;
    }

    private bool CanContinue(int runVersion)
    {
        return _isRunning &&
               runVersion == _runVersion &&
               IsInsideTree();
    }

    private void Shuffle<T>(IList<T> values)
    {
        for (int index = values.Count - 1; index > 0; index--)
        {
            int swapIndex = _random.RandiRange(0, index);
            (values[index], values[swapIndex]) =
                (values[swapIndex], values[index]);
        }
    }
}
