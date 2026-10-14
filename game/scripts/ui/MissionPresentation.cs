using AdaptiveTrials.Game.Enums;
using Godot;

namespace AdaptiveTrials.Game.UI;

/// <summary>
/// Mantém a apresentação das categorias idêntica no HUD, transição e resumo.
/// </summary>
public static class MissionPresentation
{
    public static string Name(MissionType type) => type switch
    {
        MissionType.Combat => "Combate",
        MissionType.Exploration => "Exploração",
        MissionType.Puzzle => "Quebra-cabeça",
        _ => "Desconhecida"
    };

    public static string Icon(MissionType type) => type switch
    {
        MissionType.Combat => "⚔",
        MissionType.Exploration => "⌖",
        MissionType.Puzzle => "◇",
        _ => "?"
    };

    public static Color Background(MissionType type) => type switch
    {
        MissionType.Combat => new Color("#B9878D"),
        MissionType.Exploration => new Color("#91A58D"),
        MissionType.Puzzle => new Color("#8E899E"),
        _ => new Color("#96858F")
    };

    public static Color Foreground(MissionType type) => type switch
    {
        MissionType.Combat => new Color("#3E252A"),
        MissionType.Exploration => new Color("#26362A"),
        MissionType.Puzzle => new Color("#292637"),
        _ => new Color("#342933")
    };
}
