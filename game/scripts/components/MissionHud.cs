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
	private Label _timerLabel = null!;

	private ProgressBar _progressBar = null!;
	private Button _settingsButton = null!;

	public override void _Ready()
	{
		_categoryIconLabel =
			GetNode<Label>(
				"TopCenter/HudPanel/HudMargin/HudContent/" +
				"Header/CategoryIconPanel/CategoryIconLabel");

		_missionTitleLabel =
			GetNode<Label>(
				"TopCenter/HudPanel/HudMargin/HudContent/" +
				"Header/MissionInfo/MissionTitleLabel");

		_missionCategoryLabel =
			GetNode<Label>(
				"TopCenter/HudPanel/HudMargin/HudContent/" +
				"Header/MissionInfo/MissionCategoryLabel");

		_objectiveLabel =
			GetNode<Label>(
				"TopCenter/HudPanel/HudMargin/HudContent/" +
				"ObjectiveLabel");

		_progressBar =
			GetNode<ProgressBar>(
				"TopCenter/HudPanel/HudMargin/HudContent/" +
				"ProgressArea/ProgressBar");

		_progressValueLabel =
			GetNode<Label>(
				"TopCenter/HudPanel/HudMargin/HudContent/" +
				"ProgressArea/ProgressValueLabel");

		_progressLabel =
			GetNode<Label>(
				"TopCenter/HudPanel/HudMargin/HudContent/" +
				"Footer/ProgressLabel");

		_timerLabel =
			GetNode<Label>(
				"TopCenter/HudPanel/HudMargin/HudContent/" +
				"Footer/TimerLabel");

		_settingsButton =
			GetNode<Button>(
				"SettingsMargin/SettingsButton");

		_settingsButton.Pressed += OnSettingsPressed;
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
				? "Mission"
				: missionTitle;

		_missionCategoryLabel.Text =
			GetCategoryText(missionType);

		_categoryIconLabel.Text =
			GetCategoryIcon(missionType);

		_objectiveLabel.Text =
			string.IsNullOrWhiteSpace(objective)
				? "Objective: Complete the mission."
				: $"Objective: {objective}";

		_progressBar.MinValue = 0;
		_progressBar.MaxValue = safeMaximum;
		_progressBar.Value = 0;

		SetProgress(
			current: 0,
			total: safeMaximum);

		SetElapsedTime(0);
	}

	public void SetProgress(
		int current,
		int total,
		string noun = "Progress")
	{
		int safeTotal =
			Mathf.Max(1, total);

		int safeCurrent =
			Mathf.Clamp(
				current,
				0,
				safeTotal);

		_progressBar.MinValue = 0;
		_progressBar.MaxValue = safeTotal;
		_progressBar.Value = safeCurrent;

		_progressValueLabel.Text =
			$"{safeCurrent}/{safeTotal}";

		_progressLabel.Text =
			$"{noun}: {safeCurrent}/{safeTotal}";
	}

	public void SetElapsedTime(double elapsedSeconds)
	{
		_timerLabel.Text =
			$"Time: {FormatTime(elapsedSeconds)}";
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
			MissionType.Combat =>
				"Fight",

			MissionType.Exploration =>
				"Exploration",

			MissionType.Puzzle =>
				"Puzzle",

			_ =>
                "Unknown"
		};
	}

	private static string GetCategoryIcon(
		MissionType missionType)
	{
		return missionType switch
		{
			MissionType.Combat =>
				"⚔",

			MissionType.Exploration =>
				"◆",

			MissionType.Puzzle =>
				"⌘",

			_ =>
                "?"
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
