using AdaptiveTrials.Game.Interactions;
using Godot;

namespace AdaptiveTrials.Game.Missions.Exploration;

/// <summary>
/// Objeto coletável utilizado na missão Encontrar Objetos.
/// </summary>
public partial class CollectibleObject : Area2D, IInteractable
{
	[Signal]
	public delegate void CollectedEventHandler(
		CollectibleObject collectible);

	public bool CanInteract { get; private set; } = true;

	public bool WasCollected { get; private set; }

	private Polygon2D _visual = null!;
	private PointLight2D _glow = null!;
	private Vector2 _baseScale;
	private float _pulseTime;
	private float _pulseSpeed = 2.0f;

	public override void _Ready()
	{
		_visual = GetNode<Polygon2D>("Visual");
		_glow = GetNode<PointLight2D>("Glow");
		_baseScale = _visual.Scale;

		EnsureGlowTexture();
		AddToGroup("exploration_collectible");
	}

	public override void _Process(double delta)
	{
		if (WasCollected || !Visible)
		{
			return;
		}

		_pulseTime += (float)delta * _pulseSpeed;
		float pulse = 1.0f + Mathf.Sin(_pulseTime) * 0.08f;
		_visual.Scale = _baseScale * pulse;
	}

	public void ConfigureExplorationVisibility(
		float glowEnergy,
		float pulseSpeed)
	{
		_pulseSpeed = Mathf.Max(0.5f, pulseSpeed);

		if (IsNodeReady())
		{
			EnsureGlowTexture();
			_glow.Energy = Mathf.Max(0.1f, glowEnergy);
		}
	}

	private void EnsureGlowTexture()
	{
		if (_glow.Texture is not null)
		{
			return;
		}

		Gradient gradient = new();
		gradient.SetColor(0, new Color(1, 0.88f, 0.58f, 1));
		gradient.SetColor(1, new Color(1, 0.88f, 0.58f, 0));

		_glow.Texture = new GradientTexture2D
		{
			Gradient = gradient,
			Width = 128,
			Height = 128,
			Fill = GradientTexture2D.FillEnum.Radial,
			FillFrom = new Vector2(0.5f, 0.5f),
			FillTo = new Vector2(1.0f, 0.5f)
		};

		_glow.TextureScale = 1.25f;
		_glow.Color = new Color("#ffd98a");
	}

	public void Interact()
	{
		Collect();
	}

	public void Collect()
	{
		if (!CanInteract || WasCollected)
		{
			return;
		}

		CanInteract = false;
		WasCollected = true;

		Monitoring = false;
		Monitorable = false;

		EmitSignal(
			SignalName.Collected,
			this);

		GD.Print($"Objeto coletado: {Name}");

		Hide();
	}

	public void SetAvailable(bool available)
	{
		CanInteract = available;
		WasCollected = false;
		Monitoring = available;
		Monitorable = available;
		Visible = available;

		ProcessMode = available
			? ProcessModeEnum.Inherit
			: ProcessModeEnum.Disabled;
	}
}
