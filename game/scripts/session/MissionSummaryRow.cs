using System;
using AdaptiveTrials.Game.Enums;
using Godot;

namespace AdaptiveTrials.Game.Session;

/// <summary>
/// Exibe o resultado individual de uma missão
/// na tela de resumo da sessão.
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
			GetNode<PanelContainer>(
				"RowMargin/Content/CategoryIconPanel");

		_categoryIcon =
			GetNode<Label>(
				"RowMargin/Content/CategoryIconPanel/" +
				"CategoryIcon");

		_missionNameLabel =
			GetNode<Label>(
				"RowMargin/Content/MissionInfo/" +
				"MissionNameLabel");

		_missionDetailsLabel =
			GetNode<Label>(
				"RowMargin/Content/MissionInfo/" +
				"MissionDetailsLabel");

		_timeValueLabel =
			GetNode<Label>(
				"RowMargin/Content/TimeContainer/" +
				"TimeValueLabel");

		_resultPanel =
			GetNode<PanelContainer>(
				"RowMargin/Content/ResultPanel");

		_resultLabel =
			GetNode<Label>(
				"RowMargin/Content/ResultPanel/" +
				"ResultLabel");
	}

	public void Configure(
		CompletedMissionRecord record)
	{
		ArgumentNullException.ThrowIfNull(
			record);

		_missionNameLabel.Text =
			record.Mission.Name;

		_missionDetailsLabel.Text =
			$"{GetCategoryText(record.Mission.Type)}" +
			$"  •  " +
			$"{GetDifficultyText(record.Mission.Difficulty)}";

		_categoryIcon.Text =
			GetCategoryIcon(record.Mission.Type);

		_timeValueLabel.Text =
			FormatTime(
				record.Result.CompletionTime);

		ApplyCategoryStyle(
			record.Mission.Type);

		ApplyResultStyle(
			record.Result.Success);
	}

	private void ApplyCategoryStyle(
		MissionType missionType)
	{
		StyleBoxFlat style =
			new()
			{
				BorderWidthLeft = 2,
				BorderWidthTop = 2,
				BorderWidthRight = 2,
				BorderWidthBottom = 2,

				CornerRadiusTopLeft = 12,
				CornerRadiusTopRight = 12,
				CornerRadiusBottomRight = 12,
				CornerRadiusBottomLeft = 12
			};

		switch (missionType)
		{
			case MissionType.Combat:
				style.BgColor =
					new Color("#F1D9DC");

				style.BorderColor =
					new Color("#C98891");

				_categoryIcon.Modulate =
					new Color("#874A54");

				break;

			case MissionType.Exploration:
				style.BgColor =
					new Color("#DCEBDD");

				style.BorderColor =
					new Color("#8FAF91");

				_categoryIcon.Modulate =
					new Color("#4F7655");

				break;

			case MissionType.Puzzle:
				style.BgColor =
					new Color("#DDDDF0");

				style.BorderColor =
					new Color("#9293BF");

				_categoryIcon.Modulate =
					new Color("#565888");

				break;

			default:
				style.BgColor =
					new Color("#DED0E2");

				style.BorderColor =
					new Color("#B89DBE");

				_categoryIcon.Modulate =
					new Color("#57405B");

				break;
		}

		_categoryIconPanel.AddThemeStyleboxOverride(
			"panel",
			style);
	}

	private void ApplyResultStyle(
		bool success)
	{
		StyleBoxFlat style =
			new()
			{
				BorderWidthLeft = 2,
				BorderWidthTop = 2,
				BorderWidthRight = 2,
				BorderWidthBottom = 2,

				CornerRadiusTopLeft = 12,
				CornerRadiusTopRight = 12,
				CornerRadiusBottomRight = 12,
				CornerRadiusBottomLeft = 12
			};

		if (success)
		{
			style.BgColor =
				new Color("#DDEBDD");

			style.BorderColor =
				new Color("#86AB8A");

			_resultLabel.Text =
				"CONCLUÍDA";

			_resultLabel.Modulate =
				new Color("#416E48");

			_resultPanel.AddThemeStyleboxOverride(
				"panel",
				style);

			return;
		}

		style.BgColor =
			new Color("#F1D7DA");

		style.BorderColor =
			new Color("#C77C87");

		_resultLabel.Text =
			"NÃO CONCLUÍDA";

		_resultLabel.Modulate =
			new Color("#914955");

		_resultPanel.AddThemeStyleboxOverride(
			"panel",
			style);
	}

	private static string GetCategoryText(
		MissionType missionType)
	{
		return missionType switch
		{
			MissionType.Combat =>
				"Combate",

			MissionType.Exploration =>
				"Exploração",

			MissionType.Puzzle =>
				"Quebra-cabeça",

			_ =>
				"Desconhecida"
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
				"⌖",

			MissionType.Puzzle =>
				"◆",

			_ =>
				"?"
		};
	}

	private static string GetDifficultyText(
		int difficulty)
	{
		return difficulty switch
		{
			1 => "Fácil",
			2 => "Médio",
			3 => "Difícil",
			_ => $"Nível {difficulty}"
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
}
