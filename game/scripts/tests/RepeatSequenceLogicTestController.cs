using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AdaptiveTrials.Game.Missions.Puzzle.Shared;
using Godot;

namespace AdaptiveTrials.Game.Tests;

/// <summary>
/// Teste isolado da lógica de Repetir Sequência, sem sessão ou API.
/// </summary>
public partial class RepeatSequenceLogicTestController : Node
{
    [ExportGroup("Test Settings")]
    [Export(PropertyHint.Range, "1,3,1")]
    public int Difficulty { get; set; } = 2;

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

    private readonly List<PuzzleRuneButton> _buttons = new();
    private readonly List<int> _sequence = new();
    private readonly RandomNumberGenerator _random = new();

    private Label _phaseLabel = null!;
    private Label _statusLabel = null!;
    private Label _progressLabel = null!;
    private Label _attemptsLabel = null!;

    private int _sequenceSize;
    private int _maximumFailures;
    private int _failures;
    private int _inputIndex;
    private int _presentationGeneration;
    private float _highlightSeconds;
    private float _pauseSeconds;
    private bool _acceptingInput;
    private bool _finished;

    public override void _Ready()
    {
        const string path = "../Interface/PuzzleArea/";
        _phaseLabel = GetNode<Label>(path + "PhaseLabel");
        _statusLabel = GetNode<Label>(path + "StatusLabel");
        _progressLabel = GetNode<Label>(path + "ProgressLabel");
        _attemptsLabel = GetNode<Label>(path + "AttemptsLabel");

        GridContainer grid = GetNode<GridContainer>(path + "RuneGrid");
        foreach (Node child in grid.GetChildren())
        {
            if (child is not PuzzleRuneButton button)
            {
                continue;
            }

            _buttons.Add(button);
            button.RuneSelected += OnRuneSelected;
        }

        if (_buttons.Count != 9)
        {
            GD.PushError($"O teste requer 9 botões, mas encontrou {_buttons.Count}.");
            return;
        }

        CallDeferred(MethodName.StartTest);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }

        if (key.Keycode == Key.R)
        {
            GetViewport().SetInputAsHandled();
            StartTest();
            return;
        }

        if (!_acceptingInput || _finished)
        {
            return;
        }

        int index = KeyToRuneIndex(key.Keycode);
        if (index < 0 || index >= _buttons.Count)
        {
            return;
        }

