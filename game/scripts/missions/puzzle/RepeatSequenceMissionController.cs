using System;
using System.Collections.Generic;
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
/// Controla a missão em que o jogador observa e repete uma sequência em uma grade 3x3.
/// </summary>
public partial class RepeatSequenceMissionController : Node
{
    private readonly string[] _symbols =
    {
        "◆", "●", "▲",
        "✦", "■", "⬟",
        "✚", "◇", "★"
    };

    private readonly Color[] _colors =
    {
        new("#8f72b5"), new("#5f91aa"), new("#b2788f"),
        new("#a58a55"), new("#6d80b3"), new("#8a6e9f"),
        new("#5f9b8b"), new("#b47a65"), new("#7d739f")
    };

    private SessionManager _sessionManager = null!;
    private MissionHud _missionHud = null!;
    private MissionResultPopup _resultPopup = null!;
    private Label _phaseLabel = null!;
    private Label _instructionLabel = null!;
    private Label _sequenceProgressLabel = null!;
    private Label _feedbackLabel = null!;
    private Control _puzzleArea = null!;

    private readonly List<PuzzleRuneButton> _runeButtons = new();
    private readonly List<int> _sequence = new();
    private readonly RandomNumberGenerator _random = new();

    private MissionDto _mission = null!;
    private int _sequenceSize;
    private int _maximumFailures;
    private int _failures;
    private int _inputIndex;
    private int _presentationGeneration;
    private double _elapsedTime;
    private float _highlightSeconds;
    private float _pauseSeconds;

    private bool _acceptingInput;
    private bool _missionFinished;
    private bool _isFinalizingMission;
    private bool _resultRegistered;

    private string _objectiveText = string.Empty;

    public MissionResult? Result { get; private set; }

    public override void _Ready()
    {
        GetReferences();
        _resultPopup.ContinueRequested += OnContinueRequested;
        _sessionManager = GetNode<SessionManager>("/root/SessionManager");

        MissionDto? currentMission = _sessionManager.CurrentMission;
        if (currentMission is null)
        {
            ShowInitializationError("No active mission was found.");
            return;
        }

        _mission = currentMission;
        if (_mission.Type != MissionType.Puzzle)
        {
            ShowInitializationError("The active mission is not a puzzle mission.");
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

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_acceptingInput || _missionFinished || _isFinalizingMission)
        {
            return;
        }

        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
        {
            return;
        }

        int runeIndex = KeyToRuneIndex(keyEvent.Keycode);
        if (runeIndex < 0 || runeIndex >= _runeButtons.Count)
        {
            return;
        }

        GetViewport().SetInputAsHandled();
        HandleRuneSelected(runeIndex);
    }

    private void GetReferences()
    {
        _missionHud = GetNode<MissionHud>("../MissionHud");
        _resultPopup = GetNode<MissionResultPopup>("../MissionResultPopup");
        _puzzleArea = GetNode<Control>("../PuzzleInterface/PuzzleArea");

        const string path = "../PuzzleInterface/PuzzleArea/";
        _phaseLabel = GetNode<Label>(path + "PhaseLabel");
        _instructionLabel = GetNode<Label>(path + "InstructionLabel");
        _sequenceProgressLabel = GetNode<Label>(path + "SequenceProgressLabel");
        _feedbackLabel = GetNode<Label>(path + "FeedbackLabel");

        GridContainer grid = GetNode<GridContainer>(path + "RuneGrid");
        foreach (Node child in grid.GetChildren())
        {
            if (child is not PuzzleRuneButton runeButton)
            {
                continue;
            }

            _runeButtons.Add(runeButton);
            runeButton.RuneSelected += HandleRuneSelected;
        }
    }

