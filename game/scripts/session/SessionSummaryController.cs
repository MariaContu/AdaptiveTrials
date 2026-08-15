using System;
using System.Collections.Generic;
using System.Linq;
using AdaptiveTrials.Game.Enums;
using Godot;

namespace AdaptiveTrials.Game.Session;

/// <summary>
/// Apresenta o resumo final de uma sessão concluída.
/// Esta é a última tela do fluxo antes do retorno ao menu.
/// </summary>
public partial class SessionSummaryController : Node
{
	private const string MainMenuScenePath =
		"res://scenes/menu/MainMenu.tscn";

	private static readonly PackedScene MissionSummaryRowScene =
		GD.Load<PackedScene>(
			"res://scenes/session/components/MissionSummaryRow.tscn");

	private Label _subtitleLabel = null!;
	private Label _completedBadgeLabel = null!;
	private VBoxContainer _missionList = null!;

	private Label _averageTimeValue = null!;
	private Label _averageTimeDetail = null!;
	private Label _successRateValue = null!;
	private Label _successRateDetail = null!;
	private Label _failedMissionsValue = null!;
	private Label _failedMissionsDetail = null!;
	private Label _bestCategoryValue = null!;
	private Label _bestCategoryDetail = null!;

	private Label _combatPerformanceValue = null!;
	private Label _explorationPerformanceValue = null!;
	private Label _puzzlePerformanceValue = null!;

	private Label _statusLabel = null!;
	private Button _returnButton = null!;

	private SessionManager _sessionManager = null!;
	private bool _isNavigating;

	public override void _Ready()
	{
		GetReferences();

		_sessionManager =
			GetNode<SessionManager>("/root/SessionManager");

		_returnButton.Pressed += OnReturnToMenuPressed;

		LoadSessionSummary();
	}

	private void GetReferences()
	{
		const string root =
			"../ScreenMargin/SummaryPanel/PanelMargin/RootContent/";

		const string stats =
			root + "Body/StatsSection/";

		_subtitleLabel =
			GetNode<Label>(root + "Header/HeaderText/SubtitleLabel");

		_completedBadgeLabel =
			GetNode<Label>(root + "Header/CompletedBadge/CompletedBadgeLabel");

		_missionList =
			GetNode<VBoxContainer>(
				root +
				"Body/MissionsSection/SectionMargin/MissionsContent/" +
				"MissionsScroll/MissionList");

		_averageTimeValue =
			GetNode<Label>(
				stats + "MetricsGrid/AverageTimeCard/Margin/Content/Value");
		_averageTimeDetail =
			GetNode<Label>(
				stats + "MetricsGrid/AverageTimeCard/Margin/Content/Detail");

		_successRateValue =
			GetNode<Label>(
				stats + "MetricsGrid/SuccessRateCard/Margin/Content/Value");
		_successRateDetail =
			GetNode<Label>(
				stats + "MetricsGrid/SuccessRateCard/Margin/Content/Detail");

		_failedMissionsValue =
			GetNode<Label>(
				stats + "MetricsGrid/FailedCard/Margin/Content/Value");
		_failedMissionsDetail =
			GetNode<Label>(
				stats + "MetricsGrid/FailedCard/Margin/Content/Detail");

		_bestCategoryValue =
			GetNode<Label>(
				stats + "MetricsGrid/BestCategoryCard/Margin/Content/Value");
		_bestCategoryDetail =
			GetNode<Label>(
				stats + "MetricsGrid/BestCategoryCard/Margin/Content/Detail");

		const string categoryRoot =
			stats + "CategoryPanel/Margin/Content/";

		_combatPerformanceValue =
			GetNode<Label>(categoryRoot + "CombatRow/Value");
		_explorationPerformanceValue =
			GetNode<Label>(categoryRoot + "ExplorationRow/Value");
		_puzzlePerformanceValue =
			GetNode<Label>(categoryRoot + "PuzzleRow/Value");

		_statusLabel =
			GetNode<Label>(root + "Footer/StatusLabel");

		_returnButton =
			GetNode<Button>(root + "Footer/ReturnButton");
	}

