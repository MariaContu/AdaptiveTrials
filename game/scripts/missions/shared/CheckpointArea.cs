using AdaptiveTrials.Game.Player;
using Godot;

namespace AdaptiveTrials.Game.Missions.Shared;

/// <summary>
/// Ponto intermediário obrigatório em missões de deslocamento.
/// </summary>
public partial class CheckpointArea : Area2D
{
	[Signal]
	public delegate void CheckpointReachedEventHandler(
		CheckpointArea checkpoint);

	private Polygon2D _visual = null!;
	private Line2D _outline = null!;
	private Label _symbolLabel = null!;
	
	private int _checkpointIndex;

	public bool WasReached { get; private set; }

	public override void _Ready()
	{
		_visual =
			GetNode<Polygon2D>("Visual");

		_outline =
			GetNode<Line2D>("Outline");

		_symbolLabel =
			GetNode<Label>("SymbolLabel");

		BodyEntered += OnBodyEntered;

		ApplyPendingStyle();
	}

	public void ResetCheckpoint()
	{
		WasReached = false;
		Monitoring = true;

		ApplyPendingStyle();
	}

	private void OnBodyEntered(Node2D body)
	{
		if (WasReached)
		{
			return;
		}

		if (body is not PlayerController)
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

		Scale = Vector2.One;
	}

	private void ApplyCompletedStyle()
	{
		_visual.Color =
			new Color("#a8d7b4");

		_outline.DefaultColor =
			new Color("#4f8460");

		_symbolLabel.Text = "✓";
		_symbolLabel.Modulate =
			new Color("#275f38");

		Scale =
			new Vector2(1.1f, 1.1f);
	}
	
	public void ConfigureIndex(int index)
	{
		_checkpointIndex =
			Mathf.Max(1, index);

		if (!WasReached)
		{
			_symbolLabel.Text =
				_checkpointIndex.ToString();
		}
	}
		
}
