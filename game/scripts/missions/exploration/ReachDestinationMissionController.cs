using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AdaptiveTrials.Game.Components;
using AdaptiveTrials.Game.Dto;
using AdaptiveTrials.Game.Enums;
using AdaptiveTrials.Game.Missions.Shared;
using AdaptiveTrials.Game.Player;
using AdaptiveTrials.Game.Session;
using Godot;

namespace AdaptiveTrials.Game.Missions.Exploration;

/// <summary>
/// Controla a missão de exploração baseada em alcançar um destino.
/// </summary>
public partial class ReachDestinationMissionController : Node
{
	private const int DefaultEasyCheckpoints = 2;
	private const int DefaultMediumCheckpoints = 3;
	private const int DefaultHardCheckpoints = 5;

	private const int DefaultEasyHazards = 2;
	private const int DefaultMediumHazards = 4;
	private const int DefaultHardHazards = 6;

	private const int DefaultEasyMaxFailures = 4;
	private const int DefaultMediumMaxFailures = 3;
	private const int DefaultHardMaxFailures = 2;

	private const int MaximumCheckpoints = 10;
	private const int MaximumHazards = 10;

	private static readonly PackedScene DestinationScene =
		GD.Load<PackedScene>(
			"res://scenes/missions/shared/" +
			"DestinationArea.tscn");

	private static readonly PackedScene CheckpointScene =
		GD.Load<PackedScene>(
			"res://scenes/missions/shared/" +
			"CheckpointArea.tscn");

	private static readonly PackedScene HazardScene =
		GD.Load<PackedScene>(
			"res://scenes/missions/shared/" +
			"HazardArea.tscn");

	private static readonly PackedScene LaserScene =
		GD.Load<PackedScene>(
			"res://scenes/missions/shared/" +
			"LaserObstacle.tscn");

	private SessionManager _sessionManager = null!;
	private PlayerController _player = null!;
	private MissionHud _missionHud = null!;
	private MissionResultPopup _resultPopup = null!;

	private ExplorationMaze _maze = null!;
	private Node2D _dynamicObjects = null!;

	private DestinationArea _destination = null!;

	private readonly List<CheckpointArea>
		_activeCheckpoints = new();

	private readonly List<HazardArea>
		_activeHazards = new();

	private readonly List<LaserObstacle>
		_activeLasers = new();

	private MissionDto _mission = null!;

	private int _requiredCheckpoints;
	private int _requiredHazards;
	private int _requiredLasers;
	private int _maxFailures;

	private int _reachedCheckpoints;
	private int _failures;
	private float _hazardSafeSeconds;
	private float _hazardWarningSeconds;
	private float _hazardActiveSeconds;

	private double _elapsedTime;

	private bool _missionFinished;
	private bool _isFinalizingMission;
	private bool _isRecoveringFromHazard;
	private bool _resultRegistered;

	private Vector2 _lastSafePosition;
	private string _objectiveText = string.Empty;

	public MissionResult? Result { get; private set; }

	public override void _Ready()
	{
		GetReferences();

		_sessionManager =
			GetNode<SessionManager>(
				"/root/SessionManager");

		_resultPopup.ContinueRequested +=
			OnContinueRequested;

		MissionDto? currentMission =
			_sessionManager.CurrentMission;

		if (currentMission is null)
		{
			ShowInitializationError(
				"Nenhuma missão ativa foi encontrada.");

			return;
		}

		_mission = currentMission;

		if (_mission.Type != MissionType.Exploration)
		{
			ShowInitializationError(
				"A missão ativa não é uma missão de exploração.");

			return;
		}

		ReadConfiguration();
		ConfigureMission();
	}

	public override void _Process(double delta)
	{
		if (_missionFinished ||
			_isFinalizingMission)
		{
			return;
		}

		_elapsedTime += delta;

		_missionHud.SetElapsedTime(
			_elapsedTime);
	}

	private void GetReferences()
	{
		_player =
			GetNode<PlayerController>(
				"../Player");

		_missionHud =
			GetNode<MissionHud>(
				"../MissionHud");

		_resultPopup =
			GetNode<MissionResultPopup>(
				"../MissionResultPopup");

		_maze = GetNode<ExplorationMaze>("../Environment/ExplorationMaze");

		_dynamicObjects =
			GetNode<Node2D>(
				"../DynamicObjects");
	}

