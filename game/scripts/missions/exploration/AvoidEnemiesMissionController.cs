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
/// Controla a missão de exploração baseada em evitar
/// inimigos durante o percurso até o destino.
/// </summary>
public partial class AvoidEnemiesMissionController : Node
{
	private const int DefaultEasyEnemies = 2;
	private const int DefaultMediumEnemies = 4;
	private const int DefaultHardEnemies = 6;

	private const float DefaultEasyEnemySpeed = 65f;
	private const float DefaultMediumEnemySpeed = 85f;
	private const float DefaultHardEnemySpeed = 110f;

	private const float DefaultEasyDetectionRadius = 70f;
	private const float DefaultMediumDetectionRadius = 90f;
	private const float DefaultHardDetectionRadius = 115f;

	private const int DefaultEasyMaxFailures = 4;
	private const int DefaultMediumMaxFailures = 3;
	private const int DefaultHardMaxFailures = 2;

	private const int MaximumEnemies = 6;

	private static readonly PackedScene PatrolEnemyScene =
		GD.Load<PackedScene>(
			"res://scenes/missions/shared/" +
			"PatrolEnemy.tscn");

	private static readonly PackedScene DestinationScene =
		GD.Load<PackedScene>(
			"res://scenes/missions/shared/" +
			"DestinationArea.tscn");

	private SessionManager _sessionManager = null!;
	private PlayerController _player = null!;
	private MissionHud _missionHud = null!;
	private MissionResultPopup _resultPopup = null!;

	private Marker2D _playerSpawn = null!;
	private Marker2D _destinationSpawn = null!;

	private Node2D _enemyPatrols = null!;
	private Node2D _dynamicObjects = null!;

	private readonly List<PatrolEnemy> _activeEnemies =
		new();

	private DestinationArea _destination = null!;
	private MissionDto _mission = null!;

	private int _requiredEnemies;
	private int _maxFailures;
	private int _failures;

	private float _enemySpeed;
	private float _detectionRadius;

	private double _elapsedTime;

