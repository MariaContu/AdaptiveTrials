using System;
using System.Collections.Generic;
using Godot;

namespace AdaptiveTrials.Game.Missions.Puzzle.Shared;

/// <summary>
/// Tabuleiro reutilizável que gera pares de pontos e valida suas conexões.
/// </summary>
public partial class ConnectPointsBoard : Control
{
    [Signal]
    public delegate void PairConnectedEventHandler(int connectedPairs, int totalPairs);

    [Signal]
    public delegate void MistakeMadeEventHandler();

    [Signal]
    public delegate void BoardCompletedEventHandler();

    [Export] public PackedScene? PointScene { get; set; }

    private readonly string[] _symbols = { "◆", "●", "▲", "✦" };
    private readonly Color[] _colors =
    {
        new("#8f72b5"),
        new("#5f91aa"),
        new("#b2788f"),
        new("#a58a55")
    };

    private readonly Vector2[] _boardPositions =
    {
        new(62, 42), new(190, 42), new(318, 42), new(446, 42),
        new(62, 138), new(190, 138), new(318, 138), new(446, 138),
        new(62, 234), new(190, 234), new(318, 234), new(446, 234),
        new(62, 330), new(190, 330), new(318, 330), new(446, 330)
    };

    private readonly List<ConnectPointButton> _points = new();
    private readonly RandomNumberGenerator _random = new();

    private Control _pointsContainer = null!;
    private ConnectionLineLayer _lineLayer = null!;
    private ConnectPointButton? _selectedPoint;
    private int _pairCount;
    private int _connectedPairs;
    private bool _inputEnabled;

    public int ConnectedPairs => _connectedPairs;
    public int TotalPairs => _pairCount;

    public override void _Ready()
    {
        _pointsContainer = GetNode<Control>("PointsContainer");
        _lineLayer = GetNode<ConnectionLineLayer>("ConnectionLineLayer");
    }

    public void Configure(int totalPoints)
    {
        int safePointCount = Math.Clamp(totalPoints, 4, 8);
        if (safePointCount % 2 != 0)
        {
            safePointCount--;
        }

        _pairCount = safePointCount / 2;
        _connectedPairs = 0;
        _selectedPoint = null;
        _inputEnabled = true;

        ClearBoard();
        CreatePoints(safePointCount);
    }

    public void SetInputEnabled(bool enabled)
    {
        _inputEnabled = enabled;

        foreach (ConnectPointButton point in _points)
        {
            if (!point.IsConnected)
            {
                point.Disabled = !enabled;
                point.MouseDefaultCursorShape = enabled
                    ? CursorShape.PointingHand
                    : CursorShape.Arrow;
            }
        }
    }

    private void CreatePoints(int totalPoints)
    {
        PackedScene? pointScene = PointScene;
        if (pointScene is null)
        {
            pointScene = GD.Load<PackedScene>(
                "res://scenes/missions/puzzle/shared/ConnectPointButton.tscn");
        }

        if (pointScene is null)
        {
            GD.PushError("Não foi possível carregar ConnectPointButton.tscn.");
            return;
        }

        List<Vector2> availablePositions = new(_boardPositions);
        Shuffle(availablePositions);

        List<int> pairIds = new();
        for (int pairId = 0; pairId < _pairCount; pairId++)
        {
            pairIds.Add(pairId);
            pairIds.Add(pairId);
        }
        Shuffle(pairIds);

        for (int index = 0; index < totalPoints; index++)
        {
            ConnectPointButton point = pointScene.Instantiate<ConnectPointButton>();
            point.Name = $"Point{index + 1:00}";
            point.Size = new Vector2(72, 72);
            point.Position = availablePositions[index] - point.Size / 2.0f;

            int pairId = pairIds[index];
            point.Configure(index, pairId, _symbols[pairId], _colors[pairId]);
            point.PointSelected += OnPointSelected;

            _pointsContainer.AddChild(point);
            _points.Add(point);
        }
    }

    private void OnPointSelected(int pointIndex)
    {
        if (!_inputEnabled || pointIndex < 0 || pointIndex >= _points.Count)
        {
            return;
        }

        ConnectPointButton point = _points[pointIndex];
        if (point.IsConnected)
        {
            return;
        }

        if (_selectedPoint is null)
        {
            _selectedPoint = point;
            point.SetSelected(true);
            return;
        }

        if (_selectedPoint == point)
        {
            point.SetSelected(false);
            _selectedPoint = null;
            return;
        }

        if (_selectedPoint.PairId == point.PairId)
        {
            CompletePair(_selectedPoint, point);
            _selectedPoint = null;
            return;
        }

        ConnectPointButton firstPoint = _selectedPoint;
        _selectedPoint = null;
        firstPoint.ShowError();
        point.ShowError();
        EmitSignal(SignalName.MistakeMade);
        _ = RestoreIncorrectPointsAsync(firstPoint, point);
    }

    private void CompletePair(ConnectPointButton first, ConnectPointButton second)
    {
        first.MarkConnected();
        second.MarkConnected();

        Vector2 firstCenter = first.Position + first.Size / 2.0f;
        Vector2 secondCenter = second.Position + second.Size / 2.0f;
        _lineLayer.AddConnection(firstCenter, secondCenter, _colors[first.PairId].Lightened(0.15f));

        _connectedPairs++;
        EmitSignal(SignalName.PairConnected, _connectedPairs, _pairCount);

        if (_connectedPairs >= _pairCount)
        {
            _inputEnabled = false;
            EmitSignal(SignalName.BoardCompleted);
        }
    }

    private async System.Threading.Tasks.Task RestoreIncorrectPointsAsync(
        ConnectPointButton first,
        ConnectPointButton second)
    {
        await ToSignal(GetTree().CreateTimer(0.42f), SceneTreeTimer.SignalName.Timeout);

        if (GodotObject.IsInstanceValid(first))
        {
            first.RestoreAppearance();
        }

        if (GodotObject.IsInstanceValid(second))
        {
            second.RestoreAppearance();
        }
    }

    private void ClearBoard()
    {
        foreach (Node child in _pointsContainer.GetChildren())
        {
            child.QueueFree();
        }

        _points.Clear();
        _lineLayer.ClearConnections();
    }

    private void Shuffle<T>(IList<T> items)
    {
        _random.Randomize();
        for (int index = items.Count - 1; index > 0; index--)
        {
            int swapIndex = _random.RandiRange(0, index);
            (items[index], items[swapIndex]) = (items[swapIndex], items[index]);
        }
    }
}
