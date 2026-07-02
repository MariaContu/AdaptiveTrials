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
}