using Godot;

namespace AdaptiveTrials.Game.Missions.Combat.Shared;

/// <summary>
/// Exibe um aviso central temporário antes do início de cada onda.
/// </summary>
public partial class WaveAnnouncement : Control
{
    [ExportGroup("Timing")]
    [Export]
    public float FadeInSeconds { get; set; } = 0.2f;

    [Export]
    public float VisibleSeconds { get; set; } = 1.35f;

    [Export]
    public float FadeOutSeconds { get; set; } = 0.35f;

    private Label _titleLabel = null!;
    private Label _subtitleLabel = null!;
    private Control _card = null!;
    private Tween? _activeTween;

    public override void _Ready()
    {
        _card = GetNode<Control>("Center/Card");
        _titleLabel = GetNode<Label>("Center/Card/Margin/Content/Title");
        _subtitleLabel = GetNode<Label>("Center/Card/Margin/Content/Subtitle");

        MouseFilter = MouseFilterEnum.Ignore;
        HideImmediately();
    }

    public void ShowWave(int currentWave, int totalWaves, int enemyCount)
    {
        _activeTween?.Kill();

        _titleLabel.Text = $"ONDA {currentWave}/{totalWaves}";
        _subtitleLabel.Text =
            enemyCount == 1
                ? "1 INIMIGO SE APROXIMA"
                : $"{enemyCount} INIMIGOS SE APROXIMAM";

        Visible = true;
        Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f);
        _card.Scale = new Vector2(0.88f, 0.88f);
        _card.PivotOffset = _card.Size / 2.0f;

        _activeTween = CreateTween();
        _activeTween.SetParallel(true);
        _activeTween.TweenProperty(
            this,
            "modulate:a",
            1.0f,
            Mathf.Max(0.01f, FadeInSeconds));
        _activeTween.TweenProperty(
            _card,
            "scale",
            Vector2.One,
            Mathf.Max(0.01f, FadeInSeconds))
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);

        _activeTween.Chain()
            .TweenInterval(Mathf.Max(0.0f, VisibleSeconds));

        _activeTween.Chain()
            .TweenProperty(
                this,
                "modulate:a",
                0.0f,
                Mathf.Max(0.01f, FadeOutSeconds));

        _activeTween.Finished += HideImmediately;
    }

    public void HideImmediately()
    {
        _activeTween?.Kill();
        _activeTween = null;
        Visible = false;
        Modulate = Colors.Transparent;
        _card.Scale = Vector2.One;
    }
}
