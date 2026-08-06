using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using AdaptiveTrials.Game.Components;
using AdaptiveTrials.Game.Dto;
using AdaptiveTrials.Game.Enums;
using AdaptiveTrials.Game.Missions.Combat.Shared;
using AdaptiveTrials.Game.Missions.Shared;
using AdaptiveTrials.Game.Player;
using AdaptiveTrials.Game.Session;
using Godot;

namespace AdaptiveTrials.Game.Missions.Combat;

/// <summary>
/// Controla a missão de combate em que o alvo principal deve ser eliminado.
/// </summary>
public partial class EliminateTargetMissionController : Node
{
    private static readonly PackedScene EnemyScene = GD.Load<PackedScene>(
        "res://scenes/missions/combat/shared/CombatEnemy.tscn");

    private SessionManager _sessionManager = null!;
    private PlayerController _player = null!;
    private MissionHud _missionHud = null!;
    private MissionResultPopup _resultPopup = null!;
    private Node2D _dynamicEnemies = null!;
    private Marker2D _playerSpawn = null!;
    private Marker2D _targetSpawn = null!;
    private Node2D _guardSpawns = null!;

    private readonly List<CombatEnemyController> _enemies = new();

    private MissionDto _mission = null!;
    private CombatEnemyController? _mainTarget;
    private double _elapsedTime;
    private int _failures;
    private bool _missionFinished;
    private bool _isFinalizingMission;
    private bool _resultRegistered;
    private string _objectiveText = string.Empty;

    public MissionResult? Result { get; private set; }

    public override void _Ready()
    {
        GetReferences();
        _sessionManager = GetNode<SessionManager>("/root/SessionManager");
        _resultPopup.ContinueRequested += OnContinueRequested;
        _player.HealthChanged += OnPlayerHealthChanged;
        _player.Died += OnPlayerDied;

        MissionDto? currentMission = _sessionManager.CurrentMission;
        if (currentMission is null)
        {
            ShowInitializationError("No active mission was found.");
            return;
        }

        _mission = currentMission;
        if (_mission.Type != MissionType.Combat)
        {
            ShowInitializationError("The active mission is not a combat mission.");
            return;
        }

        ConfigureMission();
    }

    public override void _Process(double delta)
    {
        if (_missionFinished || _isFinalizingMission)
        {
            return;
        }

        _elapsedTime += delta;
        _missionHud.SetElapsedTime(_elapsedTime);
    }

    private void GetReferences()
    {
        _player = GetNode<PlayerController>("../Player");
        _missionHud = GetNode<MissionHud>("../MissionHud");
        _resultPopup = GetNode<MissionResultPopup>("../MissionResultPopup");
        _dynamicEnemies = GetNode<Node2D>("../DynamicEnemies");
        _playerSpawn = GetNode<Marker2D>("../SpawnPoints/PlayerSpawn");
        _targetSpawn = GetNode<Marker2D>("../SpawnPoints/TargetSpawn");
        _guardSpawns = GetNode<Node2D>("../SpawnPoints/GuardSpawns");
    }

    private void ConfigureMission()
    {
        DifficultySettings settings = ReadSettings(_mission.Difficulty, _mission.ParametersJson);

        _elapsedTime = 0;
        _failures = 0;
        _missionFinished = false;
        _isFinalizingMission = false;
        _resultRegistered = false;
        Result = null;

        _objectiveText = "Defeat the main target before your health reaches zero.";

        _player.GlobalPosition = _playerSpawn.GlobalPosition;
        _player.Velocity = Vector2.Zero;
        _player.SetVisualMode(PlayerVisualMode.Combat);
        _player.RestoreFullHealth();
        _player.SetDamageEnabled(true);
        _player.SetMovementEnabled(true);

        _missionHud.Configure(_mission.Name, _mission.Type, _objectiveText, settings.TargetHealth);
        _missionHud.SetProgress(0, settings.TargetHealth, "Target Damage");
        _missionHud.SetAttemptsText($"Health: {_player.CurrentHealth}/{_player.MaximumHealth}");
        _missionHud.SetVisibleState(true);
        _resultPopup.HidePopup();

        SpawnMainTarget(settings);
        SpawnGuards(settings);

        GD.Print($"Missão Eliminar Alvo iniciada: MissionId={_mission.Id}, Difficulty={_mission.Difficulty}, TargetHealth={settings.TargetHealth}, Guards={settings.GuardCount}");
    }

