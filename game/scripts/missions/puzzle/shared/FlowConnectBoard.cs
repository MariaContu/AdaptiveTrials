using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AdaptiveTrials.Game.Missions.Puzzle.Shared;

/// <summary>
/// Tabuleiro de caminhos ortogonais no estilo Numberlink/Flow.
/// </summary>
public partial class FlowConnectBoard : Control
{
    [Signal]
    public delegate void PairCompletedEventHandler(int completedPairs, int totalPairs);

    [Signal]
    public delegate void InvalidMoveEventHandler();

    [Signal]
    public delegate void CoverageRequiredEventHandler(int currentPercent, int requiredPercent);

    [Signal]
    public delegate void BoardCompletedEventHandler();

    private readonly Color[] _colors =
    {
        new("#ff4b55"),
        new("#20d5e7"),
        new("#ff9f1c"),
        new("#3fd05d"),
        new("#b86cff"),
        new("#ffd93d")
    };

    private readonly Dictionary<int, EndpointPair> _pairs = new();
    private readonly Dictionary<int, List<Vector2I>> _completedPaths = new();
    private readonly Dictionary<Vector2I, int> _occupiedCells = new();
    private readonly Dictionary<Vector2I, int> _endpointOwners = new();

    private int _gridSize = 5;
    private int _pairCount = 3;
    private float _requiredCoverage = 0.55f;
    private bool _inputEnabled = true;

    private int _activeColor = -1;
    private readonly List<Vector2I> _activePath = new();
    private Vector2I _lastPointerCell = new(-1, -1);

    public int CompletedPairs => _completedPaths.Count;
    public int TotalPairs => _pairCount;
    public int CoveragePercent => Mathf.RoundToInt(GetCoverageRatio() * 100.0f);
    public int RequiredCoveragePercent => Mathf.RoundToInt(_requiredCoverage * 100.0f);
    public string LastInvalidMoveReason { get; private set; } = string.Empty;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        CustomMinimumSize = new Vector2(340, 340);
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawBoardBackground();
        DrawGrid();
        DrawCompletedPaths();
        DrawActivePath();
        DrawEndpoints();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (!_inputEnabled)
        {
            return;
        }

        if (@event is InputEventMouseButton mouseButton)
        {
            Vector2I cell = PositionToCell(mouseButton.Position);

            if (mouseButton.ButtonIndex == MouseButton.Left && mouseButton.Pressed)
            {
                _lastPointerCell = cell;
                HandleCell(cell);
                AcceptEvent();
                return;
            }

            if (mouseButton.ButtonIndex == MouseButton.Left && !mouseButton.Pressed)
            {
                FinishDragAttempt();
                _lastPointerCell = new Vector2I(-1, -1);
                AcceptEvent();
                return;
            }

            if (mouseButton.ButtonIndex == MouseButton.Right && mouseButton.Pressed)
            {
                CancelActivePath();
                AcceptEvent();
                return;
            }
        }

