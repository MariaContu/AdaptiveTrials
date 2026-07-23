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

	private void ApplyResultPalette(bool success)
	{
		if (success)
		{
			ApplyPalette(
				panelBackground: new Color("#d7e3de"),
				panelBorder: new Color("#86b49c"),
				titleColor: new Color("#07531c"),
				primaryText: new Color("#24372c"),
				secondaryText: new Color("#537060"),
				valueColor: new Color("#07531c"),
				buttonNormal: new Color("#94baa4"),
				buttonHover: new Color("#a8ccb7"),
				buttonPressed: new Color("#7da38d"),
				buttonBorder: new Color("#6c9c81"),
				overlayColor: new Color("#171b198f"));

			return;
		}

		ApplyPalette(
			panelBackground: new Color("#ead5d8"),
			panelBorder: new Color("#b86f79"),
			titleColor: new Color("#7a2632"),
			primaryText: new Color("#4e2d33"),
			secondaryText: new Color("#80535b"),
			valueColor: new Color("#8f2f3d"),
			buttonNormal: new Color("#c98b94"),
			buttonHover: new Color("#d9a0a8"),
			buttonPressed: new Color("#ae707a"),
			buttonBorder: new Color("#92515c"),
			overlayColor: new Color("#241719a6"));
	}

	private void ApplyPalette(
		Color panelBackground,
		Color panelBorder,
		Color titleColor,
		Color primaryText,
		Color secondaryText,
		Color valueColor,
		Color buttonNormal,
		Color buttonHover,
		Color buttonPressed,
		Color buttonBorder,
		Color overlayColor)
	{
		_overlay.Color = overlayColor;

		StyleBoxFlat panelStyle =
			GetOrCreateFlatStyle(
				_resultPanel,
				"panel");

		panelStyle.BgColor =
			panelBackground;

		panelStyle.BorderColor =
			panelBorder;

		panelStyle.SetBorderWidthAll(8);
		panelStyle.SetCornerRadiusAll(24);

		panelStyle.ShadowColor =
			new Color(0, 0, 0, 0.3f);

		panelStyle.ShadowSize = 12;
		panelStyle.ShadowOffset =
			new Vector2(0, 7);

		_resultPanel.AddThemeStyleboxOverride(
			"panel",
			panelStyle);

		_resultTitleLabel.AddThemeColorOverride(
			"font_color",
			titleColor);

		_missionNameLabel.AddThemeColorOverride(
			"font_color",
			primaryText);

		_objectiveTitleLabel.AddThemeColorOverride(
			"font_color",
			secondaryText);

		_objectiveLabel.AddThemeColorOverride(
			"font_color",
			primaryText);

		Label[] titleLabels =
		{
			_timeTitleLabel,
			_statisticTitleLabel,
			_difficultyTitleLabel,
			_failuresTitleLabel
		};

		foreach (Label label in titleLabels)
		{
			label.AddThemeColorOverride(
				"font_color",
				secondaryText);
		}

		Label[] valueLabels =
		{
			_timeValueLabel,
			_statisticValueLabel,
			_difficultyValueLabel,
			_failuresValueLabel
		};

		foreach (Label label in valueLabels)
		{
			label.AddThemeColorOverride(
				"font_color",
				valueColor);
		}

		StyleBoxFlat separatorStyle =
			new()
			{
				BgColor = panelBorder
			};

		_separator.AddThemeStyleboxOverride(
			"separator",
			separatorStyle);

		ConfigureButtonStyle(
			"normal",
			buttonNormal,
			buttonBorder,
			3);

		ConfigureButtonStyle(
			"hover",
			buttonHover,
			buttonBorder,
			4);

		ConfigureButtonStyle(
			"pressed",
			buttonPressed,
			buttonBorder,
			3);

		_continueButton.AddThemeColorOverride(
			"font_color",
			primaryText);

		_continueButton.AddThemeColorOverride(
			"font_hover_color",
			primaryText);

		_continueButton.AddThemeColorOverride(
			"font_pressed_color",
			primaryText);
	}

	private void ConfigureButtonStyle(
		string state,
		Color backgroundColor,
		Color borderColor,
		int borderWidth)
	{
		StyleBoxFlat style =
			GetOrCreateFlatStyle(
				_continueButton,
				state);

		style.BgColor =
			backgroundColor;

		style.BorderColor =
			borderColor;

		style.SetBorderWidthAll(
			borderWidth);

		style.SetCornerRadiusAll(24);

		_continueButton.AddThemeStyleboxOverride(
			state,
			style);
	}

	private static StyleBoxFlat GetOrCreateFlatStyle(
		Control control,
		string styleName)
	{
		StyleBox? existingStyle =
			control.GetThemeStylebox(styleName);

		if (existingStyle is StyleBoxFlat flatStyle)
		{
			return
				flatStyle.Duplicate() as StyleBoxFlat ??
				new StyleBoxFlat();
		}

		return new StyleBoxFlat();
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
