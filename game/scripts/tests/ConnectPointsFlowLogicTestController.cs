using System;
using AdaptiveTrials.Game.Missions.Puzzle.Shared;
using Godot;

namespace AdaptiveTrials.Game.Tests;

/// <summary>
/// Teste isolado do puzzle de caminhos, sem sessão e sem API.
/// </summary>
public partial class ConnectPointsFlowLogicTestController : Node
{
    [Export(PropertyHint.Range, "1,3,1")]
    public int Difficulty { get; set; } = 2;

    private FlowConnectBoard _board = null!;
    private Label _titleLabel = null!;
    private Label _statusLabel = null!;
    private Label _attemptsLabel = null!;
    private Label _coverageLabel = null!;

    private int _maximumFailures;
    private int _failures;

    public override void _Ready()
    {
        _board = GetNode<FlowConnectBoard>("../FlowConnectBoard");
        _titleLabel = GetNode<Label>("../TitleLabel");
        _statusLabel = GetNode<Label>("../StatusLabel");
        _attemptsLabel = GetNode<Label>("../AttemptsLabel");
        _coverageLabel = GetNode<Label>("../CoverageLabel");

        _board.PairCompleted += OnPairCompleted;
        _board.InvalidMove += OnInvalidMove;
        _board.CoverageRequired += OnCoverageRequired;
        _board.BoardCompleted += OnBoardCompleted;

        StartTest();
    }

    public override void _Process(double delta)
    {
        _coverageLabel.Text =
            $"Coverage: {_board.CoveragePercent}% / {_board.RequiredCoveragePercent}%";
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
        _maximumFailures = safeDifficulty switch
        {
            1 => 5,
            2 => 4,
            3 => 3,
            _ => 4
        };

        _failures = 0;
        _titleLabel.Text =
            $"CONECTAR CAMINHOS — {GetDifficultyText(safeDifficulty).ToUpperInvariant()}";
        _statusLabel.Text =
            "Arraste de um ponto colorido até o ponto correspondente.";
        _statusLabel.Modulate = new Color("#c2b4ce");
        UpdateAttempts();

        int pieces = safeDifficulty switch
        {
            1 => 4,
            2 => 6,
            3 => 8,
            _ => 6
        };

        _board.Configure(safeDifficulty, pieces);
        _board.SetInputEnabled(true);
    }

    private void OnPairCompleted(int completedPairs, int totalPairs)
    {
        _statusLabel.Text =
            $"CAMINHO CONCLUÍDO — {completedPairs}/{totalPairs}. Clique em uma extremidade para refazer.";
        _statusLabel.Modulate = new Color("#8fc9a5");
    }

    private void OnInvalidMove()
    {
        _failures++;
        UpdateAttempts();
        _statusLabel.Text =
            $"TENTATIVA INVÁLIDA — {_board.LastInvalidMoveReason}";
        _statusLabel.Modulate = new Color("#e18b96");

        if (_failures >= _maximumFailures)
        {
            _board.SetInputEnabled(false);
            _statusLabel.Text = "FALHA — PRESSIONE R PARA REINICIAR";
        }
    }

    private void OnCoverageRequired(int currentPercent, int requiredPercent)
    {
        _statusLabel.Text =
            $"COBERTURA {currentPercent}% — refaça caminhos até alcançar {requiredPercent}%.";
        _statusLabel.Modulate = new Color("#f0c674");
    }

    private void OnBoardCompleted()
    {
        _board.SetInputEnabled(false);
        _statusLabel.Text = "SUCESSO — PRESSIONE R PARA REINICIAR";
        _statusLabel.Modulate = new Color("#8fc9a5");
    }

    private void UpdateAttempts()
    {
        int remaining = Math.Max(0, _maximumFailures - _failures);
        _attemptsLabel.Text = $"Tentativas: {remaining}/{_maximumFailures}";
    }

    private static string GetDifficultyText(int difficulty) => difficulty switch
    {
        1 => "Fácil",
        2 => "Média",
        3 => "Difícil",
        _ => "Média"
    };
}
