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
/// Controla a missão de exploração baseada em coleta de objetos.
/// </summary>
public partial class FindObjectsMissionController : Node
{
	private const int DefaultRequiredItems = 3;

	private static readonly PackedScene CollectibleScene =
		GD.Load<PackedScene>(
			"res://scenes/missions/shared/" +
			"CollectibleObject.tscn");

	private SessionManager _sessionManager = null!;
	private PlayerController _player = null!;
	private MissionHud _missionHud = null!;
	private MissionResultPopup _resultPopup = null!;

	private Node2D _dynamicObjects = null!;
	private Node2D _spawnPointsContainer = null!;
	private Marker2D _playerSpawn = null!;

	private MissionDto _mission = null!;

	private int _requiredItems;
	private int _collectedItems;
	private int _failures;

	private double _elapsedTime;

	private bool _missionFinished;
	private bool _isFinalizingMission;
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

		_requiredItems =
			ReadRequiredItems(
				_mission.ParametersJson);

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

		_dynamicObjects =
			GetNode<Node2D>(
				"../DynamicObjects");

		_spawnPointsContainer =
			GetNode<Node2D>(
				"../SpawnPoints/" +
				"CollectibleSpawnPoints");

		_playerSpawn =
			GetNode<Marker2D>(
				"../SpawnPoints/PlayerSpawn");
	}

	private void ConfigureMission()
	{
		_collectedItems = 0;
		_failures = 0;
		_elapsedTime = 0;

		_missionFinished = false;
		_isFinalizingMission = false;
		_resultRegistered = false;

		Result = null;

		_objectiveText =
			$"Find and collect {_requiredItems} objects " +
			"scattered across the area.";

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
			_requiredItems);

		_missionHud.SetProgress(
			0,
			_requiredItems,
			"Objects");

		_missionHud.SetAttemptsVisible(
			false);

		_missionHud.SetVisibleState(
			true);

		_resultPopup.HidePopup();

		SpawnCollectibles();

		GD.Print(
			$"Missão iniciada: " +
			$"MissionId={_mission.Id}, " +
			$"RequiredItems={_requiredItems}, " +
			$"Difficulty={_mission.Difficulty}");
	}

	private void SpawnCollectibles()
	{
		List<Marker2D> spawnPoints =
			_spawnPointsContainer
				.GetChildren()
				.OfType<Marker2D>()
				.ToList();

		if (spawnPoints.Count <
			_requiredItems)
		{
			ShowInitializationError(
				$"The map contains {spawnPoints.Count} " +
				$"spawn points, but the mission requires " +
				$"{_requiredItems} objects.");

			return;
		}

		Shuffle(spawnPoints);

		for (int index = 0;
			 index < _requiredItems;
			 index++)
		{
			Marker2D spawnPoint =
				spawnPoints[index];

			CollectibleObject collectible =
				CollectibleScene
					.Instantiate<CollectibleObject>();

			collectible.Name =
				$"Collectible{index + 1:00}";

			collectible.Collected +=
				OnCollectibleCollected;

			_dynamicObjects.AddChild(
				collectible);

			collectible.GlobalPosition =
				spawnPoint.GlobalPosition;
		}
	}

	private void OnCollectibleCollected(
		CollectibleObject collectible)
	{
		if (_missionFinished ||
			_isFinalizingMission)
		{
			return;
		}

		_collectedItems++;

		_missionHud.SetProgress(
			_collectedItems,
			_requiredItems,
			"Objects");

		GD.Print(
			$"Progresso: " +
			$"{_collectedItems}/" +
			$"{_requiredItems}");

		if (_collectedItems >=
			_requiredItems)
		{
			_ = CompleteMissionAsync();
		}
	}

	private async Task CompleteMissionAsync()
	{
		if (_missionFinished ||
			_isFinalizingMission)
		{
			return;
		}

		_isFinalizingMission = true;

		_player.SetMovementEnabled(
			false);

		_player.Velocity =
			Vector2.Zero;

		Result = new MissionResult
		{
			MissionId =
				_mission.Id,

			CompletionTime =
				_elapsedTime,

			Failures =
				_failures,

			Success =
				true,

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

		_missionHud.SetVisibleState(
			false);

		_resultPopup.ShowResult(
			success: true,
			missionName: _mission.Name,
			objective: _objectiveText,
			completionTime: _elapsedTime,
			statisticTitle:
				"Objects Collected",
			statisticValue:
				$"{_collectedItems}/" +
				$"{_requiredItems}",
			difficulty:
				GetDifficultyText(
					_mission.Difficulty),
			failures:
				_failures);

		PrintMissionResult();
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

	private void ShowInitializationError(
		string message)
	{
		_missionFinished = true;
		_isFinalizingMission = false;

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

	private static double CalculatePersistence(
		int failures)
	{
		return Math.Clamp(
			1.0 - failures * 0.2,
			0,
			1);
	}

	private static int ReadRequiredItems(
		string parametersJson)
	{
		if (string.IsNullOrWhiteSpace(
				parametersJson))
		{
			return DefaultRequiredItems;
		}

		try
		{
			using JsonDocument document =
				JsonDocument.Parse(
					parametersJson);

			JsonElement root =
				document.RootElement;

			string[] possibleNames =
			{
				"items",
				"itemCount",
				"objects",
				"objectCount",
				"requiredItems"
			};

			foreach (string propertyName
					 in possibleNames)
			{
				if (!root.TryGetProperty(
						propertyName,
						out JsonElement value))
				{
					continue;
				}

				if (value.TryGetInt32(
						out int amount) &&
					amount > 0)
				{
					return amount;
				}
			}
		}
		catch (JsonException exception)
		{
			GD.PushWarning(
				$"ParametersJson inválido: " +
				$"{exception.Message}");
		}

		return DefaultRequiredItems;
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
}
