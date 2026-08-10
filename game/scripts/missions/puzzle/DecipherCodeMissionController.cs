using System;
using System.Globalization;
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
/// Controla a missão de dedução de sequências no estilo Termo.
/// </summary>
public partial class DecipherCodeMissionController : Node
{
    private SessionManager _sessionManager = null!;
    private MissionHud _missionHud = null!;
    private MissionResultPopup _resultPopup = null!;
    private DecipherCodeBoard _board = null!;
    private Control _puzzleArea = null!;

    private MissionDto _mission = null!;
    private int _boardCount;
    private int _maximumAttempts;
    private int _failures;
    private int _bestSolvedBoards;
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

        _board.GuessEvaluated += OnGuessEvaluated;
        _board.CodeSolved += OnCodeSolved;
        _board.AttemptsExhausted += OnAttemptsExhausted;
        _resultPopup.ContinueRequested += OnContinueRequested;

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
    }

    private void GetReferences()
    {
        _missionHud = GetNode<MissionHud>("../MissionHud");
        _resultPopup = GetNode<MissionResultPopup>("../MissionResultPopup");
        _puzzleArea = GetNode<Control>("../PuzzleInterface/PuzzleArea");
        _board = GetNode<DecipherCodeBoard>(
            "../PuzzleInterface/PuzzleArea/BoardCenter/DecipherCodeBoard");
    }

    private void ReadSettings()
    {
        _boardCount = _mission.Difficulty switch
        {
            1 => 1,
            2 => 2,
            3 => 4,
            _ => 1
        };

        _maximumAttempts = _mission.Difficulty switch
        {
            1 => 6,
            2 => 7,
            3 => 9,
            _ => 6
        };
    }

    private void ConfigureMission()
    {
        _failures = 0;
        _bestSolvedBoards = 0;
        _elapsedTime = 0;
        _missionFinished = false;
        _isFinalizingMission = false;
        _resultRegistered = false;
        Result = null;

        _objectiveText = _boardCount switch
        {
            1 => "Descubra uma sequência oculta de cinco símbolos.",
            2 => "Descubra duas sequências ocultas ao mesmo tempo.",
            _ => "Descubra quatro sequências ocultas ao mesmo tempo."
        };

        _missionHud.Configure(
            _mission.Name,
            _mission.Type,
            _objectiveText,
            _boardCount);
        _missionHud.SetProgress(0, _boardCount, "Sequências descobertas");
        _missionHud.SetAttempts(_maximumAttempts, _maximumAttempts);
        _missionHud.SetVisibleState(true);

        _puzzleArea.Visible = true;
        _resultPopup.HidePopup();
        _board.Configure(_boardCount, _maximumAttempts);

        GD.Print(
            $"Missão Decifrar Código iniciada: MissionId={_mission.Id}, " +
            $"Boards={_boardCount}, MaxAttempts={_maximumAttempts}, " +
            $"Difficulty={_mission.Difficulty}");
    }

    private void OnGuessEvaluated(
        int solvedBoards,
        int presentSymbols,
        int remainingAttempts)
    {
        if (_missionFinished || _isFinalizingMission)
        {
            return;
        }

        _bestSolvedBoards = Math.Max(_bestSolvedBoards, solvedBoards);
        _missionHud.SetProgress(
            _bestSolvedBoards,
            _boardCount,
            "Sequências descobertas");
        _missionHud.SetAttempts(remainingAttempts, _maximumAttempts);

        if (solvedBoards < _boardCount)
        {
            _failures++;
        }

        GD.Print(
            $"Tentativa em Decifrar Código: " +
            $"Solved={solvedBoards}/{_boardCount}, " +
            $"PresentSymbols={presentSymbols}, " +
            $"Remaining={remainingAttempts}");
    }

    private void OnCodeSolved(int attemptsUsed)
    {
        if (!_missionFinished && !_isFinalizingMission)
        {
            _ = FinishMissionAsync(true, "Todas as sequências foram descobertas");
        }
    }

    private void OnAttemptsExhausted(
        int solvedBoards,
        int presentSymbols)
    {
        if (!_missionFinished && !_isFinalizingMission)
        {
            _bestSolvedBoards = Math.Max(_bestSolvedBoards, solvedBoards);
            _ = FinishMissionAsync(false, "Tentativas esgotadas");
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

        _resultRegistered =
            await _sessionManager.RegisterCurrentMissionResultAsync(Result);

        _missionFinished = true;
        _isFinalizingMission = false;
        _missionHud.SetVisibleState(false);
        _puzzleArea.Visible = false;

        _resultPopup.ShowResult(
            success,
            _mission.Name,
            _objectiveText,
            _elapsedTime,
            "Sequências descobertas",
            $"{_bestSolvedBoards}/{_boardCount}",
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
            GD.PushError(
                "O evento comportamental não foi registrado. " +
                "Não é possível avançar.");
            return;
        }

        bool continued =
            await _sessionManager
                .ContinueAfterCurrentMissionAsync(GetTree());

        if (!continued)
        {
            GD.PushError(
                "Não foi possível continuar o fluxo da sessão.");
        }
    }

    private void ShowInitializationError(string message)
    {
        _missionFinished = true;
        _missionHud.SetVisibleState(false);
        _puzzleArea.Visible = false;
        _resultPopup.ShowResult(
            false,
            "Decifrar Código",
            message,
            0,
            "Initialization",
            "Falhou",
            "-",
            1);
        GD.PushError(message);
    }

    private static double CalculatePersistence(bool success, int failures)
    {
        return success
            ? 1.0
            : Math.Max(0d, 1d - (Math.Max(0, failures) * 0.2d));
    }

    private static string GetDifficultyText(int difficulty)
    {
        return difficulty switch
        {
            1 => "Fácil",
            2 => "Médio",
            3 => "Difícil",
            _ => difficulty.ToString(CultureInfo.InvariantCulture)
        };
    }

    private void PrintMissionResult()
    {
        if (Result is null)
        {
            return;
        }

        GD.Print("Resultado da missão Decifrar Código:");
        GD.Print($"MissionId={Result.MissionId}");
        GD.Print(
            $"CompletionTime=" +
            Result.CompletionTime.ToString(
                "F2",
                CultureInfo.InvariantCulture));
        GD.Print($"Failures={Result.Failures}");
        GD.Print($"Success={Result.Success}");
        GD.Print(
            $"Persistence=" +
            Result.Persistence.ToString(
                "F2",
                CultureInfo.InvariantCulture));
        GD.Print($"EventRegistered={_resultRegistered}");
    }
}
