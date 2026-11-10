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
/// Controla a missão de proteção de um objeto durante ondas de inimigos.
/// </summary>
public partial class DefendObjectMissionController : Node
{
    private SessionManager _sessionManager = null!;
    private PlayerController _player = null!;
    private DefensibleObject _defensibleObject = null!;
    private MissionHud _missionHud = null!;
    private MissionResultPopup _resultPopup = null!;
    private CombatWaveManager _waveManager = null!;
    private WaveAnnouncement _waveAnnouncement = null!;
    private Node2D _dynamicEnemies = null!;
    private Node2D _spawnPoints = null!;
    private Node2D _objectRelocationPoints = null!;
    private Marker2D _playerSpawn = null!;

    private readonly List<Marker2D> _spawnMarkers = new();
    private readonly List<Marker2D> _objectRelocationMarkers = new();
    private readonly List<CombatWaveDefinition> _waves = new();
    private readonly RandomNumberGenerator _relocationRandom = new();

    private const float MinimumDistanceFromPreviousPosition = 180.0f;
    private const float MinimumDistanceFromPlayer = 145.0f;
    private const float MinimumDistanceFromEnemy = 105.0f;

    private MissionDto _mission = null!;
    private double _elapsedTime;
    private int _activeEnemies;
    private int _completedWaves;
    private int _totalWaves;
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
        _defensibleObject.HealthChanged += OnObjectHealthChanged;
        _defensibleObject.DamageReceived += OnObjectDamageReceived;
        _defensibleObject.Destroyed += OnObjectDestroyed;
        _waveManager.WaveStarted += OnWaveStarted;
        _waveManager.WaveCompleted += OnWaveCompleted;
        _waveManager.ActiveEnemyCountChanged += OnActiveEnemyCountChanged;
        _waveManager.AllWavesCompleted += OnAllWavesCompleted;

        MissionDto? currentMission = _sessionManager.CurrentMission;
        if (currentMission is null)
        {
            ShowInitializationError("Nenhuma missão ativa foi encontrada.");
            return;
        }

        _relocationRandom.Randomize();

        _mission = currentMission;
        if (_mission.Type != MissionType.Combat)
        {
            ShowInitializationError("A missão ativa não é uma missão de combate.");
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
        _defensibleObject = GetNode<DefensibleObject>("../DefensibleObject");
        _missionHud = GetNode<MissionHud>("../MissionHud");
        _resultPopup = GetNode<MissionResultPopup>("../MissionResultPopup");
        _waveManager = GetNode<CombatWaveManager>("../CombatWaveManager");
        _waveAnnouncement = GetNode<WaveAnnouncement>(
            "../WaveAnnouncementLayer/WaveAnnouncement");
        _dynamicEnemies = GetNode<Node2D>("../DynamicEnemies");
        _spawnPoints = GetNode<Node2D>("../SpawnPoints/EnemySpawns");
        _objectRelocationPoints =
            GetNode<Node2D>("../SpawnPoints/ObjectRelocationPoints");
        _playerSpawn = GetNode<Marker2D>("../SpawnPoints/PlayerSpawn");
    }

    private void ConfigureMission()
    {
        DifficultySettings settings = ReadSettings(
            _mission.Difficulty,
            _mission.ParametersJson);

        _elapsedTime = 0.0;
        _activeEnemies = 0;
        _completedWaves = 0;
        _totalWaves = settings.WaveCount;
        _failures = 0;
        _missionFinished = false;
        _isFinalizingMission = false;
        _resultRegistered = false;
        Result = null;

        _objectiveText =
            $"Proteja o cristal e derrote todas as {_totalWaves} ondas de inimigos.";

        _player.GlobalPosition = _playerSpawn.GlobalPosition;
        _player.Velocity = Vector2.Zero;
        _player.SetVisualMode(PlayerVisualMode.Combat);
        _player.RestoreFullHealth();
        _player.SetDamageEnabled(true);
        _player.SetMovementEnabled(true);

        _defensibleObject.Configure(settings.ObjectHealth);
        _defensibleObject.SetDamageEnabled(true);

        _missionHud.Configure(
            _mission.Name,
            _mission.Type,
            _objectiveText,
            _totalWaves);
        _missionHud.SetProgress(0, _totalWaves, "Ondas");
        _missionHud.SetElapsedTime(0.0);
        UpdateStatusText();
        _missionHud.SetVisibleState(true);
        _resultPopup.HidePopup();

        ReadSpawnMarkers();
        ReadObjectRelocationMarkers();
        BuildWaves(settings);

        _waveManager.Configure(
            _defensibleObject,
            _dynamicEnemies,
            _spawnMarkers,
            _waves);
        _waveManager.AdvanceWhenEnemiesDefeated = true;
        _waveManager.ClearEnemiesOnTimedAdvance = false;
        _waveManager.LockEnemiesToConfiguredTarget = true;

        GD.Print(
            $"Missão Defender Objeto iniciada: MissionId={_mission.Id}, " +
            $"Difficulty={_mission.Difficulty}, Waves={_totalWaves}, " +
            $"ObjectHealth={settings.ObjectHealth}");

        CallDeferred(nameof(StartWaves));
    }