    private void SpawnMainTarget(DifficultySettings settings)
    {
        CombatEnemyController target = CreateEnemy("MainTarget", _targetSpawn.GlobalPosition);
        target.SetAttackMode(CombatEnemyAttackMode.Ranged);
        target.Configure(settings.TargetHealth, 0.0f, 1, settings.TargetCooldown);
        target.RangedMovementEnabled = false;
        target.ChaseOnlyAfterDetection = false;
        target.MaximumRangedDistance = 900.0f;
        target.EnemyOrbSpeed = settings.TargetOrbSpeed;
        target.Scale = new Vector2(1.3f, 1.3f);
        target.SetTarget(_player);
        target.EnemyDefeated += OnEnemyDefeated;
        target.HealthChanged += OnTargetHealthChanged;

        _mainTarget = target;
        _enemies.Add(target);
    }

    private void SpawnGuards(DifficultySettings settings)
    {
        List<Marker2D> markers = new();
        foreach (Node child in _guardSpawns.GetChildren())
        {
            if (child is Marker2D marker)
            {
                markers.Add(marker);
            }
        }

        int count = Mathf.Min(settings.GuardCount, markers.Count);
        for (int index = 0; index < count; index++)
        {
            CombatEnemyController guard = CreateEnemy($"Guard{index + 1:00}", markers[index].GlobalPosition);
            bool ranged = settings.RangedGuardCount > index;
            guard.SetAttackMode(ranged ? CombatEnemyAttackMode.Ranged : CombatEnemyAttackMode.Melee);
            guard.Configure(settings.GuardHealth, ranged ? 0.0f : settings.GuardSpeed, 1, ranged ? 2.2f : 1.2f);
            guard.ChaseOnlyAfterDetection = false;
            guard.RangedMovementEnabled = !ranged;
            guard.MaximumRangedDistance = 900.0f;
            guard.SetTarget(_player);
            guard.EnemyDefeated += OnEnemyDefeated;
            _enemies.Add(guard);
        }
    }

    private CombatEnemyController CreateEnemy(string nodeName, Vector2 position)
    {
        CombatEnemyController enemy = EnemyScene.Instantiate<CombatEnemyController>();
        enemy.Name = nodeName;
        _dynamicEnemies.AddChild(enemy);
        enemy.GlobalPosition = position;
        return enemy;
    }

    private void OnTargetHealthChanged(int currentHealth, int maximumHealth)
    {
        int damageDone = Mathf.Max(0, maximumHealth - currentHealth);
        _missionHud.SetProgress(damageDone, maximumHealth, "Target Damage");
    }

    private void OnPlayerHealthChanged(int currentHealth, int maximumHealth)
    {
        _missionHud.SetAttemptsText($"Health: {currentHealth}/{maximumHealth}");
    }

    private void OnEnemyDefeated(CombatEnemyController enemy)
    {
        if (_missionFinished || _isFinalizingMission)
        {
            return;
        }

        if (enemy == _mainTarget)
        {
            _ = FinishMissionAsync(true);
        }
    }

    private void OnPlayerDied(Node source)
    {
        if (_missionFinished || _isFinalizingMission)
        {
            return;
        }

        _failures = 1;
        _ = FinishMissionAsync(false);
    }

