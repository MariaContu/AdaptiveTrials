using Xunit;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdaptiveTrials.Application.DTOs.BehaviorEvents;
using AdaptiveTrials.Application.DTOs.Players;
using AdaptiveTrials.Application.DTOs.Recommendations;
using AdaptiveTrials.Application.DTOs.SessionQueries;
using AdaptiveTrials.Application.DTOs.Sessions;
using AdaptiveTrials.Application.DTOs.Steam;
using AdaptiveTrials.Domain.Enums;

namespace AdaptiveTrials.Api.IntegrationTests;

public sealed class AdaptiveModeIntegrationTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AdaptiveTrialsApiFactory _factory = new();
    private readonly HttpClient _client;

    public AdaptiveModeIntegrationTests()
    {
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task AdaptiveSession_WithoutProfileOrEvents_ShouldUseNeutralDistributions()
    {
        var session = await CreateAdaptiveSessionAsync();

        var recommendation = await GetNextRecommendationAsync(session);

        Assert.Equal(1, recommendation.TargetDifficulty);
        Assert.Equal(1, recommendation.Difficulty);

        AssertProbability(recommendation.ProfileProbabilities.Combat, 1.0 / 3.0);
        AssertProbability(recommendation.ProfileProbabilities.Exploration, 1.0 / 3.0);
        AssertProbability(recommendation.ProfileProbabilities.Puzzle, 1.0 / 3.0);

        AssertProbability(recommendation.BehaviorProbabilities.Combat, 1.0 / 3.0);
        AssertProbability(recommendation.BehaviorProbabilities.Exploration, 1.0 / 3.0);
        AssertProbability(recommendation.BehaviorProbabilities.Puzzle, 1.0 / 3.0);

        AssertProbability(recommendation.Weights.Profile, 0.8);
        AssertProbability(recommendation.Weights.Behavior, 0.2);
        AssertProbability(
            recommendation.FinalProbabilities.Combat
                + recommendation.FinalProbabilities.Exploration
                + recommendation.FinalProbabilities.Puzzle,
            1.0
        );
    }

    [Fact]
    public async Task AdaptiveSession_AfterStrongHardPerformance_ShouldIncreaseBehaviorWeightAndClampDifficultyToThree()
    {
        var session = await CreateAdaptiveSessionAsync();

        var preferencesResponse = await _client.PostAsJsonAsync(
            $"/api/players/{session.PlayerId}/preferences",
            new ManualPreferencesRequest
            {
                Combat = 0.6,
                Exploration = 0.3,
                Puzzle = 0.1
            },
            JsonOptions,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, preferencesResponse.StatusCode);

        var eventResponse = await RegisterEventAsync(
            session.SessionId,
            missionId: 3,
            completionTime: 45,
            failures: 0,
            success: true,
            persistence: 1.0
        );

        Assert.Equal(HttpStatusCode.Created, eventResponse.StatusCode);

        var recommendation = await GetNextRecommendationAsync(session);

        Assert.Equal(3, recommendation.TargetDifficulty);
        Assert.Equal(3, recommendation.Difficulty);
        AssertProbability(recommendation.Weights.Profile, 0.7);
        AssertProbability(recommendation.Weights.Behavior, 0.3);
        AssertProbability(recommendation.ProfileProbabilities.Combat, 0.6);
        AssertProbability(recommendation.ProfileProbabilities.Exploration, 0.3);
        AssertProbability(recommendation.ProfileProbabilities.Puzzle, 0.1);
    }

    [Fact]
    public async Task AdaptiveSession_SixRecommendations_ShouldNotRepeatMissionIds()
    {
        var session = await CreateAdaptiveSessionAsync();
        var missionIds = new List<int>();

        for (var index = 0; index < 6; index++)
        {
            var recommendation = await GetNextRecommendationAsync(session);

            Assert.InRange(recommendation.TargetDifficulty, 1, 3);
            Assert.InRange(recommendation.Difficulty, 1, 3);
            missionIds.Add(recommendation.RecommendedMissionId);
        }

        Assert.Equal(6, missionIds.Distinct().Count());

        var persistedRecommendations =
            await _client.GetFromJsonAsync<List<SessionRecommendationResponse>>(
                $"/api/sessions/{session.SessionId}/recommendations",
                JsonOptions,
                TestContext.Current.CancellationToken
            );

        Assert.NotNull(persistedRecommendations);
        Assert.Equal(6, persistedRecommendations.Count);
        Assert.Equal(6, persistedRecommendations.Select(item => item.MissionId).Distinct().Count());
    }

    [Fact]
    public async Task SteamPreview_WhenAiIsUnavailable_ShouldReturnBadRequestWithoutChangingPlayerProfile()
    {
        var session = await CreateAdaptiveSessionAsync();

        var response = await _client.PostAsJsonAsync(
            "/api/steam/preview",
            new SteamProfilePreviewRequest
            {
                SteamId = "76561198000000000"
            },
            JsonOptions,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var profileResponse = await _client.GetAsync(
            $"/api/players/{session.PlayerId}/profile",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.NotFound, profileResponse.StatusCode);
    }

    [Fact]
    public async Task SteamImport_WhenAiIsUnavailable_ShouldRequireManualFallbackInsteadOfCreatingMockProfile()
    {
        var session = await CreateAdaptiveSessionAsync();

        var response = await _client.PostAsJsonAsync(
            "/api/steam/import",
            new SteamImportRequest
            {
                PlayerId = session.PlayerId,
                SteamId = "76561198000000000"
            },
            JsonOptions,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var errorBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var normalizedErrorBody = errorBody.ToLowerInvariant();
        Assert.Contains("manual preferences", normalizedErrorBody);
        Assert.DoesNotContain("steam_mock", normalizedErrorBody);

        var profileResponse = await _client.GetAsync(
            $"/api/players/{session.PlayerId}/profile",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.NotFound, profileResponse.StatusCode);
    }

    private async Task<CreateSessionResponse> CreateAdaptiveSessionAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/sessions",
            new CreateSessionRequest
            {
                Mode = GameMode.Adaptive
            },
            JsonOptions,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var session = await response.Content.ReadFromJsonAsync<CreateSessionResponse>(
            JsonOptions,
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(session);
        Assert.True(session.SessionId > 0);
        Assert.True(session.PlayerId > 0);
        Assert.Equal(GameMode.Adaptive, session.Mode);
        Assert.Equal(SessionStatus.Started, session.Status);

        return session;
    }

    private async Task<NextRecommendationResponse> GetNextRecommendationAsync(
        CreateSessionResponse session
    )
    {
        var response = await _client.PostAsJsonAsync(
            "/api/recommendations/next",
            new NextRecommendationRequest
            {
                PlayerId = session.PlayerId,
                SessionId = session.SessionId
            },
            JsonOptions,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var recommendation = await response.Content.ReadFromJsonAsync<NextRecommendationResponse>(
            JsonOptions,
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(recommendation);
        return recommendation;
    }

    private Task<HttpResponseMessage> RegisterEventAsync(
        int sessionId,
        int missionId,
        double completionTime,
        int failures,
        bool success,
        double persistence
    )
    {
        return _client.PostAsJsonAsync(
            $"/api/sessions/{sessionId}/events",
            new RegisterBehaviorEventRequest
            {
                MissionId = missionId,
                CompletionTime = completionTime,
                Failures = failures,
                Success = success,
                Persistence = persistence
            },
            JsonOptions,
            TestContext.Current.CancellationToken
        );
    }

    private static void AssertProbability(double actual, double expected)
    {
        Assert.True(
            Math.Abs(actual - expected) < 1e-10,
            $"Expected probability {expected}, but received {actual}."
        );
    }
}
