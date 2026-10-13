using Godot;

namespace AdaptiveTrials.Game.Missions.Exploration;

/// <summary>
/// Máscara opaca que deixa SEM DESENHO o círculo em volta do jogador.
/// Do limite do círculo até o feather, alpha sobe rapidamente a 1;
/// além disso, todas as posições ficam cobertas de preto opaco.
/// </summary>
public partial class ExplorationDarknessMask : Node2D
{
    private const int Segments = 128;
    private static readonly Color Transparent = new(0f, 0f, 0f, 0f);
    private static readonly Color Black = new(0f, 0f, 0f, 1f);

    private Vector2 _viewportSize;
    private Vector2 _center;
    private float _clearRadius;
    private float _featherWidth;
    private bool _loggedDraw;

    public void Configure(float clearRadius, float featherWidth)
    {
        _clearRadius = clearRadius;
        _featherWidth = featherWidth;
        QueueRedraw();
    }

    public void UpdatePosition(Vector2 viewportSize, Vector2 center)
    {
        if (_viewportSize.IsEqualApprox(viewportSize) && _center.IsEqualApprox(center))
            return;

        _viewportSize = viewportSize;
        _center = center;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_viewportSize.X <= 0 || _viewportSize.Y <= 0 || _clearRadius <= 0)
        {
            if (!_loggedDraw)
                GD.PushWarning("[Exploracao][ILUMINACAO] Desenho aguardando viewport e raio validos.");
            return;
        }

        if (!_loggedDraw)
        {
            GD.Print($"[Exploracao][ILUMINACAO] DESENHO: " +
                $"viewport={_viewportSize}; centro={_center}; " +
                $"raio={_clearRadius}; transicao={_featherWidth}.");
            _loggedDraw = true;
        }

        float featherRadius = _clearRadius + _featherWidth;

        // Garante que o anel opaco alcance todos os cantos da tela mesmo
        // quando o personagem estiver junto a uma borda ou fora da tela.
        float farRadius = Mathf.Max(
            Mathf.Max(_center.DistanceTo(Vector2.Zero),
                _center.DistanceTo(new Vector2(_viewportSize.X, 0))),
            Mathf.Max(_center.DistanceTo(new Vector2(0, _viewportSize.Y)),
                _center.DistanceTo(_viewportSize))) + 64f;
        farRadius = Mathf.Max(farRadius, featherRadius + 64f);

        for (int i = 0; i < Segments; i++)
        {
            float a0 = Mathf.Tau * i / Segments;
            float a1 = Mathf.Tau * (i + 1) / Segments;
            Vector2 dir0 = new(Mathf.Cos(a0), Mathf.Sin(a0));
            Vector2 dir1 = new(Mathf.Cos(a1), Mathf.Sin(a1));

            Vector2 clear0 = _center + dir0 * _clearRadius;
            Vector2 clear1 = _center + dir1 * _clearRadius;
            Vector2 feather0 = _center + dir0 * featherRadius;
            Vector2 feather1 = _center + dir1 * featherRadius;
            Vector2 far0 = _center + dir0 * farRadius;
            Vector2 far1 = _center + dir1 * farRadius;

            // Apenas a borda estreita possui transparência gradual.
            DrawPolygon(
                new[] { clear0, feather0, feather1, clear1 },
                new[] { Transparent, Black, Black, Transparent });

            // Todos os pixels além da borda são cobertos com preto 100% opaco.
            DrawPolygon(
                new[] { feather0, far0, far1, feather1 },
                new[] { Black, Black, Black, Black });
        }
    }
}
