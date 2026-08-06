using System;
using AdaptiveTrials.Game.Missions.Puzzle.Shared;
using Godot;

namespace AdaptiveTrials.Game.Tests;

/// <summary>
/// Teste isolado da lógica de Conectar Pontos, sem sessão e sem API.
/// </summary>
public partial class ConnectPointsLogicTestController : Node
{
    [Export(PropertyHint.Range, "1,3,1")]
    public int Difficulty { get; set; } = 2;

    private ConnectPointsBoard _board = null!;
    private Label _titleLabel = null!;
    private Label _statusLabel = null!;
    private Label _attemptsLabel = null!;

    private int _maximumFailures;
    private int _failures;

    public override void _Ready()
    {
        _board = GetNode<ConnectPointsBoard>("../ConnectPointsBoard");
        _titleLabel = GetNode<Label>("../TitleLabel");
        _statusLabel = GetNode<Label>("../StatusLabel");
        _attemptsLabel = GetNode<Label>("../AttemptsLabel");

        _board.PairConnected += OnPairConnected;
        _board.MistakeMade += OnMistakeMade;
        _board.BoardCompleted += OnBoardCompleted;

        StartTest();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent &&
            keyEvent.Pressed &&
            !keyEvent.Echo &&
            keyEvent.Keycode == Key.R)
        {
            GetViewport().SetInputAsHandled();
            StartTest();
        }
    }

    private void StartTest()
    {
        int safeDifficulty = Math.Clamp(Difficulty, 1, 3);
        int totalPoints = safeDifficulty switch
        {
            1 => 4,
            2 => 6,
            3 => 8,
            _ => 6
        };

        _maximumFailures = safeDifficulty switch
        {
            1 => 4,
            2 => 3,
            3 => 2,
            _ => 3
        };

        _failures = 0;
        _titleLabel.Text = $"CONNECT POINTS — {GetDifficultyText(safeDifficulty).ToUpperInvariant()}";
        _statusLabel.Text = "Select two matching symbols.";
        UpdateAttempts();
        _board.Configure(totalPoints);
        _board.SetInputEnabled(true);
    }

    private void OnPairConnected(int connectedPairs, int totalPairs)
    {
        _statusLabel.Text = $"PAIR CONNECTED — {connectedPairs}/{totalPairs}";
        _statusLabel.Modulate = new Color("#8fc9a5");
    }

    private void OnMistakeMade()
    {
        _failures++;
        UpdateAttempts();
        _statusLabel.Text = "WRONG PAIR";
        _statusLabel.Modulate = new Color("#d8808c");

        if (_failures >= _maximumFailures)
        {
            _board.SetInputEnabled(false);
            _statusLabel.Text = "FAILED — PRESS R TO RESTART";
        }
    }

    private void OnBoardCompleted()
    {
        _board.SetInputEnabled(false);
        _statusLabel.Text = "SUCCESS — PRESS R TO GENERATE A NEW BOARD";
        _statusLabel.Modulate = new Color("#8fc9a5");
    }

    private void UpdateAttempts()
    {
        int remaining = Math.Max(0, _maximumFailures - _failures);
        _attemptsLabel.Text = $"Attempts: {remaining}/{_maximumFailures}";
    }

    private static string GetDifficultyText(int difficulty) =>
        difficulty switch
        {
            1 => "Easy",
            2 => "Medium",
            3 => "Hard",
            _ => "Medium"
        };
}
