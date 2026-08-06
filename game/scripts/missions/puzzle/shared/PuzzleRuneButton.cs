using Godot;

namespace AdaptiveTrials.Game.Missions.Puzzle.Shared;

/// <summary>
/// Botão reutilizável para entradas visuais das missões de quebra-cabeça.
/// </summary>
public partial class PuzzleRuneButton : Button
{
    [Signal]
    public delegate void RuneSelectedEventHandler(int runeIndex);

    [ExportGroup("Rune")]
    [Export] public int RuneIndex { get; set; }
    [Export] public string RuneSymbol { get; set; } = "◆";
    [Export] public Color RuneColor { get; set; } = new("#8f72b5");

    private readonly Color _disabledColor = new("#817985");
    private readonly Color _highlightColor = new("#f1d778");
    private readonly Color _successColor = new("#8fc9a5");
    private readonly Color _errorColor = new("#d8808c");

    private bool _inputEnabled = true;

    public override void _Ready()
    {
        Pressed += OnPressed;
        ApplyBaseAppearance();
    }

    public void Configure(int runeIndex, string symbol, Color color)
    {
        RuneIndex = runeIndex;
        RuneSymbol = string.IsNullOrWhiteSpace(symbol) ? "?" : symbol;
        RuneColor = color;
        ApplyBaseAppearance();
    }

    public void SetInputEnabled(bool enabled)
    {
        _inputEnabled = enabled;
        Disabled = !enabled;
        MouseDefaultCursorShape = enabled
            ? CursorShape.PointingHand
            : CursorShape.Arrow;

        if (!enabled)
        {
            ApplyAppearance(_disabledColor, new Color("#5e5664"), 1.0f);
        }
        else
        {
            ApplyBaseAppearance();
        }
    }

    public void ShowHighlight()
    {
        ApplyAppearance(_highlightColor, new Color("#b2943f"), 1.08f);
    }

    public void ShowSuccess()
    {
        ApplyAppearance(_successColor, new Color("#5d9572"), 1.05f);
    }

    public void ShowError()
    {
        ApplyAppearance(_errorColor, new Color("#a64f5c"), 1.05f);
    }

    public void RestoreAppearance()
    {
        if (_inputEnabled)
        {
            ApplyBaseAppearance();
            return;
        }

        ApplyAppearance(_disabledColor, new Color("#5e5664"), 1.0f);
    }

    private void OnPressed()
    {
        if (!_inputEnabled)
        {
            return;
        }

        EmitSignal(SignalName.RuneSelected, RuneIndex);
    }

    private void ApplyBaseAppearance()
    {
        Text = RuneSymbol;
        PivotOffset = Size / 2.0f;
        ApplyAppearance(RuneColor, RuneColor.Darkened(0.25f), 1.0f);
    }

    private void ApplyAppearance(Color background, Color border, float scale)
    {
        Text = RuneSymbol;
        Scale = Vector2.One * scale;

        StyleBoxFlat normal = CreateStyle(background, border);
        StyleBoxFlat hover = CreateStyle(background.Lightened(0.08f), border);
        StyleBoxFlat pressed = CreateStyle(background.Darkened(0.08f), border);
        StyleBoxFlat disabled = CreateStyle(background, border);

        AddThemeStyleboxOverride("normal", normal);
        AddThemeStyleboxOverride("hover", hover);
        AddThemeStyleboxOverride("pressed", pressed);
        AddThemeStyleboxOverride("disabled", disabled);
    }

    private static StyleBoxFlat CreateStyle(Color background, Color border)
    {
        StyleBoxFlat style = new()
        {
            BgColor = background,
            BorderColor = border,
            ShadowColor = new Color(0, 0, 0, 0.24f),
            ShadowSize = 8,
            ShadowOffset = new Vector2(0, 4)
        };

        style.SetBorderWidthAll(4);
        style.SetCornerRadiusAll(18);
        style.ContentMarginLeft = 16;
        style.ContentMarginRight = 16;
        style.ContentMarginTop = 12;
        style.ContentMarginBottom = 12;
        return style;
    }
}
