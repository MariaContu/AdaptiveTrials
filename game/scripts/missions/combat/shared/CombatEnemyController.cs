using AdaptiveTrials.Game.Player;
using Godot;

namespace AdaptiveTrials.Game.Missions.Combat.Shared;

/// <summary>
/// Inimigo reutilizável com variantes corpo a corpo e à distância.
/// </summary>
public partial class CombatEnemyController : CharacterBody2D, IDamageable
{
    [Signal]
    public delegate void EnemyDefeatedEventHandler(CombatEnemyController enemy);

    [Signal]
    public delegate void HealthChangedEventHandler(
        int currentHealth,
        int maximumHealth);

    [Signal]
    public delegate void AttackStartedEventHandler(Node target);

    [Signal]
    public delegate void ProjectileCastEventHandler(MagicOrb projectile);

    [ExportGroup("Behavior")]
    [Export]
    public CombatEnemyAttackMode AttackMode { get; set; } =
        CombatEnemyAttackMode.Melee;

    [ExportGroup("Movement")]
    [Export]
    public float MovementSpeed { get; set; } = 75.0f;

    [Export]
    public float AttackDistance { get; set; } = 46.0f;

    [Export]
    public float StopDistance { get; set; } = 34.0f;

    [ExportGroup("Attack")]
    [Export]
    public int AttackDamage { get; set; } = 1;

    [Export]
    public float AttackCooldownSeconds { get; set; } = 1.1f;

    [Export]
    public float AttackActiveSeconds { get; set; } = 0.18f;

    [ExportGroup("Ranged Attack")]
    [Export]
    public PackedScene? EnemyOrbScene { get; set; }

    [Export]
    public float PreferredRangedDistance { get; set; } = 190.0f;

    [Export]
    public float MinimumRangedDistance { get; set; } = 115.0f;

    [Export]
    public float MaximumRangedDistance { get; set; } = 320.0f;

    [Export]
    public float EnemyOrbSpeed { get; set; } = 300.0f;

    [Export]
    public float EnemyOrbLifetimeSeconds { get; set; } = 2.0f;

    [Export]
    public float EnemyOrbSpawnDistance { get; set; } = 34.0f;

    [Export]
    public bool RangedMovementEnabled { get; set; } = true;

    [ExportGroup("Detection")]
    [Export]
    public bool ChaseOnlyAfterDetection { get; set; } = true;

    [Export]
    public bool DetectionCanOverrideTarget { get; set; } = true;

    private Polygon2D _visual = null!;
    private Area2D _detectionArea = null!;
    private DamageArea _attackArea = null!;
    private HealthComponent _healthComponent = null!;

    private Node2D? _target;
    private float _attackCooldownRemaining;
    private float _attackActiveRemaining;
    private bool _isDead;
    private bool _combatEnabled = true;

    public bool CanReceiveDamage =>
        !_isDead &&
        _combatEnabled &&
        _healthComponent.CanReceiveDamage;

    public int CurrentHealth => _healthComponent.CurrentHealth;

    public int MaximumHealth => _healthComponent.MaximumHealth;

    public override void _Ready()
    {
        _visual = GetNode<Polygon2D>("Visual");
        _detectionArea = GetNode<Area2D>("DetectionArea");
        _attackArea = GetNode<DamageArea>("AttackArea");
        _healthComponent = GetNode<HealthComponent>("HealthComponent");

        _detectionArea.AreaEntered += OnDetectionAreaEntered;
        _detectionArea.AreaExited += OnDetectionAreaExited;

        _healthComponent.HealthChanged += OnHealthChanged;
        _healthComponent.DamageReceived += OnDamageReceived;
        _healthComponent.Depleted += OnHealthDepleted;

        _attackArea.Damage = Mathf.Max(1, AttackDamage);
        _attackArea.RepeatWhileOverlapping = false;
        _attackArea.SetDamageEnabled(false);

        ApplyAttackModeAppearance();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_isDead || !_combatEnabled)
        {
            Velocity = Vector2.Zero;
            MoveAndSlide();
            return;
        }

        UpdateTimers((float)delta);

        if (!IsInstanceValid(_target))
        {
            _target = null;
            Velocity = Vector2.Zero;
            MoveAndSlide();
            return;
        }