    private async Task FinishMissionAsync(bool success)
    {
        if (_missionFinished || _isFinalizingMission)
        {
            return;
        }

        _isFinalizingMission = true;
        _player.SetMovementEnabled(false);
        _player.SetDamageEnabled(false);
        _player.Velocity = Vector2.Zero;

        foreach (CombatEnemyController enemy in _enemies)
        {
            if (IsInstanceValid(enemy))
            {
                enemy.SetCombatEnabled(false);
            }
        }

        Result = new MissionResult
        {
            MissionId = _mission.Id,
            CompletionTime = _elapsedTime,
            Failures = _failures,
            Success = success,
            Persistence = CalculatePersistence(_failures)
        };

        _resultRegistered = await _sessionManager.RegisterCurrentMissionResultAsync(Result);
        _missionFinished = true;
        _isFinalizingMission = false;
        _missionHud.SetVisibleState(false);

        _resultPopup.ShowResult(
            success,
            _mission.Name,
            _objectiveText,
            _elapsedTime,
            "Main Target",
            success ? "Defeated" : "Still active",
            GetDifficultyText(_mission.Difficulty),
            _failures);

        PrintMissionResult();
    }

    private async void OnContinueRequested()
    {
        if (!_missionFinished || Result is null)
        {
            return;
        }

        if (!_resultRegistered)
        {
            GD.PushError("O evento comportamental não foi registrado. Não é possível avançar.");
            return;
        }

        bool continued = await _sessionManager.ContinueAfterCurrentMissionAsync(GetTree());
        if (!continued)
        {
            GD.PushError("Não foi possível continuar o fluxo da sessão.");
        }
    }

    private void ShowInitializationError(string message)
    {
        _missionFinished = true;
        _player.SetMovementEnabled(false);
        _missionHud.SetVisibleState(false);
        _resultPopup.ShowResult(false, "Mission unavailable", message, 0, "Status", "Initialization error", "-", 0);
        GD.PushError(message);
    }

    private void PrintMissionResult()
    {
        if (Result is null)
        {
            return;
        }

        GD.Print("Resultado da missão Eliminar Alvo:");
        GD.Print($"MissionId={Result.MissionId}");
        GD.Print($"CompletionTime={Result.CompletionTime.ToString("F2", CultureInfo.InvariantCulture)}");
        GD.Print($"Failures={Result.Failures}");
        GD.Print($"Success={Result.Success}");
        GD.Print($"Persistence={Result.Persistence.ToString("F2", CultureInfo.InvariantCulture)}");
        GD.Print($"EventRegistered={_resultRegistered}");
    }

    private static double CalculatePersistence(int failures)
    {
        return Math.Clamp(1.0 - failures * 0.2, 0, 1);
    }

    private static string GetDifficultyText(int difficulty)
    {
        return difficulty switch
        {
            1 => "Easy",
            2 => "Medium",
            3 => "Hard",
            _ => $"Level {difficulty}"
        };
    }

    private static DifficultySettings ReadSettings(int difficulty, string parametersJson)
    {
        DifficultySettings settings = difficulty switch
        {
            1 => new DifficultySettings(5, 1, 0, 2, 75.0f, 2.4f, 280.0f),
            2 => new DifficultySettings(7, 2, 1, 3, 85.0f, 2.0f, 320.0f),
            3 => new DifficultySettings(10, 3, 1, 4, 100.0f, 1.7f, 360.0f),
            _ => new DifficultySettings(7, 2, 1, 3, 85.0f, 2.0f, 320.0f)
        };

        if (string.IsNullOrWhiteSpace(parametersJson))
        {
            return settings;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(parametersJson);
            JsonElement root = document.RootElement;
            settings = settings with
            {
                TargetHealth = ReadPositiveInt(root, "targetHealth", settings.TargetHealth),
                GuardCount = ReadPositiveInt(root, "guards", settings.GuardCount)
            };
        }
        catch (JsonException exception)
        {
            GD.PushWarning($"ParametersJson inválido em Eliminar Alvo: {exception.Message}");
        }

        return settings;
    }

    private static int ReadPositiveInt(JsonElement root, string propertyName, int fallback)
    {
        return root.TryGetProperty(propertyName, out JsonElement value) && value.TryGetInt32(out int result) && result > 0
            ? result
            : fallback;
    }

    private sealed record DifficultySettings(
        int TargetHealth,
        int GuardCount,
        int RangedGuardCount,
        int GuardHealth,
        float GuardSpeed,
        float TargetCooldown,
        float TargetOrbSpeed);
}