    private void ReadSpawnMarkers()
    {
        _spawnMarkers.Clear();
        foreach (Node child in _spawnPoints.GetChildren())
        {
            if (child is Marker2D marker)
            {
                _spawnMarkers.Add(marker);
            }
        }
    }

    private void ReadObjectRelocationMarkers()
    {
        _objectRelocationMarkers.Clear();

        foreach (Node child in _objectRelocationPoints.GetChildren())
        {
            if (child is Marker2D marker)
            {
                _objectRelocationMarkers.Add(marker);
            }
        }

        if (_objectRelocationMarkers.Count == 0)
        {
            GD.PushWarning(
                "Nenhum ponto de reposicionamento foi configurado para o cristal.");
        }
    }

    private void BuildWaves(DifficultySettings settings)
    {
        _waves.Clear();

        for (int index = 0; index < settings.WaveCount; index++)
        {
            int pressureStep = index / 2;
            int meleeCount = settings.BaseMeleeCount + pressureStep;
            int rangedCount = index >= settings.FirstRangedWave
                ? settings.BaseRangedCount +
                  (index >= settings.FirstRangedWave + 2 ? 1 : 0)
                : 0;

            _waves.Add(new CombatWaveDefinition
            {
                MeleeCount = meleeCount,
                RangedCount = rangedCount,
                EnemyHealth = settings.EnemyHealth,
                EnemyDamage = 1,
                MeleeSpeed = settings.MeleeSpeed + index * 2.0f,
                MeleeCooldownSeconds = settings.MeleeCooldown,
                RangedCooldownSeconds = settings.RangedCooldown,
                RangedOrbSpeed = settings.RangedOrbSpeed,
                RangedMovementEnabled = false
            });
        }
    }

    private void StartWaves()
    {
        if (!_missionFinished && !_isFinalizingMission)
        {
            _waveManager.StartWaves();
        }
    }

    private void OnWaveStarted(
        int currentWave,
        int totalWaves,
        int enemyCount)
    {
        if (_missionFinished || _isFinalizingMission)
        {
            return;
        }

        _waveAnnouncement.ShowWave(currentWave, totalWaves, enemyCount);
    }

    private void OnWaveCompleted(int completedWave, int totalWaves)
    {
        if (_missionFinished || _isFinalizingMission)
        {
            return;
        }

        _completedWaves = Math.Clamp(completedWave, 0, totalWaves);
        _missionHud.SetProgress(_completedWaves, _totalWaves, "Ondas");
        UpdateStatusText();
    }

    private void OnActiveEnemyCountChanged(int activeEnemies)
    {
        _activeEnemies = Math.Max(0, activeEnemies);
        UpdateStatusText();
    }

    private void OnAllWavesCompleted()
    {
        if (_missionFinished || _isFinalizingMission)
        {
            return;
        }

        _completedWaves = _totalWaves;
        _missionHud.SetProgress(_completedWaves, _totalWaves, "Ondas");
        _ = FinishMissionAsync(true, "Todas as ondas de inimigos foram derrotadas.");
    }

    private void OnPlayerHealthChanged(int currentHealth, int maximumHealth)
    {
        UpdateStatusText();
    }

    private void OnObjectHealthChanged(int currentHealth, int maximumHealth)
    {
        UpdateStatusText();
    }