        Vector2 toTarget = _target.GlobalPosition - GlobalPosition;

        if (toTarget.LengthSquared() <= 0.001f)
        {
            Velocity = Vector2.Zero;
            MoveAndSlide();
            return;
        }

        if (AttackMode == CombatEnemyAttackMode.Ranged)
        {
            ProcessRangedBehavior(toTarget);
        }
        else
        {
            ProcessMeleeBehavior(toTarget);
        }

        MoveAndSlide();
    }

    public void ReceiveDamage(int damage, Node source)
    {
        _healthComponent.TryReceiveDamage(damage, source);
    }

    public void Configure(
        int maximumHealth,
        float movementSpeed,
        int attackDamage,
        float attackCooldownSeconds)
    {
        MovementSpeed = Mathf.Max(0.0f, movementSpeed);
        AttackDamage = Mathf.Max(1, attackDamage);
        AttackCooldownSeconds = Mathf.Max(0.1f, attackCooldownSeconds);

        _healthComponent.MaximumHealth = Mathf.Max(1, maximumHealth);
        _healthComponent.RestoreFullHealth();
        _attackArea.Damage = AttackDamage;
    }

    public void SetAttackMode(CombatEnemyAttackMode attackMode)
    {
        AttackMode = attackMode;
        _attackArea.SetDamageEnabled(false);
        ApplyAttackModeAppearance();
    }

    public void SetCombatEnabled(bool enabled)
    {
        _combatEnabled = enabled;
        _healthComponent.SetDamageEnabled(enabled && !_isDead);
        _attackArea.SetDamageEnabled(false);

        if (!enabled)
        {
            Velocity = Vector2.Zero;
        }
    }

    public void SetTarget(Node2D? target)
    {
        if (_isDead)
        {
            return;
        }

        _target = target;
    }

    private void ProcessMeleeBehavior(Vector2 toTarget)
    {
        float distance = toTarget.Length();

        if (distance <= AttackDistance)
        {
            Velocity = Vector2.Zero;
            FaceTarget(toTarget);
            TryMeleeAttack();
            return;
        }

        if (distance > StopDistance)
        {
            Velocity = toTarget.Normalized() * MovementSpeed;
            FaceTarget(toTarget);
        }
        else
        {
            Velocity = Vector2.Zero;
        }
    }

    private void ProcessRangedBehavior(Vector2 toTarget)
    {
        float distance = toTarget.Length();
        Vector2 direction = toTarget.Normalized();

        FaceTarget(toTarget);

        if (!RangedMovementEnabled)
        {
            Velocity = Vector2.Zero;

            if (distance <= MaximumRangedDistance)
            {
                TryRangedAttack(direction);
            }

            return;
        }

        if (distance < MinimumRangedDistance)
        {
            Velocity = -direction * MovementSpeed;
            return;
        }

        if (distance > PreferredRangedDistance)
        {
            Velocity = direction * MovementSpeed;
        }
        else
        {
            Velocity = Vector2.Zero;
        }

        if (distance <= MaximumRangedDistance)
        {
            TryRangedAttack(direction);
        }
    }

    private void UpdateTimers(float delta)
    {
        _attackCooldownRemaining = Mathf.Max(
            0.0f,
            _attackCooldownRemaining - delta);

        if (_attackActiveRemaining <= 0.0f)
        {
            return;
        }

        _attackActiveRemaining = Mathf.Max(
            0.0f,
            _attackActiveRemaining - delta);

        if (_attackActiveRemaining <= 0.0f)
        {
            _attackArea.SetDamageEnabled(false);
        }
    }

    private void TryMeleeAttack()
    {
        if (_attackCooldownRemaining > 0.0f ||
            _attackActiveRemaining > 0.0f ||
            _target is null)
        {
            return;
        }

        _attackArea.Damage = Mathf.Max(1, AttackDamage);
        _attackArea.SetDamageEnabled(true);
        _attackActiveRemaining = Mathf.Max(0.05f, AttackActiveSeconds);
        _attackCooldownRemaining = Mathf.Max(0.1f, AttackCooldownSeconds);

        EmitSignal(SignalName.AttackStarted, _target);
        GD.Print($"{Name} iniciou um ataque corpo a corpo contra {_target.Name}.");
    }

    private void TryRangedAttack(Vector2 direction)
    {
        if (_attackCooldownRemaining > 0.0f || _target is null)
        {
            return;
        }

        EnemyOrbScene ??= GD.Load<PackedScene>(
            "res://scenes/missions/combat/shared/EnemyMagicOrb.tscn");

        if (EnemyOrbScene is null)
        {
            GD.PushError($"A cena da orbe inimiga não foi configurada em {Name}.");
            _attackCooldownRemaining = Mathf.Max(0.1f, AttackCooldownSeconds);
            return;
        }

        Node? currentScene = GetTree().CurrentScene;

        if (currentScene is null)
        {
            GD.PushError("Não foi possível localizar a cena atual para criar a orbe inimiga.");
            return;
        }

        MagicOrb? orb = EnemyOrbScene.Instantiate<MagicOrb>();

        if (orb is null)
        {
            GD.PushError("A cena configurada não possui MagicOrb no nó raiz.");
            return;
        }

        orb.Initialize(
            direction,
            AttackDamage,
            this,
            EnemyOrbSpeed,
            EnemyOrbLifetimeSeconds);

        currentScene.AddChild(orb);
        orb.GlobalPosition =
            GlobalPosition + direction * EnemyOrbSpawnDistance;

        _attackCooldownRemaining = Mathf.Max(0.1f, AttackCooldownSeconds);

        EmitSignal(SignalName.AttackStarted, _target);
        EmitSignal(SignalName.ProjectileCast, orb);

        GD.Print($"{Name} lançou uma orbe contra {_target.Name}.");
    }

    private void FaceTarget(Vector2 direction)
    {
        if (Mathf.Abs(direction.X) < 0.01f)
        {
            return;
        }

        Vector2 scale = _visual.Scale;
        scale.X = Mathf.Abs(scale.X) * (direction.X < 0.0f ? -1.0f : 1.0f);
        _visual.Scale = scale;
    }

    private void ApplyAttackModeAppearance()
    {
        if (_visual is null)
        {
            return;
        }

        _visual.Color = AttackMode == CombatEnemyAttackMode.Ranged
            ? new Color(0.72f, 0.18f, 0.42f, 1.0f)
            : new Color(0.42f, 0.16f, 0.56f, 1.0f);
    }

    private void OnDetectionAreaEntered(Area2D area)
    {
        if (!DetectionCanOverrideTarget)
        {
            return;
        }

        PlayerController? player = FindPlayer(area);

        if (player is null)
        {
            return;
        }

        _target = player;
        GD.Print($"{Name} detectou o jogador.");
    }

    private void OnDetectionAreaExited(Area2D area)
    {
        if (!DetectionCanOverrideTarget || !ChaseOnlyAfterDetection)
        {
            return;
        }

        PlayerController? player = FindPlayer(area);

        if (player is null || player != _target)
        {
            return;
        }

        _target = null;
        Velocity = Vector2.Zero;
        GD.Print($"{Name} perdeu o jogador de vista.");
    }

    private void OnHealthChanged(int currentHealth, int maximumHealth)
    {
        EmitSignal(
            SignalName.HealthChanged,
            currentHealth,
            maximumHealth);

        GD.Print($"{Name}: {currentHealth}/{maximumHealth} de vida.");
    }

    private void OnDamageReceived(int damage, Node source)
    {
        GD.Print($"{Name} recebeu {damage} de dano de {source.Name}.");
    }

    private void OnHealthDepleted(Node source)
    {
        if (_isDead)
        {
            return;
        }

        _isDead = true;
        _target = null;
        Velocity = Vector2.Zero;

        _attackArea.SetDamageEnabled(false);
        _detectionArea.Monitoring = false;
        SetPhysicsProcess(false);

        CollisionLayer = 0;
        CollisionMask = 0;
        GetNode<Area2D>("Hurtbox").Monitorable = false;

        Modulate = new Color(0.45f, 0.45f, 0.55f, 0.65f);

        EmitSignal(SignalName.EnemyDefeated, this);
        GD.Print($"{Name} foi derrotado por {source.Name}.");
    }

    private static PlayerController? FindPlayer(Node node)
    {
        Node? current = node;

        while (current is not null)
        {
            if (current is PlayerController player)
            {
                return player;
            }

            current = current.GetParent();
        }

        return null;
    }
}
