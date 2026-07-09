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

    private static double CalculateCategoryScore(
        List<BehaviorEvent> events,
        Domain.Enums.MissionType missionType
    )
    {
        var categoryEvents = events
            .Where(behaviorEvent => behaviorEvent.MissionType == missionType)
            .ToList();

        if (categoryEvents.Count == 0)
        {
            return 0;
        }

        var successScore = categoryEvents.Count(behaviorEvent => behaviorEvent.Success);
        var frequencyScore = categoryEvents.Count;
        var failurePenalty = categoryEvents.Sum(behaviorEvent => behaviorEvent.Failures);

        var averageCompletionTime = categoryEvents.Average(behaviorEvent => behaviorEvent.CompletionTime);
        var timePenalty = NormalizeTimePenalty(averageCompletionTime);

        var persistenceBonus = categoryEvents.Average(behaviorEvent => behaviorEvent.Persistence);

        const double alpha = 1.0;  // sucesso
        const double beta = 0.5;   // frequência
        const double gamma = 0.4;  // falhas
        const double delta = 0.3;  // tempo
        const double omega = 0.5;  // persistência

        return
            alpha * successScore +
            beta * frequencyScore +
            omega * persistenceBonus -
            gamma * failurePenalty -
            delta * timePenalty;
    }

    private static double NormalizeTimePenalty(double averageCompletionTime)
    {
        // Penalização simples inicial.
        // A ideia é evitar que tempo bruto domine demais o score.
        // Depois podemos calibrar por tipo de missão.
        const double referenceTime = 60.0;

        return averageCompletionTime / referenceTime;
    }
    
    public async Task<BehaviorDistributionResponse?> GetBehaviorDistributionAsync(int sessionId)
    {
        var sessionExists = await _context.Sessions
            .AnyAsync(session => session.Id == sessionId);

        if (!sessionExists)
        {
            return null;
        }

        var events = await _context.BehaviorEvents
            .AsNoTracking()
            .Where(behaviorEvent => behaviorEvent.SessionId == sessionId)
            .ToListAsync();

        if (events.Count == 0)
        {
            return new BehaviorDistributionResponse
            {
                Combat = 1.0 / 3.0,
                Exploration = 1.0 / 3.0,
                Puzzle = 1.0 / 3.0,
                TotalEvents = 0
            };
        }

        var combatScore = CalculateCategoryScore(events, Domain.Enums.MissionType.Combat);
        var explorationScore = CalculateCategoryScore(events, Domain.Enums.MissionType.Exploration);
        var puzzleScore = CalculateCategoryScore(events, Domain.Enums.MissionType.Puzzle);

        const double epsilon = 0.1;

        combatScore = Math.Max(combatScore, epsilon);
        explorationScore = Math.Max(explorationScore, epsilon);
        puzzleScore = Math.Max(puzzleScore, epsilon);

        var totalScore = combatScore + explorationScore + puzzleScore;

        return new BehaviorDistributionResponse
        {
            Combat = combatScore / totalScore,
            Exploration = explorationScore / totalScore,
            Puzzle = puzzleScore / totalScore,
            TotalEvents = events.Count
        };
    }
}