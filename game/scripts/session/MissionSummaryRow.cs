using System;
using AdaptiveTrials.Game.Enums;
using Godot;

namespace AdaptiveTrials.Game.Session;

/// <summary>
/// Representa uma missão concluída no resumo final da sessão.
/// </summary>
public partial class MissionSummaryRow : PanelContainer
{
	private PanelContainer _categoryIconPanel = null!;
	private Label _categoryIcon = null!;
	private Label _missionNameLabel = null!;
	private Label _missionDetailsLabel = null!;
	private Label _timeValueLabel = null!;
	private PanelContainer _resultPanel = null!;
	private Label _resultLabel = null!;

	public override void _Ready()
	{
		_categoryIconPanel =
			GetNode<PanelContainer>("RowMargin/Content/CategoryIconPanel");
		_categoryIcon =
			GetNode<Label>("RowMargin/Content/CategoryIconPanel/CategoryIcon");
		_missionNameLabel =
			GetNode<Label>("RowMargin/Content/MissionInfo/MissionNameLabel");
		_missionDetailsLabel =
			GetNode<Label>("RowMargin/Content/MissionInfo/MissionDetailsLabel");
		_timeValueLabel =
			GetNode<Label>("RowMargin/Content/TimeContainer/TimeValueLabel");
		_resultPanel =
			GetNode<PanelContainer>("RowMargin/Content/ResultPanel");
		_resultLabel =
			GetNode<Label>("RowMargin/Content/ResultPanel/ResultLabel");
	}

	public void Configure(CompletedMissionRecord record)
	{
		ArgumentNullException.ThrowIfNull(record);

		_missionNameLabel.Text = record.Mission.Name;
		_missionDetailsLabel.Text =
			$"{GetCategoryText(record.Mission.Type)} • " +
			$"{GetDifficultyText(record.Mission.Difficulty)}";
		_categoryIcon.Text = GetCategoryIcon(record.Mission.Type);
		_timeValueLabel.Text = FormatTime(record.Result.CompletionTime);

		ApplyCategoryStyle(record.Mission.Type);
		ApplyResultStyle(record.Result.Success);
	}

	private void ApplyCategoryStyle(MissionType missionType)
	{
		StyleBoxFlat style = new()
		{
			CornerRadiusTopLeft = 12,
			CornerRadiusTopRight = 12,
			CornerRadiusBottomRight = 12,
			CornerRadiusBottomLeft = 12
		};

		switch (missionType)
		{
			case MissionType.Combat:
				style.BgColor = new Color("#B9878D");
				_categoryIcon.Modulate = new Color("#3E252A");
				break;
			case MissionType.Exploration:
				style.BgColor = new Color("#91A58D");
				_categoryIcon.Modulate = new Color("#26362A");
				break;
			case MissionType.Puzzle:
				style.BgColor = new Color("#8E899E");
				_categoryIcon.Modulate = new Color("#292637");
				break;
			default:
				style.BgColor = new Color("#96858F");
				_categoryIcon.Modulate = new Color("#342933");
				break;
		}

		_categoryIconPanel.AddThemeStyleboxOverride("panel", style);
	}

	private void ApplyResultStyle(bool success)
	{
		StyleBoxFlat style = new()
		{
			CornerRadiusTopLeft = 10,
			CornerRadiusTopRight = 10,
			CornerRadiusBottomRight = 10,
			CornerRadiusBottomLeft = 10
		};

		if (success)
		{
			style.BgColor = new Color("#4F8159");
			_resultLabel.Text = "✓";
			_resultLabel.Modulate = new Color("#F1F3ED");
		}
		else
		{
			style.BgColor = new Color("#98515C");
			_resultLabel.Text = "×";
			_resultLabel.Modulate = new Color("#F5EDEE");
		}

		_resultPanel.AddThemeStyleboxOverride("panel", style);
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

	private static string GetDifficultyText(int difficulty)
	{
		return difficulty switch
		{
			1 => "Fácil",
			2 => "Média",
			3 => "Difícil",
			_ => "Desconhecida"
		};
	}

	private static string GetCategoryIcon(MissionType missionType)
	{
		return missionType switch
		{
			MissionType.Combat => "⚔",
			MissionType.Exploration => "⌖",
			MissionType.Puzzle => "◇",
			_ => "?"
		};
	}

	private static string FormatTime(double seconds)
	{
		int totalSeconds = Math.Max(0, (int)Math.Round(seconds));
		int minutes = totalSeconds / 60;
		int remainingSeconds = totalSeconds % 60;
		return $"{minutes:00}:{remainingSeconds:00}";
	}
}
