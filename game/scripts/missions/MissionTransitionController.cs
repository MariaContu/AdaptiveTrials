using System;
using AdaptiveTrials.Game.Dto;
using AdaptiveTrials.Game.Enums;
using AdaptiveTrials.Game.Session;
using AdaptiveTrials.Game.UI;
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

	private Button _returnToMenuButton = null!;
	private PanelContainer _missionPanel = null!;
	private PanelContainer _iconPanel = null!;
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

		_returnToMenuButton = GetNode<Button>(
			"../CenterContainer/MissionPanel/PanelMargin/Content/ReturnCenter/ReturnToMenuButton");
		_missionPanel = GetNode<PanelContainer>("../CenterContainer/MissionPanel");
		_iconPanel = GetNode<PanelContainer>(
			"../CenterContainer/MissionPanel/PanelMargin/Content/MissionCard/CardMargin/CardContent/IconPanel");

		_sessionManager =
			GetNode<SessionManager>("/root/SessionManager");

		_startMissionButton.Pressed +=
			OnStartMissionPressed;

		_returnToMenuButton.Pressed += OnReturnToMenuPressed;
		GameUiStyle.ApplyMainPanel(_missionPanel);
		GameUiStyle.ApplyPrimaryButton(_startMissionButton, 18);
		GameUiStyle.ApplySecondaryButton(_returnToMenuButton, 16);

		LoadCurrentMission();
	}

	private void LoadCurrentMission()
	{
		if (!_sessionManager.HasActiveSession)
		{
			ShowBlockingError(
				"Nenhuma sessão ativa foi encontrada.");

			return;
		}

		MissionDto? mission =
			_sessionManager.CurrentMission;

		if (mission is null)
		{
			ShowBlockingError(
				"Nenhuma missão está disponível para esta sessão.");

			return;
		}

		int currentNumber =
			_sessionManager.CurrentMissionIndex + 1;

		_missionCounter.Text =
			$"Missão {currentNumber}/" +
			$"{_sessionManager.TotalMissionCount}";

		_missionTitle.Text = mission.Name;

		_missionCategory.Text =
			MissionPresentation.Name(mission.Type);

		_missionDifficulty.Text =
			$"Dificuldade: " +
			GetDifficultyText(
				mission.Difficulty);

		_missionDescription.Text =
			string.IsNullOrWhiteSpace(mission.Description)
				? GetTemplateDescription(mission)
				: mission.Description;

		_missionIcon.Text =
			MissionPresentation.Icon(mission.Type);

		ApplyCategoryAppearance(mission.Type);

		_statusLabel.Text = string.Empty;
		_returnToMenuButton.Visible = false;

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
			1 => "Fácil",
			2 => "Média",
			3 => "Difícil",
			_ => $"Nível {difficulty}"
		};
	}

	private void OnStartMissionPressed()
{
	MissionDto? mission =
		_sessionManager.CurrentMission;

	if (mission is null)
	{
		ShowBlockingError(
			"Não foi possível iniciar a missão.");

		return;
	}

	_startMissionButton.Disabled = true;

	_statusLabel.Text =
		"Preparando missão...";

	string? scenePath =
		ResolveMissionScene(mission);

	if (scenePath is null)
	{
		_statusLabel.Text =
			"Não foi possível abrir esta missão. Retorne ao menu.";
		_returnToMenuButton.Visible = true;

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
			"Não foi possível abrir a missão. Tente novamente ou retorne ao menu.";
		_returnToMenuButton.Visible = true;

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

	private void OnReturnToMenuPressed()
	{
		const string menuPath = "res://scenes/menu/MainMenu.tscn";
		Error result = GetTree().ChangeSceneToFile(menuPath);
		if (result == Error.Ok)
		{
			_sessionManager.ClearSession();
			return;
		}

		_statusLabel.Text = "Não foi possível voltar ao menu. Tente novamente.";
		GD.PushError($"Falha ao abrir {menuPath}: {result}");
	}

	private void ShowBlockingError(string message)
	{
		_missionCounter.Text = "Missão indisponível";
		_missionTitle.Text = message;
		_missionCategory.Text = string.Empty;
		_missionDifficulty.Text = string.Empty;
		_missionDescription.Text = string.Empty;
		_missionIcon.Text = "!";
		_statusLabel.Text = "Retorne ao menu principal e inicie uma nova sessão.";
		_startMissionButton.Disabled = true;
		_returnToMenuButton.Visible = true;
		GD.PushError(message);
	}

	private void ApplyCategoryAppearance(MissionType type)
	{
		_iconPanel.AddThemeStyleboxOverride("panel",
			GameUiStyle.CreateBox(MissionPresentation.Background(type),
				GameUiStyle.Border, 2, 18));
		_missionIcon.AddThemeColorOverride("font_color",
			MissionPresentation.Foreground(type));
	}

	private static string GetTemplateDescription(
		MissionDto mission)
	{
		return mission.Template switch
		{
			"Eliminar Alvo" =>
				"Derrote os inimigos necessários.",

			"Sobreviver" =>
				"Sobreviva até concluir todas as ondas.",

			"Defender Objeto" =>
				"Proteja o objetivo.",

			"Encontrar Objetos" =>
				"Encontre os objetos necessários.",

			"Chegar ao Destino" =>
				"Chegue ao destino indicado.",

			"Evitar Inimigos" =>
				"Chegue ao objetivo sem ser detectado.",

			"Repetir Sequência" =>
				"Repita a sequência na ordem correta.",

			"Conectar Pontos" =>
				"Conecte os elementos corretamente.",

			"Decifrar Código" =>
				"Descubra o código correto.",

			_ =>
				mission.Template
		};
	}
}