	private void ConfigureMission()
	{
		_reachedCheckpoints = 0;
		_failures = 0;
		_elapsedTime = 0;

		_missionFinished = false;
		_isFinalizingMission = false;
		_isRecoveringFromHazard = false;
		_resultRegistered = false;

		Result = null;

		_activeCheckpoints.Clear();
		_activeHazards.Clear();
		_activeLasers.Clear();

		_objectiveText =
			$"Siga {_requiredCheckpoints} marcos pela trilha e atravesse os desfiladeiros quando os portões apagarem.";

		_maze.Configure(_mission.Difficulty, ExplorationMaze.CourseMode.ReachDestination);
		_player.GlobalPosition = _maze.PlayerStartPosition;

		_player.Velocity =
			Vector2.Zero;

		// Apenas o nível difícil tem escuridão total e luz no jogador.
		ExplorationLighting.Configure(GetParent(), _player, _mission.Difficulty);

		_lastSafePosition = _maze.PlayerStartPosition;

		_player.SetVisualMode(
			PlayerVisualMode.Normal);

		_player.SetMovementEnabled(
			true);

		_missionHud.Configure(
			_mission.Name,
			_mission.Type,
			_objectiveText,
			_requiredCheckpoints + 1);

		_missionHud.SetProgress(
			0,
			_requiredCheckpoints + 1,
			"Rota");

		_missionHud.SetAttempts(
			_maxFailures,
			_maxFailures);

		_missionHud.SetVisibleState(
			true);

		_resultPopup.HidePopup();

		SpawnCheckpoints();

		if (_missionFinished)
		{
			return;
		}

		SpawnHazards();

		if (_missionFinished)
		{
			return;
		}

		SpawnLasers();

		if (_missionFinished)
		{
			return;
		}

		SpawnDestination();

		GD.Print(
			$"Missão Chegar ao Destino iniciada: " +
			$"MissionId={_mission.Id}, " +
			$"Checkpoints={_requiredCheckpoints}, " +
			$"Hazards={_requiredHazards}, " +
			$"Lasers={_requiredLasers}, " +
			$"MaxFailures={_maxFailures}, " +
			$"Difficulty={_mission.Difficulty}");
	}

	private void SpawnCheckpoints()
	{
		for (int index = 0; index < _requiredCheckpoints; index++)
		{
			CheckpointArea checkpoint = CheckpointScene.Instantiate<CheckpointArea>();
			checkpoint.Name = $"Checkpoint{index + 1:00}";
			checkpoint.CheckpointReached += OnCheckpointReached;
			_dynamicObjects.AddChild(checkpoint);
			checkpoint.GlobalPosition = _maze.CheckpointPositions[index];
			checkpoint.ConfigureIndex(index + 1);
			checkpoint.SetEnabledState(index == 0);
			_activeCheckpoints.Add(checkpoint);
		}
	}

	private void SpawnHazards()
	{
		// Removidos: a travessia agora possui somente portões temporizados,
		// sem perigos aleatórios sobrepostos aos objetivos.
	}

	private void SpawnLasers()
	{
		for (int index = 0; index < _requiredLasers; index++)
		{
			LaserObstacle laser = LaserScene.Instantiate<LaserObstacle>();
			laser.Name = $"Portao{index + 1:00}";
			laser.PlayerHit += OnPlayerHit;
			_dynamicObjects.AddChild(laser);
			laser.GlobalPosition = _maze.GatePositions[index];
			// Um portão ocupa um único corredor; a travessia é possível
			// durante toda a janela segura, em qualquer dificuldade.
			laser.Rotation = Mathf.Pi / 2f;
			laser.Configure(165f, _hazardSafeSeconds, _hazardWarningSeconds,
				_hazardActiveSeconds, index * 0.43f);
			_activeLasers.Add(laser);
		}
	}

	private void SpawnDestination()
	{
		_destination = DestinationScene.Instantiate<DestinationArea>();
		_destination.Name = "Destination";
		_destination.DestinationReached += OnDestinationReached;
		_dynamicObjects.AddChild(_destination);
		_destination.GlobalPosition = _maze.DestinationPosition;
		_destination.SetEnabledState(_reachedCheckpoints >= _requiredCheckpoints);
	}

	private void OnCheckpointReached(
		CheckpointArea checkpoint)
	{
		if (_missionFinished ||
			_isFinalizingMission)
		{
			return;
		}

		_reachedCheckpoints++;

		_lastSafePosition =
			checkpoint.GlobalPosition;

		_missionHud.SetProgress(
			_reachedCheckpoints,
			_requiredCheckpoints + 1,
			"Rota");

		int nextIndex =
			_reachedCheckpoints;

		if (nextIndex < _activeCheckpoints.Count)
		{
			_activeCheckpoints[nextIndex]
				.SetEnabledState(true);
		}
		else if (IsInstanceValid(_destination))
		{
			_destination.SetEnabledState(true);
		}

		GD.Print(
			$"Checkpoint alcançado: " +
			$"{_reachedCheckpoints}/" +
			$"{_requiredCheckpoints}");
	}

