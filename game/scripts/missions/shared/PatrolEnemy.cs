using AdaptiveTrials.Game.Player;
using Godot;

namespace AdaptiveTrials.Game.Missions.Shared;

/// <summary>
/// Inimigo que patrulha entre dois pontos e detecta o jogador.
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

	private Area2D _detectionArea = null!;
	private CollisionShape2D _detectionShape = null!;

	private Vector2 _startPosition;
	private Vector2 _endPosition;
	private Vector2 _currentTarget;

	private float _movementSpeed = 80f;
	private float _detectionRadius = 90f;

	private bool _isConfigured;
	private bool _isDetectionEnabled = true;
	private bool _movingTowardsEnd = true;

	public override void _Ready()
	{
		_detectionVisual =
			GetNode<Polygon2D>(
				"DetectionVisual");

		_enemyVisual =
			GetNode<Polygon2D>(
				"EnemyVisual");

		_directionIndicator =
			GetNode<Polygon2D>(
				"DirectionIndicator");

		_detectionArea =
			GetNode<Area2D>(
				"DetectionArea");

		_detectionShape =
			GetNode<CollisionShape2D>(
				"DetectionArea/CollisionShape2D");

		/*
		 * A detecção utiliza uma Area2D própria do jogador,
		 * separada de sua colisão física.
		 */
		_detectionArea.AreaEntered +=
			OnAreaEntered;

		ApplyDetectionRadius();
	}

	public override void _Process(double delta)
	{
		if (!_isConfigured)
		{
			return;
		}

		MoveAlongPatrol(
			(float)delta);
	}

	/// <summary>
	/// Define a rota, velocidade e alcance de detecção.
	/// </summary>
	public void Configure(
		Vector2 startPosition,
		Vector2 endPosition,
		float movementSpeed,
		float detectionRadius)
	{
		_startPosition =
			startPosition;

		_endPosition =
			endPosition;

		_movementSpeed =
			Mathf.Max(
				1f,
				movementSpeed);

		_detectionRadius =
			Mathf.Max(
				20f,
				detectionRadius);

		GlobalPosition =
			_startPosition;

		_currentTarget =
			_endPosition;

		_movingTowardsEnd =
			true;

		_isConfigured =
			true;

		if (IsNodeReady())
		{
			ApplyDetectionRadius();
		}
	}

	public void SetDetectionEnabled(
		bool enabled)
	{
		_isDetectionEnabled =
			enabled;

		if (IsInstanceValid(_detectionArea))
		{
			_detectionArea.SetDeferred(
				Area2D.PropertyName.Monitoring,
				enabled);
		}

		if (IsInstanceValid(_detectionVisual))
		{
			_detectionVisual.Visible =
				enabled;
		}

		if (IsInstanceValid(_enemyVisual))
		{
			_enemyVisual.Modulate =
				enabled
					? Colors.White
					: new Color(
						1,
						1,
						1,
						0.45f);
		}
	}

	private void MoveAlongPatrol(
		float delta)
	{
		Vector2 previousPosition =
			GlobalPosition;

		GlobalPosition =
			GlobalPosition.MoveToward(
				_currentTarget,
				_movementSpeed * delta);

		Vector2 movementDirection =
			GlobalPosition -
			previousPosition;

		if (movementDirection.LengthSquared() >
			0.001f)
		{
			Rotation =
				movementDirection.Angle();
		}

		if (GlobalPosition.DistanceTo(
				_currentTarget) >
			MinimumDistanceToTarget)
		{
			return;
		}

		_movingTowardsEnd =
			!_movingTowardsEnd;

		_currentTarget =
			_movingTowardsEnd
				? _endPosition
				: _startPosition;
	}

	private void ApplyDetectionRadius()
	{
		if (_detectionShape.Shape is
			CircleShape2D circleShape)
		{
			/*
			 * Duplica o recurso para evitar que inimigos
			 * compartilhem alterações na mesma Shape.
			 */
			CircleShape2D localShape =
				circleShape.Duplicate()
					as CircleShape2D ??
				new CircleShape2D();

			localShape.Radius =
				_detectionRadius;

			_detectionShape.Shape =
				localShape;
		}

		_detectionVisual.Polygon =
			CreateCirclePolygon(
				_detectionRadius,
				24);
	}

	private void OnAreaEntered(
		Area2D area)
	{
		if (!_isDetectionEnabled)
		{
			return;
		}

		if (area.Name !=
			"DetectionHitbox")
		{
			return;
		}

		PlayerController? player =
			area.GetParentOrNull<PlayerController>();

		if (player is null)
		{
			return;
		}

		EmitSignal(
			SignalName.PlayerDetected,
			this);
	}

	private static Vector2[] CreateCirclePolygon(
		float radius,
		int pointCount)
	{
		Vector2[] points =
			new Vector2[pointCount];

		for (int index = 0;
			 index < pointCount;
			 index++)
		{
			float angle =
				Mathf.Tau *
				index /
				pointCount;

			points[index] =
				new Vector2(
					Mathf.Cos(angle),
					Mathf.Sin(angle)) *
				radius;
		}

		return points;
	}
}
