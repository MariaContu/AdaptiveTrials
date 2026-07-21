using Godot;
using System;

namespace AdaptiveTrials.Game.Menu;

/// <summary>
/// Desenha camadas de ondas animadas no fundo do menu.
/// </summary>
public partial class WaveBackground : Control
{
	[Export]
	public float AnimationSpeed { get; set; } = 0.18f;

	[Export]
	public float WaveAmplitude { get; set; } = 28.0f;

	[Export]
	public float WaveLength { get; set; } = 760.0f;

	[Export]
	public float ShadowOffset { get; set; } = 14.0f;

	private float _time;

	private static readonly Color BackgroundColor =
		new("2c2330");

	private static readonly Color BackWaveColor =
		new("3a303a");

	private static readonly Color MiddleWaveColor =
		new("55484f");

	private static readonly Color FrontWaveColor =
		new("74686d");

	private static readonly Color ShadowColor =
		new(0.06f, 0.04f, 0.07f, 0.32f);

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		_time += (float)delta * AnimationSpeed;
		QueueRedraw();
	}

	public override void _Draw()
	{
		Vector2 size = Size;

		DrawRect(
			new Rect2(Vector2.Zero, size),
			BackgroundColor);

		DrawWaveWithShadow(
			verticalPosition: size.Y * 0.29f,
			amplitudeMultiplier: 1.18f,
			phaseOffset: 0.0f,
			color: BackWaveColor,
			movementDirection: 1.0f);

		DrawWaveWithShadow(
			verticalPosition: size.Y * 0.48f,
			amplitudeMultiplier: 0.95f,
			phaseOffset: 1.7f,
			color: MiddleWaveColor,
			movementDirection: -0.75f);

		DrawWaveWithShadow(
			verticalPosition: size.Y * 0.68f,
			amplitudeMultiplier: 0.72f,
			phaseOffset: 3.1f,
			color: FrontWaveColor,
			movementDirection: 0.55f);
	}

	private void DrawWaveWithShadow(
		float verticalPosition,
		float amplitudeMultiplier,
		float phaseOffset,
		Color color,
		float movementDirection)
	{
		Vector2[] shadowPoints = BuildWavePoints(
			verticalPosition + ShadowOffset,
			amplitudeMultiplier,
			phaseOffset,
			movementDirection);

		DrawColoredPolygon(shadowPoints, ShadowColor);

		Vector2[] wavePoints = BuildWavePoints(
			verticalPosition,
			amplitudeMultiplier,
			phaseOffset,
			movementDirection);

		DrawColoredPolygon(wavePoints, color);
	}

	private Vector2[] BuildWavePoints(
		float verticalPosition,
		float amplitudeMultiplier,
		float phaseOffset,
		float movementDirection)
	{
		const int segments = 96;

		Vector2[] points = new Vector2[segments + 3];

		points[0] = new Vector2(0, Size.Y);

		for (int index = 0; index <= segments; index++)
		{
			float normalized = index / (float)segments;
			float x = normalized * Size.X;

			float primaryWave = Mathf.Sin(
				(x / WaveLength) * Mathf.Tau +
				(_time * movementDirection) +
				phaseOffset);

			float secondaryWave = Mathf.Sin(
				(x / (WaveLength * 1.85f)) * Mathf.Tau -
				(_time * movementDirection * 0.45f) +
				phaseOffset);

			float y =
				verticalPosition +
				primaryWave *
				WaveAmplitude *
				amplitudeMultiplier +
				secondaryWave *
				WaveAmplitude *
				0.20f;

			points[index + 1] = new Vector2(x, y);
		}

		points[^1] = new Vector2(Size.X, Size.Y);

		return points;
	}
}