        if (@event is InputEventMouseMotion motion &&
            Input.IsMouseButtonPressed(MouseButton.Left))
        {
            Vector2I cell = PositionToCell(motion.Position);
            if (cell != _lastPointerCell)
            {
                _lastPointerCell = cell;
                HandleCell(cell);
            }

            AcceptEvent();
        }
    }

    public void Configure(int difficulty, int configuredPieces = 0)
    {
        int safeDifficulty = Math.Clamp(difficulty, 1, 3);

        _gridSize = safeDifficulty switch
        {
            1 => 5,
            2 => 6,
            3 => 7,
            _ => 5
        };

        _pairCount = safeDifficulty switch
        {
            1 => 3,
            2 => 4,
            3 => 5,
            _ => 3
        };

        if (configuredPieces > 0)
        {
            // O catálogo usa 4, 6 e 8 peças. A conversão mantém 3, 4 e 5 pares.
            _pairCount = Math.Clamp(configuredPieces / 2 + 1, 3, 5);
        }

        _requiredCoverage = safeDifficulty switch
        {
            1 => 0.55f,
            2 => 0.75f,
            3 => 1.0f,
            _ => 0.55f
        };

        GenerateSolvableLayout();
        ResetBoard();
    }

    public void SetInputEnabled(bool enabled)
    {
        _inputEnabled = enabled;
        MouseDefaultCursorShape = enabled
            ? CursorShape.Cross
            : CursorShape.Arrow;
    }

    public void ResetBoard()
    {
        _completedPaths.Clear();
        _occupiedCells.Clear();
        _activePath.Clear();
        _activeColor = -1;
        _lastPointerCell = new Vector2I(-1, -1);
        LastInvalidMoveReason = string.Empty;
        SetInputEnabled(true);
        QueueRedraw();
    }

    private void GenerateSolvableLayout()
    {
        _pairs.Clear();
        _endpointOwners.Clear();

        List<Vector2I> snake = BuildSnakeCells(_gridSize);
        int cellCount = snake.Count;
        int baseLength = cellCount / _pairCount;
        int remainder = cellCount % _pairCount;
        int cursor = 0;

        for (int color = 0; color < _pairCount; color++)
        {
            int segmentLength = baseLength + (color < remainder ? 1 : 0);
            Vector2I start = snake[cursor];
            Vector2I end = snake[cursor + segmentLength - 1];
            cursor += segmentLength;

            _pairs[color] = new EndpointPair(start, end);
            _endpointOwners[start] = color;
            _endpointOwners[end] = color;
        }
    }

    private static List<Vector2I> BuildSnakeCells(int size)
    {
        List<Vector2I> cells = new(size * size);
        for (int row = 0; row < size; row++)
        {
            if (row % 2 == 0)
            {
                for (int column = 0; column < size; column++)
                {
                    cells.Add(new Vector2I(column, row));
                }
            }
            else
            {
                for (int column = size - 1; column >= 0; column--)
                {
                    cells.Add(new Vector2I(column, row));
                }
            }
        }

        return cells;
    }

    private void HandleCell(Vector2I cell)
    {
        if (!IsInsideGrid(cell))
        {
            return;
        }

        if (_activeColor < 0)
        {
            TryStartPath(cell);
            return;
        }

        if (_activePath.Count == 0)
        {
            CancelActivePath();
            return;
        }

        Vector2I current = _activePath[^1];
        if (cell == current)
        {
            return;
        }

        if (!AreAdjacent(current, cell))
        {
            // Movimento rápido do mouse pode pular células. Isso não é uma falha do jogador.
            return;
        }

        if (_activePath.Count >= 2 && cell == _activePath[^2])
        {
            BacktrackOneCell();
            return;
        }

        if (_activePath.Contains(cell))
        {
            return;
        }

        if (_endpointOwners.TryGetValue(cell, out int endpointColor))
        {
            if (endpointColor == _activeColor && cell != _activePath[0])
            {
                CompleteActivePath(cell);
            }
            else
            {
                // O caminho permanece ativo; se o jogador soltar aqui, a tentativa será contada uma única vez.
            }

            return;
        }

        if (_occupiedCells.ContainsKey(cell))
        {
            return;
        }

        _activePath.Add(cell);
        _occupiedCells[cell] = _activeColor;
        QueueRedraw();
    }

    private void TryStartPath(Vector2I cell)
    {
        if (!_endpointOwners.TryGetValue(cell, out int color))
        {
            return;
        }

        if (_completedPaths.ContainsKey(color))
        {
            ClearCompletedPath(color);
        }

        _activeColor = color;
        _activePath.Clear();
        _activePath.Add(cell);
        QueueRedraw();
    }

    private void CompleteActivePath(Vector2I targetEndpoint)
    {
        _activePath.Add(targetEndpoint);
        List<Vector2I> completed = new(_activePath);
        _completedPaths[_activeColor] = completed;

        foreach (Vector2I cell in completed)
        {
            _occupiedCells[cell] = _activeColor;
        }

        int completedColor = _activeColor;
        _activeColor = -1;
        _activePath.Clear();
        QueueRedraw();

        EmitSignal(SignalName.PairCompleted, _completedPaths.Count, _pairCount);
        GD.Print($"Par {completedColor + 1} conectado. Cobertura: {CoveragePercent}%.");
        EvaluateCompletion();
    }

    private void EvaluateCompletion()
    {
        if (_completedPaths.Count < _pairCount)
        {
            return;
        }

        float coverage = GetCoverageRatio();
        if (coverage + 0.0001f < _requiredCoverage)
        {
            EmitSignal(
                SignalName.CoverageRequired,
                Mathf.RoundToInt(coverage * 100.0f),
                RequiredCoveragePercent);
            return;
        }

        SetInputEnabled(false);
        EmitSignal(SignalName.BoardCompleted);
    }

    private void ClearCompletedPath(int color)
    {
        if (!_completedPaths.TryGetValue(color, out List<Vector2I>? path))
        {
            return;
        }

        foreach (Vector2I cell in path)
        {
            if (!_endpointOwners.ContainsKey(cell))
            {
                _occupiedCells.Remove(cell);
            }
        }

        _completedPaths.Remove(color);
    }

    private void BacktrackOneCell()
    {
        if (_activePath.Count <= 1)
        {
            return;
        }

        Vector2I removed = _activePath[^1];
        _activePath.RemoveAt(_activePath.Count - 1);
        if (!_endpointOwners.ContainsKey(removed))
        {
            _occupiedCells.Remove(removed);
        }

        QueueRedraw();
    }


    private void FinishDragAttempt()
    {
        if (_activeColor < 0 || _activePath.Count == 0)
        {
            return;
        }

        // Apenas um caminho realmente iniciado e liberado sem chegar ao par conta como erro.
        bool shouldCountAsInvalidAttempt = _activePath.Count > 1;
        CancelActivePath();

        if (!shouldCountAsInvalidAttempt)
        {
            return;
        }

        LastInvalidMoveReason =
            "O caminho foi liberado antes de alcançar o ponto correspondente.";

        EmitSignal(SignalName.InvalidMove);
    }

    private void CancelActivePath()
    {
        foreach (Vector2I cell in _activePath)
        {
            if (!_endpointOwners.ContainsKey(cell))
            {
                _occupiedCells.Remove(cell);
            }
        }

        _activePath.Clear();
        _activeColor = -1;
        QueueRedraw();
    }

    private float GetCoverageRatio()
    {
        if (_gridSize <= 0)
        {
            return 0.0f;
        }

        HashSet<Vector2I> covered = new();
        foreach (List<Vector2I> path in _completedPaths.Values)
        {
            foreach (Vector2I cell in path)
            {
                covered.Add(cell);
            }
        }

        return covered.Count / (float)(_gridSize * _gridSize);
    }

    private void DrawBoardBackground()
    {
        Rect2 boardRect = GetBoardRect();
        DrawStyleBox(
            CreateBoardStyle(),
            boardRect);
    }

    private static StyleBoxFlat CreateBoardStyle()
    {
        StyleBoxFlat style = new()
        {
            BgColor = new Color("#17131f"),
            BorderColor = new Color("#7f6a91"),
            BorderWidthLeft = 3,
            BorderWidthTop = 3,
            BorderWidthRight = 3,
            BorderWidthBottom = 3,
            CornerRadiusTopLeft = 18,
            CornerRadiusTopRight = 18,
            CornerRadiusBottomLeft = 18,
            CornerRadiusBottomRight = 18,
            ShadowColor = new Color(0, 0, 0, 0.42f),
            ShadowSize = 12
        };

        return style;
    }

    private void DrawGrid()
    {
        Rect2 boardRect = GetBoardRect();
        float cellSize = boardRect.Size.X / _gridSize;
        Color gridColor = new("#4d4657");

        for (int index = 1; index < _gridSize; index++)
        {
            float offset = index * cellSize;
            DrawLine(
                new Vector2(boardRect.Position.X + offset, boardRect.Position.Y),
                new Vector2(boardRect.Position.X + offset, boardRect.End.Y),
                gridColor,
                1.5f);
            DrawLine(
                new Vector2(boardRect.Position.X, boardRect.Position.Y + offset),
                new Vector2(boardRect.End.X, boardRect.Position.Y + offset),
                gridColor,
                1.5f);
        }
    }

    private void DrawCompletedPaths()
    {
        foreach ((int color, List<Vector2I> path) in _completedPaths)
        {
            DrawPath(path, _colors[color], false);
        }
    }

    private void DrawActivePath()
    {
        if (_activeColor >= 0 && _activePath.Count > 0)
        {
            DrawPath(_activePath, _colors[_activeColor].Lightened(0.1f), true);
        }
    }

    private void DrawPath(IReadOnlyList<Vector2I> path, Color color, bool active)
    {
        if (path.Count < 2)
        {
            return;
        }

        Vector2[] points = new Vector2[path.Count];
        for (int index = 0; index < path.Count; index++)
        {
            points[index] = CellCenter(path[index]);
        }

        float width = GetCellSize() * (active ? 0.34f : 0.42f);
        DrawPolyline(points, new Color(0, 0, 0, 0.45f), width + 7.0f, true);
        DrawPolyline(points, color, width, true);
    }

    private void DrawEndpoints()
    {
        float radius = GetCellSize() * 0.27f;
        foreach ((int color, EndpointPair pair) in _pairs)
        {
            DrawEndpoint(pair.Start, color, radius);
            DrawEndpoint(pair.End, color, radius);
        }
    }

    private void DrawEndpoint(Vector2I cell, int color, float radius)
    {
        Vector2 center = CellCenter(cell);
        Color endpointColor = _colors[color];
        DrawCircle(center, radius + 5.0f, new Color(0, 0, 0, 0.5f));
        DrawCircle(center, radius, endpointColor);
        DrawCircle(center, radius * 0.38f, endpointColor.Lightened(0.35f));
    }

    private Rect2 GetBoardRect()
    {
        float side = Mathf.Min(Size.X, Size.Y) - 24.0f;
        side = Mathf.Max(side, 200.0f);
        Vector2 position = (Size - new Vector2(side, side)) / 2.0f;
        return new Rect2(position, new Vector2(side, side));
    }

    private float GetCellSize() => GetBoardRect().Size.X / _gridSize;

    private Vector2 CellCenter(Vector2I cell)
    {
        Rect2 boardRect = GetBoardRect();
        float cellSize = boardRect.Size.X / _gridSize;
        return boardRect.Position + new Vector2(
            (cell.X + 0.5f) * cellSize,
            (cell.Y + 0.5f) * cellSize);
    }

    private Vector2I PositionToCell(Vector2 position)
    {
        Rect2 boardRect = GetBoardRect();
        if (!boardRect.HasPoint(position))
        {
            return new Vector2I(-1, -1);
        }

        float cellSize = boardRect.Size.X / _gridSize;
        Vector2 local = position - boardRect.Position;
        return new Vector2I(
            Mathf.Clamp(Mathf.FloorToInt(local.X / cellSize), 0, _gridSize - 1),
            Mathf.Clamp(Mathf.FloorToInt(local.Y / cellSize), 0, _gridSize - 1));
    }

    private bool IsInsideGrid(Vector2I cell) =>
        cell.X >= 0 && cell.Y >= 0 && cell.X < _gridSize && cell.Y < _gridSize;

    private static bool AreAdjacent(Vector2I first, Vector2I second) =>
        Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y) == 1;

    private readonly record struct EndpointPair(Vector2I Start, Vector2I End);
}
