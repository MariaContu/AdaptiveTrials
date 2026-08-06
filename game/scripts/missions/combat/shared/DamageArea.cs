using System.Collections.Generic;
using Godot;

namespace AdaptiveTrials.Game.Missions.Combat.Shared;

/// <summary>
/// Área reutilizável que aplica dano ao entrar e em intervalos configuráveis.
/// </summary>
public partial class DamageArea : Area2D
{
    [Signal]
    public delegate void DamageAppliedEventHandler(Node target, int damage);

    [Export]
    public int Damage { get; set; } = 1;

    [Export]
    public float RepeatDelaySeconds { get; set; } = 0.8f;

    [Export]
    public bool RepeatWhileOverlapping { get; set; } = true;

    private readonly Dictionary<ulong, TrackedTarget> _targets = new();
    private bool _damageEnabled = true;

    public override void _Ready()
    {
        AreaEntered += OnAreaEntered;
        AreaExited += OnAreaExited;
        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_damageEnabled || !RepeatWhileOverlapping)
        {
            return;
        }

        List<ulong> invalidTargets = new();
        List<ulong> targetIds = new(_targets.Keys);

        foreach (ulong targetId in targetIds)
        {
            TrackedTarget tracked = _targets[targetId];

            if (!IsInstanceValid(tracked.CollisionNode))
            {
                invalidTargets.Add(targetId);
                continue;
            }

            tracked.RemainingDelay -= (float)delta;

            if (tracked.RemainingDelay <= 0.0f)
            {
                TryApplyDamage(tracked.CollisionNode);
                tracked.RemainingDelay = Mathf.Max(0.05f, RepeatDelaySeconds);
            }

            _targets[targetId] = tracked;
        }

        foreach (ulong targetId in invalidTargets)
        {
            _targets.Remove(targetId);
        }
    }

    public void SetDamageEnabled(bool enabled)
    {
        _damageEnabled = enabled;
        Monitoring = enabled;

        if (!enabled)
        {
            _targets.Clear();
        }
    }

    private void OnAreaEntered(Area2D area)
    {
        RegisterTarget(area);
    }

    private void OnAreaExited(Area2D area)
    {
        UnregisterTarget(area);
    }

    private void OnBodyEntered(Node2D body)
    {
        RegisterTarget(body);
    }

    private void OnBodyExited(Node2D body)
    {
        UnregisterTarget(body);
    }

    private void RegisterTarget(Node collisionNode)
    {
        if (!_damageEnabled)
        {
            return;
        }

        IDamageable? damageable = FindDamageable(collisionNode);

        if (damageable is null)
        {
            return;
        }

        Node targetNode = damageable as Node ?? collisionNode;
        ulong targetId = targetNode.GetInstanceId();

        if (_targets.ContainsKey(targetId))
        {
            return;
        }

        _targets[targetId] = new TrackedTarget(
            collisionNode,
            Mathf.Max(0.05f, RepeatDelaySeconds));

        TryApplyDamage(collisionNode);
    }

    private void UnregisterTarget(Node collisionNode)
    {
        IDamageable? damageable = FindDamageable(collisionNode);

        if (damageable is null)
        {
            return;
        }

        Node targetNode = damageable as Node ?? collisionNode;
        _targets.Remove(targetNode.GetInstanceId());
    }

    private void TryApplyDamage(Node collisionNode)
    {
        if (!_damageEnabled)
        {
            return;
        }

        IDamageable? damageable = FindDamageable(collisionNode);

        if (damageable is null || !damageable.CanReceiveDamage)
        {
            return;
        }

        damageable.ReceiveDamage(Mathf.Max(1, Damage), this);

        Node targetNode = damageable as Node ?? collisionNode;
        EmitSignal(SignalName.DamageApplied, targetNode, Mathf.Max(1, Damage));
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

    private struct TrackedTarget
    {
        public TrackedTarget(Node collisionNode, float remainingDelay)
        {
            CollisionNode = collisionNode;
            RemainingDelay = remainingDelay;
        }

        public Node CollisionNode { get; }
        public float RemainingDelay { get; set; }
    }
}
