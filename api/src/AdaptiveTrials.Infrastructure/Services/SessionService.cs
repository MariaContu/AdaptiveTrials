using AdaptiveTrials.Application.DTOs.SessionQueries;
using AdaptiveTrials.Application.DTOs.Sessions;
using AdaptiveTrials.Application.Interfaces;
using AdaptiveTrials.Domain.Entities;
using AdaptiveTrials.Domain.Enums;
using AdaptiveTrials.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AdaptiveTrials.Infrastructure.Services;

public class SessionService : ISessionService
{
    private readonly AppDbContext _context;

    public SessionService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<CreateSessionResponse> CreateSessionAsync(CreateSessionRequest request)
    {
        Player player;

        if (request.PlayerId.HasValue)
        {
            player = await _context.Players
                .FirstOrDefaultAsync(p => p.Id == request.PlayerId.Value)
                ?? throw new InvalidOperationException("Player not found.");
        }
        else
        {
            // Jogador anônimo para facilitar testes iniciais da API.
            player = new Player
            {
                CreatedAt = DateTime.UtcNow
            };

            _context.Players.Add(player);
            await _context.SaveChangesAsync();
        }

        var session = new GameSession
        {
            PlayerId = player.Id,
            Mode = request.Mode,
            Status = SessionStatus.Started,
            StartedAt = DateTime.UtcNow
        };

        _context.Sessions.Add(session);
        await _context.SaveChangesAsync();

        return new CreateSessionResponse
        {
            SessionId = session.Id,
            PlayerId = session.PlayerId,
            Mode = session.Mode,
            Status = session.Status,
            StartedAt = session.StartedAt
        };
    }

    public async Task<EndSessionResponse?> EndSessionAsync(int sessionId)
    {
        var session = await _context.Sessions
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session is null)
        {
            return null;
        }

        if (session.Status == SessionStatus.Finished)
        {
            return new EndSessionResponse
            {
                SessionId = session.Id,
                Status = session.Status,
                StartedAt = session.StartedAt,
                EndedAt = session.EndedAt
            };
        }

        session.Status = SessionStatus.Finished;
        session.EndedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return new EndSessionResponse
        {
            SessionId = session.Id,
            Status = session.Status,
            StartedAt = session.StartedAt,
            EndedAt = session.EndedAt
        };
    }

    public async Task<List<PlayerSessionSummaryResponse>?> GetPlayerSessionsAsync(int playerId)
    {
        var playerExists = await _context.Players
            .AnyAsync(player => player.Id == playerId);

        if (!playerExists)
        {
            return null;
        }

        return await _context.Sessions
            .AsNoTracking()
            .Where(session => session.PlayerId == playerId)
            .OrderByDescending(session => session.StartedAt)
            .Select(session => new PlayerSessionSummaryResponse
            {
                SessionId = session.Id,
                Mode = session.Mode,
                Status = session.Status,
                StartedAt = session.StartedAt,
                EndedAt = session.EndedAt,
                TotalEvents = session.BehaviorEvents.Count,
                TotalRecommendations = session.Recommendations.Count
            })
            .ToListAsync();
    }

    public async Task<SessionDetailsResponse?> GetSessionByIdAsync(int sessionId)
    {
        var session = await _context.Sessions
            .AsNoTracking()
            .Where(session => session.Id == sessionId)
            .Select(session => new SessionDetailsResponse
            {
                SessionId = session.Id,
                PlayerId = session.PlayerId,
                Mode = session.Mode,
                Status = session.Status,
                StartedAt = session.StartedAt,
                EndedAt = session.EndedAt,
                TotalEvents = session.BehaviorEvents.Count,
                TotalRecommendations = session.Recommendations.Count
            })
            .FirstOrDefaultAsync();

        return session;
    }

    public async Task<List<SessionEventResponse>?> GetSessionEventsAsync(int sessionId)
    {
        var sessionExists = await _context.Sessions
            .AnyAsync(session => session.Id == sessionId);

        if (!sessionExists)
        {
            return null;
        }

        return await _context.BehaviorEvents
            .AsNoTracking()
            .Where(behaviorEvent => behaviorEvent.SessionId == sessionId)
            .OrderBy(behaviorEvent => behaviorEvent.CreatedAt)
            .Select(behaviorEvent => new SessionEventResponse
            {
                EventId = behaviorEvent.Id,
                SessionId = behaviorEvent.SessionId,
                MissionId = behaviorEvent.MissionId,
                MissionType = behaviorEvent.MissionType,
                Template = behaviorEvent.Template,
                Difficulty = behaviorEvent.Difficulty,
                CompletionTime = behaviorEvent.CompletionTime,
                Failures = behaviorEvent.Failures,
                Success = behaviorEvent.Success,
                Persistence = behaviorEvent.Persistence,
                CreatedAt = behaviorEvent.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<List<SessionRecommendationResponse>?> GetSessionRecommendationsAsync(int sessionId)
    {
        var sessionExists = await _context.Sessions
            .AnyAsync(session => session.Id == sessionId);

        if (!sessionExists)
        {
            return null;
        }

        return await _context.Recommendations
            .AsNoTracking()
            .Where(recommendation => recommendation.SessionId == sessionId)
            .OrderBy(recommendation => recommendation.CreatedAt)
            .Select(recommendation => new SessionRecommendationResponse
            {
                RecommendationId = recommendation.Id,
                SessionId = recommendation.SessionId,
                MissionId = recommendation.MissionId,
                MissionName = recommendation.Mission != null
                    ? recommendation.Mission.Name
                    : null,
                RecommendedType = recommendation.RecommendedType,
                RecommendedDifficulty = recommendation.RecommendedDifficulty,
                CombatProbability = recommendation.CombatProbability,
                ExplorationProbability = recommendation.ExplorationProbability,
                PuzzleProbability = recommendation.PuzzleProbability,
                ProfileWeight = recommendation.ProfileWeight,
                BehaviorWeight = recommendation.BehaviorWeight,
                Reason = recommendation.Reason,
                CreatedAt = recommendation.CreatedAt
            })
            .ToListAsync();
    }
}