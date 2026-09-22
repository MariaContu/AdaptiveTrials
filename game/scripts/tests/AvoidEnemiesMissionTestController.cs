using AdaptiveTrials.Game.Missions.Shared;
using AdaptiveTrials.Game.Player;
using Godot;

namespace AdaptiveTrials.Game.Tests;

/// <summary>
/// Teste isolado de furtividade com cone, suspeita, detecção e cobertura real.
/// </summary>
public partial class AvoidEnemiesMissionTestController : Node
{
    private PlayerController _player = null!;
    private PatrolEnemy _patrolEnemy = null!;
    private CheckpointArea[] _safePoints = null!;
    private DestinationArea _destination = null!;
    private Label _statusLabel = null!;

    private int _reachedSafePoints;
    private int _detections;
    private Vector2 _lastSafePosition;
    private bool _finished;

    public override void _Ready()
    {
        _player = GetNode<PlayerController>("../Player");
        _patrolEnemy = GetNode<PatrolEnemy>("../PatrolEnemy");
        _safePoints = new[]
        {
            GetNode<CheckpointArea>("../SafePoint01"),
            GetNode<CheckpointArea>("../SafePoint02")
        };
        _destination = GetNode<DestinationArea>("../Destination");
        _statusLabel = GetNode<Label>("../Interface/StatusLabel");
        _lastSafePosition = _player.GlobalPosition;

        for (int index = 0; index < _safePoints.Length; index++)
        {
            _safePoints[index].ConfigureIndex(index + 1);
            _safePoints[index].SetEnabledState(index == 0);
            _safePoints[index].CheckpointReached += OnSafePointReached;
        }

        _destination.SetEnabledState(false);
        _destination.DestinationReached += OnDestinationReached;
        _patrolEnemy.PlayerDetected += OnPlayerDetected;

        _patrolEnemy.ConfigureStealth(
            new Vector2(520, 300),
            new Vector2(860, 300),
            85.0f,
            230.0f,
            70.0f,
            0.9f,
            _player);

        UpdateStatus();
    }

    private void OnSafePointReached(CheckpointArea safePoint)
    {
        if (_finished)
        {
            return;
        }

        _reachedSafePoints++;
        _lastSafePosition = safePoint.GlobalPosition;

        if (_reachedSafePoints < _safePoints.Length)
        {
            _safePoints[_reachedSafePoints].SetEnabledState(true);
        }
        else
        {
            _destination.SetEnabledState(true);
        }

        UpdateStatus();
    }

    private void OnPlayerDetected(PatrolEnemy enemy)
    {
        if (_finished)
        {
            return;
        }

        _detections++;
        _player.GlobalPosition = _lastSafePosition;
        _player.Velocity = Vector2.Zero;
        enemy.SetDetectionEnabled(false);

        GetTree().CreateTimer(0.65).Timeout += () =>
        {
            if (IsInstanceValid(enemy) && !_finished)
            {
                enemy.SetDetectionEnabled(true);
            }
        };

        _statusLabel.Text =
            $"DETECTADO {_detections}x — use as paredes para bloquear a visão.";
    }

    private void OnDestinationReached()
    {
        if (_finished || _reachedSafePoints < _safePoints.Length)
        {
            return;
        }

        _finished = true;
        _patrolEnemy.SetDetectionEnabled(false);
        _statusLabel.Text =
            $"TESTE CONCLUÍDO — furtividade e cobertura funcionando • detecções: {_detections}.";
    }

    private void UpdateStatus()
    {
        _statusLabel.Text =
            _reachedSafePoints >= _safePoints.Length
                ? $"PONTOS SEGUROS 2/2 • saída liberada • detecções: {_detections}."
                : $"PONTOS SEGUROS {_reachedSafePoints}/2 • esconda-se atrás das paredes para quebrar a linha de visão.";
    }
}