	private void OnDestinationReached()
	{
		if (_missionFinished ||
			_isFinalizingMission)
		{
			return;
		}

		if (_reachedCheckpoints <
			_requiredCheckpoints)
		{
			GD.Print(
				"Destino alcançado antes dos checkpoints.");

			_destination.SetEnabledState(
				false);

			GetTree()
				.CreateTimer(0.75)
				.Timeout +=
					() =>
					{
						if (_missionFinished ||
							_isFinalizingMission ||
							!IsInstanceValid(
								_destination))
						{
							return;
						}

						_destination.SetEnabledState(
							true);
					};

			return;
		}

		_missionHud.SetProgress(
			_requiredCheckpoints + 1,
			_requiredCheckpoints + 1,
			"Rota");

		_ = FinishMissionAsync(
			success: true);
	}

	private void OnPlayerHit()
	{
		if (_missionFinished ||
			_isFinalizingMission ||
			_isRecoveringFromHazard)
		{
			return;
		}

		_isRecoveringFromHazard = true;
		_failures++;

		int remainingAttempts =
			Mathf.Max(
				0,
				_maxFailures - _failures);

		_missionHud.SetAttempts(
			remainingAttempts,
			_maxFailures);

		GD.Print(
			$"Falha registrada: " +
			$"{_failures}/{_maxFailures}. " +
			$"Tentativas restantes: " +
			$"{remainingAttempts}");

		if (_failures >=
			_maxFailures)
		{
			_ = FinishMissionAsync(
				success: false);

			return;
		}

		_player.SetMovementEnabled(
			false);

		_player.Velocity =
			Vector2.Zero;

		CallDeferred(
			MethodName.RecoverPlayerAfterHazard);
	}

	private async void RecoverPlayerAfterHazard()
	{
		_player.GlobalPosition =
			_lastSafePosition;

		_player.Velocity =
			Vector2.Zero;

		await ToSignal(
			GetTree().CreateTimer(0.45),
			SceneTreeTimer.SignalName.Timeout);

		if (_missionFinished ||
			_isFinalizingMission)
		{
			return;
		}

		_player.SetMovementEnabled(
			true);

		_isRecoveringFromHazard =
			false;
	}

	private async Task FinishMissionAsync(
		bool success)
	{
		if (_missionFinished ||
			_isFinalizingMission)
		{
			return;
		}

		_isFinalizingMission = true;
		_isRecoveringFromHazard = false;

		_player.SetMovementEnabled(
			false);

		_player.Velocity =
			Vector2.Zero;

		DisableMissionAreas();

		_missionHud.SetVisibleState(
			false);

		Result = new MissionResult
		{
			MissionId =
				_mission.Id,

			CompletionTime =
				_elapsedTime,

			Failures =
				_failures,

			Success =
				success,

			Persistence =
				CalculatePersistence(
					_failures)
		};

		_resultRegistered =
			await _sessionManager
				.RegisterCurrentMissionResultAsync(
					Result);

		_missionFinished = true;
		_isFinalizingMission = false;

		string statisticValue =
			$"{_reachedCheckpoints}/" +
			$"{_requiredCheckpoints}";

		_resultPopup.ShowResult(
			success: success,
			missionName: _mission.Name,
			objective: _objectiveText,
			completionTime: _elapsedTime,
			statisticTitle:
				"Checkpoints alcançados",
			statisticValue:
				statisticValue,
			difficulty:
				GetDifficultyText(
					_mission.Difficulty),
			failures:
				_failures);

		PrintMissionResult();
	}

	private void DisableMissionAreas()
	{
		foreach (HazardArea hazard
				 in _activeHazards)
		{
			if (IsInstanceValid(hazard))
			{
				hazard.SetDeferred(
					Area2D.PropertyName.Monitoring,
					false);
			}
		}

		foreach (LaserObstacle laser
				 in _activeLasers)
		{
			if (IsInstanceValid(laser))
			{
				laser.SetEnabledState(false);
			}
		}

		foreach (CheckpointArea checkpoint
				 in _activeCheckpoints)
		{
			if (IsInstanceValid(checkpoint))
			{
				checkpoint.SetDeferred(
					Area2D.PropertyName.Monitoring,
					false);
			}
		}

		if (IsInstanceValid(_destination))
		{
			_destination.SetEnabledState(
				false);
		}
	}

