using AdaptiveTrials.Application.Interfaces;
using AdaptiveTrials.Infrastructure.Data;
using AdaptiveTrials.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"))
);

builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddScoped<IMissionService, MissionService>();
builder.Services.AddScoped<IBehaviorEventService, BehaviorEventService>();
builder.Services.AddScoped<IPlayerService, PlayerService>();
builder.Services.AddScoped<IRecommendationService, RecommendationService>();
builder.Services.AddScoped<IExportService, ExportService>();

builder.Services.AddHttpClient<ISteamService, SteamService>(
    (serviceProvider, client) =>
    {
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();

        var baseUrl = configuration["AiInference:BaseUrl"] ?? "http://127.0.0.1:8001/";

        client.BaseAddress = new Uri(baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/");

        client.Timeout = TimeSpan.FromSeconds(45);
    }
);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.MapControllers();

app.Run();
