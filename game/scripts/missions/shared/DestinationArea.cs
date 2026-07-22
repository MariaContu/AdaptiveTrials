using AdaptiveTrials.Game.Player;
using Godot;

namespace AdaptiveTrials.Game.Missions.Shared;

/// <summary>
/// Área que representa o destino final de uma missão de exploração.
/// </summary>
public partial class DestinationArea : Area2D
{
	[Signal]
	public delegate void DestinationReachedEventHandler();

	private Polygon2D _outerCircle = null!;
	private Polygon2D _innerCircle = null!;
	private Label _destinationLabel = null!;

	private bool _enabled = true;
	private bool _wasReached;
	private double _animationTime;

	public override void _Ready()
	{
		_outerCircle =
			GetNode<Polygon2D>("OuterCircle");

		_innerCircle =
			GetNode<Polygon2D>("InnerCircle");

		_destinationLabel =
			GetNode<Label>("DestinationLabel");

		BodyEntered += OnBodyEntered;

		ApplyEnabledStyle();
	}

	public override void _Process(double delta)
	{
		if (!_enabled || _wasReached)
		{
			return;
		}

		_animationTime += delta;

		float pulse =
			1.0f +
			Mathf.Sin((float)_animationTime * 3.0f) *
			0.08f;

		_innerCircle.Scale =
			new Vector2(pulse, pulse);

		_outerCircle.Rotation =
			(float)_animationTime * 0.35f;
	}

	public void SetEnabledState(bool enabled)
	{
		_enabled = enabled;
		Monitoring = enabled;

		if (enabled)
		{
			_wasReached = false;
			ApplyEnabledStyle();
			return;
		}

		ApplyDisabledStyle();
	}

	private void OnBodyEntered(Node2D body)
	{
		if (!_enabled || _wasReached)
		{
			return;
		}

		if (body is not PlayerController)
		{
			return;
		}

		_wasReached = true;

		ApplyReachedStyle();

		EmitSignal(
			SignalName.DestinationReached);
	}

	private void ApplyEnabledStyle()
	{
		Visible = true;

		_outerCircle.Color =
			new Color("#9c7fa5");

		_innerCircle.Color =
			new Color("#d8c4dd");

		_destinationLabel.Text = "GO";
		_destinationLabel.Modulate =
			new Color("#4d3853");
	}

	private void ApplyDisabledStyle()
	{
		Visible = true;

		_outerCircle.Color =
			new Color("#756a78");

		_innerCircle.Color =
			new Color("#aaa0ac");

		_destinationLabel.Text = "LOCK";
		_destinationLabel.Modulate =
			new Color("#453e47");
	}

	private void ApplyReachedStyle()
	{
		_outerCircle.Color =
			new Color("#79aa88");

		_innerCircle.Color =
			new Color("#b9dfc3");

		_destinationLabel.Text = "✓";
		_destinationLabel.Modulate =
			new Color("#275f38");
	}
}