    private void OnObjectDamageReceived(int damage, Node source)
    {
        if (_missionFinished ||
            _isFinalizingMission ||
            _defensibleObject.IsDestroyed ||
            _defensibleObject.CurrentHealth <= 0)
        {
            return;
        }

        RelocateDefensibleObject();
    }

    private void RelocateDefensibleObject()
    {
        if (_objectRelocationMarkers.Count == 0)
        {
            return;
        }

        Vector2 previousPosition =
            _defensibleObject.GlobalPosition;

        List<Marker2D> validMarkers =
            _objectRelocationMarkers.FindAll(
                marker =>
                    IsRelocationPointValid(
                        marker.GlobalPosition,
                        previousPosition));

        Marker2D? selectedMarker =
            validMarkers.Count > 0
                ? validMarkers[
                    _relocationRandom.RandiRange(
                        0,
                        validMarkers.Count - 1)]
                : FindBestFallbackRelocationPoint(previousPosition);

        if (selectedMarker is null)
        {
            GD.PushWarning(
                "Não foi encontrado um ponto válido para reposicionar o cristal.");
            return;
        }

        _defensibleObject.GlobalPosition =
            selectedMarker.GlobalPosition;

        _defensibleObject.PlayRelocationFeedback();

        GD.Print(
            $"Cristal reposicionado: " +
            $"{previousPosition} -> {_defensibleObject.GlobalPosition}. " +
            $"Vida preservada: " +
            $"{_defensibleObject.CurrentHealth}/" +
            $"{_defensibleObject.MaximumHealth}.");
    }

    private bool IsRelocationPointValid(
        Vector2 position,
        Vector2 previousPosition)
    {
        if (position.DistanceTo(previousPosition) <
            MinimumDistanceFromPreviousPosition)
        {
            return false;
        }

        if (position.DistanceTo(_player.GlobalPosition) <
            MinimumDistanceFromPlayer)
        {
            return false;
        }

        foreach (Node child in _dynamicEnemies.GetChildren())
        {
            if (child is not CombatEnemyController enemy ||
                !GodotObject.IsInstanceValid(enemy))
            {
                continue;
            }

            if (position.DistanceTo(enemy.GlobalPosition) <
                MinimumDistanceFromEnemy)
            {
                return false;
            }
        }

        return true;
    }

    private Marker2D? FindBestFallbackRelocationPoint(
        Vector2 previousPosition)
    {
        Marker2D? bestMarker = null;
        float bestScore = float.MinValue;

        foreach (Marker2D marker in _objectRelocationMarkers)
        {
            Vector2 position =
                marker.GlobalPosition;

            float distanceFromPrevious =
                position.DistanceTo(previousPosition);

            if (distanceFromPrevious <
                MinimumDistanceFromPreviousPosition)
            {
                continue;
            }

            float minimumClearance =
                position.DistanceTo(_player.GlobalPosition);

            foreach (Node child in _dynamicEnemies.GetChildren())
            {
                if (child is not CombatEnemyController enemy ||
                    !GodotObject.IsInstanceValid(enemy))
                {
                    continue;
                }

                minimumClearance =
                    Mathf.Min(
                        minimumClearance,
                        position.DistanceTo(enemy.GlobalPosition));
            }

            float score =
                minimumClearance +
                distanceFromPrevious * 0.25f;

            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestMarker = marker;
        }

        return bestMarker;
    }

    private void UpdateStatusText()
    {
        _missionHud.SetAttemptsText(
            $"Cristal: {_defensibleObject.CurrentHealth}/" +
            $"{_defensibleObject.MaximumHealth} | " +
            $"Vida: {_player.CurrentHealth}/{_player.MaximumHealth} | " +
            $"Inimigos: {_activeEnemies}");
    }

    private void OnPlayerDied(Node source)
    {
        if (_missionFinished || _isFinalizingMission)
        {
            return;
        }

        _failures = 1;
        _ = FinishMissionAsync(false, "O jogador foi derrotado.");
    }

    private void OnObjectDestroyed(Node source)
    {
        if (_missionFinished || _isFinalizingMission)
        {
            return;
        }

        _failures = 1;
        _ = FinishMissionAsync(false, "O cristal protegido foi destruído.");
    }

