using AdaptiveTrials.Game.Player;
using Godot;

namespace AdaptiveTrials.Game.Missions.Shared;

/// <summary>
/// Inimigo que patrulha entre dois pontos.
/// Pode operar no modo radial legado ou no modo de furtividade,
/// com cone de visão e tempo de suspeita antes da detecção.
/// </summary>
public partial class PatrolEnemy : Node2D
{
	[Signal]
	public delegate void PlayerDetectedEventHandler(
		PatrolEnemy enemy);

	private const float MinimumDistanceToTarget = 4f;

	private Polygon2D _detectionVisual = null!;
	private Polygon2D _enemyVisual = null!;
	private Polygon2D _directionIndicator = null!;
	private Label _stateLabel = null!;

	private Area2D _detectionArea = null!;
	private CollisionShape2D _detectionShape = null!;

	private PlayerController? _trackedPlayer;
	private Vector2 _startPosition;
	private Vector2 _endPosition;
	private Vector2 _currentTarget;

	private float _movementSpeed = 80f;
	private float _detectionRadius = 90f;
	private float _coneAngleDegrees = 70f;
	private float _suspicionSeconds = 0.9f;
	private float _suspicionProgress;

	private bool _isConfigured;
	private bool _isDetectionEnabled = true;
	private bool _movingTowardsEnd = true;
	private bool _stealthMode;
	private bool _detectionEmitted;

	public override void _Ready()
	{
		_detectionVisual = GetNode<Polygon2D>("DetectionVisual");
		_enemyVisual = GetNode<Polygon2D>("EnemyVisual");
		_directionIndicator = GetNode<Polygon2D>("DirectionIndicator");
		_stateLabel = GetNode<Label>("StateLabel");
		_detectionArea = GetNode<Area2D>("DetectionArea");
		_detectionShape = GetNode<CollisionShape2D>("DetectionArea/CollisionShape2D");

		_detectionArea.AreaEntered += OnAreaEntered;
		ApplyDetectionVisual();
	}

	public override void _Process(double delta)
	{
		if (!_isConfigured)
		{
			return;
		}

		MoveAlongPatrol((float)delta);

		_stateLabel.Rotation = -Rotation;

		if (_stealthMode)
		{
			UpdateStealthDetection((float)delta);
		}
	}

	/// <summary>
	/// Configuração legada: mantém detecção radial imediata.
	/// </summary>
	public void Configure(
		Vector2 startPosition,
		Vector2 endPosition,
		float movementSpeed,
		float detectionRadius)
	{
		ConfigureBase(startPosition, endPosition, movementSpeed, detectionRadius);
		_stealthMode = false;
		_detectionArea.Monitoring = true;
		ApplyDetectionVisual();
	}

	/// <summary>
	/// Configura furtividade com cone frontal e estado de suspeita.
	/// </summary>
	public void ConfigureStealth(
		Vector2 startPosition,
		Vector2 endPosition,
		float movementSpeed,
		float detectionDistance,
		float coneAngleDegrees,
		float suspicionSeconds,
		PlayerController player)
	{
		ConfigureBase(startPosition, endPosition, movementSpeed, detectionDistance);
		_stealthMode = true;
		_trackedPlayer = player;
		_coneAngleDegrees = Mathf.Clamp(coneAngleDegrees, 25f, 140f);
		_suspicionSeconds = Mathf.Max(0.20f, suspicionSeconds);
		_suspicionProgress = 0;
		_detectionEmitted = false;
		_detectionArea.Monitoring = false;
		ApplyDetectionVisual();
		ApplyPatrolVisual();
	}

	public void SetDetectionEnabled(bool enabled)
	{
		_isDetectionEnabled = enabled;
		_suspicionProgress = 0;
		_detectionEmitted = false;

		if (IsInstanceValid(_detectionArea) && !_stealthMode)
		{
			_detectionArea.SetDeferred(Area2D.PropertyName.Monitoring, enabled);
		}

		if (IsInstanceValid(_detectionVisual))
		{
			_detectionVisual.Visible = enabled;
		}

		if (!enabled)
		{
			_stateLabel.Text = string.Empty;
			_enemyVisual.Modulate = new Color(1, 1, 1, 0.45f);
		}
		else
		{
			_enemyVisual.Modulate = Colors.White;
			ApplyPatrolVisual();
		}
	}

	private void ConfigureBase(
		Vector2 startPosition,
		Vector2 endPosition,
		float movementSpeed,
		float detectionRadius)
	{
		_startPosition = startPosition;
		_endPosition = endPosition;
		_movementSpeed = Mathf.Max(1f, movementSpeed);
		_detectionRadius = Mathf.Max(20f, detectionRadius);
		GlobalPosition = _startPosition;
		_currentTarget = _endPosition;
		_movingTowardsEnd = true;
		_isConfigured = true;
	}

