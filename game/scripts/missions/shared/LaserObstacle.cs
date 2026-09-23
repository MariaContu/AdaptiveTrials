using AdaptiveTrials.Game.Player;
using Godot;

namespace AdaptiveTrials.Game.Missions.Shared;

/// <summary>
/// Laser temporizado utilizado como obstáculo na missão Chegar ao Destino.
/// Alterna entre seguro, aviso e ativo; somente o estado ativo registra falha.
/// </summary>
public partial class LaserObstacle : Node2D
{
    [Signal]
    public delegate void PlayerHitEventHandler();

    private const string PlayerDetectionAreaName = "DetectionHitbox";

    private Line2D _beam = null!;
    private Area2D _damageArea = null!;
    private CollisionShape2D _collision = null!;

    private float _safeSeconds = 2.2f;
    private float _warningSeconds = 0.8f;
    private float _activeSeconds = 1.2f;
    private float _phaseOffset;
    private float _elapsed;
    private float _length = 220.0f;

    private bool _canTrigger = true;
    private LaserState _state = LaserState.Safe;

    public override void _Ready()
    {
        _beam = GetNode<Line2D>("Beam");
        _damageArea = GetNode<Area2D>("DamageArea");
        _collision = GetNode<CollisionShape2D>("DamageArea/CollisionShape2D");

        _damageArea.AreaEntered += OnAreaEntered;
        ApplyGeometry();
        ApplyStateVisual();
    }

    public override void _Process(double delta)
    {
        _elapsed += (float)delta;
        UpdateTimedState();
    }

    public void Configure(
        float length,
        float safeSeconds,
        float warningSeconds,
        float activeSeconds,
        float phaseOffsetSeconds)
    {
        _length = Mathf.Max(80.0f, length);
        _safeSeconds = Mathf.Max(0.35f, safeSeconds);
        _warningSeconds = Mathf.Max(0.20f, warningSeconds);
        _activeSeconds = Mathf.Max(0.35f, activeSeconds);
        _phaseOffset = Mathf.Max(0.0f, phaseOffsetSeconds);
        _elapsed = 0.0f;

        if (IsNodeReady())
        {
            ApplyGeometry();
            UpdateTimedState(force: true);
        }
    }

    public void SetEnabledState(bool enabled)
    {
        SetProcess(enabled);
        Visible = enabled;

        if (IsInstanceValid(_damageArea))
        {
            _damageArea.SetDeferred(
                Area2D.PropertyName.Monitoring,
                enabled && _state == LaserState.Active);
        }
    }

    private void ApplyGeometry()
    {
        float half = _length * 0.5f;

        _beam.Points = new Vector2[]
        {
            new(-half, 0),
            new(half, 0)
        };

        RectangleShape2D shape =
            _collision.Shape as RectangleShape2D ?? new RectangleShape2D();

        shape = shape.Duplicate() as RectangleShape2D ?? new RectangleShape2D();
        shape.Size = new Vector2(_length, 18.0f);
        _collision.Shape = shape;
    }

    private void UpdateTimedState(bool force = false)
    {
        float cycle =
            _safeSeconds + _warningSeconds + _activeSeconds;

        float position =
            (_elapsed + _phaseOffset) % cycle;

        LaserState nextState =
            position < _safeSeconds
                ? LaserState.Safe
                : position < _safeSeconds + _warningSeconds
                    ? LaserState.Warning
                    : LaserState.Active;

        if (!force && nextState == _state)
        {
            return;
        }

        _state = nextState;
        ApplyStateVisual();
    }

    private void ApplyStateVisual()
    {
        if (!IsInstanceValid(_beam) ||
            !IsInstanceValid(_damageArea))
        {
            return;
        }

        switch (_state)
        {
            case LaserState.Safe:
                _beam.DefaultColor = new Color("#65596f66");
                _beam.Width = 3.0f;
                _damageArea.SetDeferred(Area2D.PropertyName.Monitoring, false);
                break;

            case LaserState.Warning:
                _beam.DefaultColor = new Color("#e0b45fcf");
                _beam.Width = 7.0f;
                _damageArea.SetDeferred(Area2D.PropertyName.Monitoring, false);
                break;

            default:
                _beam.DefaultColor = new Color("#e35f72ff");
                _beam.Width = 13.0f;
                _damageArea.SetDeferred(Area2D.PropertyName.Monitoring, true);
                CallDeferred(MethodName.TryTriggerOverlappingPlayer);
                break;
        }
    }

    private void OnAreaEntered(Area2D area)
    {
        if (_state != LaserState.Active ||
            !_canTrigger ||
            !IsPlayerDetectionArea(area))
        {
            return;
        }

        _canTrigger = false;
        EmitSignal(SignalName.PlayerHit);

        GetTree()
            .CreateTimer(0.75)
            .Timeout += () => _canTrigger = true;
    }

    private void TryTriggerOverlappingPlayer()
    {
        if (_state != LaserState.Active ||
            !IsInstanceValid(_damageArea))
        {
            return;
        }

        foreach (Area2D area in _damageArea.GetOverlappingAreas())
        {
            if (!IsPlayerDetectionArea(area))
            {
                continue;
            }

            OnAreaEntered(area);
            break;
        }
    }

    private static bool IsPlayerDetectionArea(Area2D area)
    {
        return area.Name == PlayerDetectionAreaName &&
            area.GetParentOrNull<PlayerController>() is not null;
    }

    private enum LaserState
    {
        Safe,
        Warning,
        Active
    }
}
