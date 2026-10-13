using System;
using System.Collections.Generic;
using Godot;

namespace AdaptiveTrials.Game.Missions.Shared;

/// <summary>
/// Cenários autorais de exploração. Mantém o nome da classe para compatibilidade
/// com as cenas existentes, mas NÃO gera labirintos: cada missão possui seu
/// próprio desenho espacial e mecânica. Coordenadas em viewport 1152x648.
/// </summary>
public partial class ExplorationMaze : Node2D
{
	public enum CourseMode { FindObjects, ReachDestination, AvoidEnemies }

	// Player.tscn: cápsula ~37x42 após escala 0.8; sprite visual
	// pode ocupar ~118px. Evitar corredores abaixo de 160px.
	private const float Top = 92f;
	private const float Bottom = 584f;
	private const float Left = 65f;
	private const float Right = 1090f;

	private readonly List<Vector2> _collectibles = new();
	private readonly List<Vector2> _checkpoints = new();
	private readonly List<Vector2> _gates = new();
	private readonly List<(Vector2 Start, Vector2 End)> _patrols = new();
	private Node2D? _generated;

	public Vector2 PlayerStartPosition { get; private set; }
	public Vector2 DestinationPosition { get; private set; }
	public IReadOnlyList<Vector2> CollectiblePositions => _collectibles;
	public IReadOnlyList<Vector2> CheckpointPositions => _checkpoints;
	public IReadOnlyList<Vector2> GatePositions => _gates;
	public IReadOnlyList<(Vector2 Start, Vector2 End)> PatrolRoutes => _patrols;
	public int ActiveDifficulty { get; private set; }

	public void Configure(int difficulty) => Configure(difficulty, CourseMode.FindObjects);

	public void Configure(int difficulty, CourseMode mode)
	{
		ActiveDifficulty = Mathf.Clamp(difficulty, 1, 3);
		if (_generated is not null && IsInstanceValid(_generated))
		{
			RemoveChild(_generated);
			_generated.Free();
		}

		_generated = new Node2D { Name = "CenarioExploracao" };
		AddChild(_generated);
		_collectibles.Clear();
		_checkpoints.Clear();
		_gates.Clear();
		_patrols.Clear();

		switch (mode)
		{
			case CourseMode.FindObjects: BuildNightGarden(); break;
			case CourseMode.ReachDestination: BuildExpeditionTrail(); break;
			case CourseMode.AvoidEnemies: BuildPatrolledCamp(); break;
			default: throw new ArgumentOutOfRangeException(nameof(mode));
		}
	}

	// 1. BUSCA: jardim aberto, com setores reconhecíveis e sem paredes.
	// A dificuldade cresce pelo número e distribuição dos achados, não
	// por corredores estreitos. Os itens têm iluminação própria.
	private void BuildNightGarden()
	{
		PlayerStartPosition = new Vector2(145, 500);
		DestinationPosition = new Vector2(975, 205);
		Floor(new Color("#211a2c"));

		Area(new Vector2(330, 240), new Vector2(255, 175), new Color("#342a40"));
		Area(new Vector2(655, 425), new Vector2(305, 180), new Color("#2c3141"));
		Area(new Vector2(900, 240), new Vector2(245, 170), new Color("#393041"));
		Area(new Vector2(480, 500), new Vector2(245, 115), new Color("#292d3d"));
		Trail(new Color("#55465a"), 26,
			PlayerStartPosition, new Vector2(330, 350), new Vector2(580, 340),
			new Vector2(750, 290), new Vector2(970, 205));
		Trail(new Color("#453d55"), 17,
			new Vector2(335, 350), new Vector2(450, 510), new Vector2(790, 505));
		Trail(new Color("#453d55"), 17,
			new Vector2(575, 340), new Vector2(550, 180), new Vector2(825, 170));

		// Cenários legíveis, não obstáculos: árvores, lago e ruínas.
		Landmark(new Vector2(310, 145), "BOSQUE", new Color("#547f69"));
		Landmark(new Vector2(660, 505), "LAGO", new Color("#477d91"));
		Landmark(new Vector2(930, 135), "RUÍNAS", new Color("#9c779e"));
		Landmark(new Vector2(470, 550), "ACAMPAMENTO", new Color("#a48758"));

		foreach (Vector2 p in new[] {
			new Vector2(210, 190), new Vector2(420, 180), new Vector2(385, 280),
			new Vector2(730, 455), new Vector2(800, 410), new Vector2(880, 275),
			new Vector2(1030, 310), new Vector2(560, 530) })
			Decor(p, 19, new Color("#394953"));

		// Ordenação intencional: os três primeiros são os mais legíveis.
		_collectibles.AddRange(new[] {
			new Vector2(315, 250), new Vector2(660, 400), new Vector2(945, 230),
			new Vector2(455, 495), new Vector2(825, 490), new Vector2(790, 175),
			new Vector2(225, 470), new Vector2(555, 165), new Vector2(1000, 445),
			new Vector2(535, 315)
		});
		AddBoundary();
	}

