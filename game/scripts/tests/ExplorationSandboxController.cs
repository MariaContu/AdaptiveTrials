using System;
using System.Collections.Generic;
using AdaptiveTrials.Game.Missions.Shared;
using AdaptiveTrials.Game.Missions.Exploration;
using AdaptiveTrials.Game.Player;
using Godot;

namespace AdaptiveTrials.Game.Tests;

/// <summary>
/// Laboratório isolado das três missões. Não depende de API ou SessionManager.
/// Altere TestDifficulty no Inspector e execute a cena diretamente.
/// </summary>
public partial class ExplorationSandboxController : Node
{
	[Export(PropertyHint.Range, "1,3,1")]
	public int TestDifficulty { get; set; } = 2;

	// O modo é definido pelo nome da cena, não por um valor exportado.
	// Assim cada teste isolado executa sempre a missão correspondente.
	private ExplorationMaze.CourseMode _mode;

	private static readonly PackedScene Collectible = GD.Load<PackedScene>(
		"res://scenes/missions/shared/CollectibleObject.tscn");
	private static readonly PackedScene Checkpoint = GD.Load<PackedScene>(
		"res://scenes/missions/shared/CheckpointArea.tscn");
	private static readonly PackedScene Destination = GD.Load<PackedScene>(
		"res://scenes/missions/shared/DestinationArea.tscn");
	private static readonly PackedScene Laser = GD.Load<PackedScene>(
		"res://scenes/missions/shared/LaserObstacle.tscn");
	private static readonly PackedScene Enemy = GD.Load<PackedScene>(
		"res://scenes/missions/shared/PatrolEnemy.tscn");

	private PlayerController _player = null!;
	private ExplorationMaze _maze = null!;
	private Node2D _dynamic = null!;
	private Label _status = null!;
	private readonly List<CheckpointArea> _checkpoints = new();
	private readonly List<PatrolEnemy> _enemies = new();
	private DestinationArea? _destination;
	private int _count;
	private int _required;
	private int _failures;
	private Vector2 _safe;
	private bool _completed;
	private bool _recovering;

	public override void _Ready()
	{
		_player = GetNode<PlayerController>("../Player");
		_maze = GetNode<ExplorationMaze>("../Environment/ExplorationMaze");
		_dynamic = GetNode<Node2D>("../DynamicObjects");
		_status = GetNode<Label>("../Interface/StatusLabel");
		TestDifficulty = Mathf.Clamp(TestDifficulty, 1, 3);
		_mode = GetParent().Name.ToString() switch
		{
			"FindObjectsMissionTest" => ExplorationMaze.CourseMode.FindObjects,
			"ReachDestinationMissionTest" => ExplorationMaze.CourseMode.ReachDestination,
			"AvoidEnemiesMissionTest" => ExplorationMaze.CourseMode.AvoidEnemies,
			var name => throw new InvalidOperationException(
				$"Cena de teste de exploração desconhecida: {name}")
		};
		GD.Print($"Teste isolado de exploração: {_mode} | dificuldade {TestDifficulty}");
		_maze.Configure(TestDifficulty, _mode);
		_player.GlobalPosition = _maze.PlayerStartPosition;
		_player.Velocity = Vector2.Zero;
		_safe = _maze.PlayerStartPosition;

		ExplorationLighting.Configure(GetParent(), _player, TestDifficulty,
			nightGarden: _mode == ExplorationMaze.CourseMode.FindObjects,
			gardenLightScale: TestDifficulty switch { 1 => 2.1f, 2 => 1.65f, _ => 1.25f });

		if (_mode == ExplorationMaze.CourseMode.FindObjects) ConfigureFind();
		else ConfigureRoute();
		UpdateStatus();
	}

	private void ConfigureFind()
	{
		_required = TestDifficulty switch { 1 => 3, 2 => 6, _ => 10 };
		for (int i = 0; i < _required; i++)
		{
			CollectibleObject item = Collectible.Instantiate<CollectibleObject>();
			item.Collected += _ => { _count++; if (_count >= _required) _completed = true; UpdateStatus(); };
			_dynamic.AddChild(item);
			item.GlobalPosition = _maze.CollectiblePositions[i];
			item.ConfigureExplorationVisibility(
				TestDifficulty switch { 1 => 0.85f, 2 => 0.6f, _ => 0.4f }, 2f);
			item.SetGlowEnabled(TestDifficulty < 3);
		}
	}

