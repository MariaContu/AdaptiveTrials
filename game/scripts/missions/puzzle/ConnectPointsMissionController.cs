using System;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using AdaptiveTrials.Game.Components;
using AdaptiveTrials.Game.Dto;
using AdaptiveTrials.Game.Enums;
using AdaptiveTrials.Game.Missions.Puzzle.Shared;
using AdaptiveTrials.Game.Missions.Shared;
using AdaptiveTrials.Game.Session;
using Godot;

namespace AdaptiveTrials.Game.Missions.Puzzle;

/// <summary>
/// Controla a missão de conectar pares por caminhos ortogonais sem cruzamentos.
/// </summary>
public partial class ConnectPointsMissionController : Node
{
    private SessionManager _sessionManager = null!;
    private MissionHud _missionHud = null!;
    private MissionResultPopup _resultPopup = null!;
    private FlowConnectBoard _board = null!;
    private Label _instructionLabel = null!;
    private Label _feedbackLabel = null!;
    private Label _coverageLabel = null!;

    private MissionDto _mission = null!;
    private int _configuredPieces;
    private int _maximumFailures;
    private int _failures;
    private double _elapsedTime;
    private bool _missionFinished;
    private bool _isFinalizingMission;
    private bool _resultRegistered;
    private string _objectiveText = string.Empty;

    public MissionResult? Result { get; private set; }

    public override void _Ready()
    {
        GetReferences();
        _sessionManager = GetNode<SessionManager>("/root/SessionManager");

        _resultPopup.ContinueRequested += OnContinueRequested;
        _board.PairCompleted += OnPairCompleted;
        _board.InvalidMove += OnInvalidMove;
        _board.CoverageRequired += OnCoverageRequired;
        _board.BoardCompleted += OnBoardCompleted;

        MissionDto? currentMission = _sessionManager.CurrentMission;
        if (currentMission is null)
        {
            ShowInitializationError("Nenhuma missão ativa foi encontrada.");
            return;
        }

        _mission = currentMission;
        if (_mission.Type != MissionType.Puzzle)
        {
            ShowInitializationError("A missão ativa não é uma missão de quebra-cabeça.");
            return;
        }

        ReadSettings();
        ConfigureMission();
    }

    public override void _Process(double delta)
    {
        if (_missionFinished || _isFinalizingMission)
        {
            return;
        }

        _elapsedTime += delta;
        _missionHud.SetElapsedTime(_elapsedTime);
        UpdateCoverageText();
    }

    private void GetReferences()
    {
        _missionHud = GetNode<MissionHud>("../MissionHud");
        _resultPopup = GetNode<MissionResultPopup>("../MissionResultPopup");
        _board = GetNode<FlowConnectBoard>("../PuzzleInterface/PuzzleArea/FlowConnectBoard");
        _instructionLabel = GetNode<Label>("../PuzzleInterface/PuzzleArea/InstructionLabel");
        _feedbackLabel = GetNode<Label>("../PuzzleInterface/PuzzleArea/FeedbackLabel");
        _coverageLabel = GetNode<Label>("../PuzzleInterface/PuzzleArea/CoverageLabel");
    }

