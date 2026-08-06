using System.Collections.Generic;
using Godot;

namespace AdaptiveTrials.Game.Missions.Puzzle.Shared;

/// <summary>
/// Desenha as conexões concluídas entre os pontos do puzzle.
/// </summary>
public partial class ConnectionLineLayer : Control
{
    private readonly List<ConnectionSegment> _segments = new();

    public void ClearConnections()
    {
        _segments.Clear();
        QueueRedraw();
    }

    public void AddConnection(Vector2 start, Vector2 end, Color color)
    {
        _segments.Add(new ConnectionSegment(start, end, color));
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (ConnectionSegment segment in _segments)
        {
            DrawLine(segment.Start, segment.End, new Color(0, 0, 0, 0.24f), 12.0f, true);
            DrawLine(segment.Start, segment.End, segment.Color, 7.0f, true);
        }
    }

    private readonly record struct ConnectionSegment(Vector2 Start, Vector2 End, Color Color);
}
