using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AdaptiveTrials.Game.Missions.Puzzle.Shared;

/// <summary>
/// Puzzle de dedução no estilo Termo usando sequências de cinco símbolos.
/// O mesmo palpite é aplicado a um, dois ou quatro tabuleiros.
/// Não depende de sessão, API ou HUD de missão.
/// </summary>
public partial class DecipherCodeBoard : Control
{
    [Signal]
    public delegate void GuessEvaluatedEventHandler(
        int solvedBoards,
        int presentSymbols,
        int remainingAttempts);

    [Signal]
    public delegate void CodeSolvedEventHandler(int attemptsUsed);

    [Signal]
    public delegate void AttemptsExhaustedEventHandler(
        int solvedBoards,
        int presentSymbols);

    private enum TileState
    {
        Empty,
        Filled,
        Absent,
        Present,
        Exact
    }

    private sealed class TileView
    {
        public PanelContainer Panel { get; init; } = null!;
        public Label Label { get; init; } = null!;
    }

    private sealed class GuessData
    {
        public List<int> Symbols { get; init; } = new();
        public List<TileState> Feedback { get; init; } = new();
    }

    private sealed class SequenceBoard
    {
        public List<int> Secret { get; init; } = new();
        public bool Solved { get; set; }
        public List<GuessData> History { get; } = new();
        public List<List<TileView>> Tiles { get; } = new();
    }

    private readonly string[] _symbols =
    {
        "◆", "●", "▲", "✦", "■", "⬟", "✚", "☾"
    };

    private readonly List<Color> _symbolColors = new()
    {
        new Color("#b99bdd"),
        new Color("#78bfe0"),
        new Color("#e49ab2"),
        new Color("#e4bd69"),
        new Color("#8fa6df"),
        new Color("#7bc2a3"),
        new Color("#d99ad8"),
        new Color("#9ba8d8")
    };

    private readonly List<SequenceBoard> _boards = new();
    private readonly List<int> _currentGuess = new();
    private readonly Dictionary<int, Button> _symbolButtons = new();
    private readonly Dictionary<int, TileState> _keyboardStates = new();
    private readonly RandomNumberGenerator _random = new();

    private Label _subtitleLabel = null!;
    private Label _feedbackLabel = null!;
    private Label _attemptLabel = null!;
    private GridContainer _boardsGrid = null!;
    private GridContainer _symbolGrid = null!;
    private Button _deleteButton = null!;
    private Button _submitButton = null!;

    private int _boardCount;
    private int _maximumAttempts;
    private int _attemptsUsed;
    private bool _inputEnabled;
    private bool _finished;

    public int SequenceLength => 5;
    public int RemainingAttempts => Math.Max(0, _maximumAttempts - _attemptsUsed);
    public int SolvedBoardsCount => _boards.Count(board => board.Solved);

    public override void _Ready()
    {
        _random.Randomize();
        GetReferences();
        BuildSymbolKeyboard();
        ConnectActions();
        ClearVisualState();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_inputEnabled || _finished || !@event.IsPressed() || @event.IsEcho())
        {
            return;
        }

        if (@event is not InputEventKey keyEvent)
        {
            return;
        }

