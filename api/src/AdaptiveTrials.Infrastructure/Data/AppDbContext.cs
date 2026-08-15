using AdaptiveTrials.Domain.Entities;
using AdaptiveTrials.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdaptiveTrials.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(
        DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Player> Players =>
        Set<Player>();

    public DbSet<GameSession> Sessions =>
        Set<GameSession>();

    public DbSet<Mission> Missions =>
        Set<Mission>();

    public DbSet<BehaviorEvent> BehaviorEvents =>
        Set<BehaviorEvent>();

    public DbSet<Recommendation> Recommendations =>
        Set<Recommendation>();

    public DbSet<NormalizedProfile> NormalizedProfiles =>
        Set<NormalizedProfile>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Player>()
            .HasOne(player =>
                player.NormalizedProfile)
            .WithOne(profile =>
                profile.Player)
            .HasForeignKey<NormalizedProfile>(
                profile =>
                    profile.PlayerId);

        modelBuilder.Entity<Player>()
            .HasMany(player =>
                player.Sessions)
            .WithOne(session =>
                session.Player)
            .HasForeignKey(session =>
                session.PlayerId);

        modelBuilder.Entity<GameSession>()
            .HasMany(session =>
                session.BehaviorEvents)
            .WithOne(behaviorEvent =>
                behaviorEvent.Session)
            .HasForeignKey(behaviorEvent =>
                behaviorEvent.SessionId);

        modelBuilder.Entity<GameSession>()
            .HasMany(session =>
                session.Recommendations)
            .WithOne(recommendation =>
                recommendation.Session)
            .HasForeignKey(recommendation =>
                recommendation.SessionId);

        modelBuilder.Entity<Mission>()
            .HasMany(mission =>
                mission.BehaviorEvents)
            .WithOne(behaviorEvent =>
                behaviorEvent.Mission)
            .HasForeignKey(behaviorEvent =>
                behaviorEvent.MissionId);

        modelBuilder.Entity<Mission>()
            .HasMany(mission =>
                mission.Recommendations)
            .WithOne(recommendation =>
                recommendation.Mission)
            .HasForeignKey(recommendation =>
                recommendation.MissionId)
            .IsRequired(false);

        SeedMissions(modelBuilder);
    }

    private static void SeedMissions(
        ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Mission>().HasData(
            /*
             * COMBATE — ELIMINAR ALVO
             */
            new Mission
            {
                Id = 1,
                Name = "Caça Simples",
                Type = MissionType.Combat,
                Template = "Eliminar Alvo",
                Difficulty = 1,
                ParametersJson =
                    "{\"enemies\":3,\"boss\":false}",
                Description =
                    "Derrotar uma pequena quantidade de inimigos."
            },
            new Mission
            {
                Id = 2,
                Name = "Caça Média",
                Type = MissionType.Combat,
                Template = "Eliminar Alvo",
                Difficulty = 2,
                ParametersJson =
                    "{\"enemies\":6,\"boss\":false}",
                Description =
                    "Derrotar uma quantidade intermediária de inimigos."
            },
            new Mission
            {
                Id = 3,
                Name = "Caça Elite",
                Type = MissionType.Combat,
                Template = "Eliminar Alvo",
                Difficulty = 3,
                ParametersJson =
                    "{\"enemies\":1,\"boss\":true}",
                Description =
                    "Derrotar um inimigo de elite."
            },

            /*
             * COMBATE — SOBREVIVER
             */
            new Mission
            {
                Id = 4,
                Name = "Sobrevivência Inicial",
                Type = MissionType.Combat,
                Template = "Sobreviver",
                Difficulty = 1,
                ParametersJson =
                    "{\"waves\":3}",
                Description =
                    "Sobreviver e eliminar três ondas de inimigos."
            },
            new Mission
            {
                Id = 5,
                Name = "Sobrevivência Intermediária",
                Type = MissionType.Combat,
                Template = "Sobreviver",
                Difficulty = 2,
                ParametersJson =
                    "{\"waves\":5}",
                Description =
                    "Sobreviver e eliminar cinco ondas de dificuldade progressiva."
            },
            new Mission
            {
                Id = 6,
                Name = "Sobrevivência Extrema",
                Type = MissionType.Combat,
                Template = "Sobreviver",
                Difficulty = 3,
                ParametersJson =
                    "{\"waves\":7}",
                Description =
                    "Sobreviver e eliminar sete ondas de inimigos sob alta pressão."
            },

            /*
             * COMBATE — DEFENDER OBJETO
             */
            new Mission
            {
                Id = 7,
                Name = "Defesa Básica",
                Type = MissionType.Combat,
                Template = "Defender Objeto",
                Difficulty = 1,
                ParametersJson =
                    "{\"waves\":2}",
                Description =
                    "Defender um objeto durante poucas ondas."
            },
            new Mission
            {
                Id = 23,
                Name = "Defesa Intermediária",
                Type = MissionType.Combat,
                Template = "Defender Objeto",
                Difficulty = 2,
                ParametersJson =
                    "{\"waves\":3}",
                Description =
                    "Defender um objeto durante uma quantidade intermediária de ondas."
            },
            new Mission
            {
                Id = 8,
                Name = "Defesa Avançada",
                Type = MissionType.Combat,
                Template = "Defender Objeto",
                Difficulty = 3,
                ParametersJson =
                    "{\"waves\":5}",
                Description =
                    "Defender um objeto contra várias ondas."
            },

            /*
             * EXPLORAÇÃO — ENCONTRAR OBJETOS
             */
            new Mission
            {
                Id = 9,
                Name = "Exploração Simples",
                Type = MissionType.Exploration,
                Template = "Encontrar Objetos",
                Difficulty = 1,
                ParametersJson =
                    "{\"items\":3}",
                Description =
                    "Buscar poucos itens pelo cenário."
            },
            new Mission
            {
                Id = 10,
                Name = "Exploração Média",
                Type = MissionType.Exploration,
                Template = "Encontrar Objetos",
                Difficulty = 2,
                ParametersJson =
                    "{\"items\":6}",
                Description =
                    "Buscar uma quantidade intermediária de itens."
            },
            new Mission
            {
                Id = 11,
                Name = "Exploração Difícil",
                Type = MissionType.Exploration,
                Template = "Encontrar Objetos",
                Difficulty = 3,
                ParametersJson =
                    "{\"items\":10}",
                Description =
                    "Buscar muitos itens em uma missão de alta complexidade."
            },

            /*
             * EXPLORAÇÃO — CHEGAR AO DESTINO
             */
            new Mission
            {
                Id = 12,
                Name = "Navegação Simples",
                Type = MissionType.Exploration,
                Template = "Chegar ao Destino",
                Difficulty = 1,
                ParametersJson =
                    "{" +
                    "\"distance\":\"short\"," +
                    "\"checkpoints\":2," +
                    "\"hazards\":2," +
                    "\"maxFailures\":4" +
                    "}",
                Description =
                    "Chegar ao destino por uma rota curta e pouco perigosa."
            },
            new Mission
            {
                Id = 24,
                Name = "Navegação Intermediária",
                Type = MissionType.Exploration,
                Template = "Chegar ao Destino",
                Difficulty = 2,
                ParametersJson =
                    "{" +
                    "\"distance\":\"medium\"," +
                    "\"checkpoints\":3," +
                    "\"hazards\":4," +
                    "\"maxFailures\":3" +
                    "}",
                Description =
                    "Chegar ao destino por uma rota de complexidade intermediária."
            },
            new Mission
            {
                Id = 13,
                Name = "Navegação Complexa",
                Type = MissionType.Exploration,
                Template = "Chegar ao Destino",
                Difficulty = 3,
                ParametersJson =
                    "{" +
                    "\"distance\":\"long\"," +
                    "\"checkpoints\":5," +
                    "\"hazards\":6," +
                    "\"maxFailures\":2" +
                    "}",
                Description =
                    "Chegar ao destino por uma rota longa e perigosa."
            },

            /*
             * EXPLORAÇÃO — EVITAR INIMIGOS
             */
            new Mission
            {
                Id = 14,
                Name = "Furtividade Básica",
                Type = MissionType.Exploration,
                Template = "Evitar Inimigos",
                Difficulty = 1,
                ParametersJson =
                    "{" +
                    "\"enemies\":2," +
                    "\"enemySpeed\":65," +
                    "\"detectionRadius\":70," +
                    "\"maxFailures\":4" +
                    "}",
                Description =
                    "Evitar poucos inimigos durante o percurso."
            },
            new Mission
            {
                Id = 25,
                Name = "Furtividade Intermediária",
                Type = MissionType.Exploration,
                Template = "Evitar Inimigos",
                Difficulty = 2,
                ParametersJson =
                    "{" +
                    "\"enemies\":4," +
                    "\"enemySpeed\":85," +
                    "\"detectionRadius\":90," +
                    "\"maxFailures\":3" +
                    "}",
                Description =
                    "Evitar patrulhas de velocidade e alcance intermediários."
            },
            new Mission
            {
                Id = 15,
                Name = "Furtividade Avançada",
                Type = MissionType.Exploration,
                Template = "Evitar Inimigos",
                Difficulty = 3,
                ParametersJson =
                    "{" +
                    "\"enemies\":6," +
                    "\"enemySpeed\":110," +
                    "\"detectionRadius\":115," +
                    "\"maxFailures\":2" +
                    "}",
                Description =
                    "Evitar várias patrulhas em uma missão de alta dificuldade."
            },

            /*
             * QUEBRA-CABEÇA — REPETIR SEQUÊNCIA
             */
            new Mission
            {
                Id = 16,
                Name = "Sequência Simples",
                Type = MissionType.Puzzle,
                Template = "Repetir Sequência",
                Difficulty = 1,
                ParametersJson =
                    "{\"sequenceSize\":3}",
                Description =
                    "Repetir uma sequência curta."
            },
            new Mission
            {
                Id = 17,
                Name = "Sequência Média",
                Type = MissionType.Puzzle,
                Template = "Repetir Sequência",
                Difficulty = 2,
                ParametersJson =
                    "{\"sequenceSize\":5}",
                Description =
                    "Repetir uma sequência de tamanho intermediário."
            },
            new Mission
            {
                Id = 18,
                Name = "Sequência Difícil",
                Type = MissionType.Puzzle,
                Template = "Repetir Sequência",
                Difficulty = 3,
                ParametersJson =
                    "{\"sequenceSize\":8}",
                Description =
                    "Repetir uma sequência de alta exigência de memória."
            },

            /*
             * QUEBRA-CABEÇA — CONECTAR PONTOS
             */
            new Mission
            {
                Id = 19,
                Name = "Conexão Básica",
                Type = MissionType.Puzzle,
                Template = "Conectar Pontos",
                Difficulty = 1,
                ParametersJson =
                    "{\"pieces\":4}",
                Description =
                    "Resolver uma conexão simples entre pontos."
            },
            new Mission
            {
                Id = 26,
                Name = "Conexão Intermediária",
                Type = MissionType.Puzzle,
                Template = "Conectar Pontos",
                Difficulty = 2,
                ParametersJson =
                    "{\"pieces\":6}",
                Description =
                    "Resolver uma conexão de complexidade intermediária."
            },
            new Mission
            {
                Id = 20,
                Name = "Conexão Avançada",
                Type = MissionType.Puzzle,
                Template = "Conectar Pontos",
                Difficulty = 3,
                ParametersJson =
                    "{\"pieces\":8}",
                Description =
                    "Resolver uma conexão complexa entre vários pontos."
            },

            /*
             * QUEBRA-CABEÇA — DECIFRAR CÓDIGO
             */
            new Mission
            {
                Id = 21,
                Name = "Código Simples",
                Type = MissionType.Puzzle,
                Template = "Decifrar Código",
                Difficulty = 1,
                ParametersJson =
                    "{\"clues\":3}",
                Description =
                    "Decifrar um código com várias pistas disponíveis."
            },
            new Mission
            {
                Id = 27,
                Name = "Código Intermediário",
                Type = MissionType.Puzzle,
                Template = "Decifrar Código",
                Difficulty = 2,
                ParametersJson =
                    "{\"clues\":2}",
                Description =
                    "Decifrar um código com uma quantidade intermediária de pistas."
            },
            new Mission
            {
                Id = 22,
                Name = "Código Difícil",
                Type = MissionType.Puzzle,
                Template = "Decifrar Código",
                Difficulty = 3,
                ParametersJson =
                    "{\"clues\":1}",
                Description =
                    "Decifrar um código com poucas pistas disponíveis."
            }
        );
    }
}