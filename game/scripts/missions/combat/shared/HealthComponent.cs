using Godot;

namespace AdaptiveTrials.Game.Missions.Combat.Shared;

/// <summary>
/// Mantém vida, cura e invulnerabilidade de qualquer entidade de combate.
/// </summary>
public partial class HealthComponent : Node
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
    public delegate void DepletedEventHandler(Node source);

    [Signal]
    public delegate void InvulnerabilityChangedEventHandler(bool isInvulnerable);

    [Export]
    public int MaximumHealth { get; set; } = 5;

    [Export]
    public float InvulnerabilitySeconds { get; set; } = 0.7f;

    [Export]
    public NodePath VisualTargetPath { get; set; } = new("../AnimatedSprite2D");

    [Export]
    public float FlashIntervalSeconds { get; set; } = 0.09f;

    public int CurrentHealth { get; private set; }

    public bool IsDepleted => CurrentHealth <= 0;

    public bool IsInvulnerable => _invulnerabilityRemaining > 0.0f;

    public bool CanReceiveDamage => _damageEnabled && !IsDepleted && !IsInvulnerable;

    private CanvasItem? _visualTarget;
    private float _invulnerabilityRemaining;
    private float _flashRemaining;
    private bool _damageEnabled = true;
    private bool _visualHiddenByFlash;

    public override void _Ready()
    {
        MaximumHealth = Mathf.Max(1, MaximumHealth);
        CurrentHealth = MaximumHealth;

        if (!VisualTargetPath.IsEmpty)
        {
            _visualTarget = GetNodeOrNull<CanvasItem>(VisualTargetPath);
        }

        EmitHealthChanged();
    }

    public override void _Process(double delta)
    {
        if (!IsInvulnerable)
        {
            return;
        }

        _invulnerabilityRemaining = Mathf.Max(
            0.0f,
            _invulnerabilityRemaining - (float)delta);

        _flashRemaining -= (float)delta;

        if (_flashRemaining <= 0.0f)
        {
            _flashRemaining = Mathf.Max(0.03f, FlashIntervalSeconds);
            SetFlashVisible(_visualHiddenByFlash);
            _visualHiddenByFlash = !_visualHiddenByFlash;
        }

        if (_invulnerabilityRemaining > 0.0f)
        {
            return;
        }

        EndInvulnerability();
    }

    public bool TryReceiveDamage(int damage, Node source)
    {
        if (damage <= 0 || !CanReceiveDamage)
        {
            return false;
        }

        CurrentHealth = Mathf.Max(0, CurrentHealth - damage);

        EmitSignal(SignalName.DamageReceived, damage, source);
        EmitHealthChanged();

        if (CurrentHealth <= 0)
        {
            EndInvulnerability();
            EmitSignal(SignalName.Depleted, source);
            return true;
        }

        BeginInvulnerability();
        return true;
    }

    public void RestoreFullHealth()
    {
        CurrentHealth = Mathf.Max(1, MaximumHealth);
        EndInvulnerability();
        EmitHealthChanged();
    }

    public void Heal(int amount)
    {
        if (amount <= 0 || IsDepleted)
        {
            return;
        }

        int updatedHealth = Mathf.Min(
            MaximumHealth,
            CurrentHealth + amount);

        if (updatedHealth == CurrentHealth)
        {
            return;
        }

        CurrentHealth = updatedHealth;
        EmitHealthChanged();
    }

    public void SetDamageEnabled(bool enabled)
    {
        _damageEnabled = enabled;

        if (!enabled)
        {
            EndInvulnerability();
        }
    }

    private void BeginInvulnerability()
    {
        _invulnerabilityRemaining = Mathf.Max(
            0.0f,
            InvulnerabilitySeconds);

        if (_invulnerabilityRemaining <= 0.0f)
        {
            return;
        }

        _flashRemaining = 0.0f;
        EmitSignal(SignalName.InvulnerabilityChanged, true);
    }

    private void EndInvulnerability()
    {
        bool wasInvulnerable = IsInvulnerable;

        _invulnerabilityRemaining = 0.0f;
        _flashRemaining = 0.0f;
        _visualHiddenByFlash = false;
        SetFlashVisible(true);

        if (wasInvulnerable)
        {
            EmitSignal(SignalName.InvulnerabilityChanged, false);
        }
    }

    private void SetFlashVisible(bool visible)
    {
        if (_visualTarget is not null)
        {
            _visualTarget.Visible = visible;
        }
    }

    private void EmitHealthChanged()
    {
        EmitSignal(
            SignalName.HealthChanged,
            CurrentHealth,
            MaximumHealth);
    }
}
