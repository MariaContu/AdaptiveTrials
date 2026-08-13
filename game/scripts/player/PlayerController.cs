using System.Collections.Generic;
using AdaptiveTrials.Game.Missions.Combat.Shared;
using AdaptiveTrials.Game.Interactions;
using Godot;

namespace AdaptiveTrials.Game.Player;

/// <summary>
/// Controla a movimentação, as animações e as interações do jogador.
/// </summary>
public partial class PlayerController : CharacterBody2D, IDamageable
{
	[Signal]
	public delegate void MagicOrbCastEventHandler(MagicOrb orb);

	[Signal]
	public delegate void HealthChangedEventHandler(
		int currentHealth,
		int maximumHealth);

	[Signal]
	public delegate void DamagedEventHandler(int damage, Node source);

	[Signal]
	public delegate void DiedEventHandler(Node source);
	[Export]
	public float MovementSpeed { get; set; } = 220.0f;

	[Export]
	public PlayerVisualMode InitialVisualMode { get; set; } =
		PlayerVisualMode.Normal;

	[ExportGroup("Camera")]
	[Export]
	public bool FollowCamera { get; set; } = true;

	[ExportGroup("Magic Attack")]
	[Export]
	public PackedScene? MagicOrbScene { get; set; }

	[Export]
	public int MagicOrbDamage { get; set; } = 1;

	[Export]
	public float MagicOrbSpeed { get; set; } = 520.0f;

	[Export]
	public float MagicOrbLifetimeSeconds { get; set; } = 1.4f;

	[Export]
	public float MagicOrbSpawnDistance { get; set; } = 48.0f;

	[Export]
	public float MagicOrbCooldownSeconds { get; set; } = 0.45f;

	private AnimatedSprite2D _animatedSprite = null!;
	private Area2D _interactionArea = null!;
	private HealthComponent _healthComponent = null!;

	private readonly List<Node> _nearbyInteractables = new();

	private PlayerVisualMode _visualMode;
	private PlayerDirection _direction = PlayerDirection.Down;

	private bool _movementEnabled = true;
	private float _magicOrbCooldownRemaining;

	public bool CanReceiveDamage =>
		_healthComponent is not null &&
		_healthComponent.CanReceiveDamage;

	public int CurrentHealth =>
		_healthComponent?.CurrentHealth ?? 0;

	public int MaximumHealth =>
		_healthComponent?.MaximumHealth ?? 0;

	public override void _Ready()
	{
		_animatedSprite =
			GetNode<AnimatedSprite2D>("AnimatedSprite2D");

		_interactionArea =
			GetNode<Area2D>("InteractionArea");

		_healthComponent =
			GetNode<HealthComponent>("HealthComponent");

		Camera2D camera =
			GetNode<Camera2D>("Camera2D");

		camera.Enabled =
			FollowCamera;

		_interactionArea.BodyEntered += OnInteractionBodyEntered;
		_interactionArea.BodyExited += OnInteractionBodyExited;
		_interactionArea.AreaEntered += OnInteractionAreaEntered;
		_interactionArea.AreaExited += OnInteractionAreaExited;

		_healthComponent.HealthChanged += OnHealthChanged;
		_healthComponent.DamageReceived += OnDamageReceived;
		_healthComponent.Depleted += OnHealthDepleted;

		SetVisualMode(InitialVisualMode);
		UpdateAnimation(Vector2.Zero);
	}

	public override void _PhysicsProcess(double delta)
	{
		_magicOrbCooldownRemaining = Mathf.Max(
			0.0f,
			_magicOrbCooldownRemaining - (float)delta);

		if (!_movementEnabled)
		{
			Velocity = Vector2.Zero;
			MoveAndSlide();
			return;
		}

		Vector2 inputDirection = Input.GetVector(
			"move_left",
			"move_right",
			"move_up",
			"move_down");

		Velocity = inputDirection * MovementSpeed;

		MoveAndSlide();

		UpdateDirection(inputDirection);
		UpdateAnimation(inputDirection);
	}

