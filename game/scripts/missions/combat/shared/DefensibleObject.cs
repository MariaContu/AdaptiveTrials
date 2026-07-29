using Godot;

namespace AdaptiveTrials.Game.Missions.Combat.Shared;

/// <summary>
/// Objeto reutilizável que pode ser protegido durante missões de combate.
/// </summary>
public partial class DefensibleObject : Area2D, IDamageable
{
    [Signal]
    public delegate void HealthChangedEventHandler(
        int currentHealth,
        int maximumHealth);

    [Signal]
    public delegate void DamageReceivedEventHandler(
        int damage,
        Node source);

    [Signal]
    public delegate void DestroyedEventHandler(Node source);

    [ExportGroup("Health")]
    [Export]
    public int MaximumHealth { get; set; } = 10;

    [ExportGroup("Visual Feedback")]
    [Export]
    public Color DamageFlashColor { get; set; } =
        new(1.0f, 0.38f, 0.48f, 1.0f);

    [Export]
    public float DamageFlashSeconds { get; set; } = 0.12f;

    [Export]
    public float DamagePulseScale { get; set; } = 1.08f;

    [Export]
    public Color DestroyedModulate { get; set; } =
        new(0.38f, 0.34f, 0.46f, 0.65f);

    public int CurrentHealth => _healthComponent.CurrentHealth;

    public bool IsDestroyed { get; private set; }

    public bool CanReceiveDamage =>
        !IsDestroyed && _healthComponent.CanReceiveDamage;

    private HealthComponent _healthComponent = null!;
    private CollisionShape2D _collisionShape = null!;
    private Node2D _visualRoot = null!;
    private Color _initialModulate = Colors.White;
    private Vector2 _initialScale = Vector2.One;
    private Tween? _damageTween;

    public override void _Ready()
    {
        _healthComponent = GetNode<HealthComponent>("HealthComponent");
        _collisionShape = GetNode<CollisionShape2D>("CollisionShape2D");
        _visualRoot = GetNode<Node2D>("VisualRoot");
        _initialModulate = _visualRoot.Modulate;
        _initialScale = _visualRoot.Scale;

        _healthComponent.MaximumHealth = Mathf.Max(1, MaximumHealth);
        _healthComponent.HealthChanged += OnHealthChanged;
        _healthComponent.DamageReceived += OnDamageReceived;
        _healthComponent.Depleted += OnDepleted;
        _healthComponent.RestoreFullHealth();
    }

    public void Configure(int maximumHealth)
    {
        MaximumHealth = Mathf.Max(1, maximumHealth);
        _healthComponent.MaximumHealth = MaximumHealth;
        Restore();
    }

    public void ReceiveDamage(int damage, Node source)
    {
        if (!CanReceiveDamage || damage <= 0)
        {
            return;
        }

        _healthComponent.TryReceiveDamage(damage, source);
    }

    public void Restore()
    {
        StopDamageFeedback();
        IsDestroyed = false;
        Monitorable = true;
        _collisionShape.Disabled = false;
        _visualRoot.Modulate = _initialModulate;
        _visualRoot.Scale = _initialScale;
        _healthComponent.SetDamageEnabled(true);
        _healthComponent.MaximumHealth = Mathf.Max(1, MaximumHealth);
        _healthComponent.RestoreFullHealth();
    }

    public void SetDamageEnabled(bool enabled)
    {
        _healthComponent.SetDamageEnabled(enabled);

        if (!IsDestroyed)
        {
            Monitorable = enabled;
        }
    }

    private void OnHealthChanged(int currentHealth, int maximumHealth)
    {
        EmitSignal(
            SignalName.HealthChanged,
            currentHealth,
            maximumHealth);
    }

    private void OnDamageReceived(int damage, Node source)
    {
        PlayDamageFeedback();

        GD.Print(
            $"{Name} recebeu {damage} de dano de {source.Name}. " +
            $"Vida: {CurrentHealth}/{_healthComponent.MaximumHealth}.");

        EmitSignal(
            SignalName.DamageReceived,
            damage,
            source);
    }

    private void PlayDamageFeedback()
    {
        if (IsDestroyed)
        {
            return;
        }

        StopDamageFeedback();

        float halfDuration = Mathf.Max(0.04f, DamageFlashSeconds);
        Vector2 pulseScale = _initialScale * Mathf.Max(1.0f, DamagePulseScale);

        _visualRoot.Modulate = DamageFlashColor;
        _visualRoot.Scale = pulseScale;

        _damageTween = CreateTween();
        _damageTween.SetParallel(true);
        _damageTween.TweenProperty(
            _visualRoot,
            "modulate",
            _initialModulate,
            halfDuration);
        _damageTween.TweenProperty(
            _visualRoot,
            "scale",
            _initialScale,
            halfDuration)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
    }

    private void StopDamageFeedback()
    {
        if (_damageTween is not null && _damageTween.IsValid())
        {
            _damageTween.Kill();
        }

        _damageTween = null;
    }

    private void OnDepleted(Node source)
    {
        if (IsDestroyed)
        {
            return;
        }

        StopDamageFeedback();
        IsDestroyed = true;
        Monitorable = false;
        _collisionShape.SetDeferred(
            CollisionShape2D.PropertyName.Disabled,
            true);
        _visualRoot.Scale = _initialScale;
        _visualRoot.Modulate = DestroyedModulate;
        _healthComponent.SetDamageEnabled(false);

        GD.Print($"{Name} foi destruído por {source.Name}.");
        EmitSignal(SignalName.Destroyed, source);
    }
}