	private void MoveAlongPatrol(float delta)
	{
		Vector2 previousPosition = GlobalPosition;
		GlobalPosition = GlobalPosition.MoveToward(_currentTarget, _movementSpeed * delta);
		Vector2 movementDirection = GlobalPosition - previousPosition;

		if (movementDirection.LengthSquared() > 0.001f)
		{
			Rotation = movementDirection.Angle();
		}

		if (GlobalPosition.DistanceTo(_currentTarget) > MinimumDistanceToTarget)
		{
			return;
		}

		_movingTowardsEnd = !_movingTowardsEnd;
		_currentTarget = _movingTowardsEnd ? _endPosition : _startPosition;
	}

	private void UpdateStealthDetection(float delta)
	{
		if (!_isDetectionEnabled || _trackedPlayer is null || !IsInstanceValid(_trackedPlayer))
		{
			return;
		}

		Vector2 toPlayer = _trackedPlayer.GlobalPosition - GlobalPosition;
		float distance = toPlayer.Length();
		bool insideCone = false;

		if (distance <= _detectionRadius && distance > 0.001f)
		{
			Vector2 facing = Vector2.Right.Rotated(GlobalRotation);
			float angle = Mathf.Abs(facing.AngleTo(toPlayer.Normalized()));
			insideCone =
				angle <= Mathf.DegToRad(_coneAngleDegrees * 0.5f) &&
				HasClearLineOfSight();
		}

		if (insideCone)
		{
			_suspicionProgress = Mathf.Min(_suspicionSeconds, _suspicionProgress + delta);

			if (_suspicionProgress >= _suspicionSeconds)
			{
				ApplyDetectedVisual();

				if (!_detectionEmitted)
				{
					_detectionEmitted = true;
					EmitSignal(SignalName.PlayerDetected, this);
				}
			}
			else
			{
				ApplySuspicionVisual();
			}
		}
		else
		{
			_suspicionProgress = Mathf.Max(0, _suspicionProgress - delta * 1.6f);
			_detectionEmitted = false;

			if (_suspicionProgress <= 0.01f)
			{
				ApplyPatrolVisual();
			}
			else
			{
				ApplySuspicionVisual();
			}
		}
	}

	private bool HasClearLineOfSight()
	{
		if (_trackedPlayer is null ||
			!IsInstanceValid(_trackedPlayer))
		{
			return false;
		}

		PhysicsDirectSpaceState2D spaceState =
			GetWorld2D().DirectSpaceState;

		PhysicsRayQueryParameters2D query =
			PhysicsRayQueryParameters2D.Create(
				GlobalPosition,
				_trackedPlayer.GlobalPosition);

		query.CollideWithAreas = false;
		query.CollideWithBodies = true;
		query.CollisionMask = 1;

		Godot.Collections.Dictionary result =
			spaceState.IntersectRay(query);

		if (result.Count == 0)
		{
			return true;
		}

		GodotObject? collider =
			result["collider"].AsGodotObject();

		return collider == _trackedPlayer;
	}

	private void ApplyDetectionVisual()
	{
		if (_stealthMode)
		{
			_detectionVisual.Polygon = CreateConePolygon(
				_detectionRadius,
				_coneAngleDegrees,
				16);
		}
		else
		{
			_detectionVisual.Polygon = CreateCirclePolygon(_detectionRadius, 24);
		}

		if (_detectionShape.Shape is CircleShape2D circleShape)
		{
			CircleShape2D localShape = circleShape.Duplicate() as CircleShape2D ?? new CircleShape2D();
			localShape.Radius = _detectionRadius;
			_detectionShape.Shape = localShape;
		}
	}

	private void ApplyPatrolVisual()
	{
		_detectionVisual.Color = new Color("#c66f7838");
		_enemyVisual.Color = new Color("#8f3f4b");
		_stateLabel.Text = string.Empty;
	}

	private void ApplySuspicionVisual()
	{
		_detectionVisual.Color = new Color("#e2ba5f68");
		_enemyVisual.Color = new Color("#b88c3f");
		_stateLabel.Text = "?";
	}

	private void ApplyDetectedVisual()
	{
		_detectionVisual.Color = new Color("#e2616f80");
		_enemyVisual.Color = new Color("#c84152");
		_stateLabel.Text = "!";
	}

	private void OnAreaEntered(Area2D area)
	{
		if (_stealthMode || !_isDetectionEnabled || area.Name != "DetectionHitbox")
		{
			return;
		}

		PlayerController? player = area.GetParentOrNull<PlayerController>();
		if (player is null)
		{
			return;
		}

		EmitSignal(SignalName.PlayerDetected, this);
	}

	private static Vector2[] CreateConePolygon(float radius, float angleDegrees, int segments)
	{
		Vector2[] points = new Vector2[segments + 2];
		points[0] = Vector2.Zero;
		float halfAngle = Mathf.DegToRad(angleDegrees * 0.5f);

		for (int index = 0; index <= segments; index++)
		{
			float t = index / (float)segments;
			float angle = Mathf.Lerp(-halfAngle, halfAngle, t);
			points[index + 1] = Vector2.Right.Rotated(angle) * radius;
		}

		return points;
	}

	private static Vector2[] CreateCirclePolygon(float radius, int pointCount)
	{
		Vector2[] points = new Vector2[pointCount];
		for (int index = 0; index < pointCount; index++)
		{
			float angle = Mathf.Tau * index / pointCount;
			points[index] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
		}
		return points;
	}
}
