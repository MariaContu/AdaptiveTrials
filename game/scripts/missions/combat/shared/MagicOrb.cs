using System.Collections.Generic;
using Godot;

namespace AdaptiveTrials.Game.Missions.Combat.Shared;

/// <summary>
/// Projétil mágico direcional utilizado pelo jogador nas missões de combate.
/// </summary>
public partial class MagicOrb : Area2D
{
	[Signal]
	public delegate void TargetHitEventHandler(Node target, int damage);

	[Export]
	public float Speed { get; set; } = 520.0f;

	[Export]
	public float LifetimeSeconds { get; set; } = 1.4f;

	private readonly HashSet<ulong> _damagedTargets = new();

	private Vector2 _direction = Vector2.Right;
	private int _damage = 1;
	private Node? _source;
	private float _remainingLifetime;
	private bool _isActive;

	public override void _Ready()
	{
		AreaEntered += OnAreaEntered;
		BodyEntered += OnBodyEntered;

		_remainingLifetime = LifetimeSeconds;
		_isActive = true;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_isActive)
		{
			return;
		}

		GlobalPosition +=
			_direction * Speed * (float)delta;

		_remainingLifetime -= (float)delta;

		if (_remainingLifetime <= 0.0f)
		{
			DestroyOrb();
		}
	}

	/// <summary>
	/// Configura o projétil antes de ele iniciar seu deslocamento.
	/// </summary>
	public void Initialize(
		Vector2 direction,
		int damage,
		Node source,
		float speed,
		float lifetimeSeconds)
	{
		_direction = direction.Normalized();
		_damage = Mathf.Max(1, damage);
		_source = source;
		Speed = Mathf.Max(1.0f, speed);
		LifetimeSeconds = Mathf.Max(0.1f, lifetimeSeconds);
		_remainingLifetime = LifetimeSeconds;

		Rotation = _direction.Angle();
	}

	private void OnAreaEntered(Area2D area)
	{
		TryDamage(area);
	}

	private void OnBodyEntered(Node2D body)
	{
		TryDamage(body);
	}

	private void TryDamage(Node collisionNode)
	{
		if (!_isActive || collisionNode == _source)
		{
			return;
		}

		IDamageable? damageable = FindDamageable(collisionNode);

		if (damageable is null || !damageable.CanReceiveDamage)
		{
			return;
		}

		Node targetNode = damageable as Node ?? collisionNode;
		ulong targetId = targetNode.GetInstanceId();

		if (!_damagedTargets.Add(targetId))
		{
			return;
		}

		damageable.ReceiveDamage(
			_damage,
			_source ?? this);

		EmitSignal(
			SignalName.TargetHit,
			targetNode,
			_damage);

		DestroyOrb();
	}

	private static IDamageable? FindDamageable(Node node)
	{
		Node? current = node;

		while (current is not null)
		{
			if (current is IDamageable damageable)
			{
				return damageable;
			}

			current = current.GetParent();
		}

		return null;
	}

	private void DestroyOrb()
	{
		if (!_isActive)
		{
			return;
		}

		_isActive = false;
		Monitoring = false;
		SetPhysicsProcess(false);
		QueueFree();
	}
}
