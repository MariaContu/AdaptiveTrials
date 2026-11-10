using AdaptiveTrials.Application.DTOs.BehaviorEvents;
using AdaptiveTrials.Application.Exceptions;
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
        RegisterBehaviorEventRequest request)
    {
        var session = await _context.Sessions
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session is null)
            return null;

        if (session.Status != SessionStatus.Started)
            throw new InvalidOperationException("Cannot register events for a session that is not started.");

        var mission = await _context.Missions
            .FirstOrDefaultAsync(m => m.Id == request.MissionId);

        if (mission is null)
            throw new InvalidOperationException("Mission not found.");

        if (!double.IsFinite(request.CompletionTime) || request.CompletionTime < 0)
            throw new InvalidOperationException("Completion time must be finite and non-negative.");

        if (request.Failures < 0)
            throw new InvalidOperationException("Failures cannot be negative.");

        if (!double.IsFinite(request.Persistence) || request.Persistence < 0 || request.Persistence > 1)
            throw new InvalidOperationException("Persistence must be finite and between 0 and 1.");

        // Transacao comum aos dois modos: uma retransmissao do mesmo
        // MissionId nunca deve aumentar a quantidade de eventos da sessao.
        await using var transaction = await _context.Database.BeginTransactionAsync();

        if (!await _context.Sessions.AsNoTracking().AnyAsync(item =>
            item.Id == session.Id && item.Status == SessionStatus.Started))
            throw new InvalidOperationException("Cannot register events for a session that is not started.");

        var events = await _context.BehaviorEvents
            .AsNoTracking()
            .Where(behaviorEvent => behaviorEvent.SessionId == session.Id)
            .OrderBy(behaviorEvent => behaviorEvent.Id)
            .ToListAsync();

        var previous = events.FirstOrDefault(behaviorEvent =>
            behaviorEvent.MissionId == request.MissionId);
        if (previous is not null)
        {
            // Idempotencia: repetir um POST identico devolve o mesmo EventId.
            if (SameResult(previous, request))
                return ToResponse(previous, alreadyRegistered: true);

            throw new AdaptiveSessionConflictException(
                "Ja existe um resultado diferente para esta missao na sessao.");
        }

        if (events.Count >= 6)
            throw new AdaptiveSessionConflictException(
                "A sessao ja possui seis eventos registrados.");

        if (session.Mode == GameMode.Adaptive)
        {
            var recommendations = await _context.Recommendations
                .AsNoTracking()
                .Where(recommendation => recommendation.SessionId == session.Id)
                .OrderBy(recommendation => recommendation.Id)
                .ToListAsync();

            if (recommendations.Count == 0 ||
                recommendations.Count > 6 ||
                recommendations.Count != events.Count + 1)
            {
                throw new AdaptiveSessionConflictException(
                    "Nenhuma recomendacao pendente de resultado nesta sessao.");
            }

            if (recommendations[^1].MissionId != request.MissionId)
            {
                throw new AdaptiveSessionConflictException(
                    "O evento nao corresponde a missao recomendada pendente.");
            }
        }

        // V1 continua sem depender de recomendacoes: somente um resultado
        // por missao, com no maximo seis eventos por sessao.
        var registered = CreateEvent(session.Id, mission, request);
        _context.BehaviorEvents.Add(registered);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        return ToResponse(registered);
    }

    private static BehaviorEvent CreateEvent(
        int sessionId,
        Mission mission,
        RegisterBehaviorEventRequest request) => new()
    {
        SessionId = sessionId,
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

    private static bool SameResult(BehaviorEvent previous, RegisterBehaviorEventRequest request) =>
        previous.MissionId == request.MissionId &&
        Math.Abs(previous.CompletionTime - request.CompletionTime) < 1e-9 &&
        previous.Failures == request.Failures &&
        previous.Success == request.Success &&
        Math.Abs(previous.Persistence - request.Persistence) < 1e-9;

    private static RegisterBehaviorEventResponse ToResponse(
        BehaviorEvent behaviorEvent,
        bool alreadyRegistered = false) => new()
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
        CreatedAt = behaviorEvent.CreatedAt,
        AlreadyRegistered = alreadyRegistered
    };
}