	// 2. TRAVESSIA: trilha panorâmica e marcos de orientação. Cada nível
	// acrescenta uma passagem de 190px com laser de 165px (rotacionado
	// nos controladores), deixando espaço visual ao personagem.
	private void BuildExpeditionTrail()
	{
		PlayerStartPosition = new Vector2(145, 490);
		DestinationPosition = new Vector2(1000, 205);
		Floor(new Color("#242437"));
		Trail(new Color("#75644f"), 65,
			PlayerStartPosition, new Vector2(235, 420), new Vector2(350, 420),
			new Vector2(495, 285), new Vector2(610, 240),
			new Vector2(745, 405), new Vector2(870, 420), DestinationPosition);
		Trail(new Color("#9a805e"), 8,
			PlayerStartPosition, new Vector2(235, 420), new Vector2(350, 420),
			new Vector2(495, 285), new Vector2(610, 240),
			new Vector2(745, 405), new Vector2(870, 420), DestinationPosition);
		Landmark(new Vector2(190, 180), "INÍCIO DA TRILHA", new Color("#6c7e8e"));
		Landmark(new Vector2(510, 165), "MIRANTE", new Color("#8a8cb0"));
		Landmark(new Vector2(790, 155), "DESFILADEIRO", new Color("#b59a6b"));
		Landmark(new Vector2(1000, 150), "FAROL", new Color("#e3c180"));

		Vector2[] checkpoints = {
			new(235, 420), new(500, 285), new(745, 405)
		};
		Vector2[] gates = {
			new(350, 420), new(610, 240), new(870, 420)
		};
		_checkpoints.AddRange(checkpoints);
		_gates.AddRange(gates);

		// Penhascos largos, nunca labirintos. A abertura tem 190px;
		// a cápsula do jogador (37px) e o sprite cabem visualmente.
		for (int i = 0; i < ActiveDifficulty; i++)
		{
			float x = gates[i].X;
			float centerY = gates[i].Y;
			float gapHalf = 95f;
			Block(new Vector2(x, (Top + centerY - gapHalf) / 2f),
				new Vector2(34, centerY - gapHalf - Top), new Color("#4d4657"));
			Block(new Vector2(x, (centerY + gapHalf + Bottom) / 2f),
				new Vector2(34, Bottom - centerY - gapHalf), new Color("#4d4657"));
			Decor(new Vector2(x - 33, centerY - 118), 18, new Color("#a58c67"));
			Decor(new Vector2(x - 33, centerY + 118), 18, new Color("#a58c67"));
		}
		AddBoundary();
	}

	// 3. FURTIVIDADE: posto de vigilância aberto, com patrulhas e
	// cobertura. O jogador observa o sentido dos guardas e escolhe
	// quando atravessar; não há labirinto nem lasers.
	private void BuildPatrolledCamp()
	{
		PlayerStartPosition = new Vector2(140, 505);
		DestinationPosition = new Vector2(1010, 500);
		Floor(new Color("#1c2935"));
		Trail(new Color("#46525d"), 74,
			PlayerStartPosition, new Vector2(260, 475), new Vector2(370, 350),
			new Vector2(520, 475), new Vector2(625, 345),
			new Vector2(780, 475), new Vector2(880, 345), DestinationPosition);
		Landmark(new Vector2(200, 175), "POSTO DE GUARDA", new Color("#708696"));
		Landmark(new Vector2(540, 175), "DEPÓSITO", new Color("#827b91"));
		Landmark(new Vector2(935, 175), "SAÍDA", new Color("#688f7e"));

		_checkpoints.AddRange(new[] {
			new Vector2(260, 475), new Vector2(520, 475), new Vector2(780, 475)
		});
		_patrols.AddRange(new[] {
			(new Vector2(365, 215), new Vector2(365, 440)),
			(new Vector2(625, 215), new Vector2(625, 440)),
			(new Vector2(880, 215), new Vector2(880, 440))
		});

		// Cobertura real: bloqueia movimento e a linha de visão do guarda.
		// Fica fora dos checkpoints e dos trajetos das patrulhas.
		Block(new Vector2(435, 335), new Vector2(75, 85), new Color("#5b6874"));
		Block(new Vector2(695, 335), new Vector2(75, 85), new Color("#5b6874"));
		Block(new Vector2(965, 335), new Vector2(75, 85), new Color("#5b6874"));
		Decor(new Vector2(220, 285), 35, new Color("#435f64"));
		Decor(new Vector2(550, 300), 25, new Color("#596577"));
		Decor(new Vector2(790, 270), 30, new Color("#435f64"));
		AddBoundary();
	}