    private void ReadSettings()
    {
        _sequenceSize = _mission.Difficulty switch
        {
            1 => 3,
            2 => 5,
            3 => 8,
            _ => 3
        };

        _maximumFailures = _mission.Difficulty switch
        {
            1 => 4,
            2 => 3,
            3 => 2,
            _ => 3
        };

        _highlightSeconds = _mission.Difficulty switch
        {
            1 => 0.62f,
            2 => 0.50f,
            3 => 0.42f,
            _ => 0.52f
        };

        _pauseSeconds = _mission.Difficulty switch
        {
            1 => 0.22f,
            2 => 0.18f,
            3 => 0.15f,
            _ => 0.18f
        };

        if (string.IsNullOrWhiteSpace(_mission.ParametersJson))
        {
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(_mission.ParametersJson);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("sequenceSize", out JsonElement sizeElement) &&
                sizeElement.TryGetInt32(out int configuredSize))
            {
                _sequenceSize = Math.Clamp(configuredSize, 2, 12);
            }

            if (root.TryGetProperty("maxFailures", out JsonElement failuresElement) &&
                failuresElement.TryGetInt32(out int configuredFailures))
            {
                _maximumFailures = Math.Clamp(configuredFailures, 1, 8);
            }
        }
        catch (JsonException exception)
        {
            GD.PushWarning(
                $"ParametersJson inválido em Repetir Sequência. " +
                $"Valores padrão serão usados. {exception.Message}");
        }
    }

    private void ConfigureMission()
    {
        if (_runeButtons.Count != 9)
        {
            ShowInitializationError(
                $"The sequence board requires 9 rune buttons, but {_runeButtons.Count} were found.");
            return;
        }

        _failures = 0;
        _inputIndex = 0;
        _elapsedTime = 0;
        _missionFinished = false;
        _isFinalizingMission = false;
        _resultRegistered = false;
        _acceptingInput = false;
        Result = null;

        _objectiveText =
            $"Watch the {_sequenceSize}-rune sequence and repeat it in the same order.";

        for (int index = 0; index < _runeButtons.Count; index++)
        {
            _runeButtons[index].Configure(index, _symbols[index], _colors[index]);
            _runeButtons[index].SetInputEnabled(false);
        }

        GenerateSequence();

        _missionHud.Configure(
            _mission.Name,
            _mission.Type,
            _objectiveText,
            _sequenceSize);
        _missionHud.SetProgress(0, _sequenceSize, "Sequence");
        _missionHud.SetAttempts(_maximumFailures, _maximumFailures);
        _missionHud.SetVisibleState(true);

        _phaseLabel.Text = "MEMORIZE";
        _instructionLabel.Text = "Watch the grid carefully.";
        _sequenceProgressLabel.Text = $"Sequence: {_sequenceSize} runes";
        _feedbackLabel.Text = string.Empty;
        _feedbackLabel.Modulate = Colors.White;
        _puzzleArea.Visible = true;
        _resultPopup.HidePopup();

        GD.Print(
            $"Missão Repetir Sequência iniciada: " +
            $"MissionId={_mission.Id}, " +
            $"SequenceSize={_sequenceSize}, " +
            $"MaxFailures={_maximumFailures}, " +
            $"Difficulty={_mission.Difficulty}");

        CallDeferred(MethodName.StartPresentationDeferred);
    }

    private void StartPresentationDeferred()
    {
        _ = PresentSequenceAsync();
    }

    private void GenerateSequence()
    {
        _sequence.Clear();
        _random.Randomize();

        int previous = -1;
        for (int index = 0; index < _sequenceSize; index++)
        {
            int next = _random.RandiRange(0, _runeButtons.Count - 1);
            if (_runeButtons.Count > 1 && next == previous)
            {
                next = (next + _random.RandiRange(1, _runeButtons.Count - 1)) % _runeButtons.Count;
            }

            _sequence.Add(next);
            previous = next;
        }
    }

    private async Task PresentSequenceAsync()
    {
        int generation = ++_presentationGeneration;
        _acceptingInput = false;
        SetButtonsInputEnabled(false);

        _phaseLabel.Text = "MEMORIZE";
        _instructionLabel.Text = "Watch the highlighted squares.";
        _sequenceProgressLabel.Text = $"Sequence: {_sequenceSize} runes";
        _feedbackLabel.Text = string.Empty;
        _missionHud.SetProgress(0, _sequenceSize, "Sequence");

        await WaitAsync(0.55f);

        for (int index = 0; index < _sequence.Count; index++)
        {
            if (!CanContinuePresentation(generation))
            {
                return;
            }

            PuzzleRuneButton button = _runeButtons[_sequence[index]];
            button.ShowHighlight();
            _sequenceProgressLabel.Text = $"Showing {index + 1}/{_sequenceSize}";
            await WaitAsync(_highlightSeconds);
            button.RestoreAppearance();
            await WaitAsync(_pauseSeconds);
        }

        if (!CanContinuePresentation(generation))
        {
            return;
        }

        _inputIndex = 0;
        _acceptingInput = true;
        SetButtonsInputEnabled(true);
        _phaseLabel.Text = "REPEAT";
        _instructionLabel.Text = "Repeat the order by clicking the grid or using keys 1–9.";
        _sequenceProgressLabel.Text = $"Your input: 0/{_sequenceSize}";
    }

    private void HandleRuneSelected(int runeIndex)
    {
        if (!_acceptingInput || _missionFinished || _isFinalizingMission || _inputIndex >= _sequence.Count)
        {
            return;
        }

        if (runeIndex < 0 || runeIndex >= _runeButtons.Count)
        {
            return;
        }

        if (runeIndex != _sequence[_inputIndex])
        {
            _ = HandleIncorrectInputAsync(runeIndex);
            return;
        }

        _ = FlashPlayerInputAsync(runeIndex);
        _inputIndex++;
        _missionHud.SetProgress(_inputIndex, _sequenceSize, "Sequence");
        _sequenceProgressLabel.Text = $"Your input: {_inputIndex}/{_sequenceSize}";
        _feedbackLabel.Text = "Correct";
        _feedbackLabel.Modulate = new Color("#8fc9a5");

        if (_inputIndex >= _sequenceSize)
        {
            _ = FinishMissionAsync(true, "Sequência reproduzida corretamente");
        }
    }

    private async Task HandleIncorrectInputAsync(int runeIndex)
    {
        if (!_acceptingInput || _missionFinished || _isFinalizingMission)
        {
            return;
        }

        _acceptingInput = false;
        SetButtonsInputEnabled(false);
        _failures++;

        int remainingAttempts = Math.Max(0, _maximumFailures - _failures);
        _missionHud.SetAttempts(remainingAttempts, _maximumFailures);
        _runeButtons[runeIndex].ShowError();
        _phaseLabel.Text = "INCORRECT";
        _instructionLabel.Text = "The same sequence will be shown again.";
        _feedbackLabel.Text = "Wrong square";
        _feedbackLabel.Modulate = new Color("#d8808c");

        GD.Print(
            $"Falha em Repetir Sequência: " +
            $"Failures={_failures}/{_maximumFailures}, " +
            $"InputIndex={_inputIndex}");

        await WaitAsync(0.55f);
        _runeButtons[runeIndex].RestoreAppearance();

        if (_failures >= _maximumFailures)
        {
            await FinishMissionAsync(false, "Limite de tentativas incorretas atingido");
            return;
        }

        _inputIndex = 0;
        await WaitAsync(0.30f);
        await PresentSequenceAsync();
    }

    private async Task FlashPlayerInputAsync(int runeIndex)
    {
        PuzzleRuneButton button = _runeButtons[runeIndex];
        button.ShowSuccess();
        await WaitAsync(0.16f);

        if (!_missionFinished && !_isFinalizingMission)
        {
            button.RestoreAppearance();
        }
    }

    private async Task FinishMissionAsync(bool success, string finishReason)
    {
        if (_missionFinished || _isFinalizingMission)
        {
            return;
        }

        _isFinalizingMission = true;
        _acceptingInput = false;
        _presentationGeneration++;
        SetButtonsInputEnabled(false);

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
        _puzzleArea.Visible = false;
        _missionHud.SetVisibleState(false);

        _resultPopup.ShowResult(
            success,
            _mission.Name,
            _objectiveText,
            _elapsedTime,
            "Sequence Repeated",
            success ? $"{_sequenceSize}/{_sequenceSize}" : $"{_inputIndex}/{_sequenceSize}",
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

    private void SetButtonsInputEnabled(bool enabled)
    {
        foreach (PuzzleRuneButton button in _runeButtons)
        {
            button.SetInputEnabled(enabled);
        }
    }

    private bool CanContinuePresentation(int generation)
    {
        return generation == _presentationGeneration &&
               !_missionFinished &&
               !_isFinalizingMission &&
               IsInsideTree();
    }

    private async Task WaitAsync(float seconds)
    {
        if (!IsInsideTree())
        {
            return;
        }

        await ToSignal(
            GetTree().CreateTimer(Math.Max(0.01f, seconds)),
            SceneTreeTimer.SignalName.Timeout);
    }

    private void ShowInitializationError(string message)
    {
        _missionFinished = true;
        _acceptingInput = false;
        _presentationGeneration++;
        _missionHud.SetVisibleState(false);
        _puzzleArea.Visible = false;
        _resultPopup.ShowResult(false, "Repeat Sequence", message, 0, "Initialization", "Failed", "-", 1);
        GD.PushError(message);
    }

    private void PrintMissionResult()
    {
        if (Result is null)
        {
            return;
        }

        GD.Print("Resultado da missão Repetir Sequência:");
        GD.Print($"MissionId={Result.MissionId}");
        GD.Print($"CompletionTime={Result.CompletionTime.ToString("F2", CultureInfo.InvariantCulture)}");
        GD.Print($"Failures={Result.Failures}");
        GD.Print($"Success={Result.Success}");
        GD.Print($"Persistence={Result.Persistence.ToString("F2", CultureInfo.InvariantCulture)}");
        GD.Print($"EventRegistered={_resultRegistered}");
    }

    private static int KeyToRuneIndex(Key keycode)
    {
        return keycode switch
        {
            Key.Key1 or Key.Kp1 => 0,
            Key.Key2 or Key.Kp2 => 1,
            Key.Key3 or Key.Kp3 => 2,
            Key.Key4 or Key.Kp4 => 3,
            Key.Key5 or Key.Kp5 => 4,
            Key.Key6 or Key.Kp6 => 5,
            Key.Key7 or Key.Kp7 => 6,
            Key.Key8 or Key.Kp8 => 7,
            Key.Key9 or Key.Kp9 => 8,
            _ => -1
        };
    }

    private static double CalculatePersistence(bool success, int failures) =>
        success
            ? 1.0
            : Math.Max(0.0, 1.0 - Math.Max(0, failures) * 0.2);

    private static string GetDifficultyText(int difficulty)
    {
        return difficulty switch
        {
            1 => "Easy",
            2 => "Medium",
            3 => "Hard",
            _ => $"Level {difficulty}"
        };
    }
}
