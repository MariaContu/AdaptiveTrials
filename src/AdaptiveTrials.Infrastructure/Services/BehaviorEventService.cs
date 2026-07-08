using AdaptiveTrials.Application.DTOs.BehaviorEvents;
using AdaptiveTrials.Application.Interfaces;
using AdaptiveTrials.Domain.Entities;
using AdaptiveTrials.Domain.Enums;
using AdaptiveTrials.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AdaptiveTrials.Infrastructure.Services;

public class BehaviorEventService : IBehaviorEventService
{
    private readonly AppDbContext _context;

    public BehaviorEventService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<RegisterBehaviorEventResponse?> RegisterEventAsync(
        int sessionId,
        RegisterBehaviorEventRequest request
    )
    {
        var session = await _context.Sessions
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session is null)
        {
            return null;
        }

        if (session.Status != SessionStatus.Started)
        {
            throw new InvalidOperationException("Cannot register events for a session that is not started.");
        }

        var mission = await _context.Missions
            .FirstOrDefaultAsync(m => m.Id == request.MissionId);

        if (mission is null)
        {
            throw new InvalidOperationException("Mission not found.");
        }

        if (request.CompletionTime < 0)
        {
            throw new InvalidOperationException("Completion time cannot be negative.");
        }

        if (request.Failures < 0)
        {
            throw new InvalidOperationException("Failures cannot be negative.");
        }

        if (request.Persistence < 0 || request.Persistence > 1)
        {
            throw new InvalidOperationException("Persistence must be between 0 and 1.");
        }

        var behaviorEvent = new BehaviorEvent
        {
            SessionId = session.Id,
            MissionId = mission.Id,
            MissionType = mission.Type,
            Template = mission.Template,
            Difficulty = mission.Difficulty,
            CompletionTime = request.CompletionTime,
            Failures = request.Failures,
            Success = request.Success,
            Persistence = request.Persistence,
            CreatedAt = DateTime.UtcNow
        };

        _context.BehaviorEvents.Add(behaviorEvent);
        await _context.SaveChangesAsync();

        return new RegisterBehaviorEventResponse
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
        };
    }
}