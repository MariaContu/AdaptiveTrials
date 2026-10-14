using AdaptiveTrials.Game.UI;
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
	private Label _objectiveTitleLabel = null!;
	private Label _objectiveLabel = null!;

	private Label _timeTitleLabel = null!;
	private Label _timeValueLabel = null!;

	private Label _statisticTitleLabel = null!;
	private Label _statisticValueLabel = null!;

	private Label _difficultyTitleLabel = null!;
	private Label _difficultyValueLabel = null!;

	private Label _failuresTitleLabel = null!;
	private Label _failuresValueLabel = null!;

	private PanelContainer _resultPanel = null!;
	private ColorRect _overlay = null!;
	private HSeparator _separator = null!;
	private Button _continueButton = null!;

	public override void _Ready()
	{
		string contentPath =
			"Overlay/CenterContainer/ResultPanel/" +
			"ResultMargin/ResultContent/";

		string statisticsPath =
			contentPath + "StatisticsGrid/";

		_overlay =
			GetNode<ColorRect>("Overlay");

		_resultPanel =
			GetNode<PanelContainer>(
				"Overlay/CenterContainer/ResultPanel");

		_resultTitleLabel =
			GetNode<Label>(
				contentPath + "ResultTitleLabel");

		_missionNameLabel =
			GetNode<Label>(
				contentPath + "MissionNameLabel");

		_separator =
			GetNode<HSeparator>(
				contentPath + "Separator");

		_objectiveTitleLabel =
			GetNode<Label>(
				contentPath + "ObjectiveTitleLabel");

		_objectiveLabel =
			GetNode<Label>(
				contentPath + "ObjectiveLabel");

		_timeTitleLabel =
			GetNode<Label>(
				statisticsPath + "TimeTitleLabel");

		_timeValueLabel =
			GetNode<Label>(
				statisticsPath + "TimeValueLabel");

		_statisticTitleLabel =
			GetNode<Label>(
				statisticsPath + "StatisticTitleLabel");

		_statisticValueLabel =
			GetNode<Label>(
				statisticsPath + "StatisticValueLabel");

		_difficultyTitleLabel =
			GetNode<Label>(
				statisticsPath + "DifficultyTitleLabel");

		_difficultyValueLabel =
			GetNode<Label>(
				statisticsPath + "DifficultyValueLabel");

		_failuresTitleLabel =
			GetNode<Label>(
				statisticsPath + "FailuresTitleLabel");

		_failuresValueLabel =
			GetNode<Label>(
				statisticsPath + "FailuresValueLabel");

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
		ApplyResultPalette(success);

		_resultTitleLabel.Text =
			success
				? "CONCLUÍDA"
				: "NÃO CONCLUÍDA";

		_missionNameLabel.Text =
			string.IsNullOrWhiteSpace(missionName)
				? "Missão"
				: missionName;

		_objectiveLabel.Text =
			string.IsNullOrWhiteSpace(objective)
				? "Conclua a missão."
				: objective;

		_timeTitleLabel.Text =
			"Tempo gasto";

		_timeValueLabel.Text =
			FormatTime(completionTime);

		_statisticTitleLabel.Text =
			string.IsNullOrWhiteSpace(statisticTitle)
				? "Progresso da missão"
				: statisticTitle;

		_statisticValueLabel.Text =
			string.IsNullOrWhiteSpace(statisticValue)
				? "-"
				: statisticValue;

		_difficultyTitleLabel.Text =
			"Dificuldade";

		_difficultyValueLabel.Text =
			string.IsNullOrWhiteSpace(difficulty)
				? "-"
				: difficulty;

		_failuresTitleLabel.Text =
			"Erros";

		_failuresValueLabel.Text =
			Mathf.Max(0, failures).ToString();

		_continueButton.Disabled = false;

		Show();
	}

	public void HidePopup()
	{
		Hide();
	}

	private void ApplyResultPalette(bool success)
	{
		// Nunca altera a cor-base do cartão: sucesso e falha são acentos.
		GameUiStyle.ApplyMainPanel(_resultPanel);
		GameUiStyle.ApplyPrimaryButton(_continueButton, 18);
		_overlay.Color = new Color("#1D1820B5");

		Color accent = success ? GameUiStyle.Success : GameUiStyle.Failure;
		_resultTitleLabel.AddThemeColorOverride("font_color", accent);
		_missionNameLabel.AddThemeColorOverride("font_color", GameUiStyle.Ink);
		_objectiveTitleLabel.AddThemeColorOverride("font_color", GameUiStyle.SecondaryInk);
		_objectiveLabel.AddThemeColorOverride("font_color", GameUiStyle.Ink);

		Label[] titles =
		{
			_timeTitleLabel, _statisticTitleLabel,
			_difficultyTitleLabel, _failuresTitleLabel
		};
		foreach (Label label in titles)
		{
			label.AddThemeColorOverride("font_color", GameUiStyle.SecondaryInk);
		}

		Label[] values =
		{
			_timeValueLabel, _statisticValueLabel,
			_difficultyValueLabel, _failuresValueLabel
		};
		foreach (Label label in values)
		{
			label.AddThemeColorOverride("font_color", GameUiStyle.Ink);
		}

		_separator.AddThemeStyleboxOverride("separator",
			new StyleBoxFlat { BgColor = accent });
	}

	private void ConfigureStaticLabels()
	{
		_timeTitleLabel.Text =
			"Tempo gasto";

		_timeValueLabel.Text =
			"00:00";

		_statisticTitleLabel.Text =
			"Progresso da missão";

		_statisticValueLabel.Text =
			"-";

		_difficultyTitleLabel.Text =
			"Dificuldade";

		_difficultyValueLabel.Text =
			"-";

		_failuresTitleLabel.Text =
			"Erros";

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