    private async Task FinishMissionAsync(bool success, string resultDetail)
    {
        if (_missionFinished || _isFinalizingMission)
        {
            return;
        }

        _isFinalizingMission = true;
        _waveManager.StopAndClear();
        _player.SetMovementEnabled(false);
        _player.SetDamageEnabled(false);
        _player.Velocity = Vector2.Zero;
        _defensibleObject.SetDamageEnabled(false);

        Result = new MissionResult
        {
            MissionId = _mission.Id,
            CompletionTime = _elapsedTime,
            Failures = _failures,
            Success = success,
            Persistence = CalculatePersistence(_failures)
        };

        _resultRegistered = await
            _sessionManager.RegisterCurrentMissionResultAsync(Result);

        _missionFinished = true;
        _isFinalizingMission = false;
        _missionHud.SetVisibleState(false);

        _resultPopup.ShowResult(
            success,
            _mission.Name,
            _objectiveText,
            _elapsedTime,
            "Resultado da defesa",
            success
                ? $"{_completedWaves}/{_totalWaves} ondas concluídas"
                : resultDetail,
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
            GD.PushError(
                "O evento comportamental não foi registrado. Não é possível avançar.");
            return;
        }

        bool continued = await
            _sessionManager.ContinueAfterCurrentMissionAsync(GetTree());

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
        _resultPopup.ShowResult(
            false,
            "Missão indisponível",
            message,
            0,
            "Status",
            "Erro de inicialização",
            "-",
            0);
        GD.PushError(message);
    }

    private void PrintMissionResult()
    {
        if (Result is null)
        {
            return;
        }

        GD.Print("Resultado da missão Defender Objeto:");
        GD.Print($"MissionId={Result.MissionId}");
        GD.Print(
            $"CompletionTime={Result.CompletionTime.ToString("F2", CultureInfo.InvariantCulture)}");
        GD.Print($"WavesCleared={_completedWaves}/{_totalWaves}");
        GD.Print(
            $"ObjectHealth={_defensibleObject.CurrentHealth}/" +
            $"{_defensibleObject.MaximumHealth}");
        GD.Print($"Failures={Result.Failures}");
        GD.Print($"Success={Result.Success}");
        GD.Print(
            $"Persistence={Result.Persistence.ToString("F2", CultureInfo.InvariantCulture)}");
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
            1 => "Fácil",
            2 => "Média",
            3 => "Difícil",
            _ => $"Nível {difficulty}"
        };
    }

    private static DifficultySettings ReadSettings(
        int difficulty,
        string parametersJson)
    {
        DifficultySettings settings = difficulty switch
        {
            1 => new DifficultySettings(2, 14, 2, 1, 2, 1, 68.0f, 1.3f, 2.5f, 260.0f),
            2 => new DifficultySettings(3, 12, 2, 1, 2, 2, 76.0f, 1.2f, 2.2f, 290.0f),
            3 => new DifficultySettings(5, 10, 3, 1, 1, 3, 84.0f, 1.1f, 2.0f, 320.0f),
            _ => new DifficultySettings(3, 12, 2, 1, 2, 2, 76.0f, 1.2f, 2.2f, 290.0f)
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
                WaveCount = ReadPositiveInt(root, "waves", settings.WaveCount),
                ObjectHealth = ReadPositiveInt(
                    root,
                    "objectHealth",
                    settings.ObjectHealth)
            };
        }
        catch (JsonException exception)
        {
            GD.PushWarning(
                $"ParametersJson inválido em Defender Objeto: {exception.Message}");
        }

        return settings;
    }

    private static int ReadPositiveInt(
        JsonElement root,
        string propertyName,
        int fallback)
    {
        return root.TryGetProperty(propertyName, out JsonElement value) &&
               value.TryGetInt32(out int result) &&
               result > 0
            ? result
            : fallback;
    }

    private sealed record DifficultySettings(
        int WaveCount,
        int ObjectHealth,
        int BaseMeleeCount,
        int BaseRangedCount,
        int FirstRangedWave,
        int EnemyHealth,
        float MeleeSpeed,
        float MeleeCooldown,
        float RangedCooldown,
        float RangedOrbSpeed);
}
