using Godot;

namespace AdaptiveTrials.Game.UI;

/// <summary>
/// Padrão visual compartilhado das telas externas às missões.
/// Mantém o fundo roxo e os cartões malva/bege existentes no projeto.
/// </summary>
public static class GameUiStyle
{
    public static readonly Color Card = new("#BEA89A");
    public static readonly Color Border = new("#8E7D83");
    public static readonly Color Ink = new("#352936");
    public static readonly Color SecondaryInk = new("#554450");
    public static readonly Color Success = new("#4F8159");
    public static readonly Color Failure = new("#98515C");

    public static void ApplyMainPanel(PanelContainer panel)
    {
        StyleBoxFlat style = CreateBox(Card, Border, 7, 28);
        style.ShadowColor = new Color(0, 0, 0, 0.22f);
        style.ShadowSize = 14;
        panel.AddThemeStyleboxOverride("panel", style);
    }

    public static void ApplyPrimaryButton(Button button, int fontSize = 16)
    {
        ApplyButton(button,
            new Color("#8F7B86"),
            new Color("#A38D99"),
            new Color("#796874"),
            fontSize);
    }

    public static void ApplySecondaryButton(Button button, int fontSize = 15)
    {
        ApplyButton(button,
            new Color("#B59FA0"),
            new Color("#C6B3B0"),
            new Color("#A18B91"),
            fontSize);
    }

    private static void ApplyButton(
        Button button, Color normal, Color hover, Color pressed, int fontSize)
    {
        Color outline = Ink;
        button.AddThemeStyleboxOverride("normal", CreateBox(normal, outline, 3, 22));
        button.AddThemeStyleboxOverride("hover", CreateBox(hover, outline, 3, 22));
        button.AddThemeStyleboxOverride("pressed", CreateBox(pressed, outline, 3, 22));
        button.AddThemeStyleboxOverride("disabled", CreateBox(new Color("#998D92"),
            new Color("#786D73"), 2, 22));
        button.AddThemeColorOverride("font_color", Ink);
        button.AddThemeColorOverride("font_hover_color", Ink);
        button.AddThemeColorOverride("font_pressed_color", Ink);
        button.AddThemeColorOverride("font_disabled_color", new Color("#524950"));
        button.AddThemeFontSizeOverride("font_size", fontSize);
    }

    public static StyleBoxFlat CreateBox(
        Color background, Color border, int borderWidth, int radius)
    {
        StyleBoxFlat style = new() { BgColor = background, BorderColor = border };
        style.SetBorderWidthAll(borderWidth);
        style.SetCornerRadiusAll(radius);
        return style;
    }
}
