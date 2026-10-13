using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdaptiveTrials.Application.DTOs.BehaviorEvents;
using AdaptiveTrials.Application.DTOs.Recommendations;
using AdaptiveTrials.Application.DTOs.Sessions;
using AdaptiveTrials.Domain.Enums;
using AdaptiveTrials.Domain.Entities;
using AdaptiveTrials.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdaptiveTrials.Api.IntegrationTests;

public sealed class AdaptiveDifficultyRegressionTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AdaptiveTrialsApiFactory _factory = new();
    private readonly HttpClient _client;

    public AdaptiveDifficultyRegressionTests()
    {
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task NoEvents_ShouldStartOnDifficultyOne()
    {
        var session = await CreateSessionAsync();
        Assert.Equal(1, await GetTargetDifficultyAsync(session));
    }

    [Theory]
    [InlineData(1, true, 0, 1.0, 15, 2)]
    [InlineData(3, true, 0, 1.0, 30, 3)]
    [InlineData(3, false, 1, 0.8, 30, 2)]
    [InlineData(1, false, 1, 0.8, 30, 1)]
    [InlineData(5, true, 0, 1.0, 180, 3)]
    [InlineData(3, true, 3, 1.0, 25, 2)]
    [InlineData(3, true, 0, 0.3, 25, 2)]
    public async Task LatestOutcome_ShouldDetermineNextDifficulty(
        int missionId,
        bool success,
        int failures,
        double persistence,
        double completionTime,
        int expectedDifficulty)
    {
        var session = await CreateSessionAsync();
        await RegisterEventAsync(session.SessionId, missionId, success, failures, persistence, completionTime);

        Assert.Equal(expectedDifficulty, await GetTargetDifficultyAsync(session));
    }

    [Fact]
    public async Task PreviousSuccesses_ShouldNotCancelMostRecentFailure()
    {
        var session = await CreateSessionAsync();

        await RegisterEventAsync(session.SessionId, 1, true, 0, 1.0, 12);
        await RegisterEventAsync(session.SessionId, 5, true, 0, 1.0, 91);
        await RegisterEventAsync(session.SessionId, 11, true, 0, 1.0, 11);
        await RegisterEventAsync(session.SessionId, 8, false, 1, 0.8, 45);

        // Regressao da sessao 15: apos falha em Defender Objeto (3),
        // a proxima dificuldade deve ser 2, nao 3.
        Assert.Equal(2, await GetTargetDifficultyAsync(session));
    }

    [Fact]
    public async Task FirstSuccessAfterFailure_ShouldHoldDifficultyInsteadOfImmediatelyIncreasing()
    {
        var session = await CreateSessionAsync();

        await RegisterEventAsync(session.SessionId, 3, false, 1, 0.8, 45);
        await RegisterEventAsync(session.SessionId, 2, true, 0, 1.0, 30);

        Assert.Equal(2, await GetTargetDifficultyAsync(session));
    }

    [Fact]
    public async Task TwoConsecutiveSuccessesAfterFailure_ShouldAllowGradualRecovery()
    {
        var session = await CreateSessionAsync();

        await RegisterEventAsync(session.SessionId, 3, false, 1, 0.8, 45);
        await RegisterEventAsync(session.SessionId, 2, true, 0, 1.0, 30);
        await RegisterEventAsync(session.SessionId, 5, true, 0, 1.0, 95);

        Assert.Equal(3, await GetTargetDifficultyAsync(session));
    }

    [Fact]
    public async Task SuccessWithModerateFailures_ShouldMaintainDifficulty()
    {
        var session = await CreateSessionAsync();
        await RegisterEventAsync(session.SessionId, 2, true, 2, 0.7, 25);

        Assert.Equal(2, await GetTargetDifficultyAsync(session));
    }

    private async Task<CreateSessionResponse> CreateSessionAsync()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/sessions",
            new CreateSessionRequest { Mode = GameMode.Adaptive },
            JsonOptions,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<CreateSessionResponse>(
            JsonOptions,
            TestContext.Current.CancellationToken);

        Assert.NotNull(session);
        return session;
    }

    // A fixture injeta historicos controlados. O endpoint HTTP permanece
    // restrito a resultados da recomendacao pendente.
    private async Task RegisterEventAsync(
        int sessionId, int missionId, bool success, int failures,
        double persistence, double completionTime)
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

    private async Task<int> GetTargetDifficultyAsync(CreateSessionResponse session)
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/recommendations/next",
            new NextRecommendationRequest
            {
                PlayerId = session.PlayerId,
                SessionId = session.SessionId
            },
            JsonOptions,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var recommendation = await response.Content.ReadFromJsonAsync<NextRecommendationResponse>(
            JsonOptions,
            TestContext.Current.CancellationToken);

        Assert.NotNull(recommendation);
        return recommendation.TargetDifficulty;
    }
}
