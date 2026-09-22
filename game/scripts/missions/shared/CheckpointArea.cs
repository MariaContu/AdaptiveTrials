using AdaptiveTrials.Game.Player;
using Godot;

namespace AdaptiveTrials.Game.Missions.Shared;

/// <summary>
/// Ponto intermediário obrigatório em missões
/// de deslocamento.
/// </summary>
public partial class CheckpointArea : Area2D
{
	[Signal]
	public delegate void CheckpointReachedEventHandler(
		CheckpointArea checkpoint);

	private const string PlayerDetectionAreaName =
		"DetectionHitbox";

	private Polygon2D _visual = null!;
	private Line2D _outline = null!;
	private Label _symbolLabel = null!;

	private int _checkpointIndex;
	private bool _enabled = true;

	public bool WasReached { get; private set; }
	public int CheckpointIndex => _checkpointIndex;

	public override void _Ready()
	{
		_visual =
			GetNode<Polygon2D>(
				"Visual");

		_outline =
			GetNode<Line2D>(
				"Outline");

		_symbolLabel =
			GetNode<Label>(
				"SymbolLabel");

		AreaEntered += OnAreaEntered;

		ApplyPendingStyle();
	}

	public void ConfigureIndex(
		int index)
	{
		_checkpointIndex =
			Mathf.Max(
				1,
				index);

		if (!WasReached)
		{
			_symbolLabel.Text =
				_checkpointIndex.ToString();
		}
	}

	public void ResetCheckpoint()
	{
		WasReached = false;
		SetEnabledState(true);
	}

	public void SetEnabledState(bool enabled)
	{
		_enabled = enabled;
		Monitoring = enabled && !WasReached;

		if (WasReached)
		{
			ApplyCompletedStyle();
			return;
		}

		if (_enabled)
		{
			ApplyPendingStyle();
			return;
		}

		ApplyLockedStyle();
	}

	private void OnAreaEntered(
		Area2D area)
	{
		if (WasReached ||
			!_enabled)
		{
			return;
		}

		if (!IsPlayerDetectionArea(area))
		{
			return;
		}

		WasReached = true;
		Monitoring = false;

		ApplyCompletedStyle();

		EmitSignal(
			SignalName.CheckpointReached,
			this);
	}

	private void ApplyPendingStyle()
	{
		_visual.Color =
			new Color("#d8bbdf");

		_outline.DefaultColor =
			new Color("#6e536f");

		_symbolLabel.Text =
			_checkpointIndex > 0
				? _checkpointIndex.ToString()
				: "◆";

		_symbolLabel.Modulate =
			new Color("#533c55");

		Scale =
			Vector2.One;
	}

	private void ApplyLockedStyle()
	{
		_visual.Color =
			new Color("#8f8492");

		_outline.DefaultColor =
			new Color("#625a64");

		_symbolLabel.Text =
			_checkpointIndex > 0
				? _checkpointIndex.ToString()
				: "◆";

		_symbolLabel.Modulate =
			new Color("#d3cad5");

		Scale =
			new Vector2(
				0.92f,
				0.92f);
	}

	private void ApplyCompletedStyle()
	{
		_visual.Color =
			new Color("#a8d7b4");

		_outline.DefaultColor =
			new Color("#4f8460");

		_symbolLabel.Text =
			"✓";

		_symbolLabel.Modulate =
			new Color("#275f38");

		Scale =
			new Vector2(
				1.1f,
				1.1f);
	}

	private static bool IsPlayerDetectionArea(
		Area2D area)
	{
		if (area.Name !=
			PlayerDetectionAreaName)
		{
			return false;
		}

		return area.GetParentOrNull<PlayerController>()
			is not null;
	}
}
