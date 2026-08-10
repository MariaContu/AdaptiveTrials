using AdaptiveTrials.Game.Enums;
using Godot;

namespace AdaptiveTrials.Game.Components;

/// <summary>
/// HUD reutilizável apresentado durante a execução das missões.
/// </summary>
public partial class MissionHud : CanvasLayer
{
	private Label _categoryIconLabel = null!;
	private Label _missionTitleLabel = null!;
	private Label _missionCategoryLabel = null!;
	private Label _objectiveLabel = null!;
	private Label _progressValueLabel = null!;
	private Label _progressLabel = null!;
	private Label _attemptsLabel = null!;
	private Label _timerLabel = null!;

	private ProgressBar _progressBar = null!;
	private Button _settingsButton = null!;

	public override void _Ready()
	{
		string contentPath =
			"TopCenter/HudPanel/HudMargin/HudContent/";

		_categoryIconLabel =
			GetNode<Label>(
				contentPath +
				"Header/CategoryIconPanel/CategoryIconLabel");

		_missionTitleLabel =
			GetNode<Label>(
				contentPath +
				"Header/MissionInfo/MissionTitleLabel");

		_missionCategoryLabel =
			GetNode<Label>(
				contentPath +
				"Header/MissionInfo/MissionCategoryLabel");

		_objectiveLabel =
			GetNode<Label>(
				contentPath +
				"ObjectiveLabel");

		_progressBar =
			GetNode<ProgressBar>(
				contentPath +
				"ProgressArea/ProgressBar");

		_progressValueLabel =
			GetNode<Label>(
				contentPath +
				"ProgressArea/ProgressValueLabel");

		_progressLabel =
			GetNode<Label>(
				contentPath +
				"Footer/ProgressLabel");

		_attemptsLabel =
			GetNode<Label>(
				contentPath +
				"Footer/AttemptsLabel");

		_timerLabel =
			GetNode<Label>(
				contentPath +
				"Footer/TimerLabel");

		_settingsButton =
			GetNode<Button>(
				"SettingsMargin/SettingsButton");

		_settingsButton.Pressed += OnSettingsPressed;

		SetAttemptsVisible(false);
	}

	public void Configure(
		string missionTitle,
		MissionType missionType,
		string objective,
		int maximumProgress)
	{
		int safeMaximum =
			Mathf.Max(1, maximumProgress);

		_missionTitleLabel.Text =
			string.IsNullOrWhiteSpace(missionTitle)
				? "Missão"
				: missionTitle;

		_missionCategoryLabel.Text =
			GetCategoryText(missionType);

		_categoryIconLabel.Text =
			GetCategoryIcon(missionType);

		_objectiveLabel.Text =
			string.IsNullOrWhiteSpace(objective)
				? "Objetivo: Conclua a missão."
				: $"Objetivo: {objective}";

		_progressBar.MinValue = 0;
		_progressBar.MaxValue = safeMaximum;
		_progressBar.Value = 0;

		SetProgress(0, safeMaximum);
		SetElapsedTime(0);
	}

	public void SetProgress(
		int current,
		int total,
		string noun = "Progresso")
	{
		int safeTotal =
			Mathf.Max(1, total);

		int safeCurrent =
			Mathf.Clamp(current, 0, safeTotal);

		_progressBar.MinValue = 0;
		_progressBar.MaxValue = safeTotal;
		_progressBar.Value = safeCurrent;

		_progressValueLabel.Text =
			$"{safeCurrent}/{safeTotal}";

		_progressLabel.Text =
			$"{noun}: {safeCurrent}/{safeTotal}";
	}

	public void SetAttempts(
		int remaining,
		int maximum)
	{
		int safeMaximum =
			Mathf.Max(1, maximum);

		int safeRemaining =
			Mathf.Clamp(
				remaining,
				0,
				safeMaximum);

		_attemptsLabel.Text =
			$"Tentativas: {safeRemaining}/{safeMaximum}";

		SetAttemptsVisible(true);
	}

	public void SetAttemptsText(string text)
	{
		_attemptsLabel.Text = string.IsNullOrWhiteSpace(text)
			? "Status"
			: text;

		SetAttemptsVisible(true);
	}

	public void SetAttemptsVisible(bool visible)
	{
		_attemptsLabel.Visible = visible;
	}

	public void SetElapsedTime(double elapsedSeconds)
	{
		_timerLabel.Text =
			$"Tempo: {FormatTime(elapsedSeconds)}";
	}

	public void SetVisibleState(bool visible)
	{
		Visible = visible;
	}

	private void OnSettingsPressed()
	{
		GD.Print(
			"Configurações da missão ainda não implementadas.");
	}

	private static string GetCategoryText(
		MissionType missionType)
	{
		return missionType switch
		{
			MissionType.Combat => "Combate",
			MissionType.Exploration => "Exploração",
			MissionType.Puzzle => "Quebra-cabeça",
			_ => "Desconhecida"
		};
	}

	private static string GetCategoryIcon(
		MissionType missionType)
	{
		return missionType switch
		{
			MissionType.Combat => "⚔",
			MissionType.Exploration => "◆",
			MissionType.Puzzle => "⌘",
			_ => "?"
		};
	}

	private static string FormatTime(double seconds)
	{
		int totalSeconds =
			Mathf.Max(
				0,
				Mathf.FloorToInt(seconds));

		int minutes =
			totalSeconds / 60;

		int remainingSeconds =
			totalSeconds % 60;

		return $"{minutes:00}:{remainingSeconds:00}";
	}
}
