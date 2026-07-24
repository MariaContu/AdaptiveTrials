using System;
using System.Collections.Generic;
using System.Linq;
using AdaptiveTrials.Game.Enums;
using Godot;

namespace AdaptiveTrials.Game.Session;

/// <summary>
/// Exibe os resultados e as estatísticas
/// da sessão experimental concluída.
/// </summary>
public partial class SessionSummaryController : Node
{
	private static readonly PackedScene
		MissionSummaryRowScene =
			GD.Load<PackedScene>(
				"res://scenes/session/components/" +
				"MissionSummaryRow.tscn");

	private Label _completedBadgeLabel = null!;
	private VBoxContainer _missionList = null!;

	private Label _averageTimeValue = null!;
	private Label _successRateValue = null!;
	private Label _failuresValue = null!;
	private Label _bestCategoryValue = null!;
	private Label _bestCategoryDetail = null!;

	private Label _statusLabel = null!;
	private Button _continueButton = null!;

	private SessionManager _sessionManager = null!;

	private bool _isNavigating;

	public override void _Ready()
	{
		GetReferences();

		_sessionManager =
			GetNode<SessionManager>(
				"/root/SessionManager");

		_continueButton.Pressed +=
			OnContinuePressed;

		LoadSessionSummary();
	}

	private void GetReferences()
	{
		const string mainContentPath =
			"../MainMargin/MainContent/";

		const string columnsPath =
			mainContentPath +
			"MainPanel/PanelMargin/Columns/";

		const string statisticsPath =
			columnsPath +
			"StatisticsColumn/";

		_completedBadgeLabel =
			GetNode<Label>(
				mainContentPath +
				"Header/CompletedBadge/" +
				"CompletedBadgeLabel");

		_missionList =
			GetNode<VBoxContainer>(
				columnsPath +
				"MissionsColumn/MissionsScroll/" +
				"MissionList");

		_averageTimeValue =
			GetNode<Label>(
				statisticsPath +
				"AverageTimeCard/CardMargin/" +
				"CardContent/CardValue");

		_successRateValue =
			GetNode<Label>(
				statisticsPath +
				"SuccessRateCard/CardMargin/" +
				"CardContent/CardValue");

		_failuresValue =
			GetNode<Label>(
				statisticsPath +
				"FailuresCard/CardMargin/" +
				"CardContent/CardValue");

		_bestCategoryValue =
			GetNode<Label>(
				statisticsPath +
				"BestCategoryCard/CardMargin/" +
				"CardContent/CardValue");

		_bestCategoryDetail =
			GetNode<Label>(
				statisticsPath +
				"BestCategoryCard/CardMargin/" +
				"CardContent/CardDetail");

		_statusLabel =
			GetNode<Label>(
				mainContentPath +
				"Footer/StatusLabel");

		_continueButton =
			GetNode<Button>(
				mainContentPath +
				"Footer/ContinueButton");
	}

	private void LoadSessionSummary()
	{
		if (!_sessionManager.SessionEnded)
		{
			ShowBlockingError(
				"The session has not been completed.");

			return;
		}

		IReadOnlyList<CompletedMissionRecord> records =
			_sessionManager.CompletedMissionRecords;

		if (records.Count == 0)
		{
			ShowBlockingError(
				"No mission results are available.");

			return;
		}

		ClearMissionList();

		foreach (CompletedMissionRecord record
				 in records)
		{
			MissionSummaryRow row =
				MissionSummaryRowScene
					.Instantiate<MissionSummaryRow>();

			_missionList.AddChild(
				row);

			row.Configure(
				record);
		}

		int totalMissions =
			records.Count;

		int successfulMissions =
			records.Count(
				record =>
					record.Result.Success);

		int totalFailures =
			records.Sum(
				record =>
					record.Result.Failures);

		double averageTime =
			records.Average(
				record =>
					record.Result.CompletionTime);

		double successRate =
			totalMissions == 0
				? 0
				: successfulMissions /
				  (double)totalMissions;

		_completedBadgeLabel.Text =
			$"{successfulMissions}/" +
			$"{totalMissions} SUCCESSFUL";

		_averageTimeValue.Text =
			FormatTime(
				averageTime);

		_successRateValue.Text =
			$"{Math.Round(successRate * 100):0}%";

		_failuresValue.Text =
			totalFailures.ToString();

		CategoryPerformance? bestCategory =
			CalculateBestCategory(
				records);

		if (bestCategory is not null)
		{
			_bestCategoryValue.Text =
				GetCategoryText(
					bestCategory.MissionType);

			_bestCategoryDetail.Text =
				$"{bestCategory.SuccessfulMissions}/" +
				$"{bestCategory.TotalMissions} successful" +
				$"  •  Avg. " +
				$"{FormatTime(bestCategory.AverageTime)}";
		}
		else
		{
			_bestCategoryValue.Text =
				"-";

			_bestCategoryDetail.Text =
				"Performance data unavailable.";
		}

		_statusLabel.Text =
			"Session successfully completed and recorded.";

		_continueButton.Disabled =
			false;

		GD.Print(
			$"Resumo da sessão carregado: " +
			$"Missions={totalMissions}, " +
			$"Successful={successfulMissions}, " +
			$"Failures={totalFailures}, " +
			$"AverageTime={averageTime:F2}");
	}

