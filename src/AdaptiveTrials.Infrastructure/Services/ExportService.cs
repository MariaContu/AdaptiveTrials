using AdaptiveTrials.Application.DTOs.Exports;
using AdaptiveTrials.Application.Interfaces;
using AdaptiveTrials.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AdaptiveTrials.Infrastructure.Services;

public class ExportService : IExportService
{
    private readonly AppDbContext _context;

    public ExportService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<SessionExportResponse>> GetSessionsExportAsync()
    {
        return await _context
            .Sessions.AsNoTracking()
            .OrderBy(session => session.StartedAt)
            .Select(session => new SessionExportResponse
            {
                SessionId = session.Id,
                PlayerId = session.PlayerId,
                Mode = session.Mode,
                Status = session.Status,
                StartedAt = session.StartedAt,
                EndedAt = session.EndedAt,
                TotalEvents = session.BehaviorEvents.Count,
                TotalRecommendations = session.Recommendations.Count,
            })
            .ToListAsync();
    }

    public async Task<List<BehaviorEventExportResponse>> GetBehaviorEventsExportAsync()
    {
        return await _context
            .BehaviorEvents.AsNoTracking()
            .OrderBy(behaviorEvent => behaviorEvent.CreatedAt)
            .Select(behaviorEvent => new BehaviorEventExportResponse
            {
                EventId = behaviorEvent.Id,
                SessionId = behaviorEvent.SessionId,
                PlayerId = behaviorEvent.Session != null ? behaviorEvent.Session.PlayerId : 0,
                SessionMode = behaviorEvent.Session!.Mode,
                MissionId = behaviorEvent.MissionId,
                MissionType = behaviorEvent.MissionType,
                Template = behaviorEvent.Template,
                Difficulty = behaviorEvent.Difficulty,
                CompletionTime = behaviorEvent.CompletionTime,
                Failures = behaviorEvent.Failures,
                Success = behaviorEvent.Success,
                Persistence = behaviorEvent.Persistence,
                CreatedAt = behaviorEvent.CreatedAt,
            })
            .ToListAsync();
    }

    public async Task<List<RecommendationExportResponse>> GetRecommendationsExportAsync()
    {
        return await _context
            .Recommendations.AsNoTracking()
            .OrderBy(recommendation => recommendation.CreatedAt)
            .Select(recommendation => new RecommendationExportResponse
            {
                RecommendationId = recommendation.Id,
                SessionId = recommendation.SessionId,
                PlayerId = recommendation.Session != null ? recommendation.Session.PlayerId : 0,
                SessionMode = recommendation.Session!.Mode,
                MissionId = recommendation.MissionId,
                MissionName = recommendation.Mission != null ? recommendation.Mission.Name : null,
                RecommendedType = recommendation.RecommendedType,
                RecommendedDifficulty = recommendation.RecommendedDifficulty,
                CombatProbability = recommendation.CombatProbability,
                ExplorationProbability = recommendation.ExplorationProbability,
                PuzzleProbability = recommendation.PuzzleProbability,
                ProfileWeight = recommendation.ProfileWeight,
                BehaviorWeight = recommendation.BehaviorWeight,
                Reason = recommendation.Reason,
                CreatedAt = recommendation.CreatedAt,
            })
            .ToListAsync();
    }
}
