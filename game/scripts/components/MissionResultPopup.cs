using Godot;

namespace AdaptiveTrials.Game.Components;

/// <summary>
/// Popup reutilizável apresentado ao finalizar uma missão.
/// </summary>
public partial class MissionResultPopup : CanvasLayer
{
	[Signal]
	public delegate void ContinueRequestedEventHandler();

	private Label _resultTitleLabel = null!;
	private Label _missionNameLabel = null!;
	private Label _objectiveLabel = null!;

	private Label _timeTitleLabel = null!;
	private Label _timeValueLabel = null!;

	private Label _statisticTitleLabel = null!;
	private Label _statisticValueLabel = null!;

	private Label _difficultyTitleLabel = null!;
	private Label _difficultyValueLabel = null!;

	private Label _failuresTitleLabel = null!;
	private Label _failuresValueLabel = null!;

	private Button _continueButton = null!;

	public override void _Ready()
	{
		string contentPath =
			"Overlay/CenterContainer/ResultPanel/" +
			"ResultMargin/ResultContent/";

		string statisticsPath =
			contentPath +
			"StatisticsGrid/";

		_resultTitleLabel =
			GetNode<Label>(
				contentPath +
				"ResultTitleLabel");

		_missionNameLabel =
			GetNode<Label>(
				contentPath +
				"MissionNameLabel");

		_objectiveLabel =
			GetNode<Label>(
				contentPath +
				"ObjectiveLabel");

		_timeTitleLabel =
			GetNode<Label>(
				statisticsPath +
				"TimeTitleLabel");

		_timeValueLabel =
			GetNode<Label>(
				statisticsPath +
				"TimeValueLabel");

		_statisticTitleLabel =
			GetNode<Label>(
				statisticsPath +
				"StatisticTitleLabel");

		_statisticValueLabel =
			GetNode<Label>(
				statisticsPath +
				"StatisticValueLabel");

		_difficultyTitleLabel =
			GetNode<Label>(
				statisticsPath +
				"DifficultyTitleLabel");

		_difficultyValueLabel =
			GetNode<Label>(
				statisticsPath +
				"DifficultyValueLabel");

		_failuresTitleLabel =
			GetNode<Label>(
				statisticsPath +
				"FailuresTitleLabel");

		_failuresValueLabel =
			GetNode<Label>(
				statisticsPath +
				"FailuresValueLabel");

		_continueButton =
			GetNode<Button>(
				contentPath +
				"ContinueCenter/ContinueButton");

		_continueButton.Pressed +=
			OnContinuePressed;

		ConfigureStaticLabels();
		HidePopup();
	}

	public void ShowResult(
		bool success,
		string missionName,
		string objective,
		double completionTime,
		string statisticTitle,
		string statisticValue,
		string difficulty,
		int failures)
	{
		_resultTitleLabel.Text =
			success
				? "SUCCESS"
				: "MISSION FAILED";

		_missionNameLabel.Text =
			string.IsNullOrWhiteSpace(missionName)
				? "Mission"
				: missionName;

		_objectiveLabel.Text =
			string.IsNullOrWhiteSpace(objective)
				? "Complete the mission."
				: objective;

		_timeTitleLabel.Text =
			"Time Spent";

		_timeValueLabel.Text =
			FormatTime(completionTime);

		_statisticTitleLabel.Text =
			string.IsNullOrWhiteSpace(statisticTitle)
				? "Mission Progress"
				: statisticTitle;

		_statisticValueLabel.Text =
			string.IsNullOrWhiteSpace(statisticValue)
				? "-"
				: statisticValue;

		_difficultyTitleLabel.Text =
			"Difficulty";

		_difficultyValueLabel.Text =
			string.IsNullOrWhiteSpace(difficulty)
				? "-"
				: difficulty;

		_failuresTitleLabel.Text =
			"Failures";

		_failuresValueLabel.Text =
			Mathf.Max(0, failures).ToString();

		_continueButton.Disabled = false;

		Show();
	}

	public void HidePopup()
	{
		Hide();
	}

	private void ConfigureStaticLabels()
	{
		_timeTitleLabel.Text =
			"Time Spent";

		_timeValueLabel.Text =
			"00:00";

		_statisticTitleLabel.Text =
			"Mission Progress";

		_statisticValueLabel.Text =
			"-";

		_difficultyTitleLabel.Text =
			"Difficulty";

		_difficultyValueLabel.Text =
			"-";

		_failuresTitleLabel.Text =
			"Failures";

		_failuresValueLabel.Text =
			"0";
	}

	private void OnContinuePressed()
	{
		if (_continueButton.Disabled)
		{
			return;
		}

		_continueButton.Disabled = true;

		EmitSignal(
			SignalName.ContinueRequested);
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