	private void ConfigureRoute()
	{
		_required = TestDifficulty;
		for (int i = 0; i < _required; i++)
		{
			CheckpointArea cp = Checkpoint.Instantiate<CheckpointArea>();
			_dynamic.AddChild(cp);
			cp.GlobalPosition = _maze.CheckpointPositions[i];
			cp.ConfigureIndex(i + 1);
			cp.SetEnabledState(i == 0);
			cp.CheckpointReached += OnCheckpoint;
			_checkpoints.Add(cp);
		}
		_destination = Destination.Instantiate<DestinationArea>();
		_dynamic.AddChild(_destination);
		_destination.GlobalPosition = _maze.DestinationPosition;
		_destination.SetEnabledState(false);
		_destination.DestinationReached += () => { _completed = true; UpdateStatus(); };

		if (_mode == ExplorationMaze.CourseMode.ReachDestination)
		{
			for (int i = 0; i < TestDifficulty; i++)
			{
				LaserObstacle gate = Laser.Instantiate<LaserObstacle>();
				_dynamic.AddChild(gate);
				gate.GlobalPosition = _maze.GatePositions[i];
				gate.PlayerHit += OnDetected;
				gate.Rotation = Mathf.Pi / 2f;
				gate.Configure(165f, TestDifficulty switch { 1 => 3f, 2 => 2.4f, _ => 1.8f },
					0.65f, 1.5f, i * 0.43f);
			}
		}
		else
		{
			for (int i = 0; i < TestDifficulty; i++)
			{
				var patrol = _maze.PatrolRoutes[i];
				PatrolEnemy enemy = Enemy.Instantiate<PatrolEnemy>();
				_dynamic.AddChild(enemy);
				enemy.PlayerDetected += _ => OnDetected();
				enemy.ConfigureStealth(patrol.Start, patrol.End,
					TestDifficulty switch { 1 => 60f, 2 => 82f, _ => 105f },
					TestDifficulty switch { 1 => 60f, 2 => 85f, _ => 105f },
					TestDifficulty switch { 1 => 55f, 2 => 70f, _ => 85f },
					TestDifficulty switch { 1 => 1.5f, 2 => 1.0f, _ => 0.65f }, _player);
				_enemies.Add(enemy);
			}
		}
	}

	private void OnCheckpoint(CheckpointArea cp)
	{
		if (_completed) return;
		_count++;
		_safe = cp.GlobalPosition;
		if (_count < _checkpoints.Count) _checkpoints[_count].SetEnabledState(true);
		else _destination?.SetEnabledState(true);
		UpdateStatus();
	}

	private void OnDetected()
	{
		if (_completed || _recovering) return;
		_recovering = true;
		_failures++;
		_player.SetMovementEnabled(false);
		foreach (var enemy in _enemies) enemy.SetDetectionEnabled(false);
		_player.SetDeferred(Node2D.PropertyName.GlobalPosition, _safe);
		GetTree().CreateTimer(0.8).Timeout += () =>
		{
			if (!IsInstanceValid(_player)) return;
			_player.SetMovementEnabled(true);
			foreach (var enemy in _enemies)
				if (IsInstanceValid(enemy)) enemy.SetDetectionEnabled(true);
			_recovering = false;
		};
		UpdateStatus();
	}

	private void UpdateStatus()
	{
		string name = _mode switch
		{
			ExplorationMaze.CourseMode.FindObjects => "ENCONTRAR OBJETOS",
			ExplorationMaze.CourseMode.ReachDestination => "CHEGAR AO DESTINO",
			_ => "FURTIVIDADE"
		};
		string difficulty = TestDifficulty switch { 1 => "FÁCIL", 2 => "MÉDIO", _ => "DIFÍCIL" };
		_status.Text = _completed
			? $"{name} | {difficulty} | CONCLUÍDO!"
			: $"{name} | {difficulty} | {_count}/{_required} | Falhas: {_failures}";
	}
}
