using System.Collections.Generic;
using AdaptiveTrials.Game.Interactions;
using Godot;

namespace AdaptiveTrials.Game.Player;

/// <summary>
/// Controla a movimentação, as animações e as interações do jogador.
/// </summary>
public partial class PlayerController : CharacterBody2D
{
	[Export]
	public float MovementSpeed { get; set; } = 220.0f;

	[Export]
	public PlayerVisualMode InitialVisualMode { get; set; } =
		PlayerVisualMode.Normal;

	private AnimatedSprite2D _animatedSprite = null!;
	private Area2D _interactionArea = null!;

	private readonly List<Node> _nearbyInteractables = new();

	private PlayerVisualMode _visualMode;
	private PlayerDirection _direction = PlayerDirection.Down;

	private bool _movementEnabled = true;

	public override void _Ready()
	{
		_animatedSprite =
			GetNode<AnimatedSprite2D>("AnimatedSprite2D");

		_interactionArea =
			GetNode<Area2D>("InteractionArea");

		_interactionArea.BodyEntered += OnInteractionBodyEntered;
		_interactionArea.BodyExited += OnInteractionBodyExited;
		_interactionArea.AreaEntered += OnInteractionAreaEntered;
		_interactionArea.AreaExited += OnInteractionAreaExited;

		SetVisualMode(InitialVisualMode);
		UpdateAnimation(Vector2.Zero);
	}

	public override void _PhysicsProcess(double delta)
	{
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
}
