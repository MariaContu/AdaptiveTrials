using System;
using AdaptiveTrials.Game.Dto;
using AdaptiveTrials.Game.Enums;
using AdaptiveTrials.Game.Session;
using Godot;

namespace AdaptiveTrials.Game.Missions;

/// <summary>
/// Exibe os dados da missão atual antes de sua execução.
/// </summary>
public partial class MissionTransitionController : Node
{
	private Label _missionCounter = null!;
	private Label _missionIcon = null!;
	private Label _missionTitle = null!;
	private Label _missionCategory = null!;
	private Label _missionDifficulty = null!;
	private Label _missionDescription = null!;
	private Label _statusLabel = null!;

	private Button _settingsButton = null!;
	private Button _startMissionButton = null!;

	private SessionManager _sessionManager = null!;

	public override void _Ready()
	{
		_missionCounter =
			GetNode<Label>(
				"../CenterContainer/MissionPanel/" +
				"PanelMargin/Content/MissionCounter");

		_missionIcon =
			GetNode<Label>(
				"../CenterContainer/MissionPanel/" +
				"PanelMargin/Content/MissionCard/" +
				"CardMargin/CardContent/IconPanel/MissionIcon");

		_missionTitle =
			GetNode<Label>(
				"../CenterContainer/MissionPanel/" +
				"PanelMargin/Content/MissionCard/" +
				"CardMargin/CardContent/MissionInfo/MissionTitle");

		_missionCategory =
			GetNode<Label>(
				"../CenterContainer/MissionPanel/" +
				"PanelMargin/Content/MissionCard/" +
				"CardMargin/CardContent/MissionInfo/MissionCategory");

		_missionDifficulty =
			GetNode<Label>(
				"../CenterContainer/MissionPanel/" +
				"PanelMargin/Content/MissionCard/" +
				"CardMargin/CardContent/MissionInfo/MissionDifficulty");

		_missionDescription =
			GetNode<Label>(
				"../CenterContainer/MissionPanel/" +
				"PanelMargin/Content/MissionCard/" +
				"CardMargin/CardContent/MissionInfo/MissionDescription");

		_statusLabel =
			GetNode<Label>(
				"../CenterContainer/MissionPanel/" +
				"PanelMargin/Content/StatusLabel");

		_startMissionButton =
			GetNode<Button>(
				"../CenterContainer/MissionPanel/" +
				"PanelMargin/Content/StartCenter/" +
				"StartMissionButton");

		_settingsButton =
			GetNode<Button>(
				"../TopMargin/TopBar/SettingsButton");

		_sessionManager =
			GetNode<SessionManager>("/root/SessionManager");

		_startMissionButton.Pressed +=
			OnStartMissionPressed;

		_settingsButton.Pressed +=
			OnSettingsPressed;

		LoadCurrentMission();
	}

