using AdaptiveTrials.Game.Player;
using Godot;

namespace AdaptiveTrials.Game.Missions.Shared;

/// <summary>
/// Área perigosa que registra uma falha ao tocar
/// a área de detecção do jogador.
/// </summary>
public partial class HazardArea : Area2D
{
	[Signal]
	public delegate void PlayerHitEventHandler();

	private const string PlayerDetectionAreaName =
		"DetectionHitbox";

	private Polygon2D _visual = null!;
	private bool _canTrigger = true;
	private bool _timedMode;
	private bool _isActive = true;
	private float _safeSeconds = 2.0f;
	private float _warningSeconds = 0.8f;
	private float _activeSeconds = 1.2f;
	private float _phaseOffset;
	private float _elapsed;
	private HazardState _state = HazardState.Active;

	public override void _Ready()
	{
		_visual = GetNode<Polygon2D>("Visual");
		AreaEntered += OnAreaEntered;
		ApplyStateVisual();
	}

	public override void _Process(double delta)
	{
		if (!_timedMode)
		{
			return;
		}

		_elapsed += (float)delta;
		UpdateTimedState();
	}

	public void ConfigureTimed(
		float safeSeconds,
		float warningSeconds,
		float activeSeconds,
		float phaseOffsetSeconds)
	{
		_timedMode = true;
		_safeSeconds = Mathf.Max(0.25f, safeSeconds);
		_warningSeconds = Mathf.Max(0.20f, warningSeconds);
		_activeSeconds = Mathf.Max(0.35f, activeSeconds);
		_phaseOffset = Mathf.Max(0, phaseOffsetSeconds);
		_elapsed = 0;
		UpdateTimedState();
	}

	public void ResetTrigger()
	{
		_canTrigger = true;
	}

	private void OnAreaEntered(
		Area2D area)
	{
		if (!_canTrigger || !_isActive)
		{
			return;
		}

		if (!IsPlayerDetectionArea(area))
		{
			return;
		}

		_canTrigger = false;

		EmitSignal(
			SignalName.PlayerHit);

		GetTree()
			.CreateTimer(0.75)
			.Timeout += ResetTrigger;
	}

	private void UpdateTimedState()
	{
		float cycle =
			_safeSeconds + _warningSeconds + _activeSeconds;

		float position =
			(_elapsed + _phaseOffset) % cycle;

		if (position < 0)
		{
			position += cycle;
		}

		HazardState nextState =
			position < _safeSeconds
				? HazardState.Safe
				: position < _safeSeconds + _warningSeconds
					? HazardState.Warning
					: HazardState.Active;

		if (nextState == _state)
		{
			return;
		}

		_state = nextState;
		_isActive = _state == HazardState.Active;
		ApplyStateVisual();

		if (_isActive)
		{
			TryTriggerOverlappingPlayer();
		}
	}

	private void TryTriggerOverlappingPlayer()
	{
		foreach (Area2D area in GetOverlappingAreas())
		{
			if (!IsPlayerDetectionArea(area))
			{
				continue;
			}

			OnAreaEntered(area);
			break;
		}
	}

	private void ApplyStateVisual()
	{
		if (!IsInstanceValid(_visual))
		{
			return;
		}

		switch (_state)
		{
			case HazardState.Safe:
				_visual.Color = new Color("#62556e80");
				_visual.Scale = new Vector2(0.82f, 0.82f);
				break;

			case HazardState.Warning:
				_visual.Color = new Color("#d6ad5fc0");
				_visual.Scale = new Vector2(0.94f, 0.94f);
				break;

			default:
				_visual.Color = new Color("#c66f78f2");
				_visual.Scale = Vector2.One;
				break;
		}
	}

	private enum HazardState
	{
		Safe,
		Warning,
		Active
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
