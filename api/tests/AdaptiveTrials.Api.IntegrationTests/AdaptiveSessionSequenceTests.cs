using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdaptiveTrials.Application.DTOs.BehaviorEvents;
using AdaptiveTrials.Application.DTOs.Recommendations;
using AdaptiveTrials.Application.DTOs.SessionQueries;
using AdaptiveTrials.Application.DTOs.Sessions;
using AdaptiveTrials.Domain.Enums;
using Xunit;

namespace AdaptiveTrials.Api.IntegrationTests;

/// <summary>
/// A sequencia V2 e verificada pela API real com SQLite em memoria.
/// </summary>
public sealed class AdaptiveSessionSequenceTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AdaptiveTrialsApiFactory _factory = new();
    private readonly HttpClient _client;

    public AdaptiveSessionSequenceTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task CannotRegisterAdaptiveEventWithoutRecommendation()
    {
        var session = await CreateSessionAsync();
        using var response = await PostEventAsync(session.SessionId, 1);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(await EventsAsync(session.SessionId));
    }

    [Fact]
    public async Task RepeatedNextReturnsTheSamePendingRecommendation()
    {
        var session = await CreateSessionAsync();
        var first = await NextAsync(session);
        var retry = await NextAsync(session);

        Assert.Equal(first.RecommendationId, retry.RecommendationId);
        Assert.Equal(first.RecommendedMissionId, retry.RecommendedMissionId);
        Assert.Equal(first.TargetDifficulty, retry.TargetDifficulty);
        Assert.Equal(first.FinalProbabilities.Combat, retry.FinalProbabilities.Combat);

        var persisted = await RecommendationsAsync(session.SessionId);
        Assert.Single(persisted);
        Assert.Empty(await EventsAsync(session.SessionId));
    }

    [Fact]
    public async Task EventForDifferentMissionIsRejectedAndPendingRecommendationRemains()
    {
        var session = await CreateSessionAsync();
        var first = await NextAsync(session);
        var otherMissionId = first.RecommendedMissionId == 1 ? 2 : 1;

        using var wrong = await PostEventAsync(session.SessionId, otherMissionId);
        Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);
        Assert.Empty(await EventsAsync(session.SessionId));

        var retry = await NextAsync(session);
        Assert.Equal(first.RecommendationId, retry.RecommendationId);

        using var correct = await PostEventAsync(session.SessionId, first.RecommendedMissionId);
        Assert.Equal(HttpStatusCode.Created, correct.StatusCode);
        Assert.Single(await EventsAsync(session.SessionId));
    }

    [Fact]
    public async Task RetryOfSameEventReturnsOriginalIdWithoutIncreasingCount()
    {
        var session = await CreateSessionAsync();
        var first = await NextAsync(session);

        using var created = await PostEventAsync(session.SessionId, first.RecommendedMissionId);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var registered = await created.Content.ReadFromJsonAsync<RegisterBehaviorEventResponse>(
            JsonOptions, TestContext.Current.CancellationToken);
        Assert.NotNull(registered);

        using var retry = await PostEventAsync(session.SessionId, first.RecommendedMissionId);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var persisted = await retry.Content.ReadFromJsonAsync<RegisterBehaviorEventResponse>(
            JsonOptions, TestContext.Current.CancellationToken);
        Assert.NotNull(persisted);
        Assert.Equal(registered.EventId, persisted.EventId);

        Assert.Single(await EventsAsync(session.SessionId));
        Assert.Single(await RecommendationsAsync(session.SessionId));
    }

    [Fact]
    public async Task RetryOfMissionWithDifferentMetricsIsConflict()
    {
        var session = await CreateSessionAsync();
        var first = await NextAsync(session);
        using var created = await PostEventAsync(session.SessionId, first.RecommendedMissionId);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var incompatible = await PostEventAsync(
            session.SessionId, first.RecommendedMissionId,
            completionTime: 31, failures: 1, success: false, persistence: 0.8);
        Assert.Equal(HttpStatusCode.Conflict, incompatible.StatusCode);
        Assert.Single(await EventsAsync(session.SessionId));
    }

    [Fact]
    public async Task CannotCreateNextNewRecommendationBeforePendingEvent()
    {
        var session = await CreateSessionAsync();
        var first = await NextAsync(session);

        // Repetir /next devolve a pendente; so depois do evento a etapa avanca.
        for (var attempt = 0; attempt < 3; attempt++)
            Assert.Equal(first.RecommendationId, (await NextAsync(session)).RecommendationId);

        using var posted = await PostEventAsync(session.SessionId, first.RecommendedMissionId);
        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);
        var second = await NextAsync(session);
        Assert.NotEqual(first.RecommendationId, second.RecommendationId);
        Assert.Equal(2, (await RecommendationsAsync(session.SessionId)).Count);
    }

    [Fact]
    public async Task SixCompletedPairsCannotGenerateASeventhRecommendation()
    {
        var session = await CreateSessionAsync();
        for (var i = 0; i < 6; i++)
        {
            var next = await NextAsync(session);
            using var response = await PostEventAsync(session.SessionId, next.RecommendedMissionId);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        using var seventh = await _client.PostAsJsonAsync(
            "/api/recommendations/next",
            new NextRecommendationRequest
            {
                PlayerId = session.PlayerId, SessionId = session.SessionId
            }, JsonOptions, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, seventh.StatusCode);
        Assert.Equal(6, (await EventsAsync(session.SessionId)).Count);
        Assert.Equal(6, (await RecommendationsAsync(session.SessionId)).Count);
    }

    [Fact]
    public async Task ControlSession_RetryDoesNotDuplicateAndDoesNotRequireRecommendations()
    {
        var session = await CreateSessionAsync(GameMode.Control);
        using var first = await PostEventAsync(session.SessionId, 1);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var retry = await PostEventAsync(session.SessionId, 1);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Single(await EventsAsync(session.SessionId));
        Assert.Empty(await RecommendationsAsync(session.SessionId));

        using var altered = await PostEventAsync(session.SessionId, 1, completionTime: 50);
        Assert.Equal(HttpStatusCode.Conflict, altered.StatusCode);
    }

    [Fact]
    public async Task ControlSession_CannotExceedSixEvents()
    {
        var session = await CreateSessionAsync(GameMode.Control);
        foreach (var missionId in new[] { 1, 4, 9, 12, 16, 19 })
        {
            using var saved = await PostEventAsync(session.SessionId, missionId);
            Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        }

        using var extra = await PostEventAsync(session.SessionId, 2);
        Assert.Equal(HttpStatusCode.Conflict, extra.StatusCode);
        Assert.Equal(6, (await EventsAsync(session.SessionId)).Count);
    }

    private async Task<CreateSessionResponse> CreateSessionAsync(
        GameMode mode = GameMode.Adaptive)
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/sessions", new CreateSessionRequest { Mode = mode },
            JsonOptions, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<CreateSessionResponse>(
            JsonOptions, TestContext.Current.CancellationToken);
        Assert.NotNull(session);
        return session;
    }

    private async Task<NextRecommendationResponse> NextAsync(CreateSessionResponse session)
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/recommendations/next",
            new NextRecommendationRequest { PlayerId = session.PlayerId, SessionId = session.SessionId },
            JsonOptions, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<NextRecommendationResponse>(
            JsonOptions, TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        return result;
    }

    private Task<HttpResponseMessage> PostEventAsync(
        int sessionId, int missionId, double completionTime = 30,
        int failures = 0, bool success = true, double persistence = 1) =>
        _client.PostAsJsonAsync(
            $"/api/sessions/{sessionId}/events",
            new RegisterBehaviorEventRequest
            {
                MissionId = missionId, CompletionTime = completionTime,
                Failures = failures, Success = success, Persistence = persistence
            }, JsonOptions, TestContext.Current.CancellationToken);

    private async Task<List<SessionEventResponse>> EventsAsync(int sessionId) =>
        await _client.GetFromJsonAsync<List<SessionEventResponse>>(
            $"/api/sessions/{sessionId}/events", JsonOptions,
            TestContext.Current.CancellationToken) ?? [];

    private async Task<List<SessionRecommendationResponse>> RecommendationsAsync(int sessionId) =>
        await _client.GetFromJsonAsync<List<SessionRecommendationResponse>>(
            $"/api/sessions/{sessionId}/recommendations", JsonOptions,
            TestContext.Current.CancellationToken) ?? [];
}
