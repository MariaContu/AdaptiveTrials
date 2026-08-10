using AdaptiveTrials.Game.Missions.Puzzle.Shared;
using Godot;

namespace AdaptiveTrials.Game.Tests;

/// <summary>
/// Teste isolado do puzzle de sequências no estilo Termo.
/// Não utiliza SessionManager, API, HUD de missão ou popup.
/// </summary>
public partial class DecipherCodeLogicTestController : Node
{
	[Export(PropertyHint.Range, "1,3,1")]
	public int Difficulty { get; set; } = 3;

	private DecipherCodeBoard _board = null!;
	private Label _statusLabel = null!;

	public override void _Ready()
	{
		_board = GetNode<DecipherCodeBoard>(
			"../BoardCenter/DecipherCodeBoard");
		_statusLabel = GetNode<Label>("../StatusLabel");

		_board.GuessEvaluated += OnGuessEvaluated;
		_board.CodeSolved += OnCodeSolved;
		_board.AttemptsExhausted += OnAttemptsExhausted;

		StartTest();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel"))
		{
			GetTree().Quit();
			return;
		}

		if (@event is InputEventKey keyEvent &&
			keyEvent.Pressed &&
			!keyEvent.Echo &&
			keyEvent.Keycode == Key.R)
		{
			StartTest();
		}
	}

	private void StartTest()
	{
		int boardCount = Difficulty switch
		{
			1 => 1,
			2 => 2,
			3 => 4,
			_ => 1
		};

		int attempts = Difficulty switch
		{
			1 => 6,
			2 => 7,
			3 => 9,
			_ => 6
		};

		_statusLabel.Text =
			$"TESTE ISOLADO — dificuldade {Difficulty} | " +
			"1–8: símbolos | R: reiniciar | Esc: sair";
		_statusLabel.Modulate = new Color("#cbbdd4");
		_board.Configure(boardCount, attempts);
	}

	private void OnGuessEvaluated(
		int solvedBoards,
		int presentSymbols,
		int remaining)
	{
		_statusLabel.Text =
			$"Tentativa validada: {solvedBoards} concluída(s), " +
			$"{remaining} tentativa(s) restante(s).";
		_statusLabel.Modulate = new Color("#e4c47a");
	}

	private void OnCodeSolved(int attemptsUsed)
	{
		_statusLabel.Text =
			$"SUCESSO em {attemptsUsed} tentativa(s). " +
			"Pressione R para gerar novas sequências.";
		_statusLabel.Modulate = new Color("#8fc9a5");
	}

	private void OnAttemptsExhausted(
		int solvedBoards,
		int presentSymbols)
	{
		_statusLabel.Text =
			$"FALHA. {solvedBoards} tabuleiro(s) concluído(s). " +
			"Pressione R para reiniciar.";
		_statusLabel.Modulate = new Color("#e18b96");
	}
}
