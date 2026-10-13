using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdaptiveTrials.Application.DTOs.BehaviorEvents;
using AdaptiveTrials.Application.DTOs.Players;
using AdaptiveTrials.Application.DTOs.Recommendations;
using AdaptiveTrials.Application.DTOs.Sessions;
using AdaptiveTrials.Domain.Enums;
using AdaptiveTrials.Domain.Entities;
using AdaptiveTrials.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdaptiveTrials.Api.IntegrationTests;

/// <summary>
/// Regressões da distribuição comportamental, incluindo uma reprodução
/// dos seis eventos registrados na sessão 14 (10/10/2026).
/// Não acessa o banco real: cada teste usa SQLite em memória.
/// </summary>
public sealed class BehaviorDistributionRegressionTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AdaptiveTrialsApiFactory _factory = new();
    private readonly HttpClient _client;

    public BehaviorDistributionRegressionTests()
    {
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task OneSuccessfulExploration_ShouldNotTurnMissionFrequencyIntoNearCertainPreference()
    {
        var session = await CreateSessionAsync();
        await SetPreferencesAsync(session.PlayerId, 0.5155210468868904, 0.4392412542599835, 0.04523769885312616);
        await AddEventAsync(session.SessionId, 12, 5.796244888888982, 0, true, 1.0);

        var behavior = await GetBehaviorAsync(session.SessionId);
        Assert.Equal(1, behavior.TotalEvents);
        AssertNear(behavior.Combat, 0.3148, 0.001);
        AssertNear(behavior.Exploration, 0.3705, 0.001);
        AssertNear(behavior.Puzzle, 0.3148, 0.001);

        var next = await GetRecommendationAsync(session);
        AssertNear(next.Weights.Profile, 0.7);
        AssertNear(next.Weights.Behavior, 0.3);
        Assert.True(next.FinalProbabilities.Combat > next.FinalProbabilities.Exploration,
            "Uma única exploração sorteada não deve inverter automaticamente a preferência inicial por combate.");
        Assert.True(next.FinalProbabilities.Puzzle > 0.10,
            "Categorias não experimentadas não devem ter probabilidade quase nula por falta de exposição.");
        AssertDistributionSumsToOne(next.FinalProbabilities);
    }

    [Fact]
    public async Task Session14Replay_ShouldKeepBehaviorSmoothedAfterFiveExplorationsAndOneCombat()
    {
        var session = await CreateSessionAsync();
        await SetPreferencesAsync(session.PlayerId, 0.5155210468868904, 0.4392412542599835, 0.04523769885312616);

        // Mesma ordem, dificuldade implícita no catálogo, resultado e métricas
        // dos seis eventos fornecidos para a sessão 14.
        await AddEventAsync(session.SessionId, 12, 5.796244888888982, 0, true, 1.0);
        await AddEventAsync(session.SessionId, 24, 11.439847555555687, 0, true, 1.0);
        await AddEventAsync(session.SessionId, 11, 17.029992444444602, 0, true, 1.0);
        await AddEventAsync(session.SessionId, 8, 37.313639222217766, 1, false, 0.8);
        await AddEventAsync(session.SessionId, 13, 10.068563365079456, 2, false, 0.6);
        await AddEventAsync(session.SessionId, 10, 15.28513333333379, 0, true, 1.0);

        var behavior = await GetBehaviorAsync(session.SessionId);
        Assert.Equal(6, behavior.TotalEvents);
        AssertNear(behavior.Combat, 0.2740, 0.001);
        AssertNear(behavior.Exploration, 0.3980, 0.001);
        AssertNear(behavior.Puzzle, 0.3280, 0.001);
        Assert.True(behavior.Exploration < 0.5,
            "Cinco exposições à exploração não justificam, por si só, 93,65% de preferência comportamental.");
        Assert.True(behavior.Puzzle > 0.25,
            "Puzzle não foi apresentado ao jogador; ausência de eventos não equivale a rejeição.");

        // Depois de seis resultados nao deve haver setima recomendacao.
        // A combinacao hibrida continua verificavel diretamente pela formula.
        const double profileWeight = 0.3;
        const double behaviorWeight = 0.7;
        AssertNear(
            profileWeight * 0.5155210468868904 + behaviorWeight * behavior.Combat,
            0.3465, 0.002);
        AssertNear(
            profileWeight * 0.4392412542599835 + behaviorWeight * behavior.Exploration,
            0.4104, 0.002);
        AssertNear(
            profileWeight * 0.04523769885312616 + behaviorWeight * behavior.Puzzle,
            0.2432, 0.002);
    }

    [Fact]
    public async Task ExplorationFailure_ShouldReduceItsBehaviorScoreComparedToSuccess()
    {
        var successfulSession = await CreateSessionAsync();
        await AddEventAsync(successfulSession.SessionId, 12, 30, 0, true, 1.0);
        var successful = await GetBehaviorAsync(successfulSession.SessionId);

        var failedSession = await CreateSessionAsync();
        await AddEventAsync(failedSession.SessionId, 12, 30, 2, false, 0.6);
        var failed = await GetBehaviorAsync(failedSession.SessionId);

        Assert.True(failed.Exploration < successful.Exploration,
            "Uma falha com menor persistência deve reduzir o escore relativo da categoria.");
        Assert.True(failed.Combat > successful.Combat);
        Assert.True(failed.Puzzle > successful.Puzzle);
        Assert.True(failed.Exploration > 0,
            "Uma falha isolada não deve zerar a categoria.");
    }

    private async Task<CreateSessionResponse> CreateSessionAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/sessions",
            new CreateSessionRequest { Mode = GameMode.Adaptive },
            JsonOptions,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<CreateSessionResponse>(
            JsonOptions, TestContext.Current.CancellationToken);
        Assert.NotNull(session);
        return session;
    }

    private async Task SetPreferencesAsync(int playerId, double combat, double exploration, double puzzle)
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/players/{playerId}/preferences",
            new ManualPreferencesRequest { Combat = combat, Exploration = exploration, Puzzle = puzzle },
            JsonOptions,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task AddEventAsync(
        int sessionId, int missionId, double completionTime,
        int failures, bool success, double persistence)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var mission = await context.Missions.SingleAsync(item => item.Id == missionId);
        context.Recommendations.Add(new Recommendation
        {
            SessionId = sessionId, MissionId = mission.Id,
            RecommendedType = mission.Type, RecommendedDifficulty = mission.Difficulty
        });
        context.BehaviorEvents.Add(new BehaviorEvent
        {
            SessionId = sessionId, MissionId = mission.Id,
            MissionType = mission.Type, Template = mission.Template,
            Difficulty = mission.Difficulty, CompletionTime = completionTime,
            Failures = failures, Success = success, Persistence = persistence
        });
        await context.SaveChangesAsync();
    }

    private async Task<BehaviorDistributionResponse> GetBehaviorAsync(int sessionId)
    {
        var result = await _client.GetFromJsonAsync<BehaviorDistributionResponse>(
            $"/api/recommendations/behavior-distribution/{sessionId}",
            JsonOptions,
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        return result;
    }

    private async Task<NextRecommendationResponse> GetRecommendationAsync(CreateSessionResponse session)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/recommendations/next",
            new NextRecommendationRequest { PlayerId = session.PlayerId, SessionId = session.SessionId },
            JsonOptions,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var recommendation = await response.Content.ReadFromJsonAsync<NextRecommendationResponse>(
            JsonOptions,
            TestContext.Current.CancellationToken);
        Assert.NotNull(recommendation);
        return recommendation;
    }

    private static void AssertDistributionSumsToOne(RecommendationProbabilitiesResponse distribution)
    {
        AssertNear(distribution.Combat + distribution.Exploration + distribution.Puzzle, 1.0);
        Assert.InRange(distribution.Combat, 0.0, 1.0);
        Assert.InRange(distribution.Exploration, 0.0, 1.0);
        Assert.InRange(distribution.Puzzle, 0.0, 1.0);
    }

    private static void AssertNear(double actual, double expected, double tolerance = 1e-10)
    {
        Assert.True(Math.Abs(actual - expected) < tolerance,
            $"Esperado {expected:F6}; obtido {actual:F6} (tolerância {tolerance}).");
    }
}