	private void LoadSessionSummary()
	{
		if (!_sessionManager.SessionEnded)
		{
			ShowBlockingError("A sessão ainda não foi concluída.");
			return;
		}

		IReadOnlyList<CompletedMissionRecord> records =
			_sessionManager.CompletedMissionRecords;

		if (records.Count == 0)
		{
			ShowBlockingError(
				"Nenhum resultado de missão está disponível.");
			return;
		}

		ClearMissionList();

		foreach (CompletedMissionRecord record in records)
		{
			MissionSummaryRow row =
				MissionSummaryRowScene.Instantiate<MissionSummaryRow>();

			_missionList.AddChild(row);
			row.Configure(record);
		}

		int totalMissions = records.Count;
		int successfulMissions =
			records.Count(record => record.Result.Success);
		int failedMissions = totalMissions - successfulMissions;
		int totalMistakes =
			records.Sum(record => record.Result.Failures);

		double totalTime =
			records.Sum(record => record.Result.CompletionTime);
		double averageTime =
			records.Average(record => record.Result.CompletionTime);
		double successRate =
			totalMissions == 0
				? 0
				: successfulMissions / (double)totalMissions;

		string modeText = GetModeText(_sessionManager.Mode);

		_subtitleLabel.Text =
			$"Modo {modeText} • {totalMissions} missões registradas";

		_completedBadgeLabel.Text =
			$"{successfulMissions}/{totalMissions} CONCLUÍDAS";

		_averageTimeValue.Text = FormatTime(averageTime);
		_averageTimeDetail.Text = $"Total: {FormatTime(totalTime)}";

		_successRateValue.Text =
			$"{Math.Round(successRate * 100):0}%";
		_successRateDetail.Text =
			$"{successfulMissions} de {totalMissions} concluídas";

		_failedMissionsValue.Text = failedMissions.ToString();
		_failedMissionsDetail.Text =
			$"{totalMistakes} erros durante as missões";

		List<CategoryPerformance> performances =
			CalculateCategoryPerformances(records);

		CategoryPerformance? bestCategory =
			performances
				.OrderByDescending(performance => performance.SuccessRate)
				.ThenBy(performance => performance.AverageTime)
				.FirstOrDefault();

		if (bestCategory is null)
		{
			_bestCategoryValue.Text = "-";
			_bestCategoryDetail.Text = "Sem dados";
		}
		else
		{
			_bestCategoryValue.Text =
				GetCategoryText(bestCategory.MissionType);
			_bestCategoryDetail.Text =
				$"{bestCategory.SuccessfulMissions}/" +
				$"{bestCategory.TotalMissions} • " +
				$"média {FormatTime(bestCategory.AverageTime)}";
		}

		SetCategoryPerformance(
			MissionType.Combat,
			performances,
			_combatPerformanceValue);
		SetCategoryPerformance(
			MissionType.Exploration,
			performances,
			_explorationPerformanceValue);
		SetCategoryPerformance(
			MissionType.Puzzle,
			performances,
			_puzzlePerformanceValue);

		_statusLabel.Text =
			"Sessão finalizada. Você já pode voltar ao menu.";

		_returnButton.Disabled = false;

		GD.Print(
			$"Resumo da sessão carregado: " +
			$"Mode={modeText}, " +
			$"Missions={totalMissions}, " +
			$"Successful={successfulMissions}, " +
			$"FailedMissions={failedMissions}, " +
			$"Mistakes={totalMistakes}, " +
			$"TotalTime={totalTime:F2}, " +
			$"AverageTime={averageTime:F2}");
	}

	private static void SetCategoryPerformance(
		MissionType type,
		IReadOnlyList<CategoryPerformance> performances,
		Label target)
	{
		CategoryPerformance? performance =
			performances.FirstOrDefault(item => item.MissionType == type);

		target.Text =
			performance is null
				? "Sem dados"
				: $"{performance.SuccessfulMissions}/" +
				  $"{performance.TotalMissions} • " +
				  $"{FormatTime(performance.AverageTime)}";
	}

	private static List<CategoryPerformance>
		CalculateCategoryPerformances(
			IReadOnlyList<CompletedMissionRecord> records)
	{
		return records
			.GroupBy(record => record.Mission.Type)
			.Select(group =>
				new CategoryPerformance(
					group.Key,
					group.Count(),
					group.Count(record => record.Result.Success),
					group.Average(
						record => record.Result.CompletionTime)))
			.ToList();
	}

	private void ClearMissionList()
	{
		foreach (Node child in _missionList.GetChildren())
		{
			child.QueueFree();
		}
	}

	private void ShowBlockingError(string message)
	{
		ClearMissionList();

		_subtitleLabel.Text =
			"Não foi possível carregar o resumo da sessão";
		_completedBadgeLabel.Text = "INDISPONÍVEL";

		_averageTimeValue.Text = "-";
		_averageTimeDetail.Text = "-";
		_successRateValue.Text = "-";
		_successRateDetail.Text = "-";
		_failedMissionsValue.Text = "-";
		_failedMissionsDetail.Text = "-";
		_bestCategoryValue.Text = "-";
		_bestCategoryDetail.Text = "-";
		_combatPerformanceValue.Text = "Sem dados";
		_explorationPerformanceValue.Text = "Sem dados";
		_puzzlePerformanceValue.Text = "Sem dados";

		_statusLabel.Text = message;
		_returnButton.Disabled = true;

		GD.PushError(message);
	}

	private void OnReturnToMenuPressed()
	{
		if (_isNavigating)
		{
			return;
		}

		_isNavigating = true;
		_returnButton.Disabled = true;

		if (!ResourceLoader.Exists(MainMenuScenePath))
		{
			_statusLabel.Text = "A tela inicial não foi encontrada.";
			_returnButton.Disabled = false;
			_isNavigating = false;
			GD.PushError($"Cena não encontrada: {MainMenuScenePath}");
			return;
		}

		Error navigationError =
			GetTree().ChangeSceneToFile(MainMenuScenePath);

		if (navigationError != Error.Ok)
		{
			_statusLabel.Text = "Não foi possível retornar ao menu.";
			_returnButton.Disabled = false;
			_isNavigating = false;
			GD.PushError(
				$"Erro ao abrir {MainMenuScenePath}: {navigationError}");
			return;
		}

		_sessionManager.ClearSession();
	}

	private static string GetModeText(GameMode? mode)
	{
		return mode switch
		{
			GameMode.Control => "Controle",
			GameMode.Adaptive => "Adaptativo",
			_ => "Desconhecido"
		};
	}

	private static string GetCategoryText(MissionType missionType)
	{
		return missionType switch
		{
			MissionType.Combat => "Combate",
			MissionType.Exploration => "Exploração",
			MissionType.Puzzle => "Quebra-cabeça",
			_ => "Desconhecida"
		};
	}

	private static string FormatTime(double seconds)
	{
		int totalSeconds = Math.Max(0, (int)Math.Round(seconds));
		int minutes = totalSeconds / 60;
		int remainingSeconds = totalSeconds % 60;
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
				: SuccessfulMissions / (double)TotalMissions;
	}
}
