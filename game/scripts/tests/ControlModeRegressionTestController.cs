using System;
using System.Collections.Generic;
using System.Linq;
using AdaptiveTrials.Game.Dto;
using AdaptiveTrials.Game.Enums;
using AdaptiveTrials.Game.Missions.Providers;
using Godot;

namespace AdaptiveTrials.Game.Tests;

/// <summary>
/// Regressão automatizada da montagem da sequência do modo Controle.
/// Executa sem SessionManager e sem chamadas à API.
/// </summary>
public partial class ControlModeRegressionTestController : Node
{
    [Export(PropertyHint.Range, "1,100,1")]
    public int Iterations { get; set; } = 25;

    private Label _statusLabel = null!;

    public override void _Ready()
    {
        _statusLabel = GetNode<Label>("../Interface/Center/Panel/Margin/Content/StatusLabel");
        RunRegression();
    }

    private void RunRegression()
    {
        try
        {
            IReadOnlyList<MissionDto> catalog = BuildSyntheticCatalog();
            ControlMissionProvider provider = new();

            for (int iteration = 1; iteration <= Iterations; iteration++)
            {
                IReadOnlyList<MissionDto> sequence = provider.BuildSequence(catalog);
                ValidateSequence(sequence, iteration);
            }

            ValidateInvalidCatalogIsRejected(provider);

            string message =
                $"REGRESSÃO APROVADA\n\n" +
                $"{Iterations} sequências do modo Controle validadas.\n" +
                "Cada sequência possui exatamente 6 missões, " +
                "distribuição 2/2/2 e nenhum ID repetido.\n\n" +
                "Também foi validada a rejeição de catálogo incompleto.";

            _statusLabel.Text = message;
            GD.Print(message);
        }
        catch (Exception exception)
        {
            string message =
                "REGRESSÃO REPROVADA\n\n" +
                exception.Message;

            _statusLabel.Text = message;
            GD.PushError($"{message}\n{exception}");
        }
    }

    private static IReadOnlyList<MissionDto> BuildSyntheticCatalog()
    {
        List<MissionDto> catalog = new();
        int id = 1;

        AddCategory(catalog, MissionType.Combat, "Combate", ref id);
        AddCategory(catalog, MissionType.Exploration, "Exploração", ref id);
        AddCategory(catalog, MissionType.Puzzle, "Puzzle", ref id);

        return catalog;
    }

    private static void AddCategory(
        ICollection<MissionDto> catalog,
        MissionType type,
        string prefix,
        ref int nextId)
    {
        string[] templates = type switch
        {
            MissionType.Combat => ["Eliminar Alvo", "Sobreviver", "Defender Objeto"],
            MissionType.Exploration => ["Encontrar Objetos", "Chegar ao Destino", "Evitar Inimigos"],
            MissionType.Puzzle => ["Repetir Sequência", "Conectar Pontos", "Decifrar Código"],
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        foreach (string template in templates)
        {
            for (int difficulty = 1; difficulty <= 3; difficulty++)
            {
                catalog.Add(new MissionDto
                {
                    Id = nextId++,
                    Name = $"{prefix} - {template} - {difficulty}",
                    Type = type,
                    Template = template,
                    Difficulty = difficulty,
                    ParametersJson = "{}"
                });
            }
        }
    }

    private static void ValidateSequence(
        IReadOnlyList<MissionDto> sequence,
        int iteration)
    {
        if (sequence.Count != 6)
        {
            throw new InvalidOperationException(
                $"Iteração {iteration}: quantidade esperada 6, recebida {sequence.Count}."
            );
        }

        ValidateCategoryCount(sequence, MissionType.Combat, iteration);
        ValidateCategoryCount(sequence, MissionType.Exploration, iteration);
        ValidateCategoryCount(sequence, MissionType.Puzzle, iteration);

        int uniqueIds = sequence.Select(mission => mission.Id).Distinct().Count();

        if (uniqueIds != 6)
        {
            throw new InvalidOperationException(
                $"Iteração {iteration}: a sequência contém missões repetidas."
            );
        }
    }

    private static void ValidateCategoryCount(
        IEnumerable<MissionDto> sequence,
        MissionType type,
        int iteration)
    {
        int count = sequence.Count(mission => mission.Type == type);

        if (count != 2)
        {
            throw new InvalidOperationException(
                $"Iteração {iteration}: categoria {type} deveria possuir 2 missões, mas possui {count}."
            );
        }
    }

    private static void ValidateInvalidCatalogIsRejected(ControlMissionProvider provider)
    {
        List<MissionDto> invalidCatalog = BuildSyntheticCatalog()
            .Where(mission => mission.Type != MissionType.Puzzle)
            .ToList();

        invalidCatalog.Add(new MissionDto
        {
            Id = 999,
            Name = "Puzzle insuficiente",
            Type = MissionType.Puzzle,
            Template = "Repetir Sequência",
            Difficulty = 1
        });

        try
        {
            provider.BuildSequence(invalidCatalog);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException(
            "O provider aceitou um catálogo com menos de duas missões de Puzzle."
        );
    }
}
