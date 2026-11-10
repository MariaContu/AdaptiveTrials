using AdaptiveTrials.Game.Player;
using Godot;

namespace AdaptiveTrials.Game.Missions.Exploration;

/// <summary>
/// Configura visibilidade para as três missões de exploração.
/// Nível 1: mantém iluminação original da missão.
/// Nível 2: máscara preta com abertura grande.
/// Nível 3: máscara preta com abertura pequena.
/// </summary>
public static class ExplorationLighting
{
    private const int DarknessLayer = 100;

    public static void Configure(
        Node sceneRoot,
        PlayerController player,
        int difficulty,
        bool nightGarden = false,
        float gardenLightScale = 1.65f)
    {
        GD.Print($"[Exploracao][ILUMINACAO] Configure: dificuldade={difficulty}; " +
            $"cena={sceneRoot.Name}; raizNaArvore={sceneRoot.IsInsideTree()}.");

        if (difficulty >= 2)
        {
            if (sceneRoot.GetNodeOrNull<ExplorationDarknessOverlay>("EscuridaoExploracao") is not null)
            {
                GD.PushWarning("[Exploracao][ILUMINACAO] Ja existe uma mascara ativa; ignorando segunda criacao.");
                return;
            }

            // CanvasLayer na camada 100 cobre todo o mundo (incluindo ZIndex alto).
            // Mantém a ordem relativa dos CanvasLayers do HUD acima da máscara.
            foreach (Node child in sceneRoot.GetChildren())
            {
                if (child is CanvasLayer ui &&
                    child is not ExplorationDarknessOverlay &&
                    ui.Layer <= DarknessLayer)
                {
                    ui.Layer += DarknessLayer + 1;
                }
            }

            // CRÍTICO: Initialize cria a máscara ANTES de entrar na árvore.
            // O AddChild na raiz é adiado: estamos dentro do _Ready de outro
            // filho da própria raiz, e AddChild imediato pode ser recusado
            // enquanto Godot ainda está montando os filhos da cena.
            ExplorationDarknessOverlay overlay = new()
            {
                Name = "EscuridaoExploracao",
                Layer = DarknessLayer
            };
            overlay.Initialize(player, difficulty);
            sceneRoot.CallDeferred(Node.MethodName.AddChild, overlay);

            GD.Print("[Exploracao][ILUMINACAO] Inclusao do overlay AGENDADA " +
                "para apos a inicializacao da cena. Aguarde o log 'ATIVO'.");
            return;
        }

        if (!nightGarden)
        {
            return;
        }

        // Comportamento anterior da missão Encontrar Objetos no nível 1.
        sceneRoot.CallDeferred(Node.MethodName.AddChild, new CanvasModulate
        {
            Name = "EscuridaoExploracao",
            Color = new Color("#0b0a10")
        });

        Gradient gradient = new();
        gradient.SetColor(0, Colors.White);
        gradient.SetColor(1, new Color(1f, 1f, 1f, 0f));

        GradientTexture2D texture = new()
        {
            Gradient = gradient,
            Width = 256,
            Height = 256,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f)
        };

        // A mesma regra de inclusão diferida evita conflitos no _Ready da cena.
        player.CallDeferred(Node.MethodName.AddChild, new PointLight2D
        {
            Name = "LuzDoJogador",
            Texture = texture,
            TextureScale = gardenLightScale,
            Energy = 1.15f,
            Color = new Color("#f3ddc5"),
            ShadowEnabled = false
        });
    }
}