	private void ClearMissionList()
	{
		foreach (Node child
				 in _missionList.GetChildren())
		{
			child.QueueFree();
		}
	}

	private void ShowBlockingError(
		string message)
	{
		ClearMissionList();

		_completedBadgeLabel.Text =
			"UNAVAILABLE";

		_averageTimeValue.Text =
			"-";

		_successRateValue.Text =
			"-";

		_failuresValue.Text =
			"-";

		_bestCategoryValue.Text =
			"-";

		_bestCategoryDetail.Text =
			"Performance data unavailable.";

		_statusLabel.Text =
			message;

		_continueButton.Disabled =
			true;

		GD.PushError(message);
	}

	private void OnContinuePressed()
	{
		if (_isNavigating)
		{
			return;
		}

		_isNavigating = true;
		_continueButton.Disabled = true;

		const string scenePath =
			SessionManager.QuestionnaireInfoScenePath;

		if (!ResourceLoader.Exists(
				scenePath))
		{
			_statusLabel.Text =
				"The questionnaire information screen " +
				"was not found.";

			_continueButton.Disabled =
				false;

			_isNavigating =
				false;

			GD.PushError(
				$"Cena não encontrada: {scenePath}");

			return;
		}

		Error navigationError =
			GetTree().ChangeSceneToFile(
				scenePath);

		if (navigationError == Error.Ok)
		{
			return;
		}

		_statusLabel.Text =
			"The next screen could not be opened.";

		_continueButton.Disabled =
			false;

		_isNavigating =
			false;

		GD.PushError(
			$"Erro ao abrir {scenePath}: " +
			$"{navigationError}");
	}

	private static CategoryPerformance?
		CalculateBestCategory(
			IReadOnlyList<CompletedMissionRecord> records)
	{
		return records
			.GroupBy(
				record =>
					record.Mission.Type)
			.Select(
				group =>
					new CategoryPerformance(
						group.Key,
						group.Count(),
						group.Count(
							record =>
								record.Result.Success),
						group.Average(
							record =>
								record.Result
									.CompletionTime)))
			.OrderByDescending(
				performance =>
					performance.SuccessRate)
			.ThenBy(
				performance =>
					performance.AverageTime)
			.FirstOrDefault();
	}

	private static string GetCategoryText(
		MissionType missionType)
	{
		return missionType switch
		{
			MissionType.Combat =>
				"Combat",

			MissionType.Exploration =>
				"Exploration",

			MissionType.Puzzle =>
				"Puzzle",

			_ =>
				"Unknown"
		};
	}

	private static string FormatTime(
		double seconds)
	{
		int totalSeconds =
			Math.Max(
				0,
				(int)Math.Round(seconds));

		int minutes =
			totalSeconds / 60;

		int remainingSeconds =
			totalSeconds % 60;

		return $"{minutes:00}:{remainingSeconds:00}";
	}

	private sealed record CategoryPerformance(
		MissionType MissionType,
		int TotalMissions,
		int SuccessfulMissions,
		double AverageTime)
	{
		public double SuccessRate =>
			TotalMissions == 0
				? 0
				: SuccessfulMissions /
				  (double)TotalMissions;
	}
}