        GetViewport().SetInputAsHandled();
        OnRuneSelected(index);
    }

    private void StartTest()
    {
        _presentationGeneration++;
        _acceptingInput = false;
        _finished = false;
        _failures = 0;
        _inputIndex = 0;

        int difficulty = Math.Clamp(Difficulty, 1, 3);
        _sequenceSize = difficulty switch { 1 => 3, 2 => 5, 3 => 8, _ => 5 };
        _maximumFailures = difficulty switch { 1 => 4, 2 => 3, 3 => 2, _ => 3 };
        _highlightSeconds = difficulty switch { 1 => 0.62f, 2 => 0.50f, 3 => 0.42f, _ => 0.50f };
        _pauseSeconds = difficulty switch { 1 => 0.22f, 2 => 0.18f, 3 => 0.15f, _ => 0.18f };

        for (int i = 0; i < _buttons.Count; i++)
        {
            _buttons[i].Configure(i, _symbols[i], _colors[i]);
            _buttons[i].SetInputEnabled(false);
        }

        GenerateSequence();
        UpdateAttempts();
        _phaseLabel.Text = "MEMORIZE";
        _progressLabel.Text = $"Sequência: {_sequenceSize} runas";
        _statusLabel.Text = "Observe os quadrados destacados.";
        _statusLabel.Modulate = Colors.White;
        _ = PresentSequenceAsync();
    }

    private void GenerateSequence()
    {
        _sequence.Clear();
        _random.Randomize();
        int previous = -1;

        for (int i = 0; i < _sequenceSize; i++)
        {
            int next = _random.RandiRange(0, _buttons.Count - 1);
            if (_buttons.Count > 1 && next == previous)
            {
                next = (next + _random.RandiRange(1, _buttons.Count - 1)) % _buttons.Count;
            }

            _sequence.Add(next);
            previous = next;
        }
    }

    private async Task PresentSequenceAsync()
    {
        int generation = ++_presentationGeneration;
        _acceptingInput = false;
        SetButtonsEnabled(false);
        _phaseLabel.Text = "MEMORIZE";
        _statusLabel.Text = "Observe os quadrados destacados.";
        _progressLabel.Text = $"Sequência: {_sequenceSize} runas";

        await WaitAsync(0.55f);

        for (int i = 0; i < _sequence.Count; i++)
        {
            if (!CanContinue(generation))
            {
                return;
            }

            PuzzleRuneButton button = _buttons[_sequence[i]];
            button.ShowHighlight();
            _progressLabel.Text = $"Exibindo: {i + 1}/{_sequenceSize}";
            await WaitAsync(_highlightSeconds);
            button.RestoreAppearance();
            await WaitAsync(_pauseSeconds);
        }

        if (!CanContinue(generation))
        {
            return;
        }

        _inputIndex = 0;
        _acceptingInput = true;
        SetButtonsEnabled(true);
        _phaseLabel.Text = "REPITA";
        _statusLabel.Text = "Repita clicando na grade ou usando as teclas 1–9.";
        _progressLabel.Text = $"Progresso: 0/{_sequenceSize}";
    }

    private void OnRuneSelected(int runeIndex)
    {
        if (!_acceptingInput || _finished || _inputIndex >= _sequence.Count)
        {
            return;
        }

        if (runeIndex != _sequence[_inputIndex])
        {
            _ = HandleWrongInputAsync(runeIndex);
            return;
        }

        _ = FlashSuccessAsync(runeIndex);
        _inputIndex++;
        _progressLabel.Text = $"Progresso: {_inputIndex}/{_sequenceSize}";
        _statusLabel.Text = "Correto.";
        _statusLabel.Modulate = new Color("#8fc9a5");

        if (_inputIndex >= _sequenceSize)
        {
            Finish(true);
        }
    }

    private async Task HandleWrongInputAsync(int runeIndex)
    {
        _acceptingInput = false;
        SetButtonsEnabled(false);
        _failures++;
        UpdateAttempts();

        _buttons[runeIndex].ShowError();
        _phaseLabel.Text = "INCORRETO";
        _statusLabel.Text = "A mesma sequência será exibida novamente.";
        _statusLabel.Modulate = new Color("#d8808c");
        await WaitAsync(0.55f);
        _buttons[runeIndex].RestoreAppearance();

        if (_failures >= _maximumFailures)
        {
            Finish(false);
            return;
        }

        _inputIndex = 0;
        await WaitAsync(0.30f);
        await PresentSequenceAsync();
    }

    private async Task FlashSuccessAsync(int runeIndex)
    {
        PuzzleRuneButton button = _buttons[runeIndex];
        button.ShowSuccess();
        await WaitAsync(0.16f);
        if (!_finished)
        {
            button.RestoreAppearance();
        }
    }

    private void Finish(bool success)
    {
        _finished = true;
        _acceptingInput = false;
        _presentationGeneration++;
        SetButtonsEnabled(false);
        _phaseLabel.Text = success ? "SUCESSO" : "FALHA";
        _statusLabel.Text = success
            ? "Sequência concluída. Pressione R para gerar outra."
            : "Limite de erros atingido. Pressione R para tentar novamente.";
        _statusLabel.Modulate = success ? new Color("#8fc9a5") : new Color("#d8808c");
        _progressLabel.Text = success
            ? $"Resultado: {_sequenceSize}/{_sequenceSize}"
            : $"Resultado: {_inputIndex}/{_sequenceSize}";
    }

    private void UpdateAttempts()
    {
        int remaining = Math.Max(0, _maximumFailures - _failures);
        _attemptsLabel.Text = $"Tentativas: {remaining}/{_maximumFailures}";
    }

    private void SetButtonsEnabled(bool enabled)
    {
        foreach (PuzzleRuneButton button in _buttons)
        {
            button.SetInputEnabled(enabled);
        }
    }

    private bool CanContinue(int generation) =>
        generation == _presentationGeneration && !_finished && IsInsideTree();

    private async Task WaitAsync(float seconds)
    {
        await ToSignal(
            GetTree().CreateTimer(Math.Max(0.01f, seconds)),
            SceneTreeTimer.SignalName.Timeout);
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
}
