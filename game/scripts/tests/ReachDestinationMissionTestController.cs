using AdaptiveTrials.Game.Missions.Shared;
using AdaptiveTrials.Game.Player;
using Godot;

namespace AdaptiveTrials.Game.Tests;

/// <summary>
/// Teste isolado da rota com checkpoints, área energizada e lasers temporizados.
/// </summary>
public partial class ReachDestinationMissionTestController : Node
{
    private PlayerController _player = null!;
    private CheckpointArea[] _checkpoints = null!;
    private HazardArea _hazard = null!;
    private LaserObstacle[] _lasers = null!;
    private DestinationArea _destination = null!;
    private Label _statusLabel = null!;

    private int _reachedCheckpoints;
    private Vector2 _lastSafePosition;
    private bool _finished;

    public override void _Ready()
    {
        _player = GetNode<PlayerController>("../Player");
        _checkpoints = new[]
        {
            GetNode<CheckpointArea>("../Checkpoint01"),
            GetNode<CheckpointArea>("../Checkpoint02"),
            GetNode<CheckpointArea>("../Checkpoint03")
        };
        _hazard = GetNode<HazardArea>("../Hazard");
        _lasers = new[]
        {
            GetNode<LaserObstacle>("../Laser01"),
            GetNode<LaserObstacle>("../Laser02")
        };
        _destination = GetNode<DestinationArea>("../Destination");
        _statusLabel = GetNode<Label>("../Interface/StatusLabel");

        _lastSafePosition = _player.GlobalPosition;

        for (int index = 0; index < _checkpoints.Length; index++)
        {
            _checkpoints[index].ConfigureIndex(index + 1);
            _checkpoints[index].SetEnabledState(index == 0);
            _checkpoints[index].CheckpointReached += OnCheckpointReached;
        }

        _hazard.PlayerHit += OnPlayerHitObstacle;
        _hazard.ConfigureTimed(2.0f, 0.8f, 1.25f, 0.3f);

        for (int index = 0; index < _lasers.Length; index++)
        {
            _lasers[index].PlayerHit += OnPlayerHitObstacle;
            _lasers[index].Configure(250.0f, 2.2f, 0.75f, 1.25f, index * 0.9f);
        }

        _destination.DestinationReached += OnDestinationReached;
        _destination.SetEnabledState(false);
        UpdateStatus();
    }

    private void OnCheckpointReached(CheckpointArea checkpoint)
    {
        if (_finished)
        {
            return;
        }

        _reachedCheckpoints++;
        _lastSafePosition = checkpoint.GlobalPosition;

        if (_reachedCheckpoints < _checkpoints.Length)
        {
            _checkpoints[_reachedCheckpoints].SetEnabledState(true);
        }
        else
        {
            _destination.SetEnabledState(true);
        }

        UpdateStatus();
    }

    private void OnPlayerHitObstacle()
    {
        if (_finished)
        {
            return;
        }

        _player.GlobalPosition = _lastSafePosition;
        _player.Velocity = Vector2.Zero;
        _statusLabel.Text = "OBSTÁCULO ATINGIDO — retorno ao último checkpoint.";
    }

    private void OnDestinationReached()
    {
        if (_finished || _reachedCheckpoints < _checkpoints.Length)
        {
            return;
        }

        _finished = true;
        _statusLabel.Text = "TESTE CONCLUÍDO — checkpoints e obstáculos temporizados funcionando.";
    }

    private void UpdateStatus()
    {
        _statusLabel.Text =
            _reachedCheckpoints >= _checkpoints.Length
                ? "CHECKPOINTS 3/3 • destino liberado."
                : $"CHECKPOINTS {_reachedCheckpoints}/3 • atravesse lasers apenas quando apagados.";
    }
}