        int symbolIndex = KeyToSymbolIndex(keyEvent.Keycode);
        if (symbolIndex >= 0)
        {
            AddSymbol(symbolIndex);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (keyEvent.Keycode is Key.Backspace or Key.Delete)
        {
            RemoveLastSymbol();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (keyEvent.Keycode is Key.Enter or Key.KpEnter)
        {
            SubmitGuess();
            GetViewport().SetInputAsHandled();
        }
    }

    public void Configure(int boardCount, int maximumAttempts)
    {
        _boardCount = Math.Clamp(boardCount, 1, 4);
        _maximumAttempts = Math.Clamp(maximumAttempts, 1, 10);
        _attemptsUsed = 0;
        _finished = false;
        _inputEnabled = true;

        _currentGuess.Clear();
        _keyboardStates.Clear();
        _boards.Clear();

        CreateBoards();
        UpdateSubtitle();
        UpdateAttemptLabel();
        RenderBoards();
        UpdateKeyboardVisuals();
        SetInputEnabled(true);

        _feedbackLabel.Text =
            "Monte uma sequência de 5 símbolos. Verde: posição certa. Dourado: existe em outra posição. Escuro: não existe.";
        _feedbackLabel.Modulate = new Color("#d8d0dc");

        GD.Print(
            $"Decifrar Código configurado: Boards={_boardCount}, " +
            $"Attempts={_maximumAttempts}, SequenceLength={SequenceLength}");
    }

    public void SetInputEnabled(bool enabled)
    {
        _inputEnabled = enabled && !_finished;

        foreach (Button button in _symbolButtons.Values)
        {
            button.Disabled = !_inputEnabled;
        }

        UpdateActionButtons();
    }

    private void GetReferences()
    {
        _subtitleLabel = GetNode<Label>("Margin/Content/SubtitleLabel");
        _boardsGrid = GetNode<GridContainer>("Margin/Content/BoardsGrid");
        _feedbackLabel = GetNode<Label>("Margin/Content/FeedbackLabel");
        _symbolGrid = GetNode<GridContainer>("Margin/Content/SymbolGrid");
        _deleteButton = GetNode<Button>("Margin/Content/Actions/DeleteButton");
        _submitButton = GetNode<Button>("Margin/Content/Actions/SubmitButton");
        _attemptLabel = GetNode<Label>("Margin/Content/AttemptLabel");
    }

    private void BuildSymbolKeyboard()
    {
        foreach (Node child in _symbolGrid.GetChildren())
        {
            child.QueueFree();
        }

        _symbolButtons.Clear();
        _symbolGrid.Columns = 8;

        for (int index = 0; index < _symbols.Length; index++)
        {
            int capturedIndex = index;
            Button button = new()
            {
                Text = _symbols[index],
                CustomMinimumSize = new Vector2(78, 50),
                FocusMode = FocusModeEnum.None,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                Theme = Theme
            };

            button.AddThemeFontSizeOverride("font_size", 25);
            button.Pressed += () => AddSymbol(capturedIndex);
            _symbolGrid.AddChild(button);
            _symbolButtons[index] = button;
        }
    }

    private void ConnectActions()
    {
        _deleteButton.Pressed += RemoveLastSymbol;
        _submitButton.Pressed += SubmitGuess;
    }

    private void CreateBoards()
    {
        foreach (Node child in _boardsGrid.GetChildren())
        {
            child.QueueFree();
        }

        _boardsGrid.Columns = _boardCount == 1 ? 1 : 2;

        for (int boardIndex = 0; boardIndex < _boardCount; boardIndex++)
        {
            SequenceBoard board = new();
            for (int position = 0; position < SequenceLength; position++)
            {
                board.Secret.Add(_random.RandiRange(0, _symbols.Length - 1));
            }

            PanelContainer card = new()
            {
                Theme = Theme,
                CustomMinimumSize = GetBoardMinimumSize()
            };
            card.AddThemeStyleboxOverride("panel", CreateBoardStyle());

            MarginContainer margin = new();
            margin.AddThemeConstantOverride("margin_left", 10);
            margin.AddThemeConstantOverride("margin_top", 8);
            margin.AddThemeConstantOverride("margin_right", 10);
            margin.AddThemeConstantOverride("margin_bottom", 8);
            card.AddChild(margin);

            VBoxContainer content = new() { Theme = Theme };
            content.AddThemeConstantOverride("separation", 6);
            margin.AddChild(content);

            if (_boardCount > 1)
            {
                Label boardTitle = new()
                {
                    Text = $"SEQUÊNCIA {boardIndex + 1}",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Theme = Theme
                };
                boardTitle.AddThemeFontSizeOverride("font_size", 13);
                boardTitle.AddThemeColorOverride("font_color", new Color("#c9c0cf"));
                content.AddChild(boardTitle);
            }

            GridContainer grid = new()
            {
                Columns = SequenceLength,
                Theme = Theme,
                SizeFlagsHorizontal = SizeFlags.ShrinkCenter
            };
            grid.AddThemeConstantOverride("h_separation", GetTileSeparation());
            grid.AddThemeConstantOverride("v_separation", GetTileSeparation());
            content.AddChild(grid);

            for (int row = 0; row < _maximumAttempts; row++)
            {
                List<TileView> tileRow = new();
                for (int column = 0; column < SequenceLength; column++)
                {
                    PanelContainer panel = CreateTilePanel();
                    Label label = new()
                    {
                        Text = string.Empty,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        MouseFilter = MouseFilterEnum.Ignore,
                        Theme = Theme
                    };
                    label.AddThemeFontSizeOverride("font_size", GetTileFontSize());
                    label.AddThemeColorOverride("font_color", Colors.White);

                    MarginContainer tileMargin = new();
                    tileMargin.AddChild(label);
                    panel.AddChild(tileMargin);
                    grid.AddChild(panel);
                    tileRow.Add(new TileView { Panel = panel, Label = label });
                }
                board.Tiles.Add(tileRow);
            }

            _boards.Add(board);
            _boardsGrid.AddChild(card);
        }
    }

    private PanelContainer CreateTilePanel()
    {
        PanelContainer panel = new()
        {
            Theme = Theme,
            CustomMinimumSize = GetTileSize()
        };
        panel.AddThemeStyleboxOverride(
            "panel",
            CreateTileStyle(new Color("#4f4551"), new Color("#6c6170")));
        return panel;
    }

    private void AddSymbol(int symbolIndex)
    {
        if (!_inputEnabled || _finished || _currentGuess.Count >= SequenceLength)
        {
            return;
        }

        if (symbolIndex < 0 || symbolIndex >= _symbols.Length)
        {
            return;
        }

        _currentGuess.Add(symbolIndex);
        RenderBoards();

        _feedbackLabel.Text = _currentGuess.Count == SequenceLength
            ? "Sequência completa. Pressione Enter ou VALIDAR."
            : $"Escolha mais {SequenceLength - _currentGuess.Count} símbolo(s).";
        _feedbackLabel.Modulate = new Color("#d8d0dc");
    }

    private void RemoveLastSymbol()
    {
        if (!_inputEnabled || _finished || _currentGuess.Count == 0)
        {
            return;
        }

        _currentGuess.RemoveAt(_currentGuess.Count - 1);
        RenderBoards();
        _feedbackLabel.Text = "Último símbolo removido.";
        _feedbackLabel.Modulate = new Color("#d8d0dc");
    }

    private void SubmitGuess()
    {
        if (!_inputEnabled || _finished)
        {
            return;
        }

        if (_currentGuess.Count != SequenceLength)
        {
            _feedbackLabel.Text = "Complete as cinco posições antes de validar.";
            _feedbackLabel.Modulate = new Color("#e18b96");
            return;
        }

        _attemptsUsed++;
        int presentCount = 0;

        foreach (SequenceBoard board in _boards)
        {
            if (board.Solved)
            {
                continue;
            }

            GuessData evaluation = EvaluateGuess(board.Secret, _currentGuess);
            board.History.Add(evaluation);
            presentCount += evaluation.Feedback.Count(
                state => state is TileState.Exact or TileState.Present);

            if (evaluation.Feedback.All(state => state == TileState.Exact))
            {
                board.Solved = true;
            }

            UpdateKeyboardState(evaluation);
        }

        _currentGuess.Clear();
        RenderBoards();
        UpdateKeyboardVisuals();
        UpdateAttemptLabel();

        EmitSignal(
            SignalName.GuessEvaluated,
            SolvedBoardsCount,
            presentCount,
            RemainingAttempts);

        if (SolvedBoardsCount == _boardCount)
        {
            _finished = true;
            _inputEnabled = false;
            SetInputEnabled(false);
            _feedbackLabel.Text = "TODAS AS SEQUÊNCIAS FORAM DECIFRADAS";
            _feedbackLabel.Modulate = new Color("#6aaa64");
            EmitSignal(SignalName.CodeSolved, _attemptsUsed);
            return;
        }

        if (_attemptsUsed >= _maximumAttempts)
        {
            _finished = true;
            _inputEnabled = false;
            SetInputEnabled(false);
            _feedbackLabel.Text = "As tentativas terminaram.";
            _feedbackLabel.Modulate = new Color("#e18b96");
            EmitSignal(
                SignalName.AttemptsExhausted,
                SolvedBoardsCount,
                presentCount);
            return;
        }

        _feedbackLabel.Text = _boardCount == 1
            ? "Use as cores para montar a próxima tentativa."
            : "A mesma tentativa foi aplicada a todos os tabuleiros ativos.";
        _feedbackLabel.Modulate = new Color("#c9b458");
    }

    private GuessData EvaluateGuess(
        IReadOnlyList<int> secret,
        IReadOnlyList<int> guess)
    {
        int[] remainingCounts = new int[_symbols.Length];
        TileState[] feedback = Enumerable
            .Repeat(TileState.Absent, SequenceLength)
            .ToArray();

        for (int index = 0; index < SequenceLength; index++)
        {
            remainingCounts[secret[index]]++;
        }

        // Primeiro marca posições exatas e consome essas ocorrências.
        for (int index = 0; index < SequenceLength; index++)
        {
            if (guess[index] != secret[index])
            {
                continue;
            }

            feedback[index] = TileState.Exact;
            remainingCounts[guess[index]]--;
        }

        // Depois marca ocorrências existentes em posições diferentes.
        // Isso preserva corretamente a quantidade de símbolos repetidos.
        for (int index = 0; index < SequenceLength; index++)
        {
            if (feedback[index] == TileState.Exact)
            {
                continue;
            }

            int symbol = guess[index];
            if (remainingCounts[symbol] > 0)
            {
                feedback[index] = TileState.Present;
                remainingCounts[symbol]--;
            }
            else
            {
                feedback[index] = TileState.Absent;
            }
        }

        return new GuessData
        {
            Symbols = guess.ToList(),
            Feedback = feedback.ToList()
        };
    }

    private void RenderBoards()
    {
        foreach (SequenceBoard board in _boards)
        {
            for (int row = 0; row < board.Tiles.Count; row++)
            {
                for (int column = 0; column < SequenceLength; column++)
                {
                    string symbol = string.Empty;
                    TileState state = TileState.Empty;

                    if (row < board.History.Count)
                    {
                        symbol = _symbols[board.History[row].Symbols[column]];
                        state = board.History[row].Feedback[column];
                    }
                    else if (!board.Solved &&
                             row == board.History.Count &&
                             column < _currentGuess.Count)
                    {
                        symbol = _symbols[_currentGuess[column]];
                        state = TileState.Filled;
                    }

                    ApplyTileVisual(board.Tiles[row][column], symbol, state);
                }
            }
        }

        UpdateActionButtons();
    }

    private void UpdateKeyboardState(GuessData evaluation)
    {
        for (int index = 0; index < evaluation.Symbols.Count; index++)
        {
            int symbol = evaluation.Symbols[index];
            TileState incoming = evaluation.Feedback[index];
            TileState current = _keyboardStates.TryGetValue(symbol, out TileState value)
                ? value
                : TileState.Empty;

            _keyboardStates[symbol] = StrongerState(current, incoming);
        }
    }

    private void UpdateKeyboardVisuals()
    {
        foreach ((int symbolIndex, Button button) in _symbolButtons)
        {
            TileState state = _keyboardStates.TryGetValue(symbolIndex, out TileState value)
                ? value
                : TileState.Empty;

            Color background = state switch
            {
                TileState.Absent => new Color("#3a3a3c"),
                TileState.Present => new Color("#c9b458"),
                TileState.Exact => new Color("#6aaa64"),
                _ => new Color("#786e7b")
            };

            Color border = state switch
            {
                TileState.Absent => new Color("#3a3a3c"),
                TileState.Present => new Color("#d8c26b"),
                TileState.Exact => new Color("#7cc276"),
                _ => new Color("#918694")
            };

            button.AddThemeStyleboxOverride(
                "normal",
                CreateKeyStyle(background, border));
            button.AddThemeStyleboxOverride(
                "hover",
                CreateKeyStyle(background.Lightened(0.08f), border));
            button.AddThemeStyleboxOverride(
                "pressed",
                CreateKeyStyle(background.Darkened(0.08f), border));
            button.AddThemeStyleboxOverride(
                "disabled",
                CreateKeyStyle(background.Darkened(0.08f), border.Darkened(0.05f)));
            button.AddThemeColorOverride("font_color", Colors.White);
            button.AddThemeColorOverride("font_disabled_color", Colors.White);
        }
    }

    private void UpdateSubtitle()
    {
        _subtitleLabel.Text = _boardCount switch
        {
            1 => "Descubra uma sequência de cinco símbolos.",
            2 => "Descubra duas sequências ao mesmo tempo.",
            _ => "Descubra quatro sequências ao mesmo tempo."
        };
    }

    private void UpdateAttemptLabel()
    {
        _attemptLabel.Text =
            $"Tentativas restantes: {RemainingAttempts}/{_maximumAttempts}";
    }

    private void UpdateActionButtons()
    {
        _deleteButton.Disabled = !_inputEnabled || _currentGuess.Count == 0;
        _submitButton.Disabled =
            !_inputEnabled || _currentGuess.Count != SequenceLength;
    }

    private void ClearVisualState()
    {
        _subtitleLabel.Text = string.Empty;
        _feedbackLabel.Text = string.Empty;
        _attemptLabel.Text = string.Empty;
        _currentGuess.Clear();
        _keyboardStates.Clear();
        UpdateKeyboardVisuals();
        UpdateActionButtons();
    }

    private void ApplyTileVisual(
        TileView tile,
        string symbol,
        TileState state)
    {
        Color background = state switch
        {
            TileState.Filled => new Color("#887d89"),
            TileState.Absent => new Color("#3a3a3c"),
            TileState.Present => new Color("#c9b458"),
            TileState.Exact => new Color("#6aaa64"),
            _ => new Color("#4f4551")
        };

        Color border = state switch
        {
            TileState.Filled => new Color("#a397a5"),
            TileState.Absent => new Color("#3a3a3c"),
            TileState.Present => new Color("#d8c26b"),
            TileState.Exact => new Color("#7cc276"),
            _ => new Color("#6c6170")
        };

        tile.Panel.AddThemeStyleboxOverride(
            "panel",
            CreateTileStyle(background, border));
        tile.Label.Text = symbol;
        tile.Label.AddThemeColorOverride("font_color", Colors.White);
    }

    private TileState StrongerState(TileState current, TileState incoming)
    {
        int Rank(TileState state) => state switch
        {
            TileState.Exact => 4,
            TileState.Present => 3,
            TileState.Absent => 2,
            TileState.Filled => 1,
            _ => 0
        };

        return Rank(incoming) > Rank(current) ? incoming : current;
    }

    private Vector2 GetBoardMinimumSize()
    {
        return _boardCount switch
        {
            1 => new Vector2(440, 390),
            2 => new Vector2(420, 300),
            _ => new Vector2(400, 205)
        };
    }

    private Vector2 GetTileSize()
    {
        return _boardCount switch
        {
            1 => new Vector2(56, 56),
            2 => new Vector2(42, 42),
            _ => new Vector2(30, 30)
        };
    }

    private int GetTileFontSize()
    {
        return _boardCount switch
        {
            1 => 25,
            2 => 20,
            _ => 15
        };
    }

    private int GetTileSeparation()
    {
        return _boardCount switch
        {
            1 => 6,
            2 => 5,
            _ => 3
        };
    }

    private StyleBoxFlat CreateBoardStyle()
    {
        return new StyleBoxFlat
        {
            BgColor = new Color("#241f28"),
            BorderColor = new Color("#382f3d"),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 14,
            CornerRadiusTopRight = 14,
            CornerRadiusBottomLeft = 14,
            CornerRadiusBottomRight = 14
        };
    }

    private StyleBoxFlat CreateTileStyle(Color background, Color border)
    {
        return new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = border,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6
        };
    }

    private StyleBoxFlat CreateKeyStyle(Color background, Color border)
    {
        return new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = border,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10
        };
    }

    private static int KeyToSymbolIndex(Key keycode)
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
            _ => -1
        };
    }
}