	private async void OnContinueRequested()
	{
		if (!_missionFinished ||
			Result is null)
		{
			return;
		}

		if (!_resultRegistered)
		{
			GD.PushError(
				"O evento comportamental não foi " +
				"registrado. Não é possível avançar.");

			return;
		}

		bool continued =
			await _sessionManager
				.ContinueAfterCurrentMissionAsync(
					GetTree());

		if (!continued)
		{
			GD.PushError(
				"Não foi possível continuar o fluxo " +
				"da sessão.");
		}
	}

	private void PrintMissionResult()
	{
		if (Result is null)
		{
			return;
		}

		GD.Print(
			"Resultado da missão:");

		GD.Print(
			$"MissionId={Result.MissionId}");

		GD.Print(
			$"CompletionTime=" +
			$"{Result.CompletionTime.ToString(
				"F2",
				CultureInfo.InvariantCulture)}");

		GD.Print(
			$"Failures={Result.Failures}");

		GD.Print(
			$"Success={Result.Success}");

		GD.Print(
			$"Persistence=" +
			$"{Result.Persistence.ToString(
				"F2",
				CultureInfo.InvariantCulture)}");

		GD.Print(
			$"EventRegistered=" +
			$"{_resultRegistered}");
	}

	private void ReadConfiguration()
	{
		MissionConfiguration defaults =
			GetDefaultsByDifficulty(
				_mission.Difficulty);

		_requiredCheckpoints =
			defaults.Checkpoints;

		_requiredHazards =
			defaults.Hazards;

		_maxFailures =
			defaults.MaxFailures;

		_requiredLasers =
			GetRequiredLasersByDifficulty(
				_mission.Difficulty);

		ConfigureHazardTiming(
			_mission.Difficulty);

		if (!string.IsNullOrWhiteSpace(
				_mission.ParametersJson))
		{
			try
			{
				using JsonDocument document =
					JsonDocument.Parse(
						_mission.ParametersJson);

				JsonElement root =
					document.RootElement;

				int apiCheckpoints =
					ReadPositiveInteger(
						root,
						"checkpoints",
						_requiredCheckpoints);

				_requiredCheckpoints =
					Mathf.Max(
						_requiredCheckpoints,
						apiCheckpoints);

				int apiHazards =
					ReadPositiveInteger(
						root,
						"hazards",
						_requiredHazards);

				_requiredHazards =
					Mathf.Max(
						_requiredHazards,
						apiHazards);

				_requiredLasers =
					ReadPositiveInteger(
						root,
						"lasers",
						_requiredLasers);

				_maxFailures =
					ReadPositiveInteger(
						root,
						"maxFailures",
						_maxFailures);

				_hazardSafeSeconds =
					ReadPositiveFloat(
						root,
						"safeSeconds",
						_hazardSafeSeconds);

				_hazardWarningSeconds =
					ReadPositiveFloat(
						root,
						"warningSeconds",
						_hazardWarningSeconds);

				_hazardActiveSeconds =
					ReadPositiveFloat(
						root,
						"activeSeconds",
						_hazardActiveSeconds);
			}
			catch (JsonException exception)
			{
				GD.PushWarning(
					$"ParametersJson inválido: " +
					$"{exception.Message}");
			}
		}

		// Número de etapas definido pelo mapa, não por parâmetros antigos da API.
		_requiredCheckpoints = Mathf.Clamp(_mission.Difficulty, 1, 3);

		_requiredHazards =
			Mathf.Clamp(
				_requiredHazards,
				1,
				MaximumHazards);

		_requiredLasers = Mathf.Clamp(_mission.Difficulty, 1, 3);
		_requiredHazards = 0;

		// Balanceamento: janelas seguras menores e fase ativa mais longa.
		// Usa limites conservadores para não criar travessias impossíveis.
		float minActive = _mission.Difficulty switch
		{
			1 => 1.15f, 2 => 1.40f, _ => 1.65f
		};
		_hazardSafeSeconds = Mathf.Clamp(_hazardSafeSeconds, 1.75f, 3.25f);
		_hazardWarningSeconds = Mathf.Clamp(_hazardWarningSeconds, 0.55f, 1.0f);
		_hazardActiveSeconds = Mathf.Clamp(_hazardActiveSeconds, minActive, 2.5f);

		GD.Print(
			$"Configuração da missão carregada: " +
			$"Difficulty={_mission.Difficulty}, " +
			$"Checkpoints={_requiredCheckpoints}, " +
			$"Hazards={_requiredHazards}, " +
			$"Lasers={_requiredLasers}, " +
			$"MaxFailures={_maxFailures}");
	}

	private void ShowInitializationError(
		string message)
	{
		_missionFinished = true;
		_isFinalizingMission = false;
		_isRecoveringFromHazard = false;

		if (_player is not null)
		{
			_player.SetMovementEnabled(
				false);

			_player.Velocity =
				Vector2.Zero;
		}

		if (_missionHud is not null)
		{
			_missionHud.SetVisibleState(
				false);
		}

		if (_resultPopup is not null)
		{
			_resultPopup.ShowResult(
				success: false,
				missionName:
					"Missão indisponível",
				objective:
					message,
				completionTime: 0,
				statisticTitle:
					"Status",
				statisticValue:
					"Erro de inicialização",
				difficulty: "-",
				failures: 0);
		}

		GD.PushError(message);
	}

	private static List<Marker2D> GetUniqueSpawnPoints(
		IEnumerable<Marker2D> spawnPoints)
	{
		return spawnPoints
			.GroupBy(
				point =>
					new Vector2I(
						Mathf.RoundToInt(
							point.GlobalPosition.X),
						Mathf.RoundToInt(
							point.GlobalPosition.Y)))
			.Select(
				group =>
					group.First())
			.ToList();
	}

	private static double CalculatePersistence(
		int failures)
	{
		return Math.Clamp(
			1.0 - failures * 0.2,
			0,
			1);
	}

	private static int ReadPositiveInteger(
		JsonElement root,
		string propertyName,
		int fallback)
	{
		if (!root.TryGetProperty(
				propertyName,
				out JsonElement value))
		{
			return fallback;
		}

		if (!value.TryGetInt32(
				out int result))
		{
			return fallback;
		}

		return result > 0
			? result
			: fallback;
	}

	private static float ReadPositiveFloat(
		JsonElement root,
		string propertyName,
		float fallback)
	{
		if (!root.TryGetProperty(
				propertyName,
				out JsonElement value))
		{
			return fallback;
		}

		if (!value.TryGetSingle(
				out float result))
		{
			return fallback;
		}

		return result > 0
			? result
			: fallback;
	}


	private static MissionConfiguration
		GetDefaultsByDifficulty(
			int difficulty)
	{
		return difficulty switch
		{
			1 =>
				new MissionConfiguration(
					DefaultEasyCheckpoints,
					DefaultEasyHazards,
					DefaultEasyMaxFailures),

			2 =>
				new MissionConfiguration(
					DefaultMediumCheckpoints,
					DefaultMediumHazards,
					DefaultMediumMaxFailures),

			3 =>
				new MissionConfiguration(
					DefaultHardCheckpoints,
					DefaultHardHazards,
					DefaultHardMaxFailures),

			_ =>
				new MissionConfiguration(
					DefaultEasyCheckpoints,
					DefaultEasyHazards,
					DefaultEasyMaxFailures)
		};
	}

	private static int GetRequiredLasersByDifficulty(
		int difficulty)
	{
		return difficulty switch
		{
			1 => 1,
			2 => 3,
			3 => 5,
			_ => 1
		};
	}

	private static float GetLaserLength(
		int difficulty,
		int index)
	{
		float baseLength = difficulty switch
		{
			1 => 180.0f,
			2 => 225.0f,
			3 => 265.0f,
			_ => 200.0f
		};

		return baseLength + (index % 2) * 30.0f;
	}

	private void ConfigureHazardTiming(
		int difficulty)
	{
		switch (difficulty)
		{
			case 1:
				_hazardSafeSeconds = 2.8f;
				_hazardWarningSeconds = 1.15f;
				_hazardActiveSeconds = 1.0f;
				break;

			case 3:
				_hazardSafeSeconds = 1.35f;
				_hazardWarningSeconds = 0.55f;
				_hazardActiveSeconds = 1.55f;
				break;

			default:
				_hazardSafeSeconds = 2.0f;
				_hazardWarningSeconds = 0.8f;
				_hazardActiveSeconds = 1.25f;
				break;
		}
	}

	private static string GetDifficultyText(
		int difficulty)
	{
		return difficulty switch
		{
			1 => "Fácil",
			2 => "Média",
			3 => "Difícil",
			_ => $"Nível {difficulty}"
		};
	}

	private static void Shuffle<T>(
		IList<T> values)
	{
		for (int index = values.Count - 1;
			 index > 0;
			 index--)
		{
			int swapIndex =
				GD.RandRange(
					0,
					index);

			(values[index], values[swapIndex]) =
				(values[swapIndex], values[index]);
		}
	}

	private readonly record struct MissionConfiguration(
		int Checkpoints,
		int Hazards,
		int MaxFailures);
}
