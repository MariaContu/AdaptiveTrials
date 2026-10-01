using Xunit;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdaptiveTrials.Application.DTOs.BehaviorEvents;
using AdaptiveTrials.Application.DTOs.Missions;
using AdaptiveTrials.Application.DTOs.Players;
using AdaptiveTrials.Application.DTOs.Recommendations;
using AdaptiveTrials.Application.DTOs.SessionQueries;
using AdaptiveTrials.Application.DTOs.Sessions;
using AdaptiveTrials.Domain.Enums;

namespace AdaptiveTrials.Api.IntegrationTests;

public sealed class ControlModeIntegrationTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AdaptiveTrialsApiFactory _factory = new();
    private readonly HttpClient _client;

    public ControlModeIntegrationTests()
    {
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Catalog_ShouldContainNineTemplatesWithThreeDifficultiesAndValidParameters()
    {
        var missions = await _client.GetFromJsonAsync<List<MissionResponse>>(
            "/api/missions",
            JsonOptions
        );

        Assert.NotNull(missions);
        Assert.Equal(27, missions.Count);

        var groups = missions
            .GroupBy(mission => mission.Template)
            .ToDictionary(group => group.Key, group => group.ToList());

        Assert.Equal(9, groups.Count);

        foreach (var group in groups.Values)
        {
            Assert.Equal(3, group.Count);
            Assert.Equal(
                new[] { 1, 2, 3 },
                group.Select(mission => mission.Difficulty).OrderBy(value => value).ToArray()
            );
        }

        var requiredParameters = new Dictionary<string, string[]>
        {
            ["Eliminar Alvo"] = ["targetHealth", "guards"],
            ["Sobreviver"] = ["waves"],
            ["Defender Objeto"] = ["waves", "objectHealth"],
            ["Encontrar Objetos"] = ["items", "lightScale"],
            ["Chegar ao Destino"] =
            [
                "checkpoints", "hazards", "lasers", "maxFailures",
                "safeSeconds", "warningSeconds", "activeSeconds"
            ],
            ["Evitar Inimigos"] =
            [
                "enemies", "enemySpeed", "detectionRadius", "coneAngle",
                "suspicionSeconds", "safePoints", "maxFailures"
            ],
            ["Repetir Sequência"] = ["sequenceSize", "maxFailures"],
            ["Conectar Pontos"] = ["pieces", "maxFailures"],
            ["Decifrar Código"] = ["boards", "attempts"]
        };

        foreach (var mission in missions)
        {
            using var parameters = JsonDocument.Parse(mission.ParametersJson);
            var root = parameters.RootElement;

            Assert.Equal(JsonValueKind.Object, root.ValueKind);
            Assert.True(requiredParameters.ContainsKey(mission.Template));

            foreach (var parameterName in requiredParameters[mission.Template])
            {
                Assert.True(
                    root.TryGetProperty(parameterName, out _),
                    $"A missão {mission.Id} ({mission.Template}) não possui o parâmetro {parameterName}."
                );
            }

            Assert.False(root.TryGetProperty("clues", out _));
            Assert.False(root.TryGetProperty("distance", out _));
        }
    }

    [Fact]
    public async Task ControlSession_FullLifecycle_ShouldPersistSixEventsAndFinish()
    {
        var session = await CreateControlSessionAsync();

        var missionIds = new[] { 1, 4, 9, 12, 16, 19 };

        for (var index = 0; index < missionIds.Length; index++)
        {
            var response = await RegisterEventAsync(
                session.SessionId,
                missionIds[index],
                completionTime: 30 + index * 10,
                failures: index == 2 ? 1 : 0,
                success: index != 4,
                persistence: index == 4 ? 0.5 : 0.9
            );

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        var detailsBeforeEnd = await _client.GetFromJsonAsync<SessionDetailsResponse>(
            $"/api/sessions/{session.SessionId}",
            JsonOptions
        );

        Assert.NotNull(detailsBeforeEnd);
        Assert.Equal(GameMode.Control, detailsBeforeEnd.Mode);
        Assert.Equal(SessionStatus.Started, detailsBeforeEnd.Status);
        Assert.Equal(6, detailsBeforeEnd.TotalEvents);
        Assert.Equal(0, detailsBeforeEnd.TotalRecommendations);

        var events = await _client.GetFromJsonAsync<List<SessionEventResponse>>(
            $"/api/sessions/{session.SessionId}/events",
            JsonOptions
        );

        Assert.NotNull(events);
        Assert.Equal(6, events.Count);
        Assert.Equal(2, events.Count(item => item.MissionType == MissionType.Combat));
        Assert.Equal(2, events.Count(item => item.MissionType == MissionType.Exploration));
        Assert.Equal(2, events.Count(item => item.MissionType == MissionType.Puzzle));
        Assert.Contains(events, item => item.Failures == 1);
        Assert.Contains(events, item => !item.Success);

        var endResponse = await _client.PostAsync(
            $"/api/sessions/{session.SessionId}/end",
            content: null
        );

        Assert.Equal(HttpStatusCode.OK, endResponse.StatusCode);

        var endedSession = await endResponse.Content.ReadFromJsonAsync<EndSessionResponse>(JsonOptions);

        Assert.NotNull(endedSession);
        Assert.Equal(SessionStatus.Finished, endedSession.Status);
        Assert.NotNull(endedSession.EndedAt);

        var secondEndResponse = await _client.PostAsync(
            $"/api/sessions/{session.SessionId}/end",
            content: null
        );

        Assert.Equal(HttpStatusCode.OK, secondEndResponse.StatusCode);

        var eventAfterEnd = await RegisterEventAsync(
            session.SessionId,
            missionId: 1,
            completionTime: 10,
            failures: 0,
            success: true,
            persistence: 1
        );

        Assert.Equal(HttpStatusCode.BadRequest, eventAfterEnd.StatusCode);
    }

    [Fact]
    public async Task ControlSession_BehaviorEvents_ShouldNotChangePlayerProfile()
    {
        var session = await CreateControlSessionAsync();

        var preferencesResponse = await _client.PostAsJsonAsync(
            $"/api/players/{session.PlayerId}/preferences",
            new ManualPreferencesRequest
            {
                Combat = 0.6,
                Exploration = 0.3,
                Puzzle = 0.1
            },
            JsonOptions
        );

        preferencesResponse.EnsureSuccessStatusCode();

        var profileBefore = await _client.GetFromJsonAsync<PlayerProfileResponse>(
            $"/api/players/{session.PlayerId}/profile",
            JsonOptions
        );

        Assert.NotNull(profileBefore);

        foreach (var missionId in new[] { 1, 4, 9, 12, 16, 19 })
        {
            var eventResponse = await RegisterEventAsync(
                session.SessionId,
                missionId,
                completionTime: 180,
                failures: 4,
                success: false,
                persistence: 0.2
            );

            Assert.Equal(HttpStatusCode.Created, eventResponse.StatusCode);
        }

        var profileAfter = await _client.GetFromJsonAsync<PlayerProfileResponse>(
            $"/api/players/{session.PlayerId}/profile",
            JsonOptions
        );

        Assert.NotNull(profileAfter);
        Assert.Equal(profileBefore.Source, profileAfter.Source);
        Assert.True(Math.Abs(profileBefore.Combat - profileAfter.Combat) < 1e-10);
        Assert.True(Math.Abs(profileBefore.Exploration - profileAfter.Exploration) < 1e-10);
        Assert.True(Math.Abs(profileBefore.Puzzle - profileAfter.Puzzle) < 1e-10);
        Assert.Equal(profileBefore.CreatedAt, profileAfter.CreatedAt);

        var recommendations = await _client.GetFromJsonAsync<List<SessionRecommendationResponse>>(
            $"/api/sessions/{session.SessionId}/recommendations",
            JsonOptions
        );

        Assert.NotNull(recommendations);
        Assert.Empty(recommendations);
    }

    [Fact]
    public async Task ControlSession_RecommendationEndpoint_ShouldRejectAndNotPersistRecommendation()
    {
        var session = await CreateControlSessionAsync();

        var response = await _client.PostAsJsonAsync(
            "/api/recommendations/next",
            new NextRecommendationRequest
            {
                PlayerId = session.PlayerId,
                SessionId = session.SessionId
            },
            JsonOptions
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var errorBody = await response.Content.ReadAsStringAsync();
        Assert.Contains("adaptive", errorBody.ToLowerInvariant());

        var recommendations = await _client.GetFromJsonAsync<List<SessionRecommendationResponse>>(
            $"/api/sessions/{session.SessionId}/recommendations",
            JsonOptions
        );

        Assert.NotNull(recommendations);
        Assert.Empty(recommendations);
    }

    [Theory]
    [InlineData(-1, 0, 0.5)]
    [InlineData(30, -1, 0.5)]
    [InlineData(30, 0, -0.1)]
    [InlineData(30, 0, 1.1)]
    public async Task BehaviorEvent_InvalidMetrics_ShouldBeRejected(
        double completionTime,
        int failures,
        double persistence)
    {
        var session = await CreateControlSessionAsync();

        var response = await RegisterEventAsync(
            session.SessionId,
            missionId: 1,
            completionTime,
            failures,
            success: true,
            persistence
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var events = await _client.GetFromJsonAsync<List<SessionEventResponse>>(
            $"/api/sessions/{session.SessionId}/events",
            JsonOptions
        );

        Assert.NotNull(events);
        Assert.Empty(events);
    }

    private async Task<CreateSessionResponse> CreateControlSessionAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/sessions",
            new CreateSessionRequest
            {
                Mode = GameMode.Control
            },
            JsonOptions
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var session = await response.Content.ReadFromJsonAsync<CreateSessionResponse>(JsonOptions);

        Assert.NotNull(session);
        Assert.True(session.SessionId > 0);
        Assert.True(session.PlayerId > 0);
        Assert.Equal(GameMode.Control, session.Mode);
        Assert.Equal(SessionStatus.Started, session.Status);

        return session;
    }

    private Task<HttpResponseMessage> RegisterEventAsync(
        int sessionId,
        int missionId,
        double completionTime,
        int failures,
        bool success,
        double persistence)
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
            JsonOptions
        );
    }
}