    private void ReadSettings()
    {
        _configuredPieces = _mission.Difficulty switch
        {
            1 => 4,
            2 => 6,
            3 => 8,
            _ => 4
        };

        _maximumFailures = _mission.Difficulty switch
        {
            1 => 5,
            2 => 4,
            3 => 3,
            _ => 4
        };

        if (string.IsNullOrWhiteSpace(_mission.ParametersJson))
        {
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(_mission.ParametersJson);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("pieces", out JsonElement piecesElement) &&
                piecesElement.TryGetInt32(out int configuredPieces))
            {
                _configuredPieces = Math.Clamp(configuredPieces, 4, 8);
            }

            if (root.TryGetProperty("maxFailures", out JsonElement failuresElement) &&
                failuresElement.TryGetInt32(out int configuredFailures))
            {
                _maximumFailures = Math.Clamp(configuredFailures, 1, 10);
            }
        }
        catch (JsonException exception)
        {
            GD.PushWarning(
                $"ParametersJson inválido em Conectar Pontos. " +
                $"Valores padrão serão usados. {exception.Message}");
        }
    }

    private void ConfigureMission()
    {
        _failures = 0;
        _elapsedTime = 0;
        _missionFinished = false;
        _isFinalizingMission = false;
        _resultRegistered = false;
        Result = null;

        _board.Configure(_mission.Difficulty, _configuredPieces);
        _objectiveText = BuildObjectiveText();

        _missionHud.Configure(
            _mission.Name,
            _mission.Type,
            _objectiveText,
            _board.TotalPairs);
        _missionHud.SetProgress(0, _board.TotalPairs, "Caminhos");
        _missionHud.SetAttempts(_maximumFailures, _maximumFailures);
        _missionHud.SetVisibleState(true);

        _instructionLabel.Text =
            "Arraste de um ponto colorido até o par correspondente. Os caminhos não podem se cruzar.";
        _feedbackLabel.Text =
            "Clique com o botão direito para cancelar. Clique em uma extremidade para refazer um caminho.";
        _feedbackLabel.Modulate = new Color("#c2b4ce");
        UpdateCoverageText();
        _resultPopup.HidePopup();

        GD.Print(
            $"Missão Conectar Pontos iniciada: " +
            $"MissionId={_mission.Id}, " +
            $"Grid={GetGridSize(_mission.Difficulty)}x{GetGridSize(_mission.Difficulty)}, " +
            $"Pairs={_board.TotalPairs}, " +
            $"RequiredCoverage={_board.RequiredCoveragePercent}%, " +
            $"MaxFailures={_maximumFailures}, " +
            $"Difficulty={_mission.Difficulty}");
    }

    private string BuildObjectiveText()
    {
        return _mission.Difficulty switch
        {
            1 => $"Conecte os {_board.TotalPairs} pares de cores sem cruzar os caminhos.",
            2 => $"Conecte os {_board.TotalPairs} pares e cubra pelo menos {_board.RequiredCoveragePercent}% da grade.",
            3 => $"Conecte os {_board.TotalPairs} pares e preencha toda a grade.",
            _ => $"Conecte os {_board.TotalPairs} pares de cores."
        };
    }

    private void OnPairCompleted(int completedPairs, int totalPairs)
    {
        if (_missionFinished || _isFinalizingMission)
        {
            return;
        }

        _missionHud.SetProgress(completedPairs, totalPairs, "Caminhos");
        _feedbackLabel.Text = "Caminho concluído. Clique em uma extremidade para refazê-lo.";
        _feedbackLabel.Modulate = new Color("#8fc9a5");
        UpdateCoverageText();
    }

    private void OnInvalidMove()
    {
        if (_missionFinished || _isFinalizingMission)
        {
            return;
        }

        _failures++;
        int remainingAttempts = Math.Max(0, _maximumFailures - _failures);
        _missionHud.SetAttempts(remainingAttempts, _maximumFailures);
        _feedbackLabel.Text = "Tentativa inválida. O caminho deve terminar no ponto correspondente.";
        _feedbackLabel.Modulate = new Color("#e18b96");

        GD.Print(
            $"Erro em Conectar Pontos: Erros={_failures}/{_maximumFailures}, " +
            $"Motivo={_board.LastInvalidMoveReason}");

        if (_failures >= _maximumFailures)
        {
            _ = FinishMissionAsync(
                false,
                "Limite de tentativas inválidas atingido");
        }
    }

    private void OnCoverageRequired(int currentPercent, int requiredPercent)
    {
        if (_missionFinished || _isFinalizingMission)
        {
            return;
        }

        _feedbackLabel.Text =
            $"Todos os pares estão conectados, mas a cobertura é {currentPercent}%. " +
            $"Refaça os caminhos até atingir {requiredPercent}%.";
        _feedbackLabel.Modulate = new Color("#f0c674");
        UpdateCoverageText();
    }

    private void OnBoardCompleted()
    {
        if (!_missionFinished && !_isFinalizingMission)
        {
            _ = FinishMissionAsync(true, "Tabuleiro concluído");
        }
    }

    private async Task FinishMissionAsync(bool success, string finishReason)
    {
        if (_missionFinished || _isFinalizingMission)
        {
            return;
        }

        _isFinalizingMission = true;
        _board.SetInputEnabled(false);

        Result = new MissionResult
        {
            MissionId = _mission.Id,
            CompletionTime = _elapsedTime,
            Failures = _failures,
            Success = success,
            Persistence = CalculatePersistence(success, _failures)
        };

        _resultRegistered = await _sessionManager.RegisterCurrentMissionResultAsync(Result);

        _missionFinished = true;
        _isFinalizingMission = false;
        _missionHud.SetVisibleState(false);
        GetNode<Control>("../PuzzleInterface/PuzzleArea").Visible = false;

        _resultPopup.ShowResult(
            success,
            _mission.Name,
            _objectiveText,
            _elapsedTime,
            "Caminhos conectados",
            $"{_board.CompletedPairs}/{_board.TotalPairs} | {_board.CoveragePercent}% coverage",
            GetDifficultyText(_mission.Difficulty),
            _failures);

        GD.Print($"MotivoEncerramento={finishReason}");
        PrintMissionResult();
    }

    private async void OnContinueRequested()
    {
        if (!_missionFinished || Result is null)
        {
            return;
        }

        if (!_resultRegistered)
        {
            GD.PushError("O evento comportamental não foi registrado. Não é possível avançar.");
            return;
        }

        bool continued = await _sessionManager.ContinueAfterCurrentMissionAsync(GetTree());
        if (!continued)
        {
            GD.PushError("Não foi possível continuar o fluxo da sessão.");
        }
    }

    private void UpdateCoverageText()
    {
        _coverageLabel.Text =
            $"Cobertura: {_board.CoveragePercent}% / {_board.RequiredCoveragePercent}%";
    }

    private void ShowInitializationError(string message)
    {
        _missionFinished = true;
        _missionHud.SetVisibleState(false);
        GetNode<Control>("../PuzzleInterface/PuzzleArea").Visible = false;
        _resultPopup.ShowResult(
            false,
            "Conectar Pontos",
            message,
            0,
            "Inicialização",
            "Falhou",
            "-",
            1);
        GD.PushError(message);
    }

    private void PrintMissionResult()
    {
        if (Result is null)
        {
            return;
        }

        GD.Print("Resultado da missão Conectar Pontos:");
        GD.Print($"MissionId={Result.MissionId}");
        GD.Print($"CompletionTime={Result.CompletionTime.ToString("F2", CultureInfo.InvariantCulture)}");
        GD.Print($"Failures={Result.Failures}");
        GD.Print($"Success={Result.Success}");
        GD.Print($"Coverage={_board.CoveragePercent}%");
        GD.Print($"Persistence={Result.Persistence.ToString("F2", CultureInfo.InvariantCulture)}");
        GD.Print($"EventRegistered={_resultRegistered}");
    }

    private static int GetGridSize(int difficulty) => difficulty switch
    {
        1 => 5,
        2 => 6,
        3 => 7,
        _ => 5
    };

    private static double CalculatePersistence(bool success, int failures) =>
        success
            ? 1.0
            : Math.Max(0.0, 1.0 - Math.Max(0, failures) * 0.2);

    private static string GetDifficultyText(int difficulty) => difficulty switch
    {
        1 => "Fácil",
        2 => "Médio",
        3 => "Difícil",
        _ => $"Level {difficulty}"
    };
}