	private bool _missionFinished;
	private bool _isFinalizingMission;
	private bool _isRecoveringAfterDetection;
	private bool _resultRegistered;

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
				"No active mission was found.");

			return;
		}

		_mission = currentMission;

		if (_mission.Type != MissionType.Exploration)
		{
			ShowInitializationError(
				"The active mission is not an exploration mission.");

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

		_playerSpawn =
			GetNode<Marker2D>(
				"../SpawnPoints/PlayerSpawn");

		_destinationSpawn =
			GetNode<Marker2D>(
				"../SpawnPoints/DestinationSpawn");

		_enemyPatrols =
			GetNode<Node2D>(
				"../SpawnPoints/EnemyPatrols");

		_dynamicObjects =
			GetNode<Node2D>(
				"../DynamicObjects");
	}

	private void ConfigureMission()
	{
		_failures = 0;
		_elapsedTime = 0;

		_missionFinished = false;
		_isFinalizingMission = false;
		_isRecoveringAfterDetection = false;
		_resultRegistered = false;

		Result = null;

		_activeEnemies.Clear();

		_objectiveText =
			"Avoid the enemy patrols and reach " +
			"the destination without being detected.";

		_player.GlobalPosition =
			_playerSpawn.GlobalPosition;

		_player.Velocity =
			Vector2.Zero;

		_player.SetVisualMode(
			PlayerVisualMode.Normal);

		_player.SetMovementEnabled(
			true);

		_missionHud.Configure(
			_mission.Name,
			_mission.Type,
			_objectiveText,
			1);

		_missionHud.SetProgress(
			0,
			1,
			"Escape");

		_missionHud.SetAttempts(
			_maxFailures,
			_maxFailures);

		_missionHud.SetVisibleState(
			true);

		_resultPopup.HidePopup();

		SpawnEnemies();

		if (_missionFinished)
		{
			return;
		}

		SpawnDestination();

		GD.Print(
			$"Missão Evitar Inimigos iniciada: " +
			$"MissionId={_mission.Id}, " +
			$"Enemies={_requiredEnemies}, " +
			$"EnemySpeed={_enemySpeed}, " +
			$"DetectionRadius={_detectionRadius}, " +
			$"MaxFailures={_maxFailures}, " +
			$"Difficulty={_mission.Difficulty}");
	}

	private void SpawnEnemies()
	{
		List<Node2D> patrolDefinitions =
			_enemyPatrols
				.GetChildren()
				.OfType<Node2D>()
				.ToList();

		if (patrolDefinitions.Count <
			_requiredEnemies)
		{
			ShowInitializationError(
				$"The map contains " +
				$"{patrolDefinitions.Count} patrol routes, " +
				$"but the mission requires " +
				$"{_requiredEnemies} enemies.");

			return;
		}

		Shuffle(patrolDefinitions);

		for (int index = 0;
			 index < _requiredEnemies;
			 index++)
		{
			Node2D patrolDefinition =
				patrolDefinitions[index];

			Marker2D? start =
				patrolDefinition.GetNodeOrNull<Marker2D>(
					"Start");

			Marker2D? end =
				patrolDefinition.GetNodeOrNull<Marker2D>(
					"End");

			if (start is null ||
				end is null)
			{
				ShowInitializationError(
					$"The patrol route " +
					$"{patrolDefinition.Name} does not " +
					"contain Start and End markers.");

				return;
			}

			PatrolEnemy enemy =
				PatrolEnemyScene
					.Instantiate<PatrolEnemy>();

			enemy.Name =
				$"PatrolEnemy{index + 1:00}";

			enemy.PlayerDetected +=
				OnPlayerDetected;

			_dynamicObjects.AddChild(
				enemy);

			enemy.Configure(
				start.GlobalPosition,
				end.GlobalPosition,
				_enemySpeed,
				_detectionRadius);

			_activeEnemies.Add(
				enemy);
		}
	}

	private void SpawnDestination()
	{
		_destination =
			DestinationScene
				.Instantiate<DestinationArea>();

		_destination.Name =
			"Destination";

		_destination.DestinationReached +=
			OnDestinationReached;

		_dynamicObjects.AddChild(
			_destination);

		_destination.GlobalPosition =
			_destinationSpawn.GlobalPosition;
	}

	private void OnPlayerDetected(
		PatrolEnemy enemy)
	{
		if (_missionFinished ||
			_isFinalizingMission ||
			_isRecoveringAfterDetection)
		{
			return;
		}

		_isRecoveringAfterDetection = true;
		_failures++;

		int remainingAttempts =
			Mathf.Max(
				0,
				_maxFailures - _failures);

		_missionHud.SetAttempts(
			remainingAttempts,
			_maxFailures);

		GD.Print(
			$"Jogador detectado por {enemy.Name}. " +
			$"Falhas={_failures}/{_maxFailures}. " +
			$"Tentativas restantes={remainingAttempts}");

		_player.SetMovementEnabled(
			false);

		_player.Velocity =
			Vector2.Zero;

		SetEnemiesDetectionEnabled(
			false);

		if (_failures >=
			_maxFailures)
		{
			_ = FinishMissionAsync(
				success: false);

			return;
		}

		CallDeferred(
			MethodName.RecoverPlayerAfterDetection);
	}

	private async void RecoverPlayerAfterDetection()
	{
		_player.GlobalPosition =
			_playerSpawn.GlobalPosition;

		_player.Velocity =
			Vector2.Zero;

		await ToSignal(
			GetTree().CreateTimer(0.75),
			SceneTreeTimer.SignalName.Timeout);

		if (_missionFinished ||
			_isFinalizingMission)
		{
			return;
		}

		SetEnemiesDetectionEnabled(
			true);

		_player.SetMovementEnabled(
			true);

		_isRecoveringAfterDetection =
			false;
	}

	private void OnDestinationReached()
	{
		if (_missionFinished ||
			_isFinalizingMission)
		{
			return;
		}

		_missionHud.SetProgress(
			1,
			1,
			"Escape");

		_ = FinishMissionAsync(
			success: true);
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
		_isRecoveringAfterDetection = false;

		_player.SetMovementEnabled(
			false);

		_player.Velocity =
			Vector2.Zero;

		SetEnemiesDetectionEnabled(
			false);

		if (IsInstanceValid(_destination))
		{
			_destination.SetEnabledState(
				false);
		}

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

		_resultPopup.ShowResult(
			success: success,
			missionName: _mission.Name,
			objective: _objectiveText,
			completionTime: _elapsedTime,
			statisticTitle:
				"Times Detected",
			statisticValue:
				_failures.ToString(),
			difficulty:
				GetDifficultyText(
					_mission.Difficulty),
			failures:
				_failures);

		PrintMissionResult();
	}

	private void SetEnemiesDetectionEnabled(
		bool enabled)
	{
		foreach (PatrolEnemy enemy
				 in _activeEnemies)
		{
			if (!IsInstanceValid(enemy))
			{
				continue;
			}

			enemy.SetDetectionEnabled(
				enabled);
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

	private void ReadConfiguration()
	{
		MissionConfiguration defaults =
			GetDefaultsByDifficulty(
				_mission.Difficulty);

		_requiredEnemies =
			defaults.Enemies;

		_enemySpeed =
			defaults.EnemySpeed;

		_detectionRadius =
			defaults.DetectionRadius;

		_maxFailures =
			defaults.MaxFailures;

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

				_requiredEnemies =
					ReadPositiveInteger(
						root,
						"enemies",
						_requiredEnemies);

				_enemySpeed =
					ReadPositiveFloat(
						root,
						"enemySpeed",
						_enemySpeed);

				_detectionRadius =
					ReadPositiveFloat(
						root,
						"detectionRadius",
						_detectionRadius);

				_maxFailures =
					ReadPositiveInteger(
						root,
						"maxFailures",
						_maxFailures);
			}
			catch (JsonException exception)
			{
				GD.PushWarning(
					$"ParametersJson inválido: " +
					$"{exception.Message}");
			}
		}

		_requiredEnemies =
			Mathf.Clamp(
				_requiredEnemies,
				1,
				MaximumEnemies);

		_enemySpeed =
			Mathf.Clamp(
				_enemySpeed,
				30f,
				180f);

		_detectionRadius =
			Mathf.Clamp(
				_detectionRadius,
				30f,
				180f);

		GD.Print(
			$"Configuração da missão carregada: " +
			$"Difficulty={_mission.Difficulty}, " +
			$"Enemies={_requiredEnemies}, " +
			$"EnemySpeed={_enemySpeed}, " +
			$"DetectionRadius={_detectionRadius}, " +
			$"MaxFailures={_maxFailures}");
	}

	private void ShowInitializationError(
		string message)
	{
		_missionFinished = true;
		_isFinalizingMission = false;
		_isRecoveringAfterDetection = false;

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
					"Mission unavailable",
				objective:
					message,
				completionTime: 0,
				statisticTitle:
					"Status",
				statisticValue:
					"Initialization error",
				difficulty: "-",
				failures: 0);
		}

		GD.PushError(message);
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
					DefaultEasyEnemies,
					DefaultEasyEnemySpeed,
					DefaultEasyDetectionRadius,
					DefaultEasyMaxFailures),

			2 =>
				new MissionConfiguration(
					DefaultMediumEnemies,
					DefaultMediumEnemySpeed,
					DefaultMediumDetectionRadius,
					DefaultMediumMaxFailures),

			3 =>
				new MissionConfiguration(
					DefaultHardEnemies,
					DefaultHardEnemySpeed,
					DefaultHardDetectionRadius,
					DefaultHardMaxFailures),

			_ =>
				new MissionConfiguration(
					DefaultEasyEnemies,
					DefaultEasyEnemySpeed,
					DefaultEasyDetectionRadius,
					DefaultEasyMaxFailures)
		};
	}

	private static string GetDifficultyText(
		int difficulty)
	{
		return difficulty switch
		{
			1 => "Easy",
			2 => "Medium",
			3 => "Hard",
			_ => $"Level {difficulty}"
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
		int Enemies,
		float EnemySpeed,
		float DetectionRadius,
		int MaxFailures);
}
