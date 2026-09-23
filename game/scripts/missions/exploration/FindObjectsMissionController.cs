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
	private ExplorationMaze _maze = null!;
	private CanvasModulate _darkness = null!;
	private PointLight2D _playerLight = null!;

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

		_maze =
			GetNode<ExplorationMaze>(
				"../Environment/ExplorationMaze");
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
			$"Explore o labirinto escuro e encontre {_requiredItems} objetos. " +
			"A complexidade do labirinto e a visibilidade variam com a dificuldade.";

		_maze.Configure(
			_mission.Difficulty);

		_player.GlobalPosition =
			_maze.PlayerStartPosition;

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
			"Objetos");

		_missionHud.SetAttemptsVisible(
			false);

		_missionHud.SetVisibleState(
			true);

		_resultPopup.HidePopup();

		ConfigureLimitedVisibility();
		SpawnCollectibles();

		GD.Print(
			$"Missão iniciada: " +
			$"MissionId={_mission.Id}, " +
			$"RequiredItems={_requiredItems}, " +
			$"Difficulty={_mission.Difficulty}");
	}

	private void SpawnCollectibles()
	{
		IReadOnlyList<Vector2> spawnPoints =
			_maze.CollectiblePositions;

		if (spawnPoints.Count <
			_requiredItems)
		{
			ShowInitializationError(
				$"O labirinto da dificuldade {_mission.Difficulty} contém apenas " +
				$"{spawnPoints.Count} pontos de coleta, mas a missão exige " +
				$"{_requiredItems} objetos.");

			return;
		}

		for (int index = 0;
			 index < _requiredItems;
			 index++)
		{
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
				spawnPoints[index];

			collectible.ConfigureExplorationVisibility(
				GetCollectibleGlowEnergy(_mission.Difficulty),
				GetCollectiblePulseSpeed(_mission.Difficulty));
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
			"Objetos");

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

	private void ConfigureLimitedVisibility()
	{
		_darkness =
			new CanvasModulate
			{
				Name = "ExplorationDarkness",
				Color = GetDarknessColor(
					_mission.Difficulty)
			};

		GetParent().AddChild(
			_darkness);

		Gradient lightGradient =
			new();

		lightGradient.SetColor(
			0,
			Colors.White);

		lightGradient.SetColor(
			1,
			new Color(1, 1, 1, 0));

		GradientTexture2D lightTexture =
			new()
			{
				Gradient = lightGradient,
				Width = 256,
				Height = 256,
				Fill = GradientTexture2D.FillEnum.Radial,
				FillFrom = new Vector2(0.5f, 0.5f),
				FillTo = new Vector2(1.0f, 0.5f)
			};

		_playerLight =
			new PointLight2D
			{
				Name = "ExplorationLight",
				Texture = lightTexture,
				TextureScale = GetLightScale(
					_mission.Difficulty),
				Energy = 1.15f,
				Color = new Color("#f3ddc5"),
				ShadowEnabled = false
			};

		_player.AddChild(
			_playerLight);
	}

	private static Color GetDarknessColor(
		int difficulty)
	{
		return difficulty switch
		{
			1 => new Color("#61536a"),
			2 => new Color("#46394f"),
			3 => new Color("#30263a"),
			_ => new Color("#46394f")
		};
	}

	private static float GetLightScale(
		int difficulty)
	{
		return difficulty switch
		{
			1 => 3.0f,
			2 => 2.35f,
			3 => 1.8f,
			_ => 2.35f
		};
	}

	private static float GetCollectibleGlowEnergy(
		int difficulty)
	{
		return difficulty switch
		{
			1 => 1.15f,
			2 => 0.85f,
			3 => 0.60f,
			_ => 0.85f
		};
	}

	private static float GetCollectiblePulseSpeed(
		int difficulty)
	{
		return difficulty switch
		{
			1 => 2.6f,
			2 => 2.1f,
			3 => 1.7f,
			_ => 2.1f
		};
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
				"Objetos coletados",
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
			1 => "Fácil",
			2 => "Média",
			3 => "Difícil",
			_ => $"Nível {difficulty}"
		};
	}

	private static List<Marker2D> SelectDistributedSpawnPoints(
		IReadOnlyList<Marker2D> candidates,
		int amount,
		Vector2 playerStartPosition)
	{
		List<Marker2D> remaining =
			candidates.ToList();

		List<Marker2D> selected =
			new();

		if (remaining.Count == 0 ||
			amount <= 0)
		{
			return selected;
		}

		Marker2D first =
			remaining
				.OrderByDescending(
					marker =>
						marker.GlobalPosition.DistanceTo(
							playerStartPosition))
				.First();

		selected.Add(first);
		remaining.Remove(first);

		while (selected.Count < amount &&
			remaining.Count > 0)
		{
			Marker2D next =
				remaining
					.OrderByDescending(
						candidate =>
						{
							float minimumDistance =
								selected.Min(
									chosen =>
										candidate.GlobalPosition.DistanceTo(
											chosen.GlobalPosition));

							float distanceFromStart =
								candidate.GlobalPosition.DistanceTo(
									playerStartPosition);

							return
								minimumDistance +
								distanceFromStart * 0.20f;
						})
					.First();

			selected.Add(next);
			remaining.Remove(next);
		}

		return selected;
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