	public override void _UnhandledInput(InputEvent inputEvent)
	{
		if (!_movementEnabled)
		{
			return;
		}

		if (inputEvent.IsActionPressed("debug_normal_mode"))
		{
			SetVisualMode(PlayerVisualMode.Normal);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (inputEvent.IsActionPressed("debug_combat_mode"))
		{
			SetVisualMode(PlayerVisualMode.Combat);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (inputEvent.IsActionPressed("attack"))
		{
			TryCastMagicOrb();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (inputEvent.IsActionPressed("interact"))
		{
			TryInteract();
			GetViewport().SetInputAsHandled();
		}
	}

	/// <summary>
	/// Ativa ou bloqueia a movimentação e as interações do jogador.
	/// </summary>
	public void SetMovementEnabled(bool enabled)
	{
		_movementEnabled = enabled;

		if (enabled)
		{
			return;
		}

		Velocity = Vector2.Zero;
		UpdateAnimation(Vector2.Zero);
	}

	public bool IsMovementEnabled()
	{
		return _movementEnabled;
	}

	public void SetVisualMode(PlayerVisualMode mode)
	{
		_visualMode = mode;

		GD.Print(
			$"Modo visual do jogador alterado para: {_visualMode}");

		Vector2 animationDirection =
			Velocity.LengthSquared() > 0
				? Velocity.Normalized()
				: Vector2.Zero;

		UpdateAnimation(animationDirection);
	}

	public PlayerVisualMode GetVisualMode()
	{
		return _visualMode;
	}


	/// <summary>
	/// Encaminha o dano ao componente reutilizável de vida.
	/// </summary>
	public void ReceiveDamage(int damage, Node source)
	{
		_healthComponent.TryReceiveDamage(damage, source);
	}

	public void RestoreFullHealth()
	{
		_healthComponent.RestoreFullHealth();
	}

	public void SetDamageEnabled(bool enabled)
	{
		_healthComponent.SetDamageEnabled(enabled);
	}

	private void UpdateDirection(Vector2 movement)
	{
		if (movement == Vector2.Zero)
		{
			return;
		}

		if (Mathf.Abs(movement.X) > Mathf.Abs(movement.Y))
		{
			_direction = movement.X > 0
				? PlayerDirection.Right
				: PlayerDirection.Left;

			return;
		}

		_direction = movement.Y > 0
			? PlayerDirection.Down
			: PlayerDirection.Up;
	}

	private void UpdateAnimation(Vector2 movement)
	{
		bool isMoving = movement != Vector2.Zero;

		string modePrefix =
			_visualMode == PlayerVisualMode.Combat
				? "combat"
				: "normal";

		string movementState =
			isMoving
				? "walk"
				: "idle";

		string directionName =
			_direction switch
			{
				PlayerDirection.Down => "down",
				PlayerDirection.Up => "up",
				PlayerDirection.Right => "right",
				PlayerDirection.Left => "left",
				_ => "down"
			};

		string animationName =
			$"{modePrefix}_{movementState}_{directionName}";

		if (!_animatedSprite.SpriteFrames.HasAnimation(
				animationName))
		{
			GD.PushWarning(
				$"A animação '{animationName}' não existe.");

			return;
		}

		if (isMoving)
		{
			if (_animatedSprite.Animation != animationName ||
				!_animatedSprite.IsPlaying())
			{
				_animatedSprite.Play(animationName);
			}

			return;
		}

		if (_animatedSprite.Animation != animationName)
		{
			_animatedSprite.Play(animationName);
		}

		_animatedSprite.Stop();
		_animatedSprite.Frame = 0;
	}

	private void TryCastMagicOrb()
	{
		if (_visualMode != PlayerVisualMode.Combat)
		{
			GD.Print("O ataque mágico está disponível apenas no modo de combate.");
			return;
		}

		if (_magicOrbCooldownRemaining > 0.0f)
		{
			return;
		}

		if (MagicOrbScene is null)
		{
			GD.PushError("A cena da orbe mágica não foi configurada no Player.");
			return;
		}

		Node? currentScene = GetTree().CurrentScene;

		if (currentScene is null)
		{
			GD.PushError("Não foi possível localizar a cena atual para criar a orbe.");
			return;
		}

		MagicOrb? orb = MagicOrbScene.Instantiate<MagicOrb>();

		if (orb is null)
		{
			GD.PushError("A cena configurada não possui MagicOrb no nó raiz.");
			return;
		}

		Vector2 attackDirection = GetDirectionVector();

		orb.Initialize(
			attackDirection,
			MagicOrbDamage,
			this,
			MagicOrbSpeed,
			MagicOrbLifetimeSeconds);

		currentScene.AddChild(orb);
		orb.GlobalPosition =
			GlobalPosition +
			attackDirection * MagicOrbSpawnDistance;

		_magicOrbCooldownRemaining =
			Mathf.Max(0.05f, MagicOrbCooldownSeconds);

		EmitSignal(SignalName.MagicOrbCast, orb);

		GD.Print($"Orbe mágica lançada para {_direction}.");
	}

	private Vector2 GetDirectionVector()
	{
		return _direction switch
		{
			PlayerDirection.Down => Vector2.Down,
			PlayerDirection.Up => Vector2.Up,
			PlayerDirection.Right => Vector2.Right,
			PlayerDirection.Left => Vector2.Left,
			_ => Vector2.Down
		};
	}

	private void TryInteract()
	{
		for (int index = _nearbyInteractables.Count - 1;
			 index >= 0;
			 index--)
		{
			Node candidate = _nearbyInteractables[index];

			if (!IsInstanceValid(candidate))
			{
				_nearbyInteractables.RemoveAt(index);
				continue;
			}

			if (candidate is not IInteractable interactable)
			{
				continue;
			}

			if (!interactable.CanInteract)
			{
				continue;
			}

			interactable.Interact();

			GD.Print(
				$"Interação executada com: {candidate.Name}");

			return;
		}

		GD.Print("Nenhum objeto interativo próximo.");
	}

	private void OnInteractionBodyEntered(Node2D body)
	{
		RegisterInteractable(body);
	}

	private void OnInteractionBodyExited(Node2D body)
	{
		UnregisterInteractable(body);
	}

	private void OnInteractionAreaEntered(Area2D area)
	{
		RegisterInteractable(area);
	}

	private void OnInteractionAreaExited(Area2D area)
	{
		UnregisterInteractable(area);
	}

	private void RegisterInteractable(Node node)
	{
		if (node is not IInteractable)
		{
			return;
		}

		if (_nearbyInteractables.Contains(node))
		{
			return;
		}

		_nearbyInteractables.Add(node);

		GD.Print(
			$"Objeto interativo próximo: {node.Name}");
	}

	private void UnregisterInteractable(Node node)
	{
		_nearbyInteractables.Remove(node);
	}


	private void OnHealthChanged(int currentHealth, int maximumHealth)
	{
		EmitSignal(
			SignalName.HealthChanged,
			currentHealth,
			maximumHealth);

		GD.Print($"Vida do jogador: {currentHealth}/{maximumHealth}.");
	}

	private void OnDamageReceived(int damage, Node source)
	{
		EmitSignal(SignalName.Damaged, damage, source);
	}

	private void OnHealthDepleted(Node source)
	{
		SetMovementEnabled(false);
		SetDamageEnabled(false);
		EmitSignal(SignalName.Died, source);
		GD.Print("O jogador ficou sem vida.");
	}
}
