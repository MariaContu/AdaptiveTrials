using System.Collections.Generic;
using AdaptiveTrials.Game.Missions.Combat.Shared;
using Godot;

namespace AdaptiveTrials.Game.Tests;

/// <summary>
/// Valida o objeto defensável usando os projéteis reais dos inimigos.
/// </summary>
public partial class DefensibleObjectTestController : Node2D
{
    [Export]
    public PackedScene? EnemyOrbScene { get; set; }

    [Export]
    public float ShotIntervalSeconds { get; set; } = 1.4f;

    private DefensibleObject _defensibleObject = null!;
    private Node2D _projectiles = null!;
    private Marker2D _emitter = null!;
    private Label _healthLabel = null!;
    private Label _statusLabel = null!;
    private readonly List<Marker2D> _relocationPoints = new();
    private float _shotRemaining;
    private int _lastRelocationIndex = -1;
    private bool _testFinished;

    public override void _Ready()
    {
        _defensibleObject = GetNode<DefensibleObject>("DefensibleObject");
        _projectiles = GetNode<Node2D>("Projectiles");
        _emitter = GetNode<Marker2D>("EnemyOrbEmitter");
        _healthLabel = GetNode<Label>("Interface/Panel/Margin/Content/HealthLabel");
        _statusLabel = GetNode<Label>("Interface/Panel/Margin/Content/StatusLabel");

        Node2D relocationPoints =
            GetNode<Node2D>("RelocationPoints");

        foreach (Node child in relocationPoints.GetChildren())
        {
            if (child is Marker2D marker)
            {
                _relocationPoints.Add(marker);
            }
        }

        EnemyOrbScene ??= GD.Load<PackedScene>(
            "res://scenes/missions/combat/shared/EnemyMagicOrb.tscn");

        _defensibleObject.HealthChanged += OnHealthChanged;
        _defensibleObject.DamageReceived += OnDamageReceived;
        _defensibleObject.Destroyed += OnDestroyed;

        _shotRemaining = 0.6f;
        UpdateHealthLabel(
            _defensibleObject.CurrentHealth,
            _defensibleObject.MaximumHealth);
    }

    public override void _Process(double delta)
    {
        if (Input.IsActionJustPressed("ui_cancel"))
        {
            GetTree().Quit();
            return;
        }

        if (Input.IsKeyPressed(Key.R))
        {
            GetTree().ReloadCurrentScene();
            return;
        }

        if (_testFinished)
        {
            return;
        }

        _shotRemaining -= (float)delta;

        if (_shotRemaining <= 0.0f)
        {
            LaunchEnemyOrb();
            _shotRemaining = Mathf.Max(0.2f, ShotIntervalSeconds);
        }
    }

    private void LaunchEnemyOrb()
    {
        if (EnemyOrbScene?.Instantiate<MagicOrb>() is not MagicOrb orb)
        {
            GD.PushError("Não foi possível instanciar a orbe inimiga no teste.");
            return;
        }

        _projectiles.AddChild(orb);
        orb.GlobalPosition = _emitter.GlobalPosition;

        Vector2 direction =
            _defensibleObject.GlobalPosition - _emitter.GlobalPosition;

        orb.Initialize(
            direction,
            1,
            this,
            260.0f,
            3.0f);

        _statusLabel.Text = "ORBE INIMIGA LANÇADA";
    }

    private void OnHealthChanged(int currentHealth, int maximumHealth)
    {
        UpdateHealthLabel(currentHealth, maximumHealth);
    }

    private void OnDamageReceived(int damage, Node source)
    {
        if (_defensibleObject.CurrentHealth <= 0)
        {
            return;
        }

        RelocateObject();

        _statusLabel.Text =
            $"ATINGIDO: -{damage} • REPOSICIONADO • " +
            $"VIDA {_defensibleObject.CurrentHealth}/" +
            $"{_defensibleObject.MaximumHealth}";
    }

    private void RelocateObject()
    {
        if (_relocationPoints.Count == 0)
        {
            return;
        }

        int nextIndex =
            (_lastRelocationIndex + 1) %
            _relocationPoints.Count;

        _lastRelocationIndex =
            nextIndex;

        _defensibleObject.GlobalPosition =
            _relocationPoints[nextIndex].GlobalPosition;

        _defensibleObject.PlayRelocationFeedback();
    }

    private void OnDestroyed(Node source)
    {
        _testFinished = true;
        _statusLabel.Text =
            "OBJETO DESTRUÍDO — PRESSIONE R PARA REINICIAR";

        foreach (Node child in _projectiles.GetChildren())
        {
            child.QueueFree();
        }
    }

    private void UpdateHealthLabel(int currentHealth, int maximumHealth)
    {
        _healthLabel.Text =
            $"VIDA DO OBJETO: {currentHealth}/{maximumHealth}";
    }
}
