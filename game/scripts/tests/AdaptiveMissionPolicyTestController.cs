using System;
using System.Collections.Generic;
using AdaptiveTrials.Game.Dto;
using AdaptiveTrials.Game.Dto.Adaptive;
using AdaptiveTrials.Game.Enums;
using AdaptiveTrials.Game.Missions.Providers;
using Godot;

namespace AdaptiveTrials.Game.Tests;

/// <summary>Teste isolado das regras do modo adaptativo, sem sessão ativa ou API.</summary>
public partial class AdaptiveMissionPolicyTestController : Node
{
    public override void _Ready()
    {
        Label status = GetNode<Label>(
            "../Interface/Center/Panel/Margin/Content/StatusLabel");
        try
        {
            List<MissionDto> prepared = new();
            // A V2 pode recomendar seis missões da mesma categoria.
            MissionType[] types =
            [
                MissionType.Combat, MissionType.Combat, MissionType.Combat,
                MissionType.Combat, MissionType.Combat, MissionType.Combat
            ];

            for (int index = 0; index < types.Length; index++)
            {
                MissionDto mission = new()
                {
                    Id = index + 1, Type = types[index], Difficulty = 1,
                    Name = "Missão de teste"
                };
                NextRecommendationResponse recommendation = new()
                {
                    RecommendedMissionId = mission.Id,
                    RecommendedType = mission.Type,
                    Difficulty = mission.Difficulty
                };
                AdaptiveMissionPolicy.ValidateNext(
                    prepared, prepared.Count, recommendation, mission);
                prepared.Add(mission);
            }

            ExpectRejected(prepared, 6, MissionType.Combat, 7);
            ExpectRejected(prepared.GetRange(0, 3), 3, MissionType.Combat, 1);
            ExpectRejected(prepared.GetRange(0, 4), 3, MissionType.Combat, 8);

            status.Text = "TESTES APROVADOS\n\n" +
                "6 missões da mesma categoria permitidas; " +
                "7ª missão, repetição e progresso inválido bloqueados.";
            GD.Print(status.Text);
        }
        catch (Exception exception)
        {
            status.Text = "TESTES REPROVADOS\n\n" + exception.Message;
            GD.PushError(status.Text);
        }
    }

    private static void ExpectRejected(
        IReadOnlyList<MissionDto> prepared, int completed,
        MissionType type, int id)
    {
        MissionDto mission = new() { Id = id, Type = type, Difficulty = 1 };
        NextRecommendationResponse recommendation = new()
        {
            RecommendedMissionId = id, RecommendedType = type, Difficulty = 1
        };
        try
        {
            AdaptiveMissionPolicy.ValidateNext(prepared, completed, recommendation, mission);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException("Uma recomendação inválida foi aceita.");
    }
}
