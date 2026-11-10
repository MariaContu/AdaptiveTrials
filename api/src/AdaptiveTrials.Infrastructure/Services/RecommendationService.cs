using AdaptiveTrials.Application.DTOs.Recommendations;
using AdaptiveTrials.Application.Exceptions;
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
        NextRecommendationRequest request)
    {
        var player = await _context.Players
            .Include(player => player.NormalizedProfile)
            .FirstOrDefaultAsync(player => player.Id == request.PlayerId);

        if (player is null)
            return null;

        var session = await _context.Sessions
            .FirstOrDefaultAsync(session =>
                session.Id == request.SessionId &&
                session.PlayerId == request.PlayerId);

        if (session is null)
            return null;

        if (session.Status != SessionStatus.Started)
            throw new InvalidOperationException("Cannot generate recommendation for a session that is not started.");

        if (session.Mode != GameMode.Adaptive)
            throw new InvalidOperationException("Recommendations are only available for adaptive sessions.");

        // A transacao impede que duas requisicoes simultaneas criem
        // recomendacoes diferentes para a mesma etapa.
        await using var transaction = await _context.Database.BeginTransactionAsync();

        // Revalidar apos adquirir a transacao: a sessao pode ter sido
        // encerrada enquanto a requisicao aguardava o bloqueio do banco.
        if (!await _context.Sessions.AsNoTracking().AnyAsync(item =>
            item.Id == session.Id && item.Status == SessionStatus.Started))
            throw new InvalidOperationException("Cannot generate recommendation for a session that is not started.");

        var recommendations = await _context.Recommendations
            .AsNoTracking()
            .Include(recommendation => recommendation.Mission)
            .Where(recommendation => recommendation.SessionId == session.Id)
            .OrderBy(recommendation => recommendation.Id)
            .ToListAsync();

        var eventCount = await _context.BehaviorEvents
            .AsNoTracking()
            .CountAsync(behaviorEvent => behaviorEvent.SessionId == session.Id);

        if (eventCount > recommendations.Count ||
            recommendations.Count > eventCount + 1)
        {
            throw new AdaptiveSessionConflictException(
                "A sequencia de recomendacoes e eventos desta sessao esta inconsistente.");
        }

        var profileDistribution = GetProfileDistribution(player.NormalizedProfile);
        var behaviorDistribution = await GetBehaviorDistributionAsync(session.Id)
            ?? throw new InvalidOperationException("Session not found.");
        var weights = CalculateWeights(eventCount);
        var finalDistribution = CombineDistributions(
            profileDistribution, behaviorDistribution, weights);

        // Repeticao de /next (ex.: timeout) reutiliza a recomendacao
        // pendente. Nenhum novo sorteio ou registro e realizado.
        if (recommendations.Count == eventCount + 1)
        {
            var pending = recommendations[^1];
            if (pending.Mission is null)
                throw new AdaptiveSessionConflictException("Recomendacao pendente sem missao valida.");

            return BuildResponse(
                pending, pending.Mission, profileDistribution, behaviorDistribution,
                new RecommendationProbabilitiesResponse
                {
                    Combat = pending.CombatProbability,
                    Exploration = pending.ExplorationProbability,
                    Puzzle = pending.PuzzleProbability
                },
                new RecommendationWeightsResponse
                {
                    Profile = pending.ProfileWeight,
                    Behavior = pending.BehaviorWeight
                });
        }

        if (recommendations.Count >= 6)
            throw new AdaptiveSessionConflictException(
                "A sessao adaptativa ja concluiu as seis recomendacoes.");

        // V2: sorteio por distribuicao, sem cota por categoria.
        var recommendedType = SelectMissionType(finalDistribution);
        var targetDifficulty = await CalculateTargetDifficultyAsync(session.Id);
        var mission = await SelectMissionByTypeAndDifficultyAsync(
            recommendedType, session.Id, targetDifficulty);

        if (mission is null)
            throw new InvalidOperationException("No available mission found for the recommended type.");

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
        await transaction.CommitAsync();

        return BuildResponse(
            recommendation, mission, profileDistribution, behaviorDistribution,
            finalDistribution, weights);
    }

    private static NextRecommendationResponse BuildResponse(
        Recommendation recommendation,
        Mission mission,
        RecommendationProbabilitiesResponse profileDistribution,
        BehaviorDistributionResponse behaviorDistribution,
        RecommendationProbabilitiesResponse finalDistribution,
        RecommendationWeightsResponse weights)
    {
        return new NextRecommendationResponse
        {
            RecommendationId = recommendation.Id,
            RecommendedMissionId = mission.Id,
            MissionName = mission.Name,
            RecommendedType = recommendation.RecommendedType,
            Template = mission.Template,
            Difficulty = mission.Difficulty,
            TargetDifficulty = recommendation.RecommendedDifficulty,
            ProfileProbabilities = profileDistribution,
            BehaviorProbabilities = new RecommendationProbabilitiesResponse
            {
                Combat = behaviorDistribution.Combat,
                Exploration = behaviorDistribution.Exploration,
                Puzzle = behaviorDistribution.Puzzle
            },
            FinalProbabilities = finalDistribution,
            Weights = weights,
            Reason = recommendation.Reason
        };
    }

    private async Task<int> CalculateTargetDifficultyAsync(int sessionId)
    {
        // A dificuldade acompanha a missao realmente jogada, sem usar
        // a media de dificuldades antigas como nivel de referencia.
        var recentEvents = await _context.BehaviorEvents
            .AsNoTracking()
            .Where(behaviorEvent => behaviorEvent.SessionId == sessionId)
            .OrderByDescending(behaviorEvent => behaviorEvent.CreatedAt)
            .ThenByDescending(behaviorEvent => behaviorEvent.Id)
            .Take(2)
            .ToListAsync();

        if (recentEvents.Count == 0)
        {
            return 1;
        }

        var latest = recentEvents[0];
        var currentDifficulty = ClampDifficulty(latest.Difficulty);

        // Uma falha deve gerar alivio imediatamente, mesmo quando o
        // jogador teve varios sucessos antes dela.
        if (!latest.Success)
        {
            return ClampDifficulty(currentDifficulty - 1);
        }

        // Um sucesso com muitas falhas ou pouca persistencia nao indica
        // que o jogador esta pronto para um desafio maior.
        if (latest.Failures >= 3 || latest.Persistence < 0.4)
        {
            return ClampDifficulty(currentDifficulty - 1);
        }

        // A primeira vitoria apos uma falha estabiliza a dificuldade.
        // Para subir novamente sao necessarios dois sucessos consecutivos.
        if (recentEvents.Count > 1 && !recentEvents[1].Success)
        {
            return currentDifficulty;
        }

        var averageFailures = recentEvents.Average(behaviorEvent => behaviorEvent.Failures);
        var averagePersistence = recentEvents.Average(behaviorEvent => behaviorEvent.Persistence);

        // O tempo bruto nao define progressao: Sobreviver, Puzzle e
        // Chegar ao Destino possuem duracoes naturais muito diferentes.
        var playerIsDoingWell = averageFailures <= 1 && averagePersistence >= 0.7;

        return ClampDifficulty(currentDifficulty + (playerIsDoingWell ? 1 : 0));
    }

    private static int ClampDifficulty(int difficulty)
    {
        if (difficulty < 1)
        {
            return 1;
        }

        if (difficulty > 3)
        {
            return 3;
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
        RecommendationProbabilitiesResponse finalDistribution)
    {
        var weightedTypes = new (MissionType Type, double Weight)[]
        {
            (MissionType.Combat, Math.Max(0, finalDistribution.Combat)),
            (MissionType.Exploration, Math.Max(0, finalDistribution.Exploration)),
            (MissionType.Puzzle, Math.Max(0, finalDistribution.Puzzle))
        };

        var totalWeight = weightedTypes.Sum(item => item.Weight);
        if (totalWeight <= 0)
            return weightedTypes[Random.Shared.Next(weightedTypes.Length)].Type;

        var randomValue = Random.Shared.NextDouble() * totalWeight;
        foreach (var item in weightedTypes)
        {
            randomValue -= item.Weight;
            if (randomValue < 0)
                return item.Type;
        }

        return weightedTypes[^1].Type;
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

        var notRepeatedMissions = availableMissions
            .Where(mission => !previouslyRecommendedMissionIds.Contains(mission.Id))
            .ToList();

        // During a normal six-mission adaptive session there are enough catalog
        // options to avoid repetition. Only fall back to the full category when
        // every mission of that category has already been recommended.
        var missionPool = notRepeatedMissions.Count > 0
            ? notRepeatedMissions
            : availableMissions;

        var bestDistance = missionPool
            .Min(mission => Math.Abs(mission.Difficulty - targetDifficulty));

        var candidateMissions = missionPool
            .Where(mission => Math.Abs(mission.Difficulty - targetDifficulty) == bestDistance)
            .OrderBy(mission => mission.Difficulty)
            .ThenBy(mission => mission.Id)
            .ToList();

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

        // Não usar a frequência da categoria como recompensa: isso criava
        // retroalimentação (uma exploração sorteada aumentava a chance de
        // mais explorações, mesmo com perfil 52% combate / 48% exploração).
        // Categorias não experimentadas recebem uma estimativa neutra.
        const double neutralPerformance = 0.65;
        const double priorWeight = 2.0;
        if (categoryEvents.Count == 0)
            return neutralPerformance;

        var performance = categoryEvents.Average(behaviorEvent =>
        {
            var success = behaviorEvent.Success ? 0.65 : 0.10;
            var persistence = 0.20 * Math.Clamp(behaviorEvent.Persistence, 0, 1);
            var speed = 0.15 * Math.Clamp(1.0 - behaviorEvent.CompletionTime / 180.0, 0, 1);
            var failurePenalty = 0.05 * Math.Min(behaviorEvent.Failures, 4);
            return Math.Clamp(success + persistence + speed - failurePenalty, 0.05, 1.0);
        });

        return (priorWeight * neutralPerformance + categoryEvents.Count * performance)
            / (priorWeight + categoryEvents.Count);
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