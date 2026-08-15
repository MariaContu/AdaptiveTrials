using System;
using System.Collections.Generic;
using System.Linq;
using AdaptiveTrials.Game.Dto;
using AdaptiveTrials.Game.Enums;
using Godot;

namespace AdaptiveTrials.Game.Missions.Providers;

/// <summary>
/// Prepara a sequência equilibrada utilizada no modo controle.
/// </summary>
public sealed class ControlMissionProvider
{
    private const int MissionsPerCategory = 2;

    public IReadOnlyList<MissionDto> BuildSequence(
        IReadOnlyList<MissionDto> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        List<MissionDto> combat =
            GetCategoryMissions(
                catalog,
                MissionType.Combat);

        List<MissionDto> exploration =
            GetCategoryMissions(
                catalog,
                MissionType.Exploration);

        List<MissionDto> puzzle =
            GetCategoryMissions(
                catalog,
                MissionType.Puzzle);

        ValidateCategory(
            combat,
            MissionType.Combat);

        ValidateCategory(
            exploration,
            MissionType.Exploration);

        ValidateCategory(
            puzzle,
            MissionType.Puzzle);

        List<MissionDto> sequence = new();

        sequence.AddRange(
            SelectRandomMissions(
                combat,
                MissionsPerCategory));

        sequence.AddRange(
            SelectRandomMissions(
                exploration,
                MissionsPerCategory));

        sequence.AddRange(
            SelectRandomMissions(
                puzzle,
                MissionsPerCategory));

        Shuffle(sequence);

        ValidateFinalSequence(sequence);
        PrintSequence(sequence);

        return sequence;
    }

    private static List<MissionDto> GetCategoryMissions(
        IReadOnlyList<MissionDto> catalog,
        MissionType type)
    {
        return catalog
            .Where(mission => mission.Type == type)
            .ToList();
    }

    private static void ValidateCategory(
        IReadOnlyCollection<MissionDto> missions,
        MissionType type)
    {
        if (missions.Count < MissionsPerCategory)
        {
            throw new InvalidOperationException(
                $"O catálogo precisa possuir pelo menos " +
                $"{MissionsPerCategory} missões da categoria " +
                $"{type}.");
        }
    }

    private static IReadOnlyList<MissionDto>
        SelectRandomMissions(
            IReadOnlyList<MissionDto> missions,
            int amount)
    {
        List<MissionDto> available =
            missions.ToList();

        List<MissionDto> selected = new();

        for (int index = 0;
             index < amount;
             index++)
        {
            int selectedIndex =
                GD.RandRange(
                    0,
                    available.Count - 1);

            selected.Add(
                available[selectedIndex]);

            available.RemoveAt(
                selectedIndex);
        }

        return selected;
    }

    private static void Shuffle(
        List<MissionDto> missions)
    {
        for (int index = missions.Count - 1;
             index > 0;
             index--)
        {
            int swapIndex =
                GD.RandRange(0, index);

            (missions[index], missions[swapIndex]) =
                (missions[swapIndex], missions[index]);
        }
    }

    private static void ValidateFinalSequence(
        IReadOnlyList<MissionDto> sequence)
    {
        if (sequence.Count != 6)
        {
            throw new InvalidOperationException(
                "A sequência do modo controle deve possuir " +
                "exatamente seis missões.");
        }

        int combatCount =
            sequence.Count(
                mission =>
                    mission.Type ==
                    MissionType.Combat);

        int explorationCount =
            sequence.Count(
                mission =>
                    mission.Type ==
                    MissionType.Exploration);

        int puzzleCount =
            sequence.Count(
                mission =>
                    mission.Type ==
                    MissionType.Puzzle);

        if (combatCount != 2 ||
            explorationCount != 2 ||
            puzzleCount != 2)
        {
            throw new InvalidOperationException(
                "A sequência do modo controle não respeita " +
                "a distribuição 2/2/2.");
        }

        int uniqueMissionCount =
            sequence
                .Select(mission => mission.Id)
                .Distinct()
                .Count();

        if (uniqueMissionCount != sequence.Count)
        {
            throw new InvalidOperationException(
                "A sequência do modo controle possui " +
                "missões repetidas.");
        }
    }

    private static void PrintSequence(
        IReadOnlyList<MissionDto> sequence)
    {
        GD.Print(
            "Sequência do modo controle preparada:");

        for (int index = 0;
             index < sequence.Count;
             index++)
        {
            MissionDto mission =
                sequence[index];

            GD.Print(
                $"{index + 1}/6 | " +
                $"Id={mission.Id} | " +
                $"Type={mission.Type} | " +
                $"Name={mission.Name} | " +
                $"Difficulty={mission.Difficulty}");
        }
    }
}