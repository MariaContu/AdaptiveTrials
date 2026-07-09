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
        var player = await _context.Players
            .Include(player => player.NormalizedProfile)
            .FirstOrDefaultAsync(player => player.Id == request.PlayerId);

        if (player is null)
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

        var profileDistribution = GetProfileDistribution(player.NormalizedProfile);

        var behaviorDistribution = await GetBehaviorDistributionAsync(session.Id);

        if (behaviorDistribution is null)
        {
            return null;
        }

        var totalEvents = behaviorDistribution.TotalEvents;

        var weights = CalculateWeights(totalEvents);

        var finalDistribution = CombineDistributions(
            profileDistribution,
            behaviorDistribution,
            weights
        );

        var recommendedType = SelectMissionType(finalDistribution);

        var targetDifficulty = await CalculateTargetDifficultyAsync(session.Id);

        var mission = await SelectMissionByTypeAndDifficultyAsync(
            recommendedType,
            session.Id,
            targetDifficulty
        );

        if (mission is null)
        {
            throw new InvalidOperationException("No available mission found for the recommended type.");
        }

        var recommendation = new Recommendation
        {
            SessionId = session.Id,
            MissionId = mission.Id,
            RecommendedType = recommendedType,
            RecommendedDifficulty = targetDifficulty,
            CombatProbability = finalDistribution.Combat,
            ExplorationProbability = finalDistribution.Exploration,
            PuzzleProbability = finalDistribution.Puzzle,
            ProfileWeight = weights.Profile,
            BehaviorWeight = weights.Behavior,
            Reason = "Adaptive recommendation using probabilistic mission type selection and rule-based difficulty adjustment.",
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
            Template = mission.Template,Difficulty = mission.Difficulty,
            TargetDifficulty = targetDifficulty,

            ProfileProbabilities = new RecommendationProbabilitiesResponse
            {
                Combat = profileDistribution.Combat,
                Exploration = profileDistribution.Exploration,
                Puzzle = profileDistribution.Puzzle
            },

            BehaviorProbabilities = new RecommendationProbabilitiesResponse
            {
                Combat = behaviorDistribution.Combat,
                Exploration = behaviorDistribution.Exploration,
                Puzzle = behaviorDistribution.Puzzle
            },

            FinalProbabilities = new RecommendationProbabilitiesResponse
            {
                Combat = finalDistribution.Combat,
                Exploration = finalDistribution.Exploration,
                Puzzle = finalDistribution.Puzzle
            },

            Weights = new RecommendationWeightsResponse
            {
                Profile = weights.Profile,
                Behavior = weights.Behavior
            },

            Reason = recommendation.Reason
        };
    }

    private async Task<int> CalculateTargetDifficultyAsync(int sessionId)
    {
        var events = await _context.BehaviorEvents
            .AsNoTracking()
            .Where(behaviorEvent => behaviorEvent.SessionId == sessionId)
            .OrderByDescending(behaviorEvent => behaviorEvent.CreatedAt)
            .Take(5)
            .ToListAsync();

        if (events.Count == 0)
        {
            return 1;
        }

        var averageDifficulty = events.Average(behaviorEvent => behaviorEvent.Difficulty);
        var successRate = events.Count(behaviorEvent => behaviorEvent.Success) / (double)events.Count;
        var averageFailures = events.Average(behaviorEvent => behaviorEvent.Failures);
        var averagePersistence = events.Average(behaviorEvent => behaviorEvent.Persistence);
        var averageCompletionTime = events.Average(behaviorEvent => behaviorEvent.CompletionTime);

        var targetDifficulty = (int)Math.Round(averageDifficulty);

        var playerIsDoingWell =
            successRate >= 0.75 &&
            averageFailures <= 1 &&
            averagePersistence >= 0.7 &&
            averageCompletionTime <= 90;

        var playerIsStruggling =
            successRate < 0.5 ||
            averageFailures >= 3 ||
            averagePersistence < 0.4 ||
            averageCompletionTime >= 150;

        if (playerIsDoingWell)
        {
            targetDifficulty += 1;
        }
        else if (playerIsStruggling)
        {
            targetDifficulty -= 1;
        }

        return ClampDifficulty(targetDifficulty);
    }

    private static int ClampDifficulty(int difficulty)
    {
        if (difficulty < 1)
        {
            return 1;
        }

        if (difficulty > 5)
        {
            return 5;
        }

        return difficulty;
    }

    private static RecommendationProbabilitiesResponse GetProfileDistribution(
        NormalizedProfile? normalizedProfile
    )
    {
        if (normalizedProfile is null)
        {
            return new RecommendationProbabilitiesResponse
            {
                Combat = 1.0 / 3.0,
                Exploration = 1.0 / 3.0,
                Puzzle = 1.0 / 3.0
            };
        }

        var total = normalizedProfile.Combat +
                    normalizedProfile.Exploration +
                    normalizedProfile.Puzzle;

        if (total <= 0)
        {
            return new RecommendationProbabilitiesResponse
            {
                Combat = 1.0 / 3.0,
                Exploration = 1.0 / 3.0,
                Puzzle = 1.0 / 3.0
            };
        }

        return new RecommendationProbabilitiesResponse
        {
            Combat = normalizedProfile.Combat / total,
            Exploration = normalizedProfile.Exploration / total,
            Puzzle = normalizedProfile.Puzzle / total
        };
    }

    private static RecommendationWeightsResponse CalculateWeights(int totalEvents)
    {
        const double minimumProfileWeight = 0.3;
        const double behaviorGrowthPerEvent = 0.1;

        var behaviorWeight = 0.2 + totalEvents * behaviorGrowthPerEvent;

        if (behaviorWeight > 0.7)
        {
            behaviorWeight = 0.7;
        }

        var profileWeight = 1.0 - behaviorWeight;

        if (profileWeight < minimumProfileWeight)
        {
            profileWeight = minimumProfileWeight;
            behaviorWeight = 1.0 - profileWeight;
        }

        return new RecommendationWeightsResponse
        {
            Profile = profileWeight,
            Behavior = behaviorWeight
        };
    }

    private static RecommendationProbabilitiesResponse CombineDistributions(
        RecommendationProbabilitiesResponse profileDistribution,
        BehaviorDistributionResponse behaviorDistribution,
        RecommendationWeightsResponse weights
    )
    {
        var combat =
            weights.Profile * profileDistribution.Combat +
            weights.Behavior * behaviorDistribution.Combat;

        var exploration =
            weights.Profile * profileDistribution.Exploration +
            weights.Behavior * behaviorDistribution.Exploration;

        var puzzle =
            weights.Profile * profileDistribution.Puzzle +
            weights.Behavior * behaviorDistribution.Puzzle;

        var total = combat + exploration + puzzle;

        if (total <= 0)
        {
            return new RecommendationProbabilitiesResponse
            {
                Combat = 1.0 / 3.0,
                Exploration = 1.0 / 3.0,
                Puzzle = 1.0 / 3.0
            };
        }

        return new RecommendationProbabilitiesResponse
        {
            Combat = combat / total,
            Exploration = exploration / total,
            Puzzle = puzzle / total
        };
    }

    private static MissionType SelectMissionType(
        RecommendationProbabilitiesResponse finalDistribution
    )
    {
        var randomValue = Random.Shared.NextDouble();

        var combatLimit = finalDistribution.Combat;
        var explorationLimit = combatLimit + finalDistribution.Exploration;

        if (randomValue <= combatLimit)
        {
            return MissionType.Combat;
        }

        if (randomValue <= explorationLimit)
        {
            return MissionType.Exploration;
        }

        return MissionType.Puzzle;
    }

    private async Task<Mission?> SelectMissionByTypeAndDifficultyAsync(
        MissionType recommendedType,
        int sessionId,
        int targetDifficulty
    )
    {
        var previouslyRecommendedMissionIds = await _context.Recommendations
            .AsNoTracking()
            .Where(recommendation => recommendation.SessionId == sessionId)
            .Where(recommendation => recommendation.MissionId.HasValue)
            .Select(recommendation => recommendation.MissionId!.Value)
            .ToListAsync();

        var availableMissions = await _context.Missions
            .AsNoTracking()
            .Where(mission => mission.Type == recommendedType)
            .ToListAsync();

        if (availableMissions.Count == 0)
        {
            return null;
        }

        var bestDistance = availableMissions
            .Min(mission => Math.Abs(mission.Difficulty - targetDifficulty));

        var closestMissions = availableMissions
            .Where(mission => Math.Abs(mission.Difficulty - targetDifficulty) == bestDistance)
            .OrderBy(mission => mission.Difficulty)
            .ThenBy(mission => mission.Id)
            .ToList();

        var notRepeatedClosestMissions = closestMissions
            .Where(mission => !previouslyRecommendedMissionIds.Contains(mission.Id))
            .ToList();

        var candidateMissions = notRepeatedClosestMissions.Count > 0
            ? notRepeatedClosestMissions
            : closestMissions;

        var selectedIndex = Random.Shared.Next(candidateMissions.Count);

        return candidateMissions[selectedIndex];
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