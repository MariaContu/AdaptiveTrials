using AdaptiveTrials.Game.Missions.Exploration;
using AdaptiveTrials.Game.Missions.Shared;
using AdaptiveTrials.Game.Player;
using Godot;

namespace AdaptiveTrials.Game.Tests;

/// <summary>
/// Teste isolado de Encontrar Objetos com labirinto e visão limitada.
/// Não utiliza SessionManager nem API.
/// </summary>
public partial class FindObjectsMissionTestController : Node
{
    [Export(PropertyHint.Range, "1,3,1")]
    public int TestDifficulty { get; set; } = 2;

    private static readonly PackedScene CollectibleScene =
        GD.Load<PackedScene>(
            "res://scenes/missions/shared/CollectibleObject.tscn");

    private PlayerController _player = null!;
    private ExplorationMaze _maze = null!;
    private Node2D _dynamicObjects = null!;
    private Label _statusLabel = null!;
    private int _collectedCount;
    private int _requiredItems;

    public override void _Ready()
    {
        _player = GetNode<PlayerController>("../Player");
        _maze = GetNode<ExplorationMaze>("../Environment/ExplorationMaze");
        _dynamicObjects = GetNode<Node2D>("../DynamicObjects");
        _statusLabel = GetNode<Label>("../Interface/StatusLabel");

        TestDifficulty = Mathf.Clamp(TestDifficulty, 1, 3);
        _requiredItems = TestDifficulty switch
        {
            1 => 3,
            2 => 6,
            3 => 10,
            _ => 6
        };

        _maze.Configure(TestDifficulty);
        _player.GlobalPosition = _maze.PlayerStartPosition;
        _player.Velocity = Vector2.Zero;

        ConfigureLimitedVisibility();
        SpawnCollectibles();
        UpdateStatus();
    }

    private void SpawnCollectibles()
    {
        for (int index = 0; index < _requiredItems; index++)
        {
            CollectibleObject collectible =
                CollectibleScene.Instantiate<CollectibleObject>();

            collectible.Name = $"Collectible{index + 1:00}";
            collectible.Collected += OnCollectibleCollected;
            _dynamicObjects.AddChild(collectible);
            collectible.GlobalPosition = _maze.CollectiblePositions[index];
            collectible.ConfigureExplorationVisibility(0.85f, 2.0f);
        }
    }

    private void ConfigureLimitedVisibility()
    {
        CanvasModulate darkness = new()
        {
            Name = "TestDarkness",
            Color = TestDifficulty switch
            {
                1 => new Color("#61536a"),
                2 => new Color("#46394f"),
                3 => new Color("#30263a"),
                _ => new Color("#46394f")
            }
        };

        GetParent().AddChild(darkness);

        Gradient gradient = new();
        gradient.SetColor(0, Colors.White);
        gradient.SetColor(1, new Color(1, 1, 1, 0));

        GradientTexture2D texture = new()
        {
            Gradient = gradient,
            Width = 256,
            Height = 256,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1.0f, 0.5f)
        };

        PointLight2D light = new()
        {
            Texture = texture,
            TextureScale = TestDifficulty switch
            {
                1 => 3.0f,
                2 => 2.35f,
                3 => 1.8f,
                _ => 2.35f
            },
            Energy = 1.15f,
            Color = new Color("#f3ddc5")
        };

        _player.AddChild(light);
    }

    private void OnCollectibleCollected(CollectibleObject collectible)
    {
        _collectedCount++;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        _statusLabel.Text =
            _collectedCount >= _requiredItems
                ? "TESTE CONCLUÍDO — todos os objetos do labirinto são alcançáveis."
                : $"DIFICULDADE {TestDifficulty} • OBJETOS {_collectedCount}/{_requiredItems}";
    }
}
