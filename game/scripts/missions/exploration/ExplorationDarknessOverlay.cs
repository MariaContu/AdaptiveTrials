using AdaptiveTrials.Game.Player;
using Godot;

namespace AdaptiveTrials.Game.Missions.Exploration;

/// <summary>
/// Camada acima do mundo e abaixo do HUD. Não usa shader, CanvasModulate
/// ou iluminação física; desenha ocultação opaca em coordenadas de tela.
/// </summary>
public partial class ExplorationDarknessOverlay : CanvasLayer
{
    private PlayerController _player = null!;
    private ExplorationDarknessMask _mask = null!;
    private int _difficulty;
    private float _radius;

    public void Initialize(PlayerController player, int difficulty)
    {
        _player = player;
        _difficulty = difficulty;
        _radius = difficulty >= 3 ? 110f : 235f;
        float featherWidth = difficulty >= 3 ? 30f : 40f;

        // Instancia enquanto este CanvasLayer ainda está fora da árvore.
        // Evita chamar AddChild no _Ready de um nó que está sendo inicializado.
        _mask = new ExplorationDarknessMask { Name = "MascaraPreta" };
        _mask.Configure(_radius, featherWidth);
        AddChild(_mask);
    }

    public override void _Ready()
    {
        if (_mask is null || !IsInstanceValid(_player))
        {
            GD.PushError("[Exploracao][ILUMINACAO] ERRO: overlay sem jogador ou sem mascara.");
            return;
        }

        UpdateMask();
        GD.Print($"[Exploracao][ILUMINACAO] ATIVO: dificuldade={_difficulty}; " +
            $"raio={_radius}; camada={Layer}; caminho={GetPath()}; " +
            $"mascaraNaArvore={_mask.IsInsideTree()}.");
    }

    public override void _Process(double delta)
    {
        if (IsInstanceValid(_player) && _player.IsInsideTree())
        {
            UpdateMask();
        }
    }

    private void UpdateMask()
    {
        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        Vector2 playerOnScreen = _player.GetGlobalTransformWithCanvas().Origin;
        _mask.UpdatePosition(viewportSize, playerOnScreen);
    }
}