	private void Floor(Color color)
	{
		Rect(new Vector2((Left + Right) / 2f, (Top + Bottom) / 2f),
			new Vector2(Right - Left, Bottom - Top), color, -5);
		for (int i = 0; i < 18; i++)
		{
			float x = 120 + (i * 149 % 910);
			float y = 140 + (i * 89 % 375);
			Decor(new Vector2(x, y), 4 + i % 3, new Color("#494354"));
		}
	}

	private void Area(Vector2 center, Vector2 size, Color color)
	{
		if (_generated is null) return;
		var points = new Vector2[28];
		for (int i = 0; i < points.Length; i++)
		{
			float angle = Mathf.Tau * i / points.Length;
			points[i] = new Vector2(Mathf.Cos(angle) * size.X * 0.5f,
				Mathf.Sin(angle) * size.Y * 0.5f);
		}
		_generated.AddChild(new Polygon2D { Position = center, Polygon = points,
			Color = color, ZIndex = -4 });
	}

	private void Trail(Color color, float width, params Vector2[] points)
	{
		if (_generated is null) return;
		_generated.AddChild(new Line2D
		{
			Points = points,
			Width = width,
			DefaultColor = color,
			Antialiased = true,
			JointMode = Line2D.LineJointMode.Round,
			BeginCapMode = Line2D.LineCapMode.Round,
			EndCapMode = Line2D.LineCapMode.Round,
			ZIndex = -3
		});
	}

	private void Landmark(Vector2 position, string title, Color color)
	{
		Decor(position, 35, color);
		if (_generated is null) return;
		_generated.AddChild(new Label
		{
			Position = position + new Vector2(-70, 38),
			Size = new Vector2(140, 28),
			Text = title,
			HorizontalAlignment = HorizontalAlignment.Center
		});
	}

	private void Decor(Vector2 position, float radius, Color color)
	{
		if (_generated is null) return;
		var points = new Vector2[12];
		for (int i = 0; i < points.Length; i++)
		{
			float a = Mathf.Tau * i / points.Length;
			points[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
		}
		_generated.AddChild(new Polygon2D { Position = position, Polygon = points,
			Color = color, ZIndex = -2 });
	}

	private void Rect(Vector2 position, Vector2 size, Color color, int z)
	{
		if (_generated is null || size.X <= 0 || size.Y <= 0) return;
		Vector2 h = size / 2f;
		_generated.AddChild(new Polygon2D
		{
			Position = position,
			Polygon = new[] { new Vector2(-h.X, -h.Y), new Vector2(h.X, -h.Y),
				new Vector2(h.X, h.Y), new Vector2(-h.X, h.Y) },
			Color = color,
			ZIndex = z
		});
	}

	private void Block(Vector2 position, Vector2 size, Color color)
	{
		if (_generated is null || size.X <= 0 || size.Y <= 0) return;
		var body = new StaticBody2D { Position = position, CollisionLayer = 1,
			CollisionMask = 0 };
		_generated.AddChild(body);
		Vector2 h = size / 2f;
		body.AddChild(new Polygon2D
		{
			Polygon = new[] { new Vector2(-h.X, -h.Y), new Vector2(h.X, -h.Y),
				new Vector2(h.X, h.Y), new Vector2(-h.X, h.Y) },
			Color = color, ZIndex = 1
		});
		body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = size } });
	}

	private void AddBoundary()
	{
		var color = new Color("#343044");
		Block(new Vector2((Left + Right) / 2f, Top), new Vector2(Right - Left + 36, 22), color);
		Block(new Vector2((Left + Right) / 2f, Bottom), new Vector2(Right - Left + 36, 22), color);
		Block(new Vector2(Left, (Top + Bottom) / 2f), new Vector2(22, Bottom - Top + 36), color);
		Block(new Vector2(Right, (Top + Bottom) / 2f), new Vector2(22, Bottom - Top + 36), color);
	}
}
