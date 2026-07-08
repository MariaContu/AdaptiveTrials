using AdaptiveTrials.Application.DTOs.Recommendations;
using AdaptiveTrials.Application.Interfaces;
using AdaptiveTrials.Domain.Entities;
using AdaptiveTrials.Domain.Enums;
using AdaptiveTrials.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AdaptiveTrials.Infrastructure.Services;

public class RecommendationService : IRecommendationService
{
    private readonly AppDbContext _context;

    public RecommendationService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<NextRecommendationResponse?> GetNextRecommendationAsync(
        NextRecommendationRequest request
    )
    {
        var playerExists = await _context.Players
            .AnyAsync(player => player.Id == request.PlayerId);

        if (!playerExists)
        {
            return null;
        }

        var session = await _context.Sessions
            .FirstOrDefaultAsync(session =>
                session.Id == request.SessionId &&
                session.PlayerId == request.PlayerId
            );

        if (session is null)
        {
            return null;
        }

        if (session.Status != SessionStatus.Started)
        {
            throw new InvalidOperationException("Cannot generate recommendation for a session that is not started.");
        }

        // Distribuição fixa inicial para validar o fluxo da API.
        var combatProbability = 0.34;
        var explorationProbability = 0.33;
        var puzzleProbability = 0.33;

        var recommendedType = MissionType.Combat;

        var mission = await _context.Missions
            .AsNoTracking()
            .Where(mission => mission.Type == recommendedType)
            .OrderBy(mission => mission.Difficulty)
            .ThenBy(mission => mission.Id)
            .FirstOrDefaultAsync();

        if (mission is null)
        {
            throw new InvalidOperationException("No available mission found for the recommended type.");
        }

        var recommendation = new Recommendation
        {
            SessionId = session.Id,
            MissionId = mission.Id,
            RecommendedType = recommendedType,
            RecommendedDifficulty = mission.Difficulty,
            CombatProbability = combatProbability,
            ExplorationProbability = explorationProbability,
            PuzzleProbability = puzzleProbability,
            ProfileWeight = 0.8,
            BehaviorWeight = 0.2,
            Reason = "Initial mock recommendation using fixed probabilities.",
            CreatedAt = DateTime.UtcNow
        };

        _context.Recommendations.Add(recommendation);
        await _context.SaveChangesAsync();

        return new NextRecommendationResponse
        {
            RecommendationId = recommendation.Id,
            RecommendedMissionId = mission.Id,
            MissionName = mission.Name,
            RecommendedType = recommendation.RecommendedType,
            Template = mission.Template,
            Difficulty = recommendation.RecommendedDifficulty,
            Probabilities = new RecommendationProbabilitiesResponse
            {
                Combat = recommendation.CombatProbability,
                Exploration = recommendation.ExplorationProbability,
                Puzzle = recommendation.PuzzleProbability
            },
            Weights = new RecommendationWeightsResponse
            {
                Profile = recommendation.ProfileWeight,
                Behavior = recommendation.BehaviorWeight
            },
            Reason = recommendation.Reason
        };
    }
}