	private void LoadCurrentMission()
	{
		if (!_sessionManager.HasActiveSession)
		{
			ShowBlockingError(
				"No active session was found.");

			return;
		}

		MissionDto? mission =
			_sessionManager.CurrentMission;

		if (mission is null)
		{
			ShowBlockingError(
				"No mission is available for this session.");

			return;
		}

		int currentNumber =
			_sessionManager.CurrentMissionIndex + 1;

		_missionCounter.Text =
			$"Mission {currentNumber}/" +
			$"{_sessionManager.TotalMissionCount}";

		_missionTitle.Text = mission.Name;

		_missionCategory.Text =
			GetCategoryText(mission.Type);

		_missionDifficulty.Text =
			$"Difficulty: " +
			GetDifficultyText(
				mission.Difficulty);

		_missionDescription.Text =
			string.IsNullOrWhiteSpace(mission.Description)
				? GetTemplateDescription(mission)
				: mission.Description;

		_missionIcon.Text =
			GetCategoryIcon(mission.Type);

		_statusLabel.Text = string.Empty;

		GD.Print(
			$"Tela de transição carregada: " +
			$"MissionId={mission.Id}, " +
			$"Type={mission.Type}, " +
			$"Template={mission.Template}, " +
			$"Difficulty={mission.Difficulty}");
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

	private void OnStartMissionPressed()
{
	MissionDto? mission =
		_sessionManager.CurrentMission;

	if (mission is null)
	{
		ShowBlockingError(
			"The mission could not be started.");

		return;
	}

	_startMissionButton.Disabled = true;

	_statusLabel.Text =
		"Preparing mission...";

	string? scenePath =
		ResolveMissionScene(mission);

	if (scenePath is null)
	{
		_statusLabel.Text =
			"This mission template has not been implemented yet.";

		_startMissionButton.Disabled = false;

		GD.PushWarning(
			$"Template sem cena implementada: " +
			$"{mission.Template}");

		return;
	}

	Error navigationError =
		GetTree().ChangeSceneToFile(scenePath);

	if (navigationError != Error.Ok)
	{
		_statusLabel.Text =
			"The mission could not be opened.";

		_startMissionButton.Disabled = false;

		GD.PushError(
			$"Erro ao abrir missão: " +
			$"{navigationError}");
	}
}

private static string? ResolveMissionScene(
	MissionDto mission)
{
	string normalizedTemplate =
		mission.Template
			.Trim()
			.ToLowerInvariant();

	return normalizedTemplate switch
	{
		"encontrar objetos" =>
			"res://scenes/missions/exploration/" +
			"FindObjectsMission.tscn",

		"find objects" =>
			"res://scenes/missions/exploration/" +
			"FindObjectsMission.tscn",

		"chegar ao destino" =>
			"res://scenes/missions/exploration/" +
			"ReachDestinationMission.tscn",

		"reach destination" =>
			"res://scenes/missions/exploration/" +
			"ReachDestinationMission.tscn",
			
		"evitar inimigos" =>
			"res://scenes/missions/exploration/" +
			"AvoidEnemiesMission.tscn",

		"avoid enemies" =>
			"res://scenes/missions/exploration/" +
			"AvoidEnemiesMission.tscn",

		"eliminar alvo" =>
			"res://scenes/missions/combat/" +
			"EliminateTargetMission.tscn",

		"eliminate target" =>
			"res://scenes/missions/combat/" +
			"EliminateTargetMission.tscn",

		"sobreviver" =>
			"res://scenes/missions/combat/" +
			"SurviveMission.tscn",

		"survive" =>
			"res://scenes/missions/combat/" +
			"SurviveMission.tscn",

		"defender objeto" =>
			"res://scenes/missions/combat/" +
			"DefendObjectMission.tscn",

		"defend object" =>
			"res://scenes/missions/combat/" +
			"DefendObjectMission.tscn",

		"repetir sequência" =>
			"res://scenes/missions/puzzle/" +
			"RepeatSequenceMission.tscn",

		"repeat sequence" =>
			"res://scenes/missions/puzzle/" +
			"RepeatSequenceMission.tscn",

		"conectar pontos" =>
			"res://scenes/missions/puzzle/" +
			"ConnectPointsMission.tscn",

		"connect points" =>
			"res://scenes/missions/puzzle/" +
			"ConnectPointsMission.tscn",

		"decifrar código" =>
			"res://scenes/missions/puzzle/" +
			"DecipherCodeMission.tscn",

		"decifrar codigo" =>
			"res://scenes/missions/puzzle/" +
			"DecipherCodeMission.tscn",

		"decipher code" =>
			"res://scenes/missions/puzzle/" +
			"DecipherCodeMission.tscn",

		_ => null
	};
}

	private void OnSettingsPressed()
	{
		_statusLabel.Text =
			"Settings will be implemented later.";
	}

	private void ShowBlockingError(string message)
	{
		_missionCounter.Text =
			"Mission unavailable";

		_missionTitle.Text = message;
		_missionCategory.Text = string.Empty;
		_missionDifficulty.Text = string.Empty;
		_missionDescription.Text = string.Empty;
		_missionIcon.Text = "!";

		_statusLabel.Text =
			"Return to the main menu and start a new session.";

		_startMissionButton.Disabled = true;

		GD.PushError(message);
	}

	private static string GetCategoryText(
		MissionType type)
	{
		return type switch
		{
			MissionType.Combat => "Combat",
			MissionType.Exploration => "Exploration",
			MissionType.Puzzle => "Puzzle",
			_ => "Unknown"
		};
	}

	private static string GetCategoryIcon(
		MissionType type)
	{
		return type switch
		{
			MissionType.Combat => "⚔",
			MissionType.Exploration => "⌖",
			MissionType.Puzzle => "◆",
			_ => "?"
		};
	}

	private static string GetTemplateDescription(
		MissionDto mission)
	{
		return mission.Template switch
		{
			"Eliminar Alvo" =>
				"Defeat the required enemies.",

			"Sobreviver" =>
				"Survive until time runs out.",

			"Defender Objeto" =>
				"Protect the objective.",

			"Encontrar Objetos" =>
				"Find the required objects.",

			"Chegar ao Destino" =>
				"Reach the indicated destination.",

			"Evitar Inimigos" =>
				"Reach the objective without being detected.",

			"Repetir Sequência" =>
				"Repeat the sequence in the correct order.",

			"Conectar Pontos" =>
				"Connect the elements correctly.",

			"Decifrar Código" =>
				"Discover the correct code.",

			_ =>
				mission.Template
		};
	}
}
