using Godot;

namespace AdaptiveTrials.Game.Missions.Puzzle.Shared;

/// <summary>
/// Ponto interativo reutilizável da missão Conectar Pontos.
/// </summary>
public partial class ConnectPointButton : Button
{
    [Signal]
    public delegate void PointSelectedEventHandler(int pointIndex);

    public int PointIndex { get; private set; }
    public int PairId { get; private set; }
    public bool IsConnected { get; private set; }

    private Color _baseColor = new("#8f72b5");
    private string _symbol = "◆";

    public override void _Ready()
    {
        Pressed += OnPressed;
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
    }

    public void Configure(int pointIndex, int pairId, string symbol, Color color)
    {
        PointIndex = pointIndex;
        PairId = pairId;
        _symbol = string.IsNullOrWhiteSpace(symbol) ? "?" : symbol;
        _baseColor = color;
        IsConnected = false;
        Disabled = false;
        Text = _symbol;
        ApplyAppearance(_baseColor, _baseColor.Darkened(0.28f), 1.0f);
    }

    public void SetSelected(bool selected)
    {
        if (IsConnected)
        {
            return;
        }

        if (selected)
        {
            ApplyAppearance(new Color("#f1d778"), new Color("#b2943f"), 1.10f);
            return;
        }

        ApplyAppearance(_baseColor, _baseColor.Darkened(0.28f), 1.0f);
    }

    public void ShowError()
    {
        if (!IsConnected)
        {
            ApplyAppearance(new Color("#d8808c"), new Color("#a64f5c"), 1.06f);
        }
    }

    public void RestoreAppearance()
    {
        if (IsConnected)
        {
            ApplyConnectedAppearance();
            return;
        }

        ApplyAppearance(_baseColor, _baseColor.Darkened(0.28f), 1.0f);
    }

    public void MarkConnected()
    {
        IsConnected = true;
        Disabled = true;
        MouseDefaultCursorShape = CursorShape.Arrow;
        ApplyConnectedAppearance();
    }

    private void OnPressed()
    {
        if (Disabled || IsConnected)
        {
            return;
        }

        EmitSignal(SignalName.PointSelected, PointIndex);
    }

    private void ApplyConnectedAppearance()
    {
        ApplyAppearance(new Color("#8fc9a5"), new Color("#5d9572"), 1.0f);
    }

    private void ApplyAppearance(Color background, Color border, float scale)
    {
        Text = _symbol;
        Scale = Vector2.One * scale;
        PivotOffset = Size / 2.0f;

        AddThemeStyleboxOverride("normal", CreateStyle(background, border));
        AddThemeStyleboxOverride("hover", CreateStyle(background.Lightened(0.08f), border));
        AddThemeStyleboxOverride("pressed", CreateStyle(background.Darkened(0.08f), border));
        AddThemeStyleboxOverride("disabled", CreateStyle(background, border));
    }

    private static StyleBoxFlat CreateStyle(Color background, Color border)
    {
        StyleBoxFlat style = new()
        {
            BgColor = background,
            BorderColor = border,
            ShadowColor = new Color(0, 0, 0, 0.24f),
            ShadowSize = 7,
            ShadowOffset = new Vector2(0, 4)
        };

        style.SetBorderWidthAll(4);
        style.SetCornerRadiusAll(36);
        style.ContentMarginLeft = 12;
        style.ContentMarginRight = 12;
        style.ContentMarginTop = 10;
        style.ContentMarginBottom = 10;
        return style;
    }
}
