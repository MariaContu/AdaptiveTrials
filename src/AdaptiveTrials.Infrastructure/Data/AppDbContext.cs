using AdaptiveTrials.Domain.Entities;
using AdaptiveTrials.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdaptiveTrials.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Player> Players => Set<Player>();
    public DbSet<GameSession> Sessions => Set<GameSession>();
    public DbSet<Mission> Missions => Set<Mission>();
    public DbSet<BehaviorEvent> BehaviorEvents => Set<BehaviorEvent>();
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();
    public DbSet<NormalizedProfile> NormalizedProfiles => Set<NormalizedProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Player>()
            .HasOne(p => p.NormalizedProfile)
            .WithOne(np => np.Player)
            .HasForeignKey<NormalizedProfile>(np => np.PlayerId);

        modelBuilder.Entity<Player>()
            .HasMany(p => p.Sessions)
            .WithOne(s => s.Player)
            .HasForeignKey(s => s.PlayerId);

        modelBuilder.Entity<GameSession>()
            .HasMany(s => s.BehaviorEvents)
            .WithOne(e => e.Session)
            .HasForeignKey(e => e.SessionId);

        modelBuilder.Entity<GameSession>()
            .HasMany(s => s.Recommendations)
            .WithOne(r => r.Session)
            .HasForeignKey(r => r.SessionId);

        modelBuilder.Entity<Mission>()
            .HasMany(m => m.BehaviorEvents)
            .WithOne(e => e.Mission)
            .HasForeignKey(e => e.MissionId);

        modelBuilder.Entity<Mission>()
            .HasMany(m => m.Recommendations)
            .WithOne(r => r.Mission)
            .HasForeignKey(r => r.MissionId)
            .IsRequired(false);

        SeedMissions(modelBuilder);
    }

    private static void SeedMissions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Mission>().HasData(
            // COMBATE
            new Mission
            {
                Id = 1,
                Name = "Caça Simples",
                Type = MissionType.Combat,
                Template = "Eliminar Alvo",
                Difficulty = 1,
                ParametersJson = "{\"enemies\":3}",
                Description = "Derrotar pequenos inimigos."
            },
            new Mission
            {
                Id = 2,
                Name = "Caça Média",
                Type = MissionType.Combat,
                Template = "Eliminar Alvo",
                Difficulty = 2,
                ParametersJson = "{\"enemies\":6}",
                Description = "Derrotar uma quantidade maior de inimigos."
            },
            new Mission
            {
                Id = 3,
                Name = "Caça Elite",
                Type = MissionType.Combat,
                Template = "Eliminar Alvo",
                Difficulty = 4,
                ParametersJson = "{\"enemies\":1,\"boss\":true}",
                Description = "Derrotar um inimigo forte."
            },
            new Mission
            {
                Id = 4,
                Name = "Sobrevivência Curta",
                Type = MissionType.Combat,
                Template = "Sobreviver",
                Difficulty = 2,
                ParametersJson = "{\"timeSeconds\":30}",
                Description = "Resistir por um curto período de tempo."
            },
            new Mission
            {
                Id = 5,
                Name = "Sobrevivência Média",
                Type = MissionType.Combat,
                Template = "Sobreviver",
                Difficulty = 3,
                ParametersJson = "{\"timeSeconds\":60}",
                Description = "Resistir por um período médio de tempo."
            },
            new Mission
            {
                Id = 6,
                Name = "Sobrevivência Extrema",
                Type = MissionType.Combat,
                Template = "Sobreviver",
                Difficulty = 5,
                ParametersJson = "{\"timeSeconds\":120}",
                Description = "Resistir sob alta pressão."
            },
            new Mission
            {
                Id = 7,
                Name = "Defesa Básica",
                Type = MissionType.Combat,
                Template = "Defender Objeto",
                Difficulty = 2,
                ParametersJson = "{\"waves\":2}",
                Description = "Defender um objeto simples."
            },
            new Mission
            {
                Id = 8,
                Name = "Defesa Avançada",
                Type = MissionType.Combat,
                Template = "Defender Objeto",
                Difficulty = 4,
                ParametersJson = "{\"waves\":5}",
                Description = "Defender um objeto contra muitas ondas."
            },

            // EXPLORAÇÃO
            new Mission
            {
                Id = 9,
                Name = "Exploração Simples",
                Type = MissionType.Exploration,
                Template = "Encontrar Objetos",
                Difficulty = 1,
                ParametersJson = "{\"items\":3}",
                Description = "Buscar poucos itens pelo cenário."
            },
            new Mission
            {
                Id = 10,
                Name = "Exploração Média",
                Type = MissionType.Exploration,
                Template = "Encontrar Objetos",
                Difficulty = 3,
                ParametersJson = "{\"items\":6}",
                Description = "Buscar uma quantidade maior de itens."
            },
            new Mission
            {
                Id = 11,
                Name = "Exploração Difícil",
                Type = MissionType.Exploration,
                Template = "Encontrar Objetos",
                Difficulty = 5,
                ParametersJson = "{\"items\":10}",
                Description = "Buscar muitos itens em uma missão de alta complexidade."
            },
            new Mission
            {
                Id = 12,
                Name = "Navegação Simples",
                Type = MissionType.Exploration,
                Template = "Chegar ao Destino",
                Difficulty = 1,
                ParametersJson = "{\"distance\":\"short\"}",
                Description = "Chegar a um destino por um caminho simples."
            },
            new Mission
            {
                Id = 13,
                Name = "Navegação Complexa",
                Type = MissionType.Exploration,
                Template = "Chegar ao Destino",
                Difficulty = 4,
                ParametersJson = "{\"distance\":\"long\"}",
                Description = "Chegar a um destino em um mapa mais complexo."
            },
            new Mission
            {
                Id = 14,
                Name = "Stealth Básico",
                Type = MissionType.Exploration,
                Template = "Evitar Inimigos",
                Difficulty = 2,
                ParametersJson = "{\"enemies\":2}",
                Description = "Evitar poucos inimigos durante o percurso."
            },
            new Mission
            {
                Id = 15,
                Name = "Stealth Avançado",
                Type = MissionType.Exploration,
                Template = "Evitar Inimigos",
                Difficulty = 5,
                ParametersJson = "{\"enemies\":6}",
                Description = "Evitar vários inimigos em uma missão de alta dificuldade."
            },

            // QUEBRA-CABEÇA
            new Mission
            {
                Id = 16,
                Name = "Sequência Simples",
                Type = MissionType.Puzzle,
                Template = "Repetir Sequência",
                Difficulty = 1,
                ParametersJson = "{\"sequenceSize\":3}",
                Description = "Repetir uma sequência curta."
            },
            new Mission
            {
                Id = 17,
                Name = "Sequência Média",
                Type = MissionType.Puzzle,
                Template = "Repetir Sequência",
                Difficulty = 3,
                ParametersJson = "{\"sequenceSize\":5}",
                Description = "Repetir uma sequência maior."
            },
            new Mission
            {
                Id = 18,
                Name = "Sequência Difícil",
                Type = MissionType.Puzzle,
                Template = "Repetir Sequência",
                Difficulty = 5,
                ParametersJson = "{\"sequenceSize\":8}",
                Description = "Repetir uma sequência de alta exigência de memória."
            },
            new Mission
            {
                Id = 19,
                Name = "Conexão Básica",
                Type = MissionType.Puzzle,
                Template = "Conectar Pontos",
                Difficulty = 2,
                ParametersJson = "{\"pieces\":4}",
                Description = "Resolver uma conexão simples entre pontos."
            },
            new Mission
            {
                Id = 20,
                Name = "Conexão Avançada",
                Type = MissionType.Puzzle,
                Template = "Conectar Pontos",
                Difficulty = 4,
                ParametersJson = "{\"pieces\":8}",
                Description = "Resolver uma conexão mais complexa entre pontos."
            },
            new Mission
            {
                Id = 21,
                Name = "Código Simples",
                Type = MissionType.Puzzle,
                Template = "Decifrar Código",
                Difficulty = 2,
                ParametersJson = "{\"clues\":3}",
                Description = "Decifrar um código com várias pistas disponíveis."
            },
            new Mission
            {
                Id = 22,
                Name = "Código Difícil",
                Type = MissionType.Puzzle,
                Template = "Decifrar Código",
                Difficulty = 5,
                ParametersJson = "{\"clues\":1}",
                Description = "Decifrar um código com poucas pistas."
            }
        );
    }
}