using System;
using System.Collections.Generic;
using System.Linq;
using AdaptiveTrials.Game.Dto;
using AdaptiveTrials.Game.Dto.Adaptive;
using AdaptiveTrials.Game.Enums;

namespace AdaptiveTrials.Game.Missions.Providers;

/// <summary>
/// Regras do fluxo adaptativo: seis missões, sem cota por categoria.
/// </summary>
public static class AdaptiveMissionPolicy
{
    public const int MissionLimit = 6;

    public static void ValidateNext(
        IReadOnlyList<MissionDto> prepared,
        int completedCount,
        NextRecommendationResponse recommendation,
        MissionDto mission)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(recommendation);
        ArgumentNullException.ThrowIfNull(mission);

        if (prepared.Count >= MissionLimit || completedCount != prepared.Count)
        {
            throw new InvalidOperationException(
                "A sessão não está pronta para receber outra missão.");
        }

        if (recommendation.RecommendedMissionId <= 0 ||
            mission.Id != recommendation.RecommendedMissionId ||
            mission.Type != recommendation.RecommendedType ||
            mission.Difficulty != recommendation.Difficulty)
        {
            throw new InvalidOperationException(
                "A missão recebida não corresponde à recomendação da API.");
        }

        if (prepared.Any(previous => previous.Id == mission.Id))
        {
            throw new InvalidOperationException(
                "A recomendação repetiu uma missão já executada.");
        }

        if (mission.Type is not (MissionType.Combat or MissionType.Exploration or MissionType.Puzzle))
        {
            throw new InvalidOperationException("A categoria recomendada é inválida.");
        }
    }
}
