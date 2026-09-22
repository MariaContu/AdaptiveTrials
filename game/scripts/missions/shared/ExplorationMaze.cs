using System;
using System.Collections.Generic;
using Godot;

namespace AdaptiveTrials.Game.Missions.Shared;

/// <summary>
/// Gera um labirinto determinístico por dificuldade para a missão
/// Encontrar Objetos. Cada layout é previamente validado para que o
/// ponto inicial consiga alcançar todos os pontos de coleta.
/// </summary>
public partial class ExplorationMaze : Node2D
{
    private const float CellSize = 80.0f;
    private const float BlockSize = 76.0f;

    private static readonly Vector2 MazeOrigin =
        new(56.0f, 44.0f);

    private static readonly string[] EasyLayout =
    {
        "#############",
        "#S..#....O..#",
        "#.#.#.#####.#",
        "#.#...#...#.#",
        "#.#####.#.#.#",
        "#O......#..O#",
        "#############"
    };

    private static readonly string[] MediumLayout =
    {
        "#############",
        "#S..#O....#O#",
        "#.#.#####.#.#",
        "#.#O....#...#",
        "#.#####.###.#",
        "#O....O....O#",
        "#############"
    };

    private static readonly string[] HardLayout =
    {
        "#############",
        "#S#O..#O...O#",
        "#.#.#.#.###.#",
        "#O#.#O#O..#.#",
        "#.#.#####.#O#",
        "#O..O...O...#",
        "#############"
    };

    private readonly List<Vector2> _collectiblePositions = new();
    private Node2D? _generatedRoot;

    public Vector2 PlayerStartPosition { get; private set; }
    public IReadOnlyList<Vector2> CollectiblePositions => _collectiblePositions;
    public int ActiveDifficulty { get; private set; }

    public void Configure(int difficulty)
    {
        ActiveDifficulty = Mathf.Clamp(difficulty, 1, 3);

        string[] layout = GetLayout(ActiveDifficulty);
        ValidateLayout(layout);
        ClearGeneratedMaze();

        _generatedRoot = new Node2D
        {
            Name = "GeneratedMaze"
        };

        AddChild(_generatedRoot);
        _collectiblePositions.Clear();

        Color blockColor = ActiveDifficulty switch
        {
            1 => new Color("#514058"),
            2 => new Color("#45334d"),
            3 => new Color("#392943"),
            _ => new Color("#45334d")
        };

        Color edgeColor = ActiveDifficulty switch
        {
            1 => new Color("#745f7c"),
            2 => new Color("#67516f"),
            3 => new Color("#594262"),
            _ => new Color("#67516f")
        };

        for (int row = 0; row < layout.Length; row++)
        {
            for (int column = 0; column < layout[row].Length; column++)
            {
                char cell = layout[row][column];
                Vector2 center = GetCellCenter(row, column);

                switch (cell)
                {
                    case '#':
                        CreateWallCell(center, blockColor, edgeColor);
                        break;

                    case 'S':
                        PlayerStartPosition = center;
                        break;

                    case 'O':
                        _collectiblePositions.Add(center);
                        break;
                }
            }
        }
    }

    private void ClearGeneratedMaze()
    {
        if (_generatedRoot is not null && IsInstanceValid(_generatedRoot))
        {
            _generatedRoot.QueueFree();
        }

        _generatedRoot = null;
        _collectiblePositions.Clear();
    }

    private void CreateWallCell(
        Vector2 position,
        Color fillColor,
        Color edgeColor)
    {
        if (_generatedRoot is null)
        {
            return;
        }

        StaticBody2D body = new()
        {
            Position = position,
            CollisionLayer = 1,
            CollisionMask = 0
        };

        _generatedRoot.AddChild(body);

        float half = BlockSize * 0.5f;

        Polygon2D visual = new()
        {
            Color = fillColor,
            Polygon = new Vector2[]
            {
                new(-half, -half),
                new(half, -half),
                new(half, half),
                new(-half, half)
            },
            ZIndex = 2
        };

        body.AddChild(visual);

        Line2D edge = new()
        {
            Width = 3.0f,
            DefaultColor = edgeColor,
            Closed = true,
            ZIndex = 3
        };

        edge.Points = new Vector2[]
        {
            new(-half, -half),
            new(half, -half),
            new(half, half),
            new(-half, half)
        };

        body.AddChild(edge);

        CollisionShape2D collision = new()
        {
            Shape = new RectangleShape2D
            {
                Size = new Vector2(BlockSize, BlockSize)
            }
        };

        body.AddChild(collision);
    }

    private static Vector2 GetCellCenter(int row, int column)
    {
        return MazeOrigin +
            new Vector2(
                (column + 0.5f) * CellSize,
                (row + 0.5f) * CellSize);
    }

    private static string[] GetLayout(int difficulty)
    {
        return difficulty switch
        {
            1 => EasyLayout,
            2 => MediumLayout,
            3 => HardLayout,
            _ => MediumLayout
        };
    }

    private static void ValidateLayout(string[] layout)
    {
        if (layout.Length == 0)
        {
            throw new InvalidOperationException(
                "O labirinto não possui linhas.");
        }

        int width = layout[0].Length;
        Vector2I? start = null;
        List<Vector2I> collectibles = new();

        for (int row = 0; row < layout.Length; row++)
        {
            if (layout[row].Length != width)
            {
                throw new InvalidOperationException(
                    "O layout do labirinto possui linhas com tamanhos diferentes.");
            }

            for (int column = 0; column < width; column++)
            {
                char cell = layout[row][column];

                if (cell == 'S')
                {
                    start = new Vector2I(column, row);
                }
                else if (cell == 'O')
                {
                    collectibles.Add(new Vector2I(column, row));
                }
            }
        }

        if (start is null)
        {
            throw new InvalidOperationException(
                "O labirinto não possui ponto inicial.");
        }

        Queue<Vector2I> queue = new();
        HashSet<Vector2I> visited = new();
        queue.Enqueue(start.Value);
        visited.Add(start.Value);

        Vector2I[] directions =
        {
            Vector2I.Up,
            Vector2I.Down,
            Vector2I.Left,
            Vector2I.Right
        };

        while (queue.Count > 0)
        {
            Vector2I current = queue.Dequeue();

            foreach (Vector2I direction in directions)
            {
                Vector2I next = current + direction;

                if (next.X < 0 ||
                    next.Y < 0 ||
                    next.Y >= layout.Length ||
                    next.X >= width ||
                    layout[next.Y][next.X] == '#' ||
                    visited.Contains(next))
                {
                    continue;
                }

                visited.Add(next);
                queue.Enqueue(next);
            }
        }

        foreach (Vector2I collectible in collectibles)
        {
            if (!visited.Contains(collectible))
            {
                throw new InvalidOperationException(
                    $"O ponto de coleta {collectible} não pode ser alcançado a partir do início.");
            }
        }
    }
}